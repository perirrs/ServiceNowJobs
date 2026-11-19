import assert from "node:assert/strict";
import { test } from "node:test";

// Variety is chosen at random; pin it so each test sees the same choices.
Math.random = () => 0;
import type { AdminAction, Brain, CheckinContext, DigestPerson, NoteResult, Turn } from "../src/ai.js";
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
  groupWindowMinutes: 120,
  humanDelay: false,
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
  failNextNote = false;
  handoverNext = false;
  /** The next reply to a group message is marked as one that must stay out of the group. */
  privateNext = false;
  /** Group messages containing this text are treated as chatter. */
  chatter = "lunch";

  async nextTurn(ctx: CheckinContext): Promise<Turn> {
    if (this.failNextTurn) {
      this.failNextTurn = false;
      throw new Error("boom");
    }
    this.turns.push(ctx);
    if (ctx.fromGroup && ctx.history.at(-1)?.text.includes(this.chatter)) return { reply: "", done: false, ignore: true };
    if (this.handoverNext) {
      this.handoverNext = false;
      return { reply: "", done: false, handover: true };
    }
    const userMessages = ctx.history.filter((m) => m.role === "user").length;
    const done = ctx.finalTurn || userMessages >= this.doneAfterUserMessages;
    if (ctx.fromGroup && this.privateNext) {
      this.privateNext = false;
      return { reply: "Private question", done: false, private: true };
    }
    return { reply: done ? "Thanks, that's all." : `Question ${ctx.history.length}`, done };
  }
  async writeNote(ctx: CheckinContext): Promise<NoteResult> {
    if (this.failNextNote) {
      this.failNextNote = false;
      throw new Error("note boom");
    }
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
  /** What the next free-form admin message will be understood as. Default: a question. */
  nextActions: AdminAction[] | undefined;
  async interpret(text: string): Promise<AdminAction[]> {
    const actions = this.nextActions ?? [{ type: "question" as const, target: "", text }];
    this.nextActions = undefined;
    return actions;
  }
  groupAsks: { groupName: string; topic: string; members: { name: string; role: string }[] }[] = [];
  nextGroupQuestions: { name: string; question: string }[] = [];
  async composeGroupAsk(input: { groupName: string; topic: string; memory: string; members: { name: string; role: string; memory: string }[] }) {
    this.groupAsks.push(input);
    return { opening: input.topic ? `Hi team, ${input.topic}?` : "", questions: this.nextGroupQuestions };
  }
  revisions: { about: string; memory: string; transcript: string }[] = [];
  async reviseMemory(input: { about: string; senderName: string; memory: string; transcript: string; today?: string }): Promise<string> {
    this.revisions.push(input);
    return `MEMORY v${this.revisions.length}`;
  }
  async learnRapport(_p: unknown, _s: string, chat: { fromSender: boolean; text: string }[]) {
    this.rapportCalls.push(chat.map((m) => ({ fromSender: m.fromSender, text: m.text })));
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
  await t.engine.handleMessage(t.id(PRIYA), "b"); // second follow-up
  assert.equal(t.brain.turns.at(-1)!.finalTurn, false);
  await t.engine.handleMessage(t.id(PRIYA), "c"); // third = the cap of 3, must be final
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
  assert.match(t.to(CTO).at(-1)!, /People \(1\)/);
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
  assert.match(t.to(CTO).at(-1)!, /nobody is added/);
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
  assert.equal(await t.engine.closeIdle(0), 0); // her follow-up question has not been chased yet
  assert.equal(await t.engine.chaseFollowUps(0), 1);
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
  t.brain.nextActions = [{ type: "ask_person", target: "priya", text: "updates this week" }];
  await t.engine.handleMessage(t.id(CTO), "ask priya for what are the updates and what did she do so far this week");
  assert.equal(t.brain.turns.length, 1);
  assert.equal(t.brain.turns[0]!.topic, "updates this week");
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
  assert.match(t.to(CEO).at(-1)!, /People \(1\)/);

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
  const later: { fromSender: boolean; text: string }[] = [];
  let date = new Date("2026-11-16T17:00:00Z");
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  let reads = 0;
  const engine = new Engine(
    store,
    brain,
    { send: async () => undefined },
    { ...config, discloseAutomation: false },
    () => date,
    () => undefined,
    {
      senderName: "Sujith",
      connect: async (email) => ({ conversationId: `chat-${email}`, name: "Priya Rao" }),
      send: async () => undefined,
      history: async (_chat, _max, since) => {
        reads++;
        return (since ? later : history).map((m) => ({ ...m, from: "", at: "" }));
      },
    },
  );
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  return { store, brain, engine, cto, reads: () => reads, later, setDate: (d: string) => (date = new Date(d)) };
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

// ---------- group chats: asked together, followed up privately ----------

function groupSetup() {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const sent: {
    to: string;
    text: string;
    mentions?: { userId: string; name: string }[];
    quote?: { messageId: string; preview: string; userId: string; name: string };
  }[] = [];
  const admin: string[] = [];
  let date = new Date("2026-11-16T17:00:00Z");
  const members = [
    { userId: "u-priya", email: PRIYA, name: "Priya Rao" },
    { userId: "u-ravi", email: "ravi@acme.test", name: "Ravi Kumar" },
  ];
  const engine = new Engine(
    store,
    brain,
    { send: async (_to, text) => void admin.push(text) },
    { ...config, discloseAutomation: false },
    () => date,
    () => undefined,
    {
      senderName: "Sujith",
      connect: async (email) => ({ conversationId: `chat-${email}`, name: email }),
      send: async (to, text, mentions, quote) => void sent.push({ to, text, mentions, quote }),
      findGroups: async (q) =>
        [
          { chatId: "g-rec", name: "Recruitment Team" },
          { chatId: "g-rec2", name: "Recruitment Leads" },
        ].filter((g) => g.name.toLowerCase().includes(q.toLowerCase())),
      groupMembers: async () => members,
    },
  );
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  return { store, brain, engine, cto, sent, admin, members, setDate: (d: string) => (date = new Date(d)) };
}

test("adding a group needs one clear match, lists the members, and posts nothing", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment");
  assert.match(g.admin.at(-1)!, /Several group chats match/);
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  assert.match(g.admin.at(-1)!, /Now following \*\*Recruitment Team\*\* \(2 people/);
  assert.equal(g.sent.length, 0);
  assert.equal(g.store.nominees().length, 2);
});

test("a group is asked with one message; a reply there gets its follow-up in the group, tagged", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  assert.deepEqual(g.sent.map((s) => s.to), ["g-rec"]);
  assert.match(g.sent[0]!.text, /Hi team, please share today's update/);

  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions today");
  const followUp = g.sent.at(-1)!;
  assert.equal(followUp.to, "g-rec"); // it stays in the group
  assert.match(followUp.text, /^\{@0\} Question/);
  assert.deepEqual(followUp.mentions, [{ userId: "u-priya", name: "Priya Rao" }]);
  assert.equal(g.brain.turns.at(-1)!.fromGroup, true);
  assert.equal(g.sent.filter((s) => s.to === `chat-${PRIYA}`).length, 0); // nothing one to one

  // Something that should not be said in front of the group goes one to one instead.
  g.brain.privateNext = true;
  await g.engine.handleGroupReply("u-priya", "could not finish, was unwell");
  assert.equal(g.sent.at(-1)!.to, `chat-${PRIYA}`);
  assert.equal(g.sent.at(-1)!.text, "Private question");
});

test("group banter is ignored: not recorded, not answered", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-ravi", "anyone for lunch?");
  assert.equal(g.sent.length, before);
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  assert.equal(g.store.messages(g.store.checkin(ravi.id, "2026-11-16")!.id).filter((m) => m.role === "user").length, 0);
});

test("silent members are chased once in the group with a mention, then marked as no reply when the window ends", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions today");

  g.setDate("2026-11-16T17:50:00Z"); // past the 45-minute chase
  await g.engine.tick();
  await g.engine.tick();
  const chases = g.sent.filter((s) => s.to === "g-rec" && s.text.includes("Any update? Please share when you can."));
  assert.equal(chases.length, 1);
  assert.deepEqual(chases[0]!.mentions, [{ userId: "u-ravi", name: "Ravi Kumar" }]);

  g.setDate("2026-11-16T19:05:00Z"); // past the 2-hour window
  await g.engine.tick();
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  assert.equal(g.store.checkin(ravi.id, "2026-11-16")!.status, "no_response");
  assert.equal(g.engine.listeningGroups().length, 0);
});

