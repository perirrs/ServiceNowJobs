import assert from "node:assert/strict";
import { test } from "node:test";
import type { Brain, CheckinContext, DigestPerson, NoteResult, Turn } from "../src/ai.js";
import type { Config } from "../src/config.js";
import { Store } from "../src/db.js";
import { cardToCommand, chunk, Engine, parseTime } from "../src/engine.js";
import type { Identity } from "../src/engine.js";

const CTO = "cto@acme.test";
const CEO = "ceo@acme.test";
const PRIYA = "priya@acme.test";

const config: Config = {
  port: 0,
  adminEmails: [CTO, CEO],
  companyName: "Acme",
  timezone: "UTC",
  chaseAfterMinutes: 45,
  maxBotTurns: 3,
  dataDir: ":memory:",
  model: "fake",
  sendAsUser: false,
  pollSeconds: 20,
  discloseAutomation: true,
  idleCloseMinutes: 30,
};

/** Scripted stand-in for Claude that records what it was shown. */
class FakeBrain implements Brain {
  turns: CheckinContext[] = [];
  noteCalls: CheckinContext[] = [];
  digestCalls: DigestPerson[][] = [];
  questions: { question: string; material: string }[] = [];
  nextNote: NoteResult = {
    note: { summary: "Did things.", progressVsTargets: "", blockers: [], painPoints: [], helpNeeded: [], followUpTomorrow: [] },
    items: [],
  };
  doneAfterUserMessages = 1;
  failNextTurn = false;
  handoverNext = false;

  async nextTurn(ctx: CheckinContext): Promise<Turn> {
    if (this.failNextTurn) {
      this.failNextTurn = false;
      throw new Error("boom");
    }
    this.turns.push(ctx);
    if (this.handoverNext) {
      this.handoverNext = false;
      return { reply: "", done: false, handover: true };
    }
    const userMessages = ctx.history.filter((m) => m.role === "user").length;
    const done = ctx.finalTurn || userMessages >= this.doneAfterUserMessages;
    return { reply: done ? "Thanks, that's all." : `Question ${ctx.history.length}`, done };
  }
  async writeNote(ctx: CheckinContext): Promise<NoteResult> {
    this.noteCalls.push(ctx);
    return this.nextNote;
  }
  async digest(_date: string, people: DigestPerson[]): Promise<string> {
    this.digestCalls.push(people);
    return "DIGEST";
  }
  async answer(question: string, material: string): Promise<string> {
    this.questions.push({ question, material });
    return "ANSWER";
  }
  rapportCalls: { fromSender: boolean; text: string }[][] = [];
  learnedAddress = "";
  async learnRapport(_p: unknown, _s: string, chat: { fromSender: boolean; text: string }[]) {
    this.rapportCalls.push(chat);
    return { brief: "Casual. Says 'pls share'.", address: this.learnedAddress };
  }
}

function setup(opts: { direct?: boolean; personal?: boolean } = {}) {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const sent: { to: string; text: string }[] = [];
  let date = new Date("2026-11-16T17:00:00Z");
  const engine = new Engine(
    store,
    brain,
    { send: async (to, text) => void sent.push({ to, text }) },
    opts.personal ? { ...config, discloseAutomation: false } : config,
    () => date,
    () => undefined,
    opts.direct || opts.personal
      ? {
          senderName: "Sujith",
          connect: async (email) => ({ conversationId: `chat-${email}`, name: "Priya Rao" }),
          send: async (to, text) => void sent.push({ to, text }),
        }
      : undefined,
  );
  const id = (email: string): Identity => ({
    teamsUserId: `t-${email}`,
    conversationId: `c-${email}`,
    name: email.split("@")[0],
    lookupEmail: async () => email,
  });
  const to = (email: string) => sent.filter((s) => s.to === `c-${email}`).map((s) => s.text);
  return { store, brain, sent, engine, id, to, setDate: (d: string) => (date = new Date(d)) };
}

