import { App } from "@microsoft/teams.apps";
import cron from "node-cron";
import { ClaudeBrain } from "./ai.js";
import { loadConfig } from "./config.js";
import { Store } from "./db.js";
import { Engine } from "./engine.js";
import type { DirectChannel, Identity } from "./engine.js";
import { GraphChat, htmlToText, textToHtml } from "./graph.js";

const config = loadConfig();

if (!config.adminEmails.length) {
  console.error("ADMIN_EMAILS is empty. Set it to the CTO's and CEO's work emails, or nobody can manage the bot.");
  process.exit(1);
}
for (const [name, expr] of [
  ["CHECKIN_CRON", config.checkinCron],
  ["REMINDER_CRON", config.reminderCron],
  ["DIGEST_CRON", config.digestCron],
] as const) {
  if (!cron.validate(expr)) {
    console.error(`${name} is not a valid cron expression: "${expr}"`);
    process.exit(1);
  }
}

// Reads CLIENT_ID, CLIENT_SECRET and TENANT_ID from the environment.
// With SOCKET_MODE=true the bot dials out to Teams over a WebSocket, so it needs
// no public URL: right for trials from a laptop, not for a published app.
const socketMode = process.env.SOCKET_MODE === "true";
const app = new App({ socketMode });
const store = new Store(config.dataDir);

// In send-as-user mode, check-ins go out from a signed-in person's own Teams
// account (Microsoft Graph) and replies are read by polling those chats.
// Admin commands and the digest still go through the bot.
let graph: GraphChat | undefined;
let direct: DirectChannel | undefined;
let senderId = "";
/** Text of messages the assistant sent, so the poller can tell them from ones the person typed. */
const sentByAssistant = new Set<string>();
const normalise = (text: string) => htmlToText(textToHtml(text)).replace(/\s+/g, " ");

if (config.sendAsUser) {
  graph = new GraphChat(process.env.CLIENT_ID ?? "", process.env.TENANT_ID ?? "", config.dataDir);
  let me;
  try {
    me = await graph.whoAmI();
  } catch (err) {
    console.error(`SEND_AS_USER is on but sign-in is missing or expired. Run "npm run signin".\n${String(err)}`);
    process.exit(1);
  }
  senderId = me.id;
  const g = graph;
  direct = {
    senderName: me.name,
    connect: (email) => g.connect(email),
    send: async (chatId, text) => {
      sentByAssistant.add(normalise(text));
      await g.send(chatId, text);
    },
  };
  console.log(`Check-ins will be sent from ${me.name} <${me.email}>'s Teams account.`);
}

const brain = new ClaudeBrain(config.model, config.companyName, undefined, direct?.senderName);
const engine = new Engine(
  store,
  brain,
  { send: async (conversationId, text) => void (await app.send(conversationId, text)) },
  config,
  undefined,
  undefined,
  direct,
);

/** Read new messages in each chat that has an open check-in. */
async function pollChats(g: GraphChat): Promise<void> {
  for (const { person, checkin } of engine.openDirectCheckins()) {
    const key = `cursor:${checkin.id}`;
    // First look: start just before the assistant's opening message.
    const opened = store.messages(checkin.id)[0]?.at;
    const since =
      store.get(key) ??
      new Date((opened ? Date.parse(`${opened.replace(" ", "T")}Z`) : Date.now()) - 5000).toISOString();
    try {
      for (const line of await g.messagesSince(person.conversationId!, since)) {
        store.set(key, line.createdAt);
        if (line.fromUserId !== senderId) {
          await engine.handleDirectReply(person.id, line.text);
          continue;
        }
        // From the sender's account: the assistant's own message, or the person typing?
        // The recorded check-in messages cover the case where the bot was restarted.
        const text = normalise(line.text);
        const recorded = store.messages(checkin.id).some((m) => m.role === "bot" && normalise(m.text) === text);
        if (!recorded && !sentByAssistant.has(text)) engine.manualTakeover(person.id);
      }
    } catch (err) {
      console.error(`Could not read the chat with ${person.email}`, err);
    }
  }
}

/** Build an Identity from an inbound activity. Only one-to-one chats count. */
function identityOf(
  activity: { from: { id: string; name?: string }; conversation: { id: string; conversationType?: string } },
  api: App["api"],
): Identity | undefined {
  if (activity.conversation.conversationType !== "personal") return undefined;
  return {
    teamsUserId: activity.from.id,
    conversationId: activity.conversation.id,
    name: activity.from.name,
    lookupEmail: async () => {
      const member = await api.conversations.getMemberById(activity.conversation.id, activity.from.id);
      return member.email ?? member.userPrincipalName;
    },
  };
}

app.on("install.add", async ({ activity, api }) => {
  const id = identityOf(activity, api);
  if (id) await engine.handleInstall(id);
});

app.on("message", async ({ activity, api, send }) => {
  const id = identityOf(activity, api);
  if (!id) {
    await send("I only work in one-to-one chat. Message me directly and I'll pick it up there.");
    return;
  }
  await send({ type: "typing" });
  // Not awaited: Teams expects a quick acknowledgement, and a digest can take a minute.
  engine.handleMessage(id, activity.text ?? "").catch((err) => console.error("Message handling failed", err));
});

function schedule(name: string, expr: string, job: () => Promise<unknown>): void {
  cron.schedule(
    expr,
    async () => {
      console.log(`[${name}] starting`);
      try {
        await job();
        console.log(`[${name}] finished`);
      } catch (err) {
        console.error(`[${name}] failed`, err);
      }
    },
    { timezone: config.timezone, noOverlap: true },
  );
}

schedule("check-ins", config.checkinCron, () => engine.runCheckins());
schedule("reminders", config.reminderCron, () => engine.runReminders());
schedule("digest", config.digestCron, () => engine.runDigest());

if (graph) {
  const g = graph;
  let busy = false;
  setInterval(async () => {
    if (busy) return;
    busy = true;
    try {
      await pollChats(g);
    } finally {
      busy = false;
    }
  }, config.pollSeconds * 1000);
}

await app.start(config.port);
console.log(
  `Check-in bot running on port ${config.port}. Admins: ${config.adminEmails.join(", ")}. ` +
    `Check-ins "${config.checkinCron}", digest "${config.digestCron}" (${config.timezone}).`,
);

// One-off manual trigger: `node dist/index.js --checkin-now` asks everyone
// nominated for an update straight away, then carries on running as normal.
if (process.argv.includes("--checkin-now")) {
  const { sent, unreachable } = await engine.runCheckins();
  console.log(
    `[manual trigger] sent ${sent} check-in(s)` +
      (unreachable.length ? `; not connected: ${unreachable.map((p) => p.email).join(", ")}` : ""),
  );
}