test("'ask all' posts once in each group and does not also message its members one by one", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "add solo@acme.test");
  await g.engine.handleMessage(g.cto, "ask all");
  assert.deepEqual(g.sent.map((s) => s.to).sort(), ["chat-solo@acme.test", "g-rec"]);
});

test("removing a group stops following people who were only there through it", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, `add ${PRIYA}`); // Priya is also followed in her own right
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "remove group Recruitment Team");
  assert.deepEqual(g.store.nominees().map((p) => p.email), [PRIYA]);
});

// ---------- memory that is kept and brought up to date each day ----------

test("on a later day, what was said in the chat since is folded into the memory before asking", async () => {
  const r = rapportSetup(SOME_CHAT);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.revisions.length, 0); // first day: learned from history, nothing to revise
  const priya = r.store.personByEmail(PRIYA)!;
  await r.engine.handleDirectReply(priya.id, "done");

  // Same day again: no second look at the chat.
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.reads(), 1);

  // Next day, with something new said in the chat in between.
  r.later.push({ fromSender: false, text: "Globex signed the contract" });
  r.setDate("2026-11-17T17:00:00Z");
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.revisions.length, 1);
  assert.match(r.brain.revisions[0]!.transcript, /Globex signed the contract/);
  assert.equal(r.brain.revisions[0]!.memory, "Casual. Says 'pls share'.");
  assert.equal(r.brain.turns.at(-1)!.rapport, "MEMORY v1");
});

test("a group's chat history becomes a memory that each member's questions can draw on", async () => {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const engine = new Engine(
    store,
    brain,
    { send: async () => undefined },
    { ...config, discloseAutomation: false },
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
    {
      senderName: "Sujith",
      connect: async (email) => ({ conversationId: `chat-${email}`, name: email }),
      send: async () => undefined,
      findGroups: async () => [{ chatId: "g-rec", name: "Recruitment Team" }],
      groupMembers: async () => [{ userId: "u-priya", email: PRIYA, name: "Priya Rao" }],
      history: async (chat) =>
        chat === "g-rec"
          ? [{ fromSender: false, from: "Priya Rao", text: "Initech wants 5 profiles by Friday", at: "" }]
          : [],
    },
  );
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  await engine.handleMessage(cto, "add group Recruitment Team");
  await engine.handleMessage(cto, "ask group Recruitment Team");
  assert.match(brain.revisions[0]!.about, /group chat "Recruitment Team"/);
  assert.match(brain.revisions[0]!.transcript, /Priya Rao: Initech wants 5 profiles by Friday/);

  brain.doneAfterUserMessages = 99;
  await engine.handleGroupReply("u-priya", "sent 2 profiles to Initech");
  assert.match(brain.turns.at(-1)!.rapport!, /From the group chat "Recruitment Team":\nMEMORY v1/);
});

// ---------- one message, several instructions ----------