/** Admin nominates Priya, she connects, and gets a profile. */
async function onboard(t: ReturnType<typeof setup>) {
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  await t.engine.handleMessage(t.id(CTO), `profile ${PRIYA}: Recruiter. Target 10 submissions a week.`);
  await t.engine.handleInstall(t.id(PRIYA));
}

test("only admins can run commands; strangers are turned away and not stored", async () => {
  const t = setup();
  await t.engine.handleMessage(t.id("random@acme.test"), `nominate ${PRIYA}`);
  assert.equal(t.store.nominees().length, 0);
  assert.match(t.to("random@acme.test")[0]!, /not on that list/);
  assert.equal(t.store.personByEmail("random@acme.test"), undefined);
});

test("an org-wide install stays silent for people who are not nominated", async () => {
  const t = setup();
  await t.engine.handleInstall(t.id("random@acme.test"));
  assert.equal(t.sent.length, 0);
});

test("nominee is welcomed with the disclosure that leadership sees summaries", async () => {
  const t = setup();
  await onboard(t);
  const welcome = t.to(PRIYA)[0]!;
  assert.match(welcome, /summarised each day for the CTO and CEO/);
  assert.equal(t.store.personByEmail(PRIYA)!.profile, "Recruiter. Target 10 submissions a week.");
});

test("check-in uses the profile, writes a note, and carries open items into the next day", async () => {
  const t = setup();
  await onboard(t);

  const { sent } = await t.engine.runCheckins();
  assert.equal(sent, 1);
  assert.equal(t.brain.turns[0]!.person.profile, "Recruiter. Target 10 submissions a week.");
  assert.equal(t.brain.turns[0]!.history.length, 0);

  t.brain.nextNote = {
    note: { summary: "Sent 3 submissions to Globex.", progressVsTargets: "3 of 10", blockers: ["Client slow on feedback"], painPoints: [], helpNeeded: [], followUpTomorrow: ["Outcome of 3 Globex submissions"] },
    items: [{ itemId: null, title: "3 submissions to Globex", status: "open", update: "Awaiting feedback" }],
  };
  await t.engine.handleMessage(t.id(PRIYA), "Working on 3 submissions for Globex");

  const priya = t.store.personByEmail(PRIYA)!;
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "completed");
  assert.equal(t.store.activeItems(priya.id).length, 1);

  // Next day: the opening question is built from yesterday's note and open item.
  t.setDate("2026-11-17T17:00:00Z");
  await t.engine.runCheckins();
  const opening = t.brain.turns.at(-1)!;
  assert.equal(opening.date, "2026-11-17");
  assert.equal(opening.activeItems[0]!.title, "3 submissions to Globex");
  assert.equal(opening.recentNotes[0]!.summary, "Sent 3 submissions to Globex.");

  // She reports the outcome; the existing item is updated, not duplicated.
  const itemId = opening.activeItems[0]!.id;
  t.brain.nextNote = {
    note: { ...t.brain.nextNote.note, summary: "2 of 3 rejected, 1 interview." },
    items: [{ itemId, title: "3 submissions to Globex", status: "done", update: "1 interview booked, 2 rejected" }],
  };
  await t.engine.handleMessage(t.id(PRIYA), "2 rejected, 1 interview booked");
  assert.equal(t.store.activeItems(priya.id).length, 0);
});

test("the AI cannot update an item belonging to someone else", async () => {
  const t = setup();
  await onboard(t);
  const other = t.store.upsertPerson("other@acme.test");
  t.store.addItem(other.id, "Other's work", "open", "x", "2026-11-15");
  const otherItem = t.store.activeItems(other.id)[0]!;

  await t.engine.runCheckins();
  t.brain.nextNote = {
    ...t.brain.nextNote,
    items: [{ itemId: otherItem.id, title: "Hijack", status: "blocked", update: "y" }],
  };
  await t.engine.handleMessage(t.id(PRIYA), "update");

  assert.equal(t.store.activeItems(other.id)[0]!.status, "open");
  assert.equal(t.store.activeItems(t.store.personByEmail(PRIYA)!.id)[0]!.title, "Hijack");
});

