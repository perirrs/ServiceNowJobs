/**
 * End-to-end scenarios: two admins, each with their own assistant (as on the server), one Teams
 * shared by both, and the bot chat routed the way index.ts routes it. Each scenario walks through
 * what really happens in a working day, from button presses to what lands in the group chats.
 */
import assert from "node:assert/strict";
import { test } from "node:test";

Math.random = () => 0;
import type { Config } from "../src/config.js";
import { Store } from "../src/db.js";
import { cardToCommand, Engine } from "../src/engine.js";
import type { DirectChannel, Identity } from "../src/engine.js";
import { followedElsewhereFor, othersGroupsFor, routeAdminMessage, senderKey } from "../src/senders.js";
import { FakeBrain } from "./fakes.js";

type User = { userId: string; email: string; name: string };
const SUJITH: User = { userId: "u-s", email: "sujith@mergen.test", name: "Sujith" };
const MAHANTH: User = { userId: "u-m", email: "mahanth.m@mergen.test", name: "Mahanth M" };
const JITENDER: User = { userId: "u-j", email: "jitender@mergen.test", name: "Jitender JG" };
const PRIYA: User = { userId: "u-p", email: "priya@mergen.test", name: "Priya Rao" };
const RAVI: User = { userId: "u-r", email: "ravi@mergen.test", name: "Ravi Kumar" };
const everyone = [SUJITH, MAHANTH, JITENDER, PRIYA, RAVI];

type Message = { id: string; chatId: string; from: User; text: string; at: string; byAssistant: boolean; mentions: string[] };

/** One Teams: group chats with members, one-to-one chats, and the bot chat with each admin. */
class Teams {
  now = new Date("2026-11-16T10:00:00Z"); // a Monday
  private seq = 0;
  messages: Message[] = [];
  groups = new Map<string, { name: string; members: User[] }>([
    ["g-rec", { name: "Recruitment Team", members: [SUJITH, MAHANTH, PRIYA, RAVI] }],
    ["g-adm", { name: "Admin & Networking", members: [MAHANTH, SUJITH, JITENDER] }],
  ]);
  /** What the bot said to each admin in their bot chat. */
  botChat: { to: string; text: string }[] = [];
  trackers: { chatId: string; html: string }[] = [];

  at(iso: string): void {
    this.now = new Date(iso);
  }

  post(chatId: string, from: User, text: string, byAssistant = false, mentions: string[] = []): Message {
    const at = new Date(this.now.getTime() + ++this.seq).toISOString();
    const m = { id: `m${this.seq}`, chatId, from, text, at, byAssistant, mentions };
    this.messages.push(m);
    return m;
  }

  in(chatId: string): Message[] {
    return this.messages.filter((m) => m.chatId === chatId);
  }

  /** The Teams side of one sender's assistant. */
  direct(sender: User): DirectChannel {
    const dm = (email: string) => `dm:${sender.email}:${email}`;
    return {
      senderName: sender.name,
      senderEmail: sender.email,
      connect: async (email) => {
        const u = everyone.find((x) => x.email === email);
        return u ? { conversationId: dm(email), name: u.name } : { notFound: true, suggestions: [] };
      },
      send: async (chatId, text, mentions) => {
        let shown = text;
        (mentions ?? []).forEach((m, i) => (shown = shown.replace(`{@${i}}`, m.name)));
        return this.post(chatId, sender, shown, true, (mentions ?? []).map((m) => m.userId)).id;
      },
      postHtml: async (chatId, html) => this.post(chatId, sender, html, true).id,
      createTracker: async (chatId, html) => {
        this.trackers.push({ chatId, html });
        return this.post(chatId, sender, `Team tracker ${html}`, true).id;
      },
      updateTracker: async (chatId, _id, html) => void this.trackers.push({ chatId, html }),
      findGroups: async (q) =>
        [...this.groups]
          .filter(([, g]) => g.members.includes(sender) && g.name.toLowerCase().includes(q.toLowerCase()))
          .map(([chatId, g]) => ({ chatId, name: g.name })),
      groupMembers: async (chatId) => (this.groups.get(chatId)?.members ?? []).filter((u) => u !== sender),
      groupName: async (chatId) => this.groups.get(chatId)?.name ?? null,
      history: async (chatId, _max, since = "") =>
        this.in(chatId)
          .filter((m) => !since || m.at > since)
          .map((m) => ({ fromSender: m.from === sender, from: m.from.name, text: m.text, at: m.at })),
    };
  }
}

const config: Config = {
  port: 0,
  adminEmails: [SUJITH.email, MAHANTH.email],
  companyName: "Mergen",
  timezone: "UTC",
  chaseAfterMinutes: 45,
  maxBotTurns: 6,
  dataDir: ":memory:",
  model: "fake",
  smartModel: "fake-smart",
  sendAsUser: true,
  pollSeconds: 20,
  discloseAutomation: false,
  idleCloseMinutes: 30,
  groupWindowMinutes: 120,
  humanDelay: false,
  approveFirstDay: false,
  historyMessages: 100000,
} as Config;