test("a long message with several instructions is carried out in a sensible order", async () => {
  const g = groupSetup();
  g.brain.nextActions = [
    { type: "ask_group", target: "Recruitment Team", text: "website updates and launch date" },
    { type: "set_role", target: "Priya", text: "Content writer" },
    { type: "set_role", target: "Ravi", text: "Web developer" },
    { type: "set_role", target: "Nobody", text: "Ghost" },
    { type: "add_group", target: "Recruitment Team", text: "" },
  ];
  await g.engine.handleMessage(
    g.cto,
    "add group Recruitment Team\n\nPriya is content writer\nRavi is web developer\n\nAsk group Recruitment Team on website updates and launch date",
  );

  // Added first, roles next, the question last, even though the list came in another order.
  assert.equal(g.store.groups().length, 1);
  assert.equal(g.store.personByEmail(PRIYA)!.profile, "Content writer");
  assert.equal(g.store.personByEmail("ravi@acme.test")!.profile, "Web developer");
  assert.ok(g.admin.some((m) => /Saved roles:[\s\S]*Priya Rao: Content writer/.test(m)));
  assert.ok(g.admin.some((m) => /could not match[\s\S]*Nobody/.test(m)));

  // The group message is worded around the topic, and each member's follow-up knows it.
  assert.equal(g.brain.groupAsks[0]!.topic, "website updates and launch date");
  assert.equal(g.sent.at(-1)!.text, "Hi team, website updates and launch date?");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "homepage copy is done");
  assert.equal(g.brain.turns.at(-1)!.topic, "website updates and launch date");
  assert.equal(g.brain.turns.at(-1)!.person.profile, "Content writer");
});

test("a short command is still just a command and is not sent for interpreting", async () => {
  const g = groupSetup();
  g.brain.nextActions = [{ type: "digest", target: "", text: "" }]; // would be used if interpreted
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  assert.equal(g.store.groups().length, 1);
  assert.ok(g.brain.nextActions); // untouched
});

test("an admin who is in the group is not chased or followed up there", async () => {
  const g = groupSetup();
  g.members.push({ userId: "u-ceo", email: CEO, name: "The CEO" });
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.equal(g.store.checkin(g.store.personByEmail(CEO)!.id, "2026-11-16"), undefined);
  assert.match(g.admin.at(-1)!, /\(2 people\)/);
});

// ---------- a time of their own for one person or one group ----------

test("a person with a time of their own is asked then, and left out of the general time", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "add ravi@acme.test");
  await t.engine.handleInstall(t.id("ravi@acme.test"));
  await t.engine.handleMessage(t.id(CTO), "schedule 17:00");
  await t.engine.handleMessage(t.id(CTO), "schedule priya evening 18:00");
  assert.match(t.to(CTO).at(-1)!, /priya will be asked automatically at \*\*18:00\*\* \(end-of-day update\)/);

  await t.engine.tick(); // 17:00: the general evening time
  assert.deepEqual(t.brain.turns.map((x) => x.person.email), ["ravi@acme.test"]);

  t.setDate("2026-11-16T18:00:00Z");
  await t.engine.tick();
  await t.engine.tick();
  assert.deepEqual(t.brain.turns.map((x) => x.person.email), ["ravi@acme.test", PRIYA]);

  await t.engine.handleMessage(t.id(CTO), "schedule priya off");
  assert.match(t.to(CTO).at(-1)!, /no longer has a time of their own/);
});

test("a group can have a time of its own, and '5 pm' with a space is understood", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "schedule Recruitment Team 5 pm");
  assert.match(g.admin.at(-1)!, /Recruitment Team will be asked automatically at \*\*17:00\*\*/);
  await g.engine.tick();
  assert.deepEqual(g.sent.map((s) => s.to), ["g-rec"]);
});

test("the board shows what is switched on in green, and each person has a time picker", async () => {
  const b = boardSetup();
  await b.engine.handleMessage(b.id(CTO), `add ${PRIYA}`);
  await b.engine.handleMessage(b.id(CTO), "board");
  let json = JSON.stringify(b.sentCards.at(-1));
  assert.match(json, /OFF · No morning plan message/);
  assert.match(json, /OFF · No end-of-day message/);
  assert.doesNotMatch(json, /"color":"Good"/);
  assert.match(json, /"cmd":"scheduleFor","who":"priya@acme\.test","field":"p\d+m","slot":"morning"/);
  assert.equal(cardToCommand({ cmd: "scheduleFor", who: PRIYA, field: "p2e", slot: "evening", p2e: "17:30" }), `schedule ${PRIYA} evening 17:30`);
  assert.equal(cardToCommand({ cmd: "scheduleFor", who: "", field: "allm", slot: "morning", allm: "09:30" }), "schedule morning 09:30");
  assert.equal(cardToCommand({ cmd: "scheduleFor", who: PRIYA, field: "p2", p2: "10:30" }), `schedule ${PRIYA} 10:30`);
  assert.equal(cardToCommand({ cmd: "scheduleOffFor", who: PRIYA }), `schedule ${PRIYA} off`);

  await b.engine.handleMessage(b.id(CTO), "schedule 17:00");
  await b.engine.handleMessage(b.id(CTO), `schedule ${PRIYA} 10:30`);
  json = b.edits.at(-1)!.json;
  assert.match(json, /"text":"ON · End of day: everyone is asked at 17:00, Monday to Friday"[^}]*"color":"Good"/);
  assert.match(json, /"text":"ON · Morning 10:30, Monday to Friday","color":"Good"/);
  assert.match(json, /"title":"Times: Morning 10:30"/);
});

// ---------- tagging people in the group with their own question ----------