test("the bot is forced to close after the question cap", async () => {
  const t = setup();
  await onboard(t);
  t.brain.doneAfterUserMessages = 99;
  await t.engine.runCheckins(); // bot turn 1
  await t.engine.handleMessage(t.id(PRIYA), "a"); // bot turn 2
  await t.engine.handleMessage(t.id(PRIYA), "b"); // bot turn 3 = cap, must be final
  assert.equal(t.brain.turns.at(-1)!.finalTurn, true);
  assert.equal(t.store.checkin(t.store.personByEmail(PRIYA)!.id, "2026-11-16")!.status, "completed");
});

test("a message after the check-in closed reopens it and the note is rewritten", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  await t.engine.handleMessage(t.id(PRIYA), "first update");
  await t.engine.handleMessage(t.id(PRIYA), "forgot to mention the invoice");
  assert.equal(t.brain.noteCalls.length, 2);
  assert.ok(t.brain.noteCalls[1]!.history.some((m) => m.text === "first update"));
});

test("if Claude fails mid-chat the message is kept and reaches the digest note", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  t.brain.failNextTurn = true;
  await t.engine.handleMessage(t.id(PRIYA), "important update");
  assert.match(t.to(PRIYA).at(-1)!, /trouble replying/);

  await t.engine.runDigest();
  assert.ok(t.brain.noteCalls[0]!.history.some((m) => m.text === "important update"));
});

test("reminder goes once, only to people who have not replied", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  assert.equal(await t.engine.runReminders(), 1);
  assert.equal(await t.engine.runReminders(), 0);
});

test("digest marks non-responders and unconnected people, and goes to connected admins only", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "nominate ghost@acme.test");
  await t.engine.runCheckins();
  await t.engine.runDigest();

  const people = t.brain.digestCalls[0]!;
  assert.equal(people.find((p) => p.person.email === PRIYA)!.missing, "no_response");
  assert.equal(people.find((p) => p.person.email === "ghost@acme.test")!.missing, "not_connected");
  assert.equal(t.to(CTO).at(-1), "DIGEST");
  assert.equal(t.to(CEO).length, 0); // the CEO has not opened the bot yet
  assert.equal(t.store.digest("2026-11-16"), "DIGEST");
});

test("admin free text is answered from the notes; 'list' alone is a command", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "list who is blocked this week");
  assert.equal(t.brain.questions.length, 1);
  assert.match(t.brain.questions[0]!.material, /Recruiter\. Target 10 submissions/);

  await t.engine.handleMessage(t.id(CTO), "list");
  assert.match(t.to(CTO).at(-1)!, /Nominated \(1\)/);
});

test("removed people stop getting check-ins", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "remove priya");
  assert.equal((await t.engine.runCheckins()).sent, 0);
});

test("chunk splits long text at paragraph breaks", () => {
  const parts = chunk(["a".repeat(60), "b".repeat(60), "c".repeat(60)].join("\n\n"), 130);
  assert.deepEqual(parts.map((p) => p.length), [122, 60]);
});

// ---------- send-as-user mode ----------