/** Both assistants on one bot, as on the server. */
function world() {
  const teams = new Teams();
  const make = (sender: User) => {
    const store = new Store(":memory:");
    const brain = new FakeBrain();
    const engine = new Engine(
      store,
      brain,
      { send: async (to, text) => void teams.botChat.push({ to, text }), sendCard: async (to, card) => void teams.botChat.push({ to, text: JSON.stringify(card) }) },
      { ...config },
      () => teams.now,
      () => undefined,
      teams.direct(sender),
    );
    return { sender, store, brain, engine, name: sender.name, email: sender.email, cursors: new Map<string, string>() };
  };
  const sujith = make(SUJITH);
  const mahanth = make(MAHANTH);
  const senders = [sujith, mahanth];
  for (const s of senders) {
    s.engine.followedElsewhere = followedElsewhereFor(s, senders);
    s.engine.otherSenders = othersGroupsFor(s, senders);
    s.engine.adminChat = (email) => senders.map((o) => o.store.personByEmail(email)?.conversationId).find(Boolean) ?? undefined;
  }
  const keys = senders.map((s) => ({ email: s.email, key: senderKey(s.email) }));
  const idOf = (u: User): Identity => ({ teamsUserId: `t-${u.userId}`, conversationId: `bot-${u.userId}`, name: u.name, lookupEmail: async () => u.email });

  /** Someone types in their bot chat (or presses a button): routed as index.ts does. */
  const botMessage = async (u: User, text: string) => {
    const route = routeAdminMessage(text, u.email, keys, config.adminEmails.includes(u.email));
    await senders.find((s) => s.email === route.sender)!.engine.handleMessage(idOf(u), route.text);
  };
  const press = (u: User, value: Record<string, unknown>) => botMessage(u, cardToCommand(value));

  /** One pass of each assistant's group readers, as index.ts runs them every minute. */
  const readGroups = async () => {
    for (const s of senders) {
      for (const g of s.store.groups()) {
        const since = s.cursors.get(g.chatId) ?? "";
        const fresh = teams.in(g.chatId).filter((m) => m.at > since);
        if (fresh.length) s.cursors.set(g.chatId, fresh.at(-1)!.at);
        const listening = s.engine.listeningGroups().some((l) => l.group.id === g.id);
        for (const m of fresh) {
          if (m.from === s.sender && m.byAssistant) continue;
          await s.engine.groupLine(g.id, m.from.email, m.text, m.at);
          if (listening && m.from !== s.sender) await s.engine.handleGroupReply(m.from.userId, m.text, m.id, g.id);
        }
      }
    }
  };
  const toAdmin = (u: User) => teams.botChat.filter((m) => m.to === `bot-${u.userId}`).map((m) => m.text);
  const boardOf = (u: User) => toAdmin(u).filter((t) => t.includes('"Team board"')).at(-1) ?? "";
  return { teams, sujith, mahanth, botMessage, press, readGroups, toAdmin, boardOf };
}

/** Both admins say hello, then each adds their own group. */
async function setUp() {
  const w = world();
  await w.botMessage(SUJITH, "hi");
  await w.botMessage(MAHANTH, "hi");
  await w.botMessage(SUJITH, "add group Recruitment Team");
  await w.botMessage(MAHANTH, "add group Admin & Networking");
  for (const s of [w.sujith, w.mahanth]) for (const g of s.store.groups()) s.cursors.set(g.chatId, w.teams.now.toISOString());
  return w;
}

test("scenario 1: each admin runs their own groups; a group cannot be taken twice; each board shows the other's groups to ask", async () => {
  const w = await setUp();
  assert.deepEqual(w.sujith.store.groups().map((g) => g.name), ["Recruitment Team"]);
  assert.deepEqual(w.mahanth.store.groups().map((g) => g.name), ["Admin & Networking"]);

  await w.botMessage(MAHANTH, "add group Recruitment Team");
  assert.match(w.toAdmin(MAHANTH).at(-1)!, /already followed by Sujith/);
  assert.equal(w.mahanth.store.groups().length, 1);

  // Admins are in each other's groups but are never asked or followed there.
  assert.equal(w.mahanth.store.personByEmail(SUJITH.email)?.nominated ?? false, false);
  assert.equal(w.sujith.store.personByEmail(MAHANTH.email)?.nominated ?? false, false);

  await w.botMessage(SUJITH, "board");
  assert.match(w.boardOf(SUJITH), /Groups followed by Mahanth M/);
  assert.match(w.boardOf(SUJITH), /"cmd":"askOther","key":"mahanth","who":"Admin & Networking"/);
  await w.botMessage(MAHANTH, "board");
  assert.match(w.boardOf(MAHANTH), /Groups followed by Sujith/);
  assert.match(w.boardOf(MAHANTH), /"cmd":"askOther","key":"sujith","who":"Recruitment Team"/);
});