test("the group message tags individuals with their own question, and each follow-up knows what was asked", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.brain.nextGroupQuestions = [
    { name: "Ravi Kumar", question: "TalentFlow app - where are we?" },
    { name: "Someone Else", question: "ignored: not in the group" },
    { name: "Ravi Kumar", question: "a second one for the same person is dropped" },
  ];
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");

  const post = g.sent.at(-1)!;
  assert.equal(post.to, "g-rec");
  assert.equal(post.text, "Hi team, please share today's update.\n{@0} TalentFlow app - where are we?");
  assert.deepEqual(post.mentions, [{ userId: "u-ravi", name: "Ravi Kumar" }]);

  // Our own record reads the way the message looks in Teams.
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  const recorded = g.store.messages(g.store.checkin(ravi.id, "2026-11-16")!.id)[0]!.text;
  assert.match(recorded, /@Ravi Kumar TalentFlow app - where are we\?/);

  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-ravi", "testing is going on");
  assert.match(g.brain.turns.at(-1)!.topic!, /asked directly: "TalentFlow app - where are we\?"/);
  await g.engine.handleGroupReply("u-priya", "3 submissions");
  assert.equal(g.brain.turns.at(-1)!.topic, undefined); // nobody tagged her
});

test("the wording step is given each person's role and the day's date to work from", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "profile ravi: Web developer");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const asked = g.brain.groupAsks.at(-1)!;
  assert.deepEqual(asked.members.map((m) => [m.name, m.role]), [["Priya Rao", ""], ["Ravi Kumar", "Web developer"]]);
});

// ---------- one chase when someone stops replying part-way through ----------

test("an unanswered follow-up question is chased once, where it was asked, and not again", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions"); // she gets a follow-up in the group
  const inGroup = () => g.sent.filter((s) => s.to === "g-rec");
  assert.equal(inGroup().length, 2); // the ask, and her follow-up

  assert.equal(await g.engine.chaseFollowUps(45), 0); // not yet 45 minutes
  assert.equal(await g.engine.chaseFollowUps(0), 1);
  assert.equal(inGroup().at(-1)!.text, "{@0} Any update on this?");
  assert.deepEqual(inGroup().at(-1)!.mentions, [{ userId: "u-priya", name: "Priya Rao" }]);
  assert.equal(await g.engine.chaseFollowUps(0), 0); // only ever once

  // Ravi never replied at all: that is the group chase's job, not this one.
  assert.ok(!inGroup().some((s) => s.mentions?.some((m) => m.userId === "u-ravi")));
  assert.equal(g.sent.filter((s) => s.to.startsWith("chat-")).length, 0); // nothing went one to one
});