test("direct mode: nominating connects the chat at once and discloses the automation", async () => {
  const t = setup({ direct: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  assert.equal(priya.chatId, `chat-${PRIYA}`);
  assert.equal(priya.name, "Priya Rao");
  const welcome = t.sent.find((s) => s.to === `chat-${PRIYA}`)!.text;
  assert.match(welcome, /sent automatically by an AI assistant from my account/);
});

test("direct mode: chat outside an open check-in is ignored; replies inside it are handled", async () => {
  const t = setup({ direct: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;

  await t.engine.handleDirectReply(priya.id, "fancy lunch?");
  assert.equal(t.store.checkin(priya.id, "2026-11-16"), undefined);
  assert.equal(t.brain.turns.length, 0);

  await t.engine.runCheckins();
  assert.equal(t.engine.openDirectCheckins().length, 1);
  await t.engine.handleDirectReply(priya.id, "sent 3 submissions");
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "completed");

  // Closed again: later chat is ordinary conversation, not an update.
  await t.engine.handleDirectReply(priya.id, "see you tomorrow");
  assert.equal(t.brain.noteCalls.length, 1);
});

test("direct mode: when the sender types in the chat, the assistant goes quiet but keeps recording", async () => {
  const t = setup({ direct: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  await t.engine.runCheckins();
  const before = t.sent.length;

  t.engine.manualTakeover(priya.id);
  await t.engine.handleDirectReply(priya.id, "the Globex deal fell through");
  assert.equal(t.sent.length, before);

  await t.engine.runDigest();
  assert.ok(t.brain.noteCalls[0]!.history.some((m) => m.text === "the Globex deal fell through"));
});

test("direct mode: the digest still reaches admins through the bot", async () => {
  const t = setup({ direct: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.runDigest();
  assert.match(t.to(CTO).at(-1)!, /Nobody is nominated/);
});

test("direct mode: an unknown address is not nominated and likely matches are offered", async () => {
  const store = new Store(":memory:");
  const sent: { to: string; text: string }[] = [];
  const engine = new Engine(
    store,
    new FakeBrain(),
    { send: async (to, text) => void sent.push({ to, text }) },
    config,
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
    {
      senderName: "Sujith",
      connect: async () => ({ notFound: true, suggestions: ["Mahanth K (mahanth.k@acme.test)"] }),
      send: async () => undefined,
    },
  );
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  await engine.handleMessage(cto, "nominate mahanth@acme.test");
  assert.equal(store.nominees().length, 0);
  assert.equal(store.personByEmail("mahanth@acme.test"), undefined);
  assert.match(sent.at(-1)!.text, /Did you mean[\s\S]*mahanth\.k@acme\.test/);
});

test("a question with nobody nominated gets a pointer to nominate, not an AI answer", async () => {
  const t = setup();
  await t.engine.handleMessage(t.id(CTO), "Can I please have a daily update");
  assert.equal(t.brain.questions.length, 0);
  assert.match(t.to(CTO).at(-1)!, /nobody is nominated/);
});

test("the message straight after a nomination becomes that person's role profile", async () => {
  const t = setup();
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  await t.engine.handleMessage(t.id(CTO), "Senior recruiter, 10 submissions a week");
  assert.equal(t.store.personByEmail(PRIYA)!.profile, "Senior recruiter, 10 submissions a week");
  assert.match(t.to(CTO).at(-1)!, /checkin priya@acme\.test/);
  assert.equal(t.brain.questions.length, 0);

  // Only the very next message counts: after that, free text is a question again.
  await t.engine.handleMessage(t.id(CTO), "who is blocked?");
  assert.equal(t.brain.questions.length, 1);
  assert.equal(t.store.personByEmail(PRIYA)!.profile, "Senior recruiter, 10 submissions a week");
});

test("a command straight after a nomination is still a command, not a profile", async () => {
  const t = setup();
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  await t.engine.handleMessage(t.id(CTO), "list");
  await t.engine.handleMessage(t.id(CTO), "some later remark");
  assert.equal(t.store.personByEmail(PRIYA)!.profile, null);
});

test("if the opening message cannot be delivered, nothing is recorded and a retry works", async () => {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  let failing = false;
  const sent: string[] = [];
  const engine = new Engine(
    store,
    brain,
    {
      send: async (_to, text) => {
        if (failing) throw new Error("429");
        sent.push(text);
      },
    },
    config,
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
  );
  const id = (email: string): Identity => ({ teamsUserId: `t-${email}`, conversationId: `c-${email}`, lookupEmail: async () => email });
  await engine.handleInstall(id(CTO));
  await engine.handleMessage(id(CTO), `nominate ${PRIYA}`);
  await engine.handleInstall(id(PRIYA));
  const priya = store.personByEmail(PRIYA)!;

  failing = true;
  assert.equal((await engine.runCheckins()).sent, 0);
  assert.equal(store.checkin(priya.id, "2026-11-16"), undefined);

  failing = false;
  assert.equal((await engine.runCheckins()).sent, 1);
  assert.equal(store.messages(store.checkin(priya.id, "2026-11-16")!.id).length, 1);
});

// ---------- writing as the sender, with no mention of automation ----------

test("personal mode: nominating sends the colleague nothing", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  assert.equal(t.sent.filter((s) => s.to === `chat-${PRIYA}`).length, 0);
  assert.match(t.to(CTO).at(-1)!, /Nothing has been sent to them yet/);
});

test("personal mode: a handover silences the assistant and alerts the admin with what was said", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  await t.engine.runCheckins();
  const toPriya = () => t.sent.filter((s) => s.to === `chat-${PRIYA}`).length;
  const before = toPriya();

  t.brain.handoverNext = true;
  await t.engine.handleDirectReply(priya.id, "is this a bot?");
  assert.equal(toPriya(), before);
  assert.match(t.to(CTO).at(-1)!, /needs a personal reply[\s\S]*is this a bot\?/);

  // Still quiet afterwards, but what they say is kept.
  await t.engine.handleDirectReply(priya.id, "anyway, 2 submissions done");
  assert.equal(toPriya(), before);
  assert.equal(t.store.messages(t.store.checkin(priya.id, "2026-11-16")!.id).filter((m) => m.role === "user").length, 2);
});

test("personal mode: a failed reply never tells the colleague about a system problem", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  await t.engine.runCheckins();
  const before = t.sent.filter((s) => s.to === `chat-${PRIYA}`).length;
  t.brain.failNextTurn = true;
  await t.engine.handleDirectReply(priya.id, "update");
  assert.equal(t.sent.filter((s) => s.to === `chat-${PRIYA}`).length, before);
  assert.match(t.to(CTO).at(-1)!, /could not reply/);
});

test("the chase waits for the configured time, goes once, and is recorded", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  await t.engine.runCheckins();

  // Messages are stamped with the real clock, so "now" in 2026-11-16 is before they were sent.
  assert.equal(await t.engine.runReminders(45), 0);
  const checkin = t.store.checkin(priya.id, "2026-11-16")!;
  assert.equal(await t.engine.runReminders(0), 1);
  assert.equal(await t.engine.runReminders(0), 0);
  assert.equal(t.store.messages(checkin.id).at(-1)!.text, "Any update? Please share when you can.");
});

// ---------- summaries reach the admin without being asked ----------

test("when a conversation finishes, that person's summary is posted to the admin", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  await t.engine.handleMessage(t.id(PRIYA), "sent 3 submissions");
  assert.match(t.to(CTO).at(-1)!, /Update from priya[\s\S]*Did things\./);
});

test("a conversation that goes quiet is closed and summarised; one with no reply is left open", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  assert.equal(await t.engine.closeIdle(0), 0); // nobody has replied yet

  t.brain.doneAfterUserMessages = 99; // the assistant keeps asking, the person stops answering
  await t.engine.handleMessage(t.id(PRIYA), "half an update");
  assert.equal(await t.engine.closeIdle(30), 0); // too soon
  assert.equal(await t.engine.closeIdle(0), 1);
  assert.equal(t.store.checkin(t.store.personByEmail(PRIYA)!.id, "2026-11-16")!.status, "completed");
  assert.match(t.to(CTO).at(-1)!, /Update from priya/);
});

test("a question to the bot can see today's conversation while it is still in progress", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleMessage(t.id(PRIYA), "client rejected two profiles");
  await t.engine.handleMessage(t.id(CTO), "summary from today?");
  assert.match(t.brain.questions.at(-1)!.material, /still in progress[\s\S]*client rejected two profiles/);
});

