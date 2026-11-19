import { App } from "@microsoft/teams.apps";
import cron from "node-cron";
import { ClaudeBrain } from "./ai.js";
import { loadConfig } from "./config.js";
import { Store } from "./db.js";
import { Engine } from "./engine.js";
import type { Identity } from "./engine.js";

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
const brain = new ClaudeBrain(config.model, config.companyName);
const engine = new Engine(store, brain, { send: async (conversationId, text) => void (await app.send(conversationId, text)) }, config);

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

await app.start(config.port);
console.log(
  `Check-in bot running on port ${config.port}. Admins: ${config.adminEmails.join(", ")}. ` +
    `Check-ins "${config.checkinCron}", digest "${config.digestCron}" (${config.timezone}).`,
);
