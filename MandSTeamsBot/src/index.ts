import { appendFileSync, existsSync, readFileSync, renameSync, unlinkSync } from "node:fs";
import path from "node:path";
import { App } from "@microsoft/teams.apps";
import { ClaudeBrain } from "./ai.js";
import { loadConfig } from "./config.js";
import { Store } from "./db.js";
import { cardToCommand, Engine } from "./engine.js";
import type { DirectChannel, Identity } from "./engine.js";
import { GraphChat, htmlToText, textToHtml } from "./graph.js";
import { Base44TalentFlow } from "./ats.js";
import { Base44 } from "./base44.js";

const config = loadConfig();

if (!config.adminEmails.length) {
  console.error("ADMIN_EMAILS is empty. Set it to the CTO's and CEO's work emails, or nobody can manage the bot.");
  process.exit(1);
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
    send: async (chatId, text, mentions, quote) => {
      // Remember it the way it will read in Teams, where each tag shows as the person's name.
      let shown = text;
      (mentions ?? []).forEach((m, i) => (shown = shown.replace(`{@${i}}`, m.name)));
      if (mentions?.length && shown === text) shown = `${mentions.map((m) => m.name).join(" ")} ${text}`;
      sentByAssistant.add(normalise(shown));
      return g.send(chatId, text, mentions, quote);
    },
    senderEmail: me.email,
    unsend: (chatId, messageId) => g.softDelete(chatId, messageId),
    awayBetween: (emails, start, end) => g.awayBetween(emails, start, end),
    meetings: (start, end) => g.meetings(start, end),
    sync: () => pollNow(),
    createTracker: async (chatId, html) => {
      const id = await g.sendHtml(chatId, html);
      // Pinning is a convenience: the table is still there if it cannot be pinned.
      await g.pin(chatId, id).catch((err) => console.warn(`Could not pin the tracker: ${String(err).slice(0, 200)}`));
      return id;
    },
    updateTracker: (chatId, messageId, html) => g.updateHtml(chatId, messageId, html),
    findGroups: (query) => g.findGroups(query),
    groupMembers: (chatId) => g.groupMembers(chatId),
    history: async (chatId, max, since) =>
      (await g.history(chatId, max, since)).map((m) => ({
        fromSender: m.fromUserId === me.id,
        from: m.fromName,
        text: m.text,
        at: m.createdAt,
      })),
  };
  console.log(
    `Check-ins will be sent from ${me.name} <${me.email}>'s Teams account` +
      (config.discloseAutomation ? "." : ", written as that person with no mention of automation."),
  );
}

/** While an inbox command runs, what the bot says to that admin is also written to the outbox. */
let outbox: { conversationId: string; lines: string[] } | null = null;

const brain = new ClaudeBrain(config.model, config.companyName, undefined, direct?.senderName, config.discloseAutomation);
brain.smartModel = config.smartModel;
const engine = new Engine(
  store,
  brain,
  {
    send: async (conversationId, text) => {
      if (outbox && outbox.conversationId === conversationId) outbox.lines.push(text);
      await app.send(conversationId, text);
    },
    sendCard: async (conversationId, card) =>
      (
        await app.send(conversationId, {
          type: "message",
          attachments: [{ contentType: "application/vnd.microsoft.card.adaptive", content: card }],
        })
      ).id,
    updateCard: async (conversationId, messageId, card) =>
      void (await app.api.conversations.updateActivity(conversationId, messageId, {
        type: "message",
        attachments: [{ contentType: "application/vnd.microsoft.card.adaptive", content: card }],
      })),
  },
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
      const lines = await g.messagesSince(person.chatId!, since);
      if (!lines.length) continue;
      // Move on before acting: reading a message twice would answer it twice.
      store.set(key, lines.at(-1)!.createdAt);

      // From the sender's account: the assistant's own message, or the person typing?
      // The recorded check-in messages cover the case where the bot was restarted.
      const recorded = store.messages(checkin.id).filter((m) => m.role === "bot").map((m) => normalise(m.text));
      const typedBySender = lines.some(
        (l) => l.fromUserId === senderId && !recorded.includes(normalise(l.text)) && !sentByAssistant.has(normalise(l.text)),
      );
      // Looked at first, so that nothing else in this batch is answered over the sender.
      if (typedBySender) engine.manualTakeover(person.id);

      // Several quick messages are one thing said, and get one answer.
      const theirs = lines.filter((l) => l.fromUserId !== senderId).map((l) => l.text).join("\n");
      if (theirs) await engine.handleDirectReply(person.id, theirs);
    } catch (err) {
      console.error(`Could not read the chat with ${person.email}`, err);
    }
  }
}