test("'ask <name> ...' runs a check-in for that person", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "ask priya for what are the updates this week");
  assert.equal(t.brain.turns.length, 1);
  assert.match(t.to(CTO).at(-1)!, /Asked priya for an update/);
});

test("direct mode: an admin can also be nominated; commands use the bot chat, check-ins use the sender's chat", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleInstall(t.id(CEO));
  await t.engine.handleMessage(t.id(CTO), `nominate ${CEO}`);
  const ceo = t.store.personByEmail(CEO)!;
  assert.equal(ceo.conversationId, `c-${CEO}`);
  assert.equal(ceo.chatId, `chat-${CEO}`);

  await t.engine.runCheckins();
  assert.equal(t.sent.filter((s) => s.to === `chat-${CEO}`).length, 1); // the question, in the sender's chat
  await t.engine.handleMessage(t.id(CEO), "list"); // a command, in the bot chat
  assert.match(t.to(CEO).at(-1)!, /Nominated \(1\)/);

  await t.engine.handleDirectReply(ceo.id, "closed the Initech deal");
  assert.match(t.to(CEO).at(-1)!, /Update from/); // summary reaches both admins through the bot
  assert.match(t.to(CTO).at(-1)!, /Update from/);
});

// ---------- schedule set from the chat, and the manual controls ----------