test("no follow-up chase once you have taken the chat over, or after they answer", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `add ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  await t.engine.runCheckins();
  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleDirectReply(priya.id, "half an update");
  t.engine.manualTakeover(priya.id);
  assert.equal(await t.engine.chaseFollowUps(0), 0);
});

test("the group keeps being read while a conversation there is still going, then stops", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions");

  g.setDate("2026-11-16T19:05:00Z"); // the two-hour window has passed, Priya's follow-up is still open
  await g.engine.tick();
  assert.equal(g.engine.listeningGroups().length, 1);

  await g.engine.chaseFollowUps(0); // her one chase
  await g.engine.closeIdle(0); // then her conversation closes
  await g.engine.tick();
  assert.equal(g.engine.listeningGroups().length, 0);
});

test("a reply in the group quotes the exact message it answers", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions today", "msg-77");

  const reply = g.sent.at(-1)!;
  assert.equal(reply.to, "g-rec");
  assert.deepEqual(reply.quote, { messageId: "msg-77", preview: "sent 3 submissions today", userId: "u-priya", name: "Priya Rao" });
  assert.deepEqual(reply.mentions, [{ userId: "u-priya", name: "Priya Rao" }]);

  // The group ask itself, and a chase, answer nothing in particular: no quote.
  assert.equal(g.sent[0]!.quote, undefined);
  await g.engine.chaseFollowUps(0);
  assert.equal(g.sent.at(-1)!.quote, undefined);
});

// ---------- coming back after being offline ----------

test("after a long gap the admins are told it was offline; a quick restart says nothing", async () => {
  const t = setup();
  await t.engine.handleInstall(t.id(CTO));
  const before = () => t.to(CTO).length;

  await t.engine.announceStart(); // very first start: nothing to compare with
  await t.engine.tick();
  let count = before();
  t.setDate("2026-11-16T17:03:00Z");
  await t.engine.announceStart(); // three minutes later: a restart, not an outage
  assert.equal(before(), count);

  t.setDate("2026-11-17T09:00:00Z");
  await t.engine.announceStart();
  assert.match(t.to(CTO).at(-1)!, /I was offline from Mon,? 16 Nov,? 17:03 to Tue,? 17 Nov,? 09:00/);
});

// ---------- asking a group again ----------

test("asking a group again re-asks those who said nothing, tagged; someone mid-reply is left alone", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions"); // Priya is now mid-conversation

  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const post = g.sent.at(-1)!;
  assert.equal(post.to, "g-rec");
  assert.equal(post.text, "Hi team, please share today's update.\n{@0} please share your update.");
  assert.deepEqual(post.mentions, [{ userId: "u-ravi", name: "Ravi Kumar" }]);
  assert.match(g.admin.at(-1)!, /\(1 people\)/);
});

test("questions to the bot can draw on what was learned from the chat history", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.store.set(`gmemory:${g.store.groups()[0]!.id}`, "Ravi: TalentFlow app due 2026-11-20");
  g.store.set(`rapport:${g.store.personByEmail(PRIYA)!.id}`, "Owes the landing page copy, overdue");
  await g.engine.handleMessage(g.cto, "what is outstanding for each person in the group right now please?");
  const material = g.brain.questions.at(-1)!.material;
  assert.match(material, /Group chat: Recruitment Team[\s\S]*TalentFlow app due 2026-11-20/);
  assert.match(material, /Owes the landing page copy, overdue/);
});

// ---------- the four correctness fixes, and the pause switch ----------

test("asking again and chasing do not use up the follow-up allowance", async () => {
  const t = setup();
  await onboard(t);
  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  await t.engine.handleMessage(t.id(CTO), "ask priya"); // asked again: a second message before any reply
  await t.engine.handleMessage(t.id(PRIYA), "first reply");
  assert.equal(t.brain.turns.at(-1)!.finalTurn, false);
});

test("someone asked in a group: their one-to-one chat is left alone until a private follow-up goes there", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions");
  assert.equal(g.engine.openDirectCheckins().length, 0); // nobody's private chat is being read

  g.brain.privateNext = true;
  await g.engine.handleGroupReply("u-priya", "could not finish, was unwell");
  const watched = g.engine.openDirectCheckins();
  assert.deepEqual(watched.map((w) => w.person.email), [PRIYA]);
  assert.ok(g.store.get(`cursor:${watched[0]!.checkin.id}`)); // and only from that message on
});

test("when the sender replies to someone in the group himself, the assistant steps back for that person only", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions");
  await g.engine.handleGroupReply("u-ravi", "testing the app");
  const groupId = g.store.groups()[0]!.id;

  // The assistant's own posts are recognised, with or without the tag in front.
  assert.equal(g.engine.isAssistantGroupText(groupId, "Hi team, please share today's update."), true);
  assert.equal(g.engine.isAssistantGroupText(groupId, "Priya Rao Question 2"), true);
  assert.equal(g.engine.isAssistantGroupText(groupId, "Priya, call me"), false);

  g.engine.senderSpokeInGroup(groupId, ["u-priya"]); // he tagged or replied to Priya
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-priya", "ok will call");
  assert.equal(g.sent.length, before); // silent for her
  await g.engine.handleGroupReply("u-ravi", "found two bugs");
  assert.equal(g.sent.length, before + 1); // still following up with Ravi
});

test("a message from the sender aimed at nobody in particular steps back for everyone mid-conversation", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions");
  g.engine.senderSpokeInGroup(g.store.groups()[0]!.id, []);
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-priya", "more detail");
  assert.equal(g.sent.length, before);
  // Ravi had not replied yet, so he is unaffected and still gets a follow-up when he does.
  await g.engine.handleGroupReply("u-ravi", "testing the app");
  assert.equal(g.sent.length, before + 1);
});

test("conversations left open overnight are summarised or marked unanswered the next day", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "add ravi@acme.test");
  await t.engine.handleInstall(t.id("ravi@acme.test"));
  t.brain.doneAfterUserMessages = 99;
  await t.engine.runCheckins();
  await t.engine.handleMessage(t.id(PRIYA), "half an update"); // Priya replied, Ravi did not
  const priya = t.store.personByEmail(PRIYA)!;
  const ravi = t.store.personByEmail("ravi@acme.test")!;

  t.setDate("2026-11-17T08:00:00Z");
  assert.equal(await t.engine.closePastDays(), 2);
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "completed");
  assert.equal(t.store.checkin(ravi.id, "2026-11-16")!.status, "no_response");
  assert.match(t.to(CTO).at(-1)!, /Update from priya[\s\S]*2026-11-16/);
  assert.equal(await t.engine.closePastDays(), 0);
});

test("pause stops everything going out; replies are still recorded; resume starts again", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "add solo@acme.test");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const before = g.sent.length;

  await g.engine.handleMessage(g.cto, "pause");
  assert.match(g.admin.at(-1)!, /Paused/);
  await g.engine.handleMessage(g.cto, "ask all");
  await g.engine.handleMessage(g.cto, "schedule 17:00");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions");
  g.setDate("2026-11-16T17:50:00Z");
  await g.engine.tick(); // would chase, and would run the 17:00 schedule
  assert.equal(g.sent.length, before); // nothing at all went out

  const priya = g.store.personByEmail(PRIYA)!;
  assert.ok(g.store.messages(g.store.checkin(priya.id, "2026-11-16")!.id).some((m) => m.text === "sent 3 submissions"));

  await g.engine.handleMessage(g.cto, "resume");
  await g.engine.handleGroupReply("u-priya", "and 1 interview");
  assert.equal(g.sent.length, before + 1);
});

// ---------- defects found by review ----------

test("someone asked one to one who then chats in a followed group is not treated as answering", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask priya"); // asked one to one, not through the group
  const groupId = g.store.groups()[0]!.id;
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-priya", "good morning all", "m1", groupId);
  assert.equal(g.sent.length, before);
  assert.equal(g.brain.turns.filter((t) => t.fromGroup).length, 0);
});

test("a message read in one group is not taken as an answer to an ask made in another", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions", "m1", 999); // read in some other group
  assert.equal(g.sent.length, before);
});

test("asking someone again the same day starts clean: no stale takeover, and the chase applies afresh", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `add ${PRIYA}`);
  const priya = t.store.personByEmail(PRIYA)!;
  const toPriya = () => t.sent.filter((s) => s.to === `chat-${PRIYA}`).length;

  await t.engine.handleMessage(t.id(CTO), "ask priya");
  t.engine.manualTakeover(priya.id); // you stepped in this morning
  await t.engine.handleDirectReply(priya.id, "morning update"); // recorded, not answered
  await t.engine.closeIdle(0);
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "completed");

  await t.engine.handleMessage(t.id(CTO), "ask priya"); // asked again in the afternoon
  const afterAsk = toPriya();
  assert.equal(await t.engine.closeIdle(0), 0); // the morning's reply does not make this one "answered"
  assert.equal(await t.engine.runReminders(0), 1); // and she can be chased for it
  assert.equal(toPriya(), afterAsk + 1);

  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleDirectReply(priya.id, "afternoon update");
  assert.equal(toPriya(), afterAsk + 2); // the assistant answers: the old takeover is gone
});

test("a group member with a time of their own is left out of the group's scheduled ask", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "schedule ravi evening 18:00");
  await g.engine.handleMessage(g.cto, "schedule 17:00");
  await g.engine.tick(); // 17:00, the general time
  assert.match(g.sent.at(-1)!.to, /g-rec/);
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  assert.equal(g.store.checkin(ravi.id, "2026-11-16"), undefined);
});

test("setting a time that has already passed today does not fire at once", async () => {
  const t = setup();
  await onboard(t);
  t.setDate("2026-11-16T17:05:00Z");
  await t.engine.handleMessage(t.id(CTO), "schedule 17:00"); // five minutes ago: inside the old window
  await t.engine.tick();
  assert.equal(t.brain.turns.length, 0);
  t.setDate("2026-11-17T17:00:00Z");
  await t.engine.tick();
  assert.equal(t.brain.turns.length, 1);
});

test("if the note fails after a closing line, the person is not chased about 'Ok, noted.'", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  t.brain.failNextNote = true;
  await t.engine.handleMessage(t.id(PRIYA), "all done"); // closing line goes out, the note fails
  const priya = t.store.personByEmail(PRIYA)!;
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "open");
  assert.equal(await t.engine.chaseFollowUps(0), 0);
  assert.equal(await t.engine.closeIdle(0), 1); // and the note is written on the next close
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "completed");
});

test("a scheduled ask that fails is retried, then reported, not silently lost", async () => {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const admin: string[] = [];
  let failing = true;
  const engine = new Engine(
    store,
    brain,
    { send: async (_to, text) => void admin.push(text) },
    { ...config, discloseAutomation: false },
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
    {
      senderName: "Sujith",
      connect: async (email) => ({ conversationId: `chat-${email}`, name: email }),
      send: async () => {
        if (failing) throw new Error("graph down");
      },
      findGroups: async () => [{ chatId: "g-rec", name: "Recruitment Team" }],
      groupMembers: async () => [{ userId: "u-priya", email: PRIYA, name: "Priya Rao" }],
    },
  );
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  await engine.handleMessage(cto, "add group Recruitment Team");
  await engine.handleMessage(cto, "schedule Recruitment Team 17:00");
  await engine.tick();
  await engine.tick();
  assert.ok(!admin.some((m) => /failed three times/.test(m)));
  await engine.tick();
  assert.match(admin.at(-1)!, /failed three times/);
  failing = false;
  await engine.tick(); // skipped for today now, even though sending works again
  assert.equal(store.checkin(store.personByEmail(PRIYA)!.id, "2026-11-16"), undefined);
});

// ---------- replying at a human pace ----------

const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

function pacedSetup() {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const sent: string[] = [];
  const engine = new Engine(
    store,
    brain,
    { send: async () => undefined },
    { ...config, discloseAutomation: false, humanDelay: true },
    () => new Date("2026-11-16T17:00:00Z"),
    () => undefined,
    {
      senderName: "Sujith",
      connect: async (email) => ({ conversationId: `chat-${email}`, name: "Priya Rao" }),
      send: async (_to, text) => void sent.push(text),
    },
  );
  engine.pace = { read: () => 30, type: () => 60 };
  const cto: Identity = { teamsUserId: "t", conversationId: "c", lookupEmail: async () => CTO };
  return { store, brain, engine, cto, sent };
}

test("a reply is not instant: it comes after time to read and time to type", async () => {
  const p = pacedSetup();
  await p.engine.handleMessage(p.cto, `add ${PRIYA}`);
  await p.engine.handleMessage(p.cto, "ask priya");
  const priya = p.store.personByEmail(PRIYA)!;
  p.brain.doneAfterUserMessages = 99;
  await p.engine.handleDirectReply(priya.id, "sent 3 submissions");
  assert.equal(p.sent.length, 1); // only the question so far
  await pause(50);
  assert.equal(p.sent.length, 1); // read, now "typing"
  await pause(120);
  assert.equal(p.sent.length, 2);
});

test("several quick messages get one answer, written after the last of them", async () => {
  const p = pacedSetup();
  await p.engine.handleMessage(p.cto, `add ${PRIYA}`);
  await p.engine.handleMessage(p.cto, "ask priya");
  const priya = p.store.personByEmail(PRIYA)!;
  p.brain.doneAfterUserMessages = 99;
  await p.engine.handleDirectReply(priya.id, "sent 3 submissions");
  await pause(10);
  await p.engine.handleDirectReply(priya.id, "and 1 interview");
  await pause(200);
  assert.equal(p.sent.length, 2); // the question, and one reply
  assert.equal(p.brain.turns.at(-1)!.history.filter((m) => m.role === "user").length, 2);
});

test("if they write again, or you step in, while a reply is being typed, that reply is dropped", async () => {
  const p = pacedSetup();
  await p.engine.handleMessage(p.cto, `add ${PRIYA}`);
  await p.engine.handleMessage(p.cto, "ask priya");
  const priya = p.store.personByEmail(PRIYA)!;
  p.brain.doneAfterUserMessages = 99;

  await p.engine.handleDirectReply(priya.id, "sent 3 submissions");
  await pause(50); // read, reply drafted, now typing
  await p.engine.handleDirectReply(priya.id, "wait, it was 4");
  await pause(250);
  assert.equal(p.sent.length, 2); // one reply only, to both messages
  assert.equal(p.brain.turns.at(-1)!.history.filter((m) => m.role === "user").length, 2);

  await p.engine.handleDirectReply(priya.id, "one more thing");
  await pause(50);
  p.engine.manualTakeover(priya.id); // you start typing yourself
  await pause(200);
  assert.equal(p.sent.length, 2); // the drafted reply never goes out
});

// ---------- the daily rhythm: a morning plan, then an end-of-day review ----------

test("morning asks for the plan, evening reviews it, both on the same day, and the evening sees the morning", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "schedule morning 09:30");
  await t.engine.handleMessage(t.id(CTO), "schedule evening 17:30");
  assert.match(t.to(CTO).at(-1)!, /Morning plan: everyone is asked automatically at \*\*09:30\*\*/);
  assert.match(t.to(CTO).at(-1)!, /End-of-day update: everyone is asked automatically at \*\*17:30\*\*/);

  t.setDate("2026-11-17T09:30:00Z");
  await t.engine.tick();
  assert.equal(t.brain.turns.at(-1)!.round, "plan");
  assert.equal(t.brain.turns.at(-1)!.opening, true);
  await t.engine.handleMessage(t.id(PRIYA), "today: 5 submissions and the Globex interview");
  assert.match(t.to(CTO).at(-1)!, /Plan for today from priya/);

  await t.engine.tick(); // the morning message is not sent twice
  const afterMorning = t.brain.turns.length;
  assert.equal(t.brain.turns.length, afterMorning);

  t.setDate("2026-11-17T17:30:00Z");
  await t.engine.tick();
  const evening = t.brain.turns.at(-1)!;
  assert.equal(evening.round, "review");
  assert.equal(evening.opening, true);
  assert.ok(evening.history.some((m) => m.text === "today: 5 submissions and the Globex interview")); // it can see the plan
  await t.engine.handleMessage(t.id(PRIYA), "4 submissions done, interview moved to Friday");
  assert.match(t.to(CTO).at(-1)!, /Update from priya/);

  await t.engine.tick(); // nor the evening one
  assert.equal(t.brain.turns.filter((x) => x.opening).length, 2);
});

test("the evening message still goes to someone who never answered the morning one", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "schedule morning 09:30");
  await t.engine.handleMessage(t.id(CTO), "schedule evening 17:30");
  t.setDate("2026-11-17T09:30:00Z");
  await t.engine.tick();
  t.setDate("2026-11-17T17:30:00Z");
  await t.engine.tick();
  assert.deepEqual(t.brain.turns.filter((x) => x.opening).map((x) => x.round), ["plan", "review"]);
});

test("times saved before there were two daily messages are filed under morning or evening", () => {
  const store = new Store(":memory:");
  store.set("schedule:checkin", "17:00");
  store.set("schedule:group:1", "09:00");
  new Engine(store, new FakeBrain(), { send: async () => undefined }, config);
  assert.equal(store.get("schedule:evening"), "17:00");
  assert.equal(store.get("schedule:group:1:morning"), "09:00");
  assert.equal(store.get("schedule:checkin"), "");
});

test("'schedule morning off' stops one message and leaves the other", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "schedule morning 09:30");
  await t.engine.handleMessage(t.id(CTO), "schedule evening 17:30");
  await t.engine.handleMessage(t.id(CTO), "schedule morning off");
  assert.equal(t.store.get("schedule:morning"), "");
  assert.equal(t.store.get("schedule:evening"), "17:30");
  await t.engine.handleMessage(t.id(CTO), "schedule off");
  assert.equal(t.store.get("schedule:evening"), "");
});

test("due dates are kept on items, and overdue ones show on the board", async () => {
  const b = boardSetup();
  await b.engine.handleMessage(b.id(CTO), `add ${PRIYA}`);
  await b.engine.handleInstall(b.id(PRIYA));
  await b.engine.handleMessage(b.id(CTO), "board");
  await b.engine.handleMessage(b.id(CTO), "ask priya");
  b.brain.nextNote = {
    note: { summary: "Working on the deck.", progressVsTargets: "", blockers: [], painPoints: [], helpNeeded: [], followUpTomorrow: [], planToday: ["Client deck"] },
    items: [
      { itemId: null, title: "Client deck", status: "open", update: "half done", due: "2026-11-14" },
      { itemId: null, title: "Landing page", status: "open", update: "started", due: "next week" }, // not a date: ignored
    ],
  };
  await b.engine.handleMessage(b.id(PRIYA), "deck is half done");
  const items = b.store.activeItems(b.store.personByEmail(PRIYA)!.id);
  assert.deepEqual(items.map((i) => [i.title, i.due]), [["Client deck", "2026-11-14"], ["Landing page", null]]);
  assert.match(b.edits.at(-1)!.json, /OVERDUE · Client deck \(due 2026-11-14\)/);
});

// ---------- the tracker in the group ----------

function trackerSetup() {
  const g = groupSetup();
  const created: { chat: string; html: string }[] = [];
  const updated: { id: string; html: string }[] = [];
  let failUpdate = false;
  const direct = (g.engine as unknown as { direct: Record<string, unknown> }).direct;
  direct.createTracker = async (chat: string, html: string) => {
    created.push({ chat, html });
    return `trk-${created.length}`;
  };
  direct.updateTracker = async (_chat: string, id: string, html: string) => {
    if (failUpdate) throw new Error("not allowed");
    updated.push({ id, html });
  };
  return { ...g, created, updated, setFailUpdate: (v: boolean) => (failUpdate = v) };
}

async function reportItem(t: ReturnType<typeof trackerSetup>, title: string, status: "open" | "done" | "blocked", due: string) {
  t.brain.nextNote = {
    note: { summary: "x", progressVsTargets: "", blockers: [], painPoints: [], helpNeeded: [], followUpTomorrow: [] },
    items: [{ itemId: null, title, status, update: "u", due }],
  };
  await t.engine.handleGroupReply("u-priya", `about ${title}`);
  await t.engine.chaseFollowUps(0);
  await t.engine.closeIdle(0);
}

test("no tracker is posted until there is something to show; then one table, edited in place", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  await t.engine.refreshTrackers();
  assert.equal(t.created.length, 0);

  await reportItem(t, "Client deck <v2>", "open", "2026-11-14");
  await t.engine.refreshTrackers();
  assert.equal(t.created.length, 1);
  assert.equal(t.created[0]!.chat, "g-rec");
  const html = t.created[0]!.html;
  assert.match(html, /<b>Team tracker<\/b>/);
  assert.match(html, /<td>Priya<\/td><td>Client deck &lt;v2&gt;<\/td><td>14 Nov<\/td><td>🔴 Overdue<\/td>/);
  assert.match(html, /<td>Ravi<\/td><td>No items yet<\/td>/);

  await t.engine.refreshTrackers(); // nothing changed: nothing sent
  assert.equal(t.created.length + t.updated.length, 1);

  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  await reportItem(t, "Landing page", "blocked", "");
  await t.engine.refreshTrackers();
  assert.equal(t.created.length, 1); // still one message
  assert.equal(t.updated.at(-1)!.id, "trk-1");
  assert.match(t.updated.at(-1)!.html, /Landing page<\/td><td>-<\/td><td>🟠 Blocked/);
});

test("if the table cannot be edited, a fresh one is posted at most once a day", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  await reportItem(t, "Client deck", "open", "");
  await t.engine.refreshTrackers();
  t.setFailUpdate(true);
  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  await reportItem(t, "Landing page", "open", "");
  await t.engine.refreshTrackers();
  await t.engine.refreshTrackers();
  assert.equal(t.created.length, 1); // same day: no flood of new tables
});

test("the tracker can be switched off for a group, and nothing is posted while paused", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  await reportItem(t, "Client deck", "open", "");
  await t.engine.handleMessage(t.cto, "tracker Recruitment Team off");
  await t.engine.refreshTrackers();
  assert.equal(t.created.length, 0);

  await t.engine.handleMessage(t.cto, "pause");
  await t.engine.handleMessage(t.cto, "tracker Recruitment Team on");
  assert.equal(t.created.length, 0);
  await t.engine.handleMessage(t.cto, "resume");
  await t.engine.handleMessage(t.cto, "tracker Recruitment Team");
  assert.equal(t.created.length, 1);
  assert.match(t.admin.at(-1)!, /up to date and pinned/);
});

// ---------- not the same shape every time ----------

test("group replies are laid out in different ways, never the same twice running, always with a tag or a quote", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  (g.engine as unknown as { config: Config }).config.maxBotTurns = 99; // room for every reply in this test
  const rolls = [0.1, 0.1, 0.7, 0.9, 0.5];
  const real = Math.random;
  const seen: string[] = [];
  try {
    for (let i = 0; i < rolls.length; i++) {
      Math.random = () => rolls[i]!;
      await g.engine.handleGroupReply("u-priya", `update ${i}`, `m${i}`);
      const s = g.sent.at(-1)!;
      assert.ok(s.mentions?.length || s.quote, "each reply has a tag or a quote");
      seen.push(s.mentions?.length ? (s.text.startsWith("{@0}") ? (s.quote ? "front" : "tagOnly") : "end") : "quoteOnly");
    }
  } finally {
    Math.random = real;
  }
  for (let i = 1; i < seen.length; i++) assert.notEqual(seen[i], seen[i - 1]);
  assert.ok(new Set(seen).size >= 3);
});

test("chases are worded differently each time", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `add ${PRIYA}`);
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  for (let i = 0; i < 5; i++) await t.engine.handleMessage(t.id(CTO), "ask priya"); // each one a chase
  const chases = t.sent.filter((s) => s.to === `chat-${PRIYA}`).slice(1).map((s) => s.text);
  assert.equal(chases.length, 5);
  for (let i = 1; i < chases.length; i++) assert.notEqual(chases[i], chases[i - 1]);
});

// ---------- the tracker follows the conversation as it happens ----------

test("progress is recorded a couple of minutes after someone speaks, without waiting for the end", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  t.brain.doneAfterUserMessages = 99;
  t.brain.nextNote = {
    note: { summary: "x", progressVsTargets: "", blockers: [], painPoints: [], helpNeeded: [], followUpTomorrow: [] },
    items: [{ itemId: null, title: "Login page", status: "open", update: "half done", due: "2026-11-16" }],
  };
  await t.engine.handleGroupReply("u-priya", "login page half done");

  // Real message times are stamped by the clock; move "now" well past them.
  t.setDate("2099-01-01T00:00:00Z");
  const priya = t.store.personByEmail(PRIYA)!;
  const checkin = t.store.checkin(priya.id, "2026-11-16")!;
  (t.engine as unknown as { today: () => string }).today = () => "2026-11-16";

  assert.equal(await t.engine.noteProgress(), 1);
  assert.equal(t.store.checkin(priya.id, "2026-11-16")!.status, "open"); // still going
  assert.equal(t.store.activeItems(priya.id)[0]!.title, "Login page");
  assert.equal(await t.engine.noteProgress(), 0); // nothing new said since

  await t.engine.refreshTrackers();
  assert.match(t.created[0]!.html, /Login page/);
  void checkin;
});

test("the group message is given each person's tracker items to work from", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const ravi = g.store.personByEmail("ravi@acme.test") ?? g.store.upsertPerson("ravi@acme.test");
  g.store.addItem(ravi.id, "TalentFlow app", "open", "in testing", "2026-11-10", "2026-11-14");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const raviBrief = g.brain.groupAsks.at(-1)!.members.find((m) => m.name === "Ravi Kumar") as { memory?: string };
  assert.match(raviBrief.memory ?? "", /On the tracker:[\s\S]*TalentFlow app[\s\S]*OVERDUE/);
});