/** Read new messages in each group chat that was asked for updates and is still being followed. */
async function pollGroups(g: GraphChat): Promise<void> {
  for (const { group, askedAt } of engine.listeningGroups()) {
    const key = `gcursor:${group.id}`;
    const since = store.get(key) || new Date(Date.parse(askedAt) - 5000).toISOString();
    try {
      const lines = await g.messagesSince(group.chatId, since);
      if (!lines.length) continue;
      store.set(key, lines.at(-1)!.createdAt);

      // What the sender wrote himself comes first: the assistant steps back for whoever it was aimed at.
      for (const line of lines) {
        if (line.fromUserId !== senderId) continue;
        // The tracker table is the assistant's, not something the sender typed.
        if (line.id === store.get(`tracker:${group.id}`) || line.text.startsWith("Team tracker")) continue;
        if (sentByAssistant.has(normalise(line.text)) || engine.isAssistantGroupText(group.id, line.text)) continue;
        engine.senderSpokeInGroup(group.id, line.addressedUserIds);
      }

      // Then each person's messages, taken together, in the order people first spoke.
      const byPerson = new Map<string, { text: string[]; lastId: string }>();
      for (const line of lines) {
        if (line.fromUserId === senderId) continue;
        const entry = byPerson.get(line.fromUserId) ?? { text: [], lastId: "" };
        entry.text.push(line.text);
        entry.lastId = line.id;
        byPerson.set(line.fromUserId, entry);
      }
      for (const [userId, said] of byPerson) {
        await engine.handleGroupReply(userId, said.text.join("\n"), said.lastId, group.id);
      }
    } catch (err) {
      console.error(`Could not read the group chat ${group.name}`, err);
    }
  }
}

/** One pass over every watched chat. Calls made while a pass is running share it. */
let polling: Promise<void> | null = null;
function pollNow(): Promise<void> {
  if (!graph) return Promise.resolve();
  const g = graph;
  polling ??= (async () => {
    try {
      await pollGroups(g);
      await pollChats(g);
    } finally {
      polling = null;
    }
  })();
  return polling;
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
  // A button press on the menu card arrives as a message with a value and no text.
  const pressed = (activity as { value?: unknown }).value;
  const text = pressed && typeof pressed === "object" ? cardToCommand(pressed) : (activity.text ?? "");
  engine.handleMessage(id, text).catch((err) => console.error("Message handling failed", err));
});

// Once a minute: the chase, closing quiet conversations, and anything on the
// schedule. The schedule itself is set from the chat ("schedule 17:00") and
// kept in the database, so there is nothing to configure here.
let ticking = false;
setInterval(async () => {
  // Never two at once: a slow send must not let the next tick repeat a chase.
  if (ticking) return;
  ticking = true;
  try {
    await engine.tick();
  } catch (err) {
    console.error("[tick] failed", err);
  } finally {
    ticking = false;
  }
}, 60_000);

if (graph) {
  setInterval(() => {
    pollNow().catch((err) => console.error("[poll] failed", err));
  }, config.pollSeconds * 1000);
}

await app.start(config.port);
console.log(`Check-in bot running. Admins: ${config.adminEmails.join(", ")}. Timezone ${config.timezone}.`);

brain.onUsage = (model, input, output) => engine.recordUsage(model, input, output);
if (config.base44Key && config.talentflowApp) {
  engine.ats = new Base44TalentFlow(new Base44(config.base44Key), config.talentflowApp);
  console.log("TalentFlow connected: recruiters' real figures are used in check-ins.");
}
await engine.announceStart();

// A local command inbox, so admin commands can be given on this machine without
// Teams: each line of DATA_DIR/admin-inbox.txt runs as if the first admin had
// typed it to the bot, and the replies are appended to DATA_DIR/admin-outbox.txt.
// The replies still reach that admin in Teams too. Only someone who can write to
// the data folder can use it, and they can already read the token cache there.
const inbox = path.join(config.dataDir, "admin-inbox.txt");
const outboxFile = path.join(config.dataDir, "admin-outbox.txt");
let inboxBusy = false;
setInterval(async () => {
  if (inboxBusy || !existsSync(inbox)) return;
  inboxBusy = true;
  try {
    const working = `${inbox}.running`;
    renameSync(inbox, working);
    const commands = readFileSync(working, "utf8").split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith("#"));
    unlinkSync(working);
    const admin = config.adminEmails.map((e) => store.personByEmail(e)).find((p) => p?.teamsUserId && p.conversationId);
    if (!admin) {
      appendFileSync(outboxFile, `\n# ${new Date().toISOString()}\nNo admin has opened the bot in Teams yet, so the inbox cannot run.\n`);
      return;
    }
    const id: Identity = {
      teamsUserId: admin.teamsUserId!,
      conversationId: admin.conversationId!,
      name: admin.name ?? undefined,
      lookupEmail: async () => admin.email,
    };
    for (const command of commands) {
      outbox = { conversationId: admin.conversationId!, lines: [] };
      try {
        await engine.handleMessage(id, command);
      } catch (err) {
        outbox.lines.push(`Failed: ${String(err)}`);
      }
      appendFileSync(outboxFile, `\n# ${new Date().toISOString()} > ${command}\n${outbox.lines.join("\n\n")}\n`);
      outbox = null;
    }
  } catch (err) {
    console.error("[inbox] failed", err);
  } finally {
    outbox = null;
    inboxBusy = false;
  }
}, 10_000);

// One-off manual trigger: `node dist/index.js --checkin-now` asks everyone
// nominated for an update straight away, then carries on running as normal.
if (process.argv.includes("--checkin-now")) {
  const { sent, unreachable } = await engine.runCheckins();
  console.log(
    `[manual trigger] sent ${sent} check-in(s)` +
      (unreachable.length ? `; not connected: ${unreachable.map((p) => p.email).join(", ")}` : ""),
  );
}