test("times are understood in the ways people type them", () => {
  assert.equal(parseTime("17:00"), "17:00");
  assert.equal(parseTime("5pm"), "17:00");
  assert.equal(parseTime("5:30 PM"), "17:30");
  assert.equal(parseTime("0930"), "09:30");
  assert.equal(parseTime("12am"), "00:00");
  assert.equal(parseTime("25:00"), null);
  assert.equal(parseTime("soon"), null);
});

test("nothing is asked automatically until a schedule is set; then it runs once that day", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.tick(); // 17:00 on a Monday, but no schedule
  assert.equal(t.brain.turns.length, 0);

  await t.engine.handleMessage(t.id(CTO), "schedule 5pm");
  assert.match(t.to(CTO).at(-1)!, /automatically at \*\*17:00\*\*/);
  await t.engine.tick();
  await t.engine.tick();
  assert.equal(t.brain.turns.length, 1);

  await t.engine.handleMessage(t.id(CTO), "schedule off");
  assert.match(t.to(CTO).at(-1)!, /No automatic asking/);
  t.setDate("2026-11-17T17:00:00Z");
  await t.engine.tick();
  assert.equal(t.brain.turns.length, 1);
});

test("the schedule does not fire outside its window or at the weekend", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "schedule 17:00");
  t.setDate("2026-11-16T18:00:00Z"); // an hour late
  await t.engine.tick();
  t.setDate("2026-11-21T17:00:00Z"); // Saturday
  await t.engine.tick();
  assert.equal(t.brain.turns.length, 0);
});

test("'ask' forces it: a silent person is asked again, 'ask all' covers everyone", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.match(t.to(CTO).at(-1)!, /Asked priya for an update/);
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.match(t.to(CTO).at(-1)!, /Asked priya again/);
  assert.match(t.to(PRIYA).at(-1)!, /quick reminder/);

  await t.engine.handleMessage(t.id(PRIYA), "done 3 submissions"); // finishes the conversation
  await t.engine.handleMessage(t.id(CTO), "ask all");
  assert.match(t.to(CTO).at(-1)!, /Asked priya for an update/);
  assert.equal(t.store.checkin(t.store.personByEmail(PRIYA)!.id, "2026-11-16")!.status, "open");
});

test("'add' and 'delete' work as plain words for nominate and remove", async () => {
  const t = setup();
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `add ${PRIYA}`);
  assert.equal(t.store.nominees().length, 1);
  await t.engine.handleMessage(t.id(CTO), "delete priya");
  assert.equal(t.store.nominees().length, 0);
});