test("scenario 2: Sujith asks Mahanth's group for an update from his own board; it goes out from Mahanth's account and Sujith hears back", async () => {
  const w = await setUp();
  await w.press(SUJITH, { cmd: "askOther", key: "mahanth", who: "Admin & Networking" });
  const posted = w.teams.in("g-adm").filter((m) => m.byAssistant);
  assert.equal(posted.length, 1);
  assert.equal(posted[0]!.from, MAHANTH); // Mahanth's group, Mahanth's account
  assert.deepEqual(posted[0]!.mentions, [JITENDER.userId]); // the team member, not the admins
  assert.match(w.toAdmin(SUJITH).at(-1)!, /Posted in \*\*Admin & Networking\*\*/);
  // Nothing went to Sujith's own group.
  assert.equal(w.teams.in("g-rec").length, 0);
});

test("scenario 3: a reply gets one follow-up in the group, then the rest is only recorded", async () => {
  const w = await setUp();
  w.mahanth.brain.doneAfterUserMessages = 99; // keeps asking, if it were allowed to
  await w.botMessage(MAHANTH, "ask group Admin & Networking");
  w.teams.at("2026-11-16T10:05:00Z");
  w.teams.post("g-adm", JITENDER, "Working on the mail setup and the hosting server today");
  await w.readGroups();
  const after = () => w.teams.in("g-adm").filter((m) => m.byAssistant);
  assert.equal(after().length, 2); // the ask and one follow-up
  assert.deepEqual(after()[1]!.mentions, [JITENDER.userId]);
  w.teams.at("2026-11-16T10:08:00Z");
  w.teams.post("g-adm", JITENDER, "The SQL port is still blocked, trying a fix");
  await w.readGroups();
  assert.equal(after().length, 2); // no second follow-up
});

test("scenario 4: Sujith's instruction in Mahanth's group goes on Mahanth's tracker; 'do it Monday' moves it; Monday brings it back", async () => {
  const w = await setUp();
  await w.press(SUJITH, { cmd: "askOther", key: "mahanth", who: "Admin & Networking" }); // Sujith now talks to Mahanth's assistant too
  const jit = () => w.mahanth.store.personByEmail(JITENDER.email)!;

  w.mahanth.brain.nextInstruction = [{ kind: "add", person: "Jitender JG", itemId: null, title: "Make the TalentFlow website live", due: "2026-11-16" }];
  w.teams.at("2026-11-16T10:20:00Z");
  w.teams.post("g-adm", SUJITH, "Jitender, can you make the TalentFlow website live today, urgent and very important");
  await w.readGroups();
  const item = w.mahanth.store.activeItems(jit().id).find((i) => i.title === "Make the TalentFlow website live")!;
  assert.equal(item.due, "2026-11-16");
  // Both admins are told: Mahanth, whose group it is, and Sujith, who gave the instruction.
  assert.match(w.toAdmin(MAHANTH).join("\n"), /Sujith's instruction in Admin & Networking/);
  assert.match(w.toAdmin(SUJITH).join("\n"), /Sujith's instruction in Admin & Networking/);
  // Sujith's own assistant did not act on a group it does not follow.
  assert.equal(w.sujith.brain.instructionReads.length, 0);

  w.mahanth.brain.nextInstruction = [{ kind: "reschedule", person: "Jitender JG", itemId: item.id, title: "", due: "2026-11-23" }];
  w.teams.at("2026-11-16T10:30:00Z");
  w.teams.post("g-adm", MAHANTH, "Jitender, leave the website for now, do it on Monday");
  await w.readGroups();
  assert.equal(w.mahanth.store.itemsOf(jit().id).find((i) => i.id === item.id)!.status, "paused");
  assert.ok(!w.mahanth.store.trackerItems(jit().id, "2026-11-16").some((i) => i.id === item.id));

  w.teams.at("2026-11-23T09:00:00Z");
  await w.mahanth.engine.tick();
  assert.equal(w.mahanth.store.itemsOf(jit().id).find((i) => i.id === item.id)!.status, "open");
});

test("scenario 4b: an admin who gives an instruction in the other admin's group is told what changed, even if they never used that assistant", async () => {
  const w = await setUp();
  w.mahanth.brain.nextInstruction = [{ kind: "add", person: "Jitender JG", itemId: null, title: "Renew the domain", due: "2026-11-16" }];
  w.teams.at("2026-11-16T10:20:00Z");
  w.teams.post("g-adm", SUJITH, "Jitender, please renew the domain today");
  await w.readGroups();
  assert.match(w.toAdmin(SUJITH).join("\n"), /Sujith's instruction in Admin & Networking[\s\S]*Renew the domain/);
  // And an UPDATE posted there is confirmed to its author the same way.
  w.teams.post("g-adm", SUJITH, "UPDATE\nAll servers move to Azure this month.");
  await w.readGroups();
  assert.match(w.toAdmin(SUJITH).join("\n"), /Read Sujith's UPDATE in Admin & Networking/);
});

test("scenario 4c: a question about using the bot is answered from its help, not from the team's notes", async () => {
  const w = await setUp();
  await w.botMessage(SUJITH, "How do I add a group?");
  const asked = w.sujith.brain.questions.at(-1)!;
  assert.match(asked.material, /How to use this assistant/);
  assert.match(asked.material, /add group/);
});

test("scenario 4d: UPDATE posts (figures, targets, standing rules) and the notes after them are not read as tasks", async () => {
  const w = await setUp();
  w.sujith.brain.nextInstruction = [{ kind: "add", person: "everyone", itemId: null, title: "5 submissions a day", due: null }];
  w.teams.at("2026-11-16T11:00:00Z");
  w.teams.post("g-rec", SUJITH, "UPDATE\nRecruiters: 5 submissions a day from today.");
  w.teams.post("g-rec", SUJITH, "Ravi Kumar\nAs a recruiter 7 submissions this month");
  await w.readGroups();
  assert.equal(w.sujith.brain.instructionReads.length, 0);
  assert.equal(w.sujith.store.activeItems(w.sujith.store.personByEmail(RAVI.email)!.id).length, 0);
});

test("scenario 5: Mahanth's UPDATE and instructions in Sujith's group count there too", async () => {
  const w = await setUp();
  const ravi = () => w.sujith.store.personByEmail(RAVI.email)!;
  w.sujith.brain.nextInstruction = [{ kind: "add", person: "Ravi Kumar", itemId: null, title: "Send 3 Deloitte profiles", due: "2026-11-16" }];
  w.teams.at("2026-11-16T10:40:00Z");
  w.teams.post("g-rec", MAHANTH, "Ravi, send 3 Deloitte profiles today please");
  w.teams.post("g-rec", MAHANTH, "UPDATE\nNo leave without telling Madan first.");
  await w.readGroups();
  assert.ok(w.sujith.store.activeItems(ravi().id).some((i) => i.title === "Send 3 Deloitte profiles"));
  assert.match(w.sujith.store.get(`gupdates:${w.sujith.store.groups()[0]!.id}`) ?? "", /No leave without telling Madan/);
});

test("scenario 6: someone said to be on sick leave is not chased; the others are", async () => {
  const w = await setUp();
  await w.botMessage(SUJITH, "ask group Recruitment Team");
  w.teams.at("2026-11-16T10:10:00Z");
  w.teams.post("g-rec", PRIYA, "Ravi is on sick leave today. I sent 2 profiles to Deloitte so far");
  await w.readGroups();
  assert.match(w.toAdmin(SUJITH).join("\n"), /Priya Rao says Ravi Kumar is on leave today/);
  w.teams.at("2026-11-16T10:50:00Z"); // past the 45-minute chase
  await w.sujith.engine.tick();
  const chases = w.teams.in("g-rec").filter((m) => m.byAssistant && /Any update/.test(m.text));
  assert.ok(!chases.some((m) => m.mentions.includes(RAVI.userId)));
});

test("scenario 7: a typed message to everyone goes to the sender's own groups only, word for word", async () => {
  const w = await setUp();
  await w.press(MAHANTH, { cmd: "postEveryone", manualEveryone: "Office is closed on Friday for Diwali." });
  const posted = w.teams.messages.filter((m) => m.byAssistant);
  assert.deepEqual(
    posted.map((m) => [m.chatId, m.from.name, m.text]),
    [["g-adm", "Mahanth M", "Office is closed on Friday for Diwali."]],
  );
});

test("scenario 8: someone who joins is read and followed within a minute; someone who leaves is let go", async () => {
  const w = await setUp();
  w.teams.groups.get("g-adm")!.members.push(PRIYA);
  w.teams.at("2026-11-16T10:02:00Z");
  await w.mahanth.engine.tick();
  assert.match(w.toAdmin(MAHANTH).join("\n"), /Priya Rao joined Admin & Networking/);
  // Priya is now followed by both assistants' groups; she is in Sujith's group as well, which is fine:
  // she is asked in each group about that group's work.
  w.teams.groups.get("g-adm")!.members.splice(w.teams.groups.get("g-adm")!.members.indexOf(PRIYA), 1);
  w.teams.at("2026-11-16T10:04:00Z");
  await w.mahanth.engine.tick();
  assert.match(w.toAdmin(MAHANTH).join("\n"), /Priya Rao left Admin & Networking/);
});