test("board buttons turn into the right commands", () => {
  assert.equal(cardToCommand({ cmd: "add", newEmail: " a@b.co " }), "add a@b.co");
  assert.equal(cardToCommand({ cmd: "ask", who: "a@b.co" }), "ask a@b.co");
  assert.equal(cardToCommand({ cmd: "remove", who: "a@b.co" }), "remove a@b.co");
  assert.equal(cardToCommand({ cmd: "details", who: "a@b.co" }), "report a@b.co");
  assert.equal(cardToCommand({ cmd: "askAll" }), "ask all");
  assert.equal(cardToCommand({ cmd: "schedule", time: "17:30" }), "schedule 17:30");
  assert.equal(cardToCommand({ cmd: "scheduleOff" }), "schedule off");
  assert.equal(cardToCommand({ something: "else" }), "board");
});

/** An engine whose messenger records cards sent and edited. */
function boardSetup() {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const sentCards: Record<string, unknown>[] = [];
  const edits: { id: string; json: string }[] = [];
  const engine = new Engine(
    store,
    brain,
    {
      send: async () => undefined,
      sendCard: async (_to, card) => {
        sentCards.push(card);
        return `msg-${sentCards.length}`;
      },
      updateCard: async (_to, id, card) => void edits.push({ id, json: JSON.stringify(card) }),
    },
    config,
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
  );
  const id = (email: string): Identity => ({ teamsUserId: `t-${email}`, conversationId: `c-${email}`, name: email.split("@")[0], lookupEmail: async () => email });
  return { store, brain, engine, id, sentCards, edits };
}

test("the board groups people by where they stand, with a chat link and buttons for each", async () => {
  const b = boardSetup();
  await b.engine.handleMessage(b.id(CTO), `add ${PRIYA}`);
  await b.engine.handleInstall(b.id(PRIYA));
  await b.engine.handleMessage(b.id(CTO), "board");
  let json = JSON.stringify(b.sentCards.at(-1));
  assert.match(json, /Not asked today \(1\)/);
  assert.match(json, /l\/chat\/0\/0\?users=priya%40acme\.test/);
  assert.match(json, /"cmd":"ask","who":"priya@acme\.test"/);
  assert.match(json, /"cmd":"details","who":"priya@acme\.test"/);

  // Asked: the same message is edited in place, and she moves to "no reply yet".
  await b.engine.handleMessage(b.id(CTO), "ask priya");
  assert.equal(b.edits.at(-1)!.id, "msg-1");
  assert.match(b.edits.at(-1)!.json, /Asked, no reply yet \(1\)/);

  // She reports a blocker: she moves to the top group and the blocker text shows.
  b.brain.nextNote = {
    note: { summary: "Portal down.", progressVsTargets: "", blockers: ["Client portal is down"], painPoints: [], helpNeeded: [], followUpTomorrow: [] },
    items: [],
  };
  await b.engine.handleMessage(b.id(PRIYA), "portal is down, could not submit");
  json = b.edits.at(-1)!.json;
  assert.match(json, /Has a blocker \(1\)/);
  assert.match(json, /Client portal is down/);
});

test("the board is not edited when nothing has changed", async () => {
  const b = boardSetup();
  await b.engine.handleMessage(b.id(CTO), "board");
  await b.engine.tick();
  await b.engine.tick();
  assert.equal(b.edits.length, 0);
});

test("a handover puts the person in 'needs you personally' with what they wrote", async () => {
  const b = boardSetup();
  await b.engine.handleMessage(b.id(CTO), `add ${PRIYA}`);
  await b.engine.handleInstall(b.id(PRIYA));
  await b.engine.handleMessage(b.id(CTO), "board");
  await b.engine.handleMessage(b.id(CTO), "ask priya");
  b.brain.handoverNext = true;
  await b.engine.handleMessage(b.id(PRIYA), "can I take leave on friday");
  assert.match(b.edits.at(-1)!.json, /Needs you personally \(1\)[\s\S]*can I take leave on friday/);
});

// ---------- learning the sender's tone from past chat ----------

function rapportSetup(history: { fromSender: boolean; text: string }[]) {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  let reads = 0;
  const engine = new Engine(
    store,
    brain,
    { send: async () => undefined },
    { ...config, discloseAutomation: false },
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
    {
      senderName: "Sujith",
      connect: async (email) => ({ conversationId: `chat-${email}`, name: "Priya Rao" }),
      send: async () => undefined,
      history: async () => {
        reads++;
        return history;
      },
    },
  );
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  return { store, brain, engine, cto, reads: () => reads };
}

test("past chat is read once, the tone is stored, and it is reused on later days", async () => {
  const r = rapportSetup([
    { fromSender: true, text: "pls share the list" },
    { fromSender: false, text: "ok sir" },
    { fromSender: true, text: "any update on globex" },
    { fromSender: true, text: "call me when free" },
  ]);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.reads(), 1);
  assert.equal(r.brain.turns.at(-1)!.rapport, "Casual. Says 'pls share'.");

  const priya = r.store.personByEmail(PRIYA)!;
  await r.engine.handleDirectReply(priya.id, "done");
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.reads(), 1); // not read again
  assert.equal(r.brain.rapportCalls.length, 1);
});

test("the assistant's own earlier messages are left out of what it learns from", async () => {
  const r = rapportSetup([
    { fromSender: true, text: "pls share the list" },
    { fromSender: true, text: "any update on globex" },
    { fromSender: true, text: "call me when free" },
    { fromSender: true, text: "Thanks, that's all." }, // what the assistant sent on an earlier day
  ]);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  const priya = r.store.personByEmail(PRIYA)!;
  const earlier = r.store.openCheckin(priya.id, "2026-11-13");
  r.store.addMessage(earlier.id, "bot", "Thanks, that's all.");
  r.store.setCheckinStatus(earlier.id, "completed");

  await r.engine.handleMessage(r.cto, "ask priya");
  assert.deepEqual(r.brain.rapportCalls[0]!.map((m) => m.text), ["pls share the list", "any update on globex", "call me when free"]);
});

test("with too little past chat nothing is learned, and it does not keep looking", async () => {
  const r = rapportSetup([{ fromSender: false, text: "hello sir" }]);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "ask priya");
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.rapportCalls.length, 0);
  assert.equal(r.reads(), 1);
  assert.equal(r.brain.turns.at(-1)!.rapport, undefined);
});

// ---------- a greeting of their own for each person ----------

const SOME_CHAT = [
  { fromSender: true, text: "Hi Anna" },
  { fromSender: true, text: "pls share the list" },
  { fromSender: true, text: "any update on globex" },
];

test("the usual form of address is learned from past chat and used as that person's greeting", async () => {
  const r = rapportSetup(SOME_CHAT);
  r.brain.learnedAddress = "Anna";
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.turns.at(-1)!.greeting, "Hi Anna");
});

test("with nothing learned the greeting is a plain Hi, and each person's is separate", async () => {
  const r = rapportSetup(SOME_CHAT);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "add ravi@acme.test");
  await r.engine.handleMessage(r.cto, "greeting ravi Hi bro");
  await r.engine.handleMessage(r.cto, "ask all");
  const byEmail = new Map(r.brain.turns.map((t) => [t.person.email, t.greeting]));
  assert.equal(byEmail.get(PRIYA), undefined);
  assert.equal(byEmail.get("ravi@acme.test"), "Hi bro");
});

test("a greeting set by hand is kept when the chat is read, and 'off' means plain Hi for good", async () => {
  const r = rapportSetup(SOME_CHAT);
  r.brain.learnedAddress = "Anna";
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "greeting priya off");
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.turns.at(-1)!.greeting, undefined);
});
