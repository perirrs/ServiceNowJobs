import assert from "node:assert/strict";
import { test } from "node:test";

// Variety is chosen at random; pin it so each test sees the same choices.
Math.random = () => 0;
import type { AdminAction, Brain, CheckinContext, DigestPerson, InstructionAction, InstructionInput, NoteResult, Turn } from "../src/ai.js";
import type { Config } from "../src/config.js";
import { Store } from "../src/db.js";
import { cardToCommand, chunk, Engine, isUpdatePost, leaveToday, parseTime, roleAskLines, roleOf, takeZone } from "../src/engine.js";
import { ROUND_BRIEF } from "../src/ai.js";
import { describeTrackRecord } from "../src/trackrecord.js";
import type { Identity } from "../src/engine.js";
import { Base44TalentFlow, countMoves, describeCounts } from "../src/ats.js";
import type { Base44 } from "../src/base44.js";

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
  smartModel: "fake-smart",
  sendAsUser: false,
  pollSeconds: 20,
  discloseAutomation: true,
  idleCloseMinutes: 30,
  groupWindowMinutes: 120,
  humanDelay: false,
  approveFirstDay: false,
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
  /** Group messages containing this text are complete updates that need no reply. */
  complete = "all set";
  /** Group messages containing this text are treated as chatter. */
  chatter = "lunch";

  async nextTurn(ctx: CheckinContext): Promise<Turn> {
    if (this.failNextTurn) {
      this.failNextTurn = false;
      throw new Error("boom");
    }
    this.turns.push(ctx);
    if (ctx.fromGroup && ctx.history.at(-1)?.text.includes(this.chatter)) return { reply: "", done: false, ignore: true };
    if (ctx.fromGroup && ctx.history.at(-1)?.text.includes(this.complete)) return { reply: "", done: false, silent: true };
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
  digestRecent: string[] = [];
  weeklyInputs: { from: string; to: string; material: string }[] = [];
  async weeklyReport(input: { from: string; to: string; material: string }) {
    this.weeklyInputs.push(input);
    return "WEEKLY";
  }
  async digest(_date: string, people: DigestPerson[], recent = ""): Promise<string> {
    this.digestRecent.push(recent);
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
  /** What readTargets finds in an UPDATE post; null when it holds no targets. */
  nextTargets: Record<string, number | null> | null = null;
  targetReads: string[] = [];
  async readTargets(input: { text: string }) {
    this.targetReads.push(input.text);
    return this.nextTargets;
  }
  trackerEntries: { person: string; title: string; status: "open" | "blocked" | "done" | "dropped"; due: string; progress: number; doneSoFar: string }[] = [];
  trackerInputs: { transcript: string }[] = [];
  async extractTracker(input: { transcript: string }) {
    this.trackerInputs.push(input);
    return this.trackerEntries;
  }
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
  instructionReads: InstructionInput[] = [];
  nextInstruction: InstructionAction[] = [];
  async readInstruction(input: InstructionInput): Promise<InstructionAction[]> {
    this.instructionReads.push(input);
    return this.nextInstruction;
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
  assert.match(t.to(CTO).at(-1)!, /automatically at \*\*17:00 UTC\*\*/);
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

test("the board shows the other admin's groups with one button only: ask for an update", async () => {
  assert.equal(cardToCommand({ cmd: "askOther", key: "mahanth", who: "Admin & Networking" }), "mahanth: ask group Admin & Networking");

  const b = boardSetup();
  b.engine.otherSenders = () => [{ name: "Mahanth M", key: "mahanth", groups: ["Admin & Networking"] }];
  await b.engine.handleMessage(b.id(CTO), "board");
  const card = JSON.stringify(b.sentCards.at(-1));
  assert.match(card, /Groups followed by Mahanth M/);
  const section = (b.sentCards.at(-1) as { body: { type: string; items?: unknown[] }[] }).body.find(
    (x) => x.type === "Container" && JSON.stringify(x).includes("Admin & Networking"),
  )!;
  const actions = JSON.stringify(section).match(/"type":"Action\.[A-Za-z]+"/g) ?? [];
  assert.equal(actions.length, 1); // no times, no remove: only asking
  assert.match(JSON.stringify(section), /"title":"Ask for an update","data":\{"cmd":"askOther","key":"mahanth","who":"Admin & Networking"\}/);

  // Nobody else's groups: no section.
  b.engine.otherSenders = () => [{ name: "Mahanth M", key: "mahanth", groups: [] }];
  await b.engine.handleMessage(b.id(CTO), "board");
  assert.doesNotMatch(JSON.stringify(b.sentCards.at(-1)), /Groups followed by/);
});

test("the board has four buttons at the top: auto and typed updates, for all groups or for everyone", async () => {
  assert.equal(cardToCommand({ cmd: "autoGroups" }), "ask all groups");
  assert.equal(cardToCommand({ cmd: "postGroups", manualGroups: " Please send the covering letters by 6 " }), "post groups Please send the covering letters by 6");
  assert.equal(cardToCommand({ cmd: "postEveryone", manualEveryone: "Office closed Monday" }), "post everyone Office closed Monday");

  const b = boardSetup();
  await b.engine.handleMessage(b.id(CTO), "board");
  const card = b.sentCards.at(-1) as { body: Record<string, unknown>[] };
  // Straight under the title, before anything else.
  const top = card.body.slice(0, 3).find((x) => x.type === "ActionSet") as { actions: { title: string }[] } | undefined;
  assert.deepEqual(top?.actions.map((a) => a.title), ["Auto update: all groups", "Auto update: everyone", "Message all groups", "Message everyone"]);
  const json = JSON.stringify(top);
  assert.match(json, /"id":"manualGroups"/);
  assert.match(json, /"id":"manualEveryone"/);
});

test("'ask all groups' asks every group the usual way, reading first, and nobody one to one", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "add solo@acme.test");
  await g.engine.handleMessage(g.cto, "ask all groups");
  assert.deepEqual(g.sent.map((s) => s.to), ["g-rec"]);
  assert.equal(g.brain.groupAsks.length, 1);
});

test("a typed message goes out word for word: to every group, or to every group and everyone followed one to one", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "add solo@acme.test");
  const groupId = g.store.groups()[0]!.id;

  await g.engine.handleMessage(g.cto, "post groups Please send the covering letters by 6 pm.\nThanks");
  assert.deepEqual(g.sent.map((s) => [s.to, s.text]), [["g-rec", "Please send the covering letters by 6 pm.\nThanks"]]);
  assert.match(g.admin.at(-1)!, /Posted in \*\*Recruitment Team\*\*/);
  // The members' replies are now read and followed up, about this.
  const priya = g.store.personByEmail(PRIYA)!;
  const checkin = g.store.checkin(priya.id, "2026-11-16")!;
  assert.equal(g.store.get(`via:${checkin.id}`), String(groupId));
  assert.equal(g.store.get(`topic:${checkin.id}`), "Please send the covering letters by 6 pm.\nThanks");
  // And what was said there today includes it, for every later question.
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.match(g.brain.groupAsks.at(-1)!.memory, /covering letters by 6 pm/);

  g.sent.length = 0;
  await g.engine.handleMessage(g.cto, "post everyone Office closed on Monday");
  assert.deepEqual(g.sent.map((s) => s.to).sort(), ["chat-solo@acme.test", "g-rec"]); // group members are not messaged twice
  assert.ok(g.sent.every((s) => s.text === "Office closed on Monday"));

  // With no message, it says how to use it, and sends nothing.
  g.sent.length = 0;
  await g.engine.handleMessage(g.cto, "post groups");
  assert.equal(g.sent.length, 0);
});

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

test("past chat is read when someone is added, the tone is stored, and asking only reads what is new", async () => {
  const r = rapportSetup([
    { fromSender: true, text: "pls share the list" },
    { fromSender: false, text: "ok sir" },
    { fromSender: true, text: "any update on globex" },
    { fromSender: true, text: "call me when free" },
  ]);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  const afterAdd = r.reads(); // read once for the tone, once for the work
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.reads(), afterAdd + 1); // a look at what is new since: nothing
  assert.equal(r.brain.revisions.length, 0);
  assert.equal(r.brain.turns.at(-1)!.rapport, "Casual. Says 'pls share'.");

  const priya = r.store.personByEmail(PRIYA)!;
  await r.engine.handleDirectReply(priya.id, "done");
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.revisions.length, 0); // nothing new, so the memory is unchanged
  assert.equal(r.brain.rapportCalls.length, 1); // and the tone is not learned again
});

test("asking someone by hand first reads what is new in their chat since the last look", async () => {
  const r = rapportSetup([
    { fromSender: true, text: "pls share the list" },
    { fromSender: false, text: "ok sir" },
    { fromSender: true, text: "any update on globex" },
  ]);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  await r.engine.handleMessage(r.cto, "ask priya");
  await r.engine.handleDirectReply(r.store.personByEmail(PRIYA)!.id, "done");
  // Midday, typed by hand in her chat: a new priority.
  r.later.push({ fromSender: true, text: "pls prioritise the covering letter first" });
  const learned = r.brain.rapportCalls.length;
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.match(r.brain.revisions.at(-1)?.transcript ?? "", /prioritise the covering letter/);
  assert.equal(r.brain.rapportCalls.length, learned); // the tone is not learned again
});

test("asking a group by hand first reads everything typed there today, then asks", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  (g.engine as unknown as { direct: { history: unknown } }).direct.history = async (chatId: string) =>
    chatId === "g-rec"
      ? [
          { fromSender: false, from: "Ravi Kumar", text: "an old message from last week", at: "2026-11-15T10:00:00Z" },
          { fromSender: true, from: "Sujith", text: "Team, please prioritise the covering letter for Deloitte first", at: "2026-11-16T16:50:00Z" },
          { fromSender: true, from: "Sujith", text: "Team progress at 16:00 Who Role", at: "2026-11-16T16:00:00Z" },
          { fromSender: false, from: "Ravi Kumar", text: "covering letter half done", at: "2026-11-16T16:55:00Z" },
        ]
      : [];
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const memory = g.brain.groupAsks.at(-1)!.memory;
  assert.match(memory, /Sujith: Team, please prioritise the covering letter/);
  assert.match(memory, /Ravi Kumar: covering letter half done/);
  assert.doesNotMatch(memory, /old message from last week/);
  assert.doesNotMatch(memory, /Team progress at/); // the assistant's own post
});

test("a one-to-one history is read in full, newest to oldest in batches: the newest first, then each older batch adds background", async () => {
  const history = Array.from({ length: 12 }, (_, i) => ({ fromSender: i % 2 === 0, text: `message number ${i} about work` }));
  const r = rapportSetup(history);
  (r.engine as unknown as { config: Config }).config.historyBatch = 4; // 500 in real use
  const storedWhenOlderRead: (string | undefined)[] = [];
  const revise = r.brain.reviseMemory.bind(r.brain);
  r.brain.reviseMemory = async (input) => {
    storedWhenOlderRead.push(r.store.get(`rapport:${r.store.personByEmail(PRIYA)!.id}`));
    return revise(input);
  };
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  // First: the newest batch, for how they talk now and what is going on.
  assert.deepEqual(
    r.brain.rapportCalls.at(-1)!.map((m) => m.text),
    ["message number 8 about work", "message number 9 about work", "message number 10 about work", "message number 11 about work"],
  );
  // Kept straight away, before anything older is read.
  assert.match(storedWhenOlderRead[0] ?? "", /Casual/);
  // Then the batch before it, and the one before that, each added to what is already known, as older background.
  const [second, third] = r.brain.revisions as unknown as { transcript: string; memory: string; older?: boolean }[];
  assert.match(second!.transcript, /message number 4 about work[\s\S]*message number 7 about work/);
  assert.doesNotMatch(second!.transcript, /message number (3|8) /);
  assert.match(second!.memory, /Casual/);
  assert.equal(second!.older, true);
  assert.match(third!.transcript, /message number 0 about work[\s\S]*message number 3 about work/);
  assert.equal(third!.memory, "MEMORY v1");
  assert.equal(r.store.get(`rapport:${r.store.personByEmail(PRIYA)!.id}`), "MEMORY v2");
});

test("the assistant's own earlier messages are left out of what it learns from", async () => {
  const r = rapportSetup([
    { fromSender: true, text: "pls share the list" },
    { fromSender: true, text: "any update on globex" },
    { fromSender: true, text: "call me when free" },
    { fromSender: true, text: "Thanks, that's all." }, // what the assistant sent on an earlier day
  ]);
  // She was followed before, and the assistant wrote to her then.
  const priya = r.store.upsertPerson(PRIYA);
  const earlier = r.store.openCheckin(priya.id, "2026-11-13");
  r.store.addMessage(earlier.id, "bot", "Thanks, that's all.");
  r.store.setCheckinStatus(earlier.id, "completed");

  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  assert.deepEqual(r.brain.rapportCalls[0]!.map((m) => m.text), ["pls share the list", "any update on globex", "call me when free"]);
});

test("with too little past chat nothing is learned, and it does not keep looking", async () => {
  const r = rapportSetup([{ fromSender: false, text: "hello sir" }]);
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  const afterAdd = r.reads();
  await r.engine.handleMessage(r.cto, "ask priya");
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.rapportCalls.length, 0);
  assert.equal(r.brain.revisions.length, 0);
  assert.ok(r.reads() > afterAdd);
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
  assert.match(g.sent[0]!.text, /Hi team, end of day: what is complete so far/);

  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions today");
  const followUp = g.sent.at(-1)!;
  assert.equal(followUp.to, "g-rec"); // it stays in the group
  assert.match(followUp.text, /^\{@0\} Question/);
  assert.deepEqual(followUp.mentions, [{ userId: "u-priya", name: "Priya Rao" }]);
  assert.equal(g.brain.turns.at(-1)!.fromGroup, true);
  assert.equal(g.sent.filter((s) => s.to === `chat-${PRIYA}`).length, 0); // nothing one to one

  // Something that should not be said in front of the group goes one to one instead.
  g.setDate("2026-11-17T17:00:00Z");
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  g.brain.privateNext = true;
  await g.engine.handleGroupReply("u-priya", "could not finish, was unwell");
  assert.equal(g.sent.at(-1)!.to, `chat-${PRIYA}`);
  assert.equal(g.sent.at(-1)!.text, "Private question");
});

test("in a group, each person gets at most one follow-up; a complete update gets no reply at all", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "working on Deloitte");
  assert.match(g.sent.at(-1)!.text, /Question/);
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-priya", "allow me some time");
  assert.equal(g.sent.length, before); // no second question
  const priya = g.store.personByEmail(PRIYA)!;
  assert.ok(g.store.messages(g.store.checkin(priya.id, "2026-11-16")!.id).some((m) => m.text === "allow me some time")); // still recorded

  await g.engine.handleGroupReply("u-ravi", "3 Deloitte submissions by 4 pm, all set");
  assert.equal(g.sent.length, before); // complete: nothing posted
});

test("the group ask spells out what each role should send, and names the director for the team view", async () => {
  const g = groupSetup();
  g.members.push({ userId: "u-madan", email: "madan@acme.test", name: "Madan Mani" });
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.store.setProfile(g.store.personByEmail(PRIYA)!.id, "Recruiter. 6 submissions a day.");
  g.store.setProfile(g.store.personByEmail("ravi@acme.test")!.id, "Account Manager for Deloitte. Does no submissions.");
  g.store.setProfile(g.store.personByEmail("madan@acme.test")!.id, "Recruitment Director");
  g.setDate("2026-11-16T09:00:00Z"); // morning
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  const text = g.sent.at(-1)!.text;
  assert.match(text, /Recruiters: .*submitted to client/);
  assert.match(text, /Account managers: .*new requirements.*SPOCs/);
  assert.doesNotMatch(text, /Account owners:/); // nobody in that role here
  assert.match(text, /Madan: .*whole team/);
  assert.equal(g.store.checkin(g.store.personByEmail("madan@acme.test")!.id, "2026-11-16"), undefined);
});

test("after a group's clock-out, replies there are recorded but not answered or chased, even inside office hours", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "hours 09:00-21:00");
  await g.engine.handleMessage(g.cto, "clockout Recruitment Team 18:00");
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  g.brain.doneAfterUserMessages = 99;
  g.setDate("2026-11-16T18:30:00Z");
  const before = g.sent.length;
  await g.engine.handleGroupReply("u-priya", "working on Deloitte");
  assert.equal(g.sent.length, before);
  const priya = g.store.personByEmail(PRIYA)!;
  assert.ok(g.store.messages(g.store.checkin(priya.id, "2026-11-16")!.id).some((m) => m.text === "working on Deloitte"));
});

test("everyone asked in the group is tagged, not only those with a question of their own", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.brain.nextGroupQuestions = [{ name: "Priya Rao", question: "Deloitte profiles, how many today?" }];
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  const ask = g.sent.at(-1)!;
  assert.deepEqual(ask.mentions!.map((m) => m.userId).sort(), ["u-priya", "u-ravi"]);
  assert.match(ask.text, /{@1} please share your update./);
});

test("an update someone posted in the group earlier today counts as their answer: not chased", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  (g.engine as unknown as { direct: { history: unknown } }).direct.history = async () => [
    { fromSender: false, from: "Priya Rao", at: "2026-11-16T08:00:00.000Z", text: "Good morning, today I will send 3 Deloitte profiles and finish the calendar" },
    { fromSender: false, from: "Ravi Kumar", at: "2026-11-15T08:00:00.000Z", text: "Yesterday I fixed the login bug and two more issues" },
  ];
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  const priya = g.store.personByEmail(PRIYA)!;
  assert.ok(g.store.messages(g.store.checkin(priya.id, "2026-11-16")!.id).some((m) => m.role === "user" && m.text.includes("3 Deloitte profiles")));
  g.setDate("2026-11-16T17:50:00Z");
  await g.engine.tick();
  const chase = g.sent.find((s) => s.to === "g-rec" && s.text.includes("Any update?"));
  assert.deepEqual(chase?.mentions, [{ userId: "u-ravi", name: "Ravi Kumar" }]); // yesterday's message does not count
});

test("a group renamed in Teams takes its new name, and commands find it by the new name", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  let topic = "Recruitment Team";
  (g.engine as unknown as { direct: { groupName: (c: string) => Promise<string | null> } }).direct.groupName = async () => topic;
  topic = "M&S Recruitment Squad";
  g.setDate("2026-11-16T18:10:00Z"); // over an hour later
  await g.engine.tick();
  assert.equal(g.store.groups()[0]!.name, "M&S Recruitment Squad");
  await g.engine.handleMessage(g.cto, "schedule Recruitment Squad evening 17:00");
  assert.match(g.admin.at(-1)!, /M&S Recruitment Squad will be asked automatically at \*\*17:00/);
});

test("people added or removed in Teams are picked up within the hour; leavers are no longer followed, and the admins are told", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, `add ${PRIYA}`); // Priya is also followed in her own right
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.members.splice(0, g.members.length, { userId: "u-sam", email: "sam@acme.test", name: "Sam Lee" }); // Priya and Ravi left, Sam joined
  g.setDate("2026-11-16T18:10:00Z");
  await g.engine.tick();
  const nominees = g.store.nominees().map((p) => p.email).sort();
  assert.deepEqual(nominees, [PRIYA, "sam@acme.test"]); // Ravi was only there through the group
  const told = g.admin.join("\n");
  assert.match(told, /Sam Lee joined Recruitment Team/);
  assert.match(told, /profile sam/i);
  assert.match(told, /Ravi Kumar left Recruitment Team/);
});

test("someone who joins a followed group has their chat read straight away; someone who leaves is let go within a minute", async () => {
  const g = groupSetup();
  const direct = (g.engine as unknown as { direct: { history: unknown } }).direct;
  const reads: string[] = [];
  direct.history = async (chatId: string) => {
    reads.push(chatId);
    return chatId === "chat-sam@acme.test"
      ? [
          { fromSender: true, from: "Sujith", text: "Sam, pls share the Initech list", at: "2026-11-10T10:00:00Z" },
          { fromSender: false, from: "Sam Lee", text: "sure sir", at: "2026-11-10T10:05:00Z" },
          { fromSender: true, from: "Sujith", text: "any update on Initech", at: "2026-11-12T10:00:00Z" },
          { fromSender: true, from: "Sujith", text: "call me when free", at: "2026-11-13T10:00:00Z" },
        ]
      : [];
  };
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.members.push({ userId: "u-sam", email: "sam@acme.test", name: "Sam Lee" }); // Sam joins
  g.members.splice(1, 1); // Ravi leaves
  g.setDate("2026-11-16T17:02:00Z"); // two minutes later
  await g.engine.tick();

  const sam = g.store.personByEmail("sam@acme.test")!;
  assert.ok(reads.includes("chat-sam@acme.test"));
  assert.match(g.store.get(`rapport:${sam.id}`) ?? "", /Casual/);
  assert.equal(g.store.personByEmail("ravi@acme.test")!.nominated, false);
  const told = g.admin.join("\n");
  assert.match(told, /Sam Lee joined Recruitment Team/);
  assert.match(told, /read their whole chat with you/);
  assert.match(told, /Ravi Kumar left Recruitment Team/);
});

test("'reread' reads every followed group and everyone's one-to-one chat again from the start", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const groupId = g.store.groups()[0]!.id;
  g.store.set(`gmemory:${groupId}`, "OLD GROUP MEMORY");
  const priya = g.store.personByEmail(PRIYA)!;
  g.store.set(`rapport:${priya.id}`, "OLD PRIYA MEMORY");
  const reads: { chatId: string; since: string }[] = [];
  (g.engine as unknown as { direct: { history: unknown } }).direct.history = async (chatId: string, _max: number, since = "") => {
    reads.push({ chatId, since });
    return [
      { fromSender: true, from: "Sujith", text: "pls share the list", at: "2026-10-01T10:00:00Z" },
      { fromSender: false, from: "x", text: "ok sir", at: "2026-10-01T10:01:00Z" },
      { fromSender: true, from: "Sujith", text: "any update", at: "2026-11-01T10:00:00Z" },
      { fromSender: true, from: "Sujith", text: "call me", at: "2026-11-02T10:00:00Z" },
    ];
  };
  await g.engine.handleMessage(g.cto, "reread");
  // From the start, not just what is new.
  assert.ok(reads.some((r) => r.chatId === "g-rec" && !r.since));
  assert.ok(reads.some((r) => r.chatId === `chat-${PRIYA}` && !r.since));
  assert.ok(reads.some((r) => r.chatId === "chat-ravi@acme.test" && !r.since));
  assert.notEqual(g.store.get(`gmemory:${groupId}`), "OLD GROUP MEMORY");
  assert.match(g.store.get(`rapport:${priya.id}`)!, /Casual/);
  assert.match(g.admin.at(-1)!, /Read again from the start: 1 group and 2 people/);
});

test("a group history is read in full, newest to oldest in batches: the newest is kept at once, each older batch adds background", async () => {
  const g = groupSetup();
  (g.engine as unknown as { config: Config }).config.historyBatch = 3; // 500 in real use
  (g.engine as unknown as { direct: { history: unknown } }).direct.history = async (chatId: string) =>
    chatId === "g-rec"
      ? Array.from({ length: 10 }, (_, i) => ({ fromSender: false, from: "Ravi Kumar", text: `group message ${i}`, at: `2026-11-${String(i + 1).padStart(2, "0")}T10:00:00Z` }))
      : [];
  const storedAtEachRead: string[] = [];
  const revise = g.brain.reviseMemory.bind(g.brain);
  g.brain.reviseMemory = async (input) => {
    storedAtEachRead.push(g.store.get(`gmemory:${g.store.groups()[0]?.id}`) ?? "");
    return revise(input);
  };
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const parts = (g.brain.revisions as unknown as { about: string; transcript: string; memory: string; older?: boolean }[]).filter((r) =>
    r.about.includes("group chat"),
  );
  const said = (p: { transcript: string }) => [...p.transcript.matchAll(/group message (\d)/g)].map((m) => Number(m[1]));
  // Newest batch first, then 3-6 back, then 6-9 back, then what is left.
  assert.deepEqual(parts.map(said), [[7, 8, 9], [4, 5, 6], [1, 2, 3], [0]]);
  assert.deepEqual(
    parts.map((p) => !!p.older),
    [false, true, true, true],
  );
  // Each batch builds on what is already known, and what is known is saved after every batch.
  assert.deepEqual(
    parts.map((p) => p.memory),
    ["", "MEMORY v1", "MEMORY v2", "MEMORY v3"],
  );
  assert.equal(storedAtEachRead[1], "MEMORY v1");
  assert.equal(g.store.get(`gmemory:${g.store.groups()[0]!.id}`), "MEMORY v4");
});

test("an UPDATE posted by an admin in a group is kept as a standing instruction and its targets take effect", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "profile priya: Recruiter. Does submissions only.");
  const groupId = g.store.groups()[0]!.id;

  // Someone who is not an admin cannot give instructions this way.
  await g.engine.adminGroupPost(groupId, PRIYA, "UPDATE targets are 1 a day");
  assert.equal(g.brain.targetReads.length, 0);

  g.brain.nextTargets = { recruiterDay: 5, recruiterL1Percent: 25, recruiterMonthJoins: 2, ownerDay: null, ownerMonthJoins: null, managerMonthRequirements: 4, managerMonthSpocs: 2 };
  await g.engine.adminGroupPost(groupId, CTO, "UPDATE\nAfter the call with Madan: recruiters 5 a day, 25% L1, 2 joins a month. Account managers 4 new requirements and 2 SPOCs a month. No leave without telling Madan.");
  assert.match(g.brain.targetReads[0]!, /recruiters 5 a day/);
  assert.match(g.admin.at(-1)!, /Recruiters: at least 5 submissions a day, at least 25% .* 2 joins a month/);

  // The instruction reaches what the bot writes for the group.
  g.setDate("2026-11-16T09:00:00Z");
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  assert.match(g.brain.groupAsks.at(-1)!.memory, /No leave without telling Madan/);
  assert.match(g.sent.at(-1)!.text, /Recruiters: how many profiles submitted to client today \(target 5\)/);

  // And the hourly table measures against the new numbers.
  const posts: string[] = [];
  (g.engine as unknown as { direct: { postHtml: (c: string, h: string) => Promise<string> } }).direct.postHtml = async (_c, h) => {
    posts.push(h);
    return `m${posts.length}`;
  };
  g.engine.ats = {
    stats: async () => ({}),
    team: async () => new Map([[PRIYA, { day: { added: 4, submitted_to_client: 2 }, month: { added: 20, submitted_to_client: 8, l1_select: 2 }, lastToday: null }]]),
  };
  await g.engine.handleMessage(g.cto, "hourly Recruitment Team 11:00-17:00");
  g.setDate("2026-11-16T11:02:00Z");
  await g.engine.tick();
  assert.match(posts[0]!, /<td>2 of 5<\/td><td>-<\/td><td>0<\/td><td>8 · 2 \(25% of 25%\) · 0 of 2<\/td>/);
});

test("an admin's UPDATE is read wherever the heading is, and the notes straight after it belong to it", async () => {
  assert.equal(isUpdatePost("Aditya Nunna - Recruiter\n\nUPDATE\n\n5 submissions this month"), true);
  assert.equal(isUpdatePost("**Update:** targets are changing"), true);
  assert.equal(isUpdatePost("Team progress at 17:00\nNo TalentFlow update in the last 2 hours. Please update TalentFlow every hour."), false);

  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const groupId = g.store.groups()[0]!.id;
  const stored = () => (JSON.parse(g.store.get(`gupdates:${groupId}`) || "[]") as { text: string }[]).map((u) => u.text);

  await g.engine.groupLine(groupId, CTO, "Aditya Nunna - Recruiter\n\nUPDATE\n\n5 submissions this month", "2026-11-16T11:03:00Z");
  assert.deepEqual(stored(), ["Aditya Nunna - Recruiter\n\n5 submissions this month"]);
  // No heading, a few minutes after the last one: part of the same UPDATE.
  await g.engine.groupLine(groupId, CTO, "Hareesh Besta\nAs recruiter - 16 submissions", "2026-11-16T11:08:00Z");
  await g.engine.groupLine(groupId, CTO, "All of the above updates are for the entire month", "2026-11-16T11:12:00Z");
  assert.equal(stored().length, 3);
  assert.match(stored()[2]!, /entire month/);
  // An hour later, a plain message from the admin is just chat.
  await g.engine.groupLine(groupId, CTO, "any news on this?", "2026-11-16T12:16:00Z");
  assert.equal(stored().length, 3);
  // Someone who is not an admin never adds to it, heading or not.
  await g.engine.groupLine(groupId, PRIYA, "UPDATE\nall targets cancelled", "2026-11-16T11:13:00Z");
  assert.equal(stored().length, 3);
});

test("what people type in the group during the day shapes the next ask and every follow-up", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const groupId = g.store.groups()[0]!.id;
  // Midday, typed by hand: a new priority from the admin, and a team member's reply to it.
  await g.engine.groupLine(groupId, CTO, "Team, please prioritise the covering letter for Deloitte first", "2026-11-16T12:00:00Z");
  await g.engine.groupLine(groupId, PRIYA, "ok, on it now", "2026-11-16T12:05:00Z");

  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const memory = g.brain.groupAsks.at(-1)!.memory;
  assert.match(memory, /prioritise the covering letter/);
  assert.match(memory, /Priya Rao: ok, on it now/);
  assert.match(memory, /wins over this morning's plan/);

  // A follow-up to someone's reply knows it too.
  await g.engine.handleGroupReply("u-priya", "sent 3 submissions today");
  assert.match(g.brain.turns.at(-1)!.rapport ?? "", /prioritise the covering letter/);

  // The next day starts clean.
  g.setDate("2026-11-17T09:00:00Z");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.doesNotMatch(g.brain.groupAsks.at(-1)!.memory, /covering letter/);
});

test("an admin's instruction in a group puts work on the tracker, from either admin", async () => {
  const g = groupSetup();
  g.members.push({ userId: "u-ceo", email: CEO, name: "Mahanth M" }); // the other admin is in the group too
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const groupId = g.store.groups()[0]!.id;
  const ravi = g.store.personByEmail("ravi@acme.test")!;

  g.brain.nextInstruction = [{ kind: "add", person: "Ravi Kumar", itemId: null, title: "Make the TalentFlow website live", due: "2026-11-16" }];
  await g.engine.groupLine(groupId, CTO, "Ravi, can you make the TalentFlow website live today, urgent", "2026-11-16T17:05:00Z");
  const added = g.store.activeItems(ravi.id).find((i) => i.title === "Make the TalentFlow website live")!;
  assert.equal(added.due, "2026-11-16");
  assert.match(g.admin.at(-1)!, /Ravi Kumar: Make the TalentFlow website live \(due 2026-11-16\)/);
  // What the reader was given: who is in the group, and what is on their tracker, with ids.
  const read = g.brain.instructionReads.at(-1)!;
  assert.deepEqual(read.members.map((m) => m.name).sort(), ["Priya Rao", "Ravi Kumar"]);
  assert.match(read.today, /Monday 2026-11-16/);

  // The other admin's instructions count the same; "everyone" means every member.
  g.brain.nextInstruction = [{ kind: "add", person: "everyone", itemId: null, title: "Update TalentFlow by 6 pm", due: "2026-11-16" }];
  await g.engine.groupLine(groupId, CEO, "Everyone, update TalentFlow by 6 pm", "2026-11-16T17:06:00Z");
  // The reader is shown what is already on the tracker, with ids, so it can refer to it.
  assert.ok(g.brain.instructionReads.at(-1)!.items.some((i) => i.id === added.id && i.person === "Ravi Kumar"));
  assert.ok(g.store.activeItems(ravi.id).some((i) => i.title === "Update TalentFlow by 6 pm"));
  assert.ok(g.store.activeItems(g.store.personByEmail(PRIYA)!.id).some((i) => i.title === "Update TalentFlow by 6 pm"));

  // Team members' messages and short acknowledgements are not instructions.
  const reads = g.brain.instructionReads.length;
  await g.engine.groupLine(groupId, PRIYA, "Ravi please do the website first", "2026-11-16T17:07:00Z");
  await g.engine.groupLine(groupId, CTO, "ok thanks", "2026-11-16T17:08:00Z");
  assert.equal(g.brain.instructionReads.length, reads);
});

test("admin instructions move, pause, resume, stop and finish work on the tracker, and it is all remembered", async () => {
  const g = groupSetup(); // Monday 2026-11-16
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const groupId = g.store.groups()[0]!.id;
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  const item = (title: string) => g.store.addItem(ravi.id, title, "open", "", "2026-11-16", "2026-11-16");
  const status = (id: number) => g.store.itemsOf(ravi.id).find((i) => i.id === id)!;
  const onTracker = (id: number) => g.store.trackerItems(ravi.id, "2026-11-16").some((i) => i.id === id);
  const say = async (text: string, actions: InstructionAction[]) => {
    g.brain.nextInstruction = actions;
    await g.engine.groupLine(groupId, CTO, text, "2026-11-16T17:10:00Z");
  };

  // "Do it on Monday": off today's tracker, back on it on Monday, due then.
  const website = item("Website launch");
  await say("Ravi, leave the website for now, do it on Monday", [{ kind: "reschedule", person: "Ravi Kumar", itemId: website, title: "", due: "2026-11-23" }]);
  assert.equal(status(website).status, "paused");
  assert.equal(onTracker(website), false);
  assert.match(g.admin.at(-1)!, /Website launch: moved to 2026-11-23/);
  g.setDate("2026-11-23T09:00:00Z");
  await g.engine.tick();
  assert.equal(status(website).status, "open");
  assert.equal(status(website).due, "2026-11-23");
  g.setDate("2026-11-16T17:10:00Z");

  // "Stop for now", then "carry on".
  const report = item("Monthly report");
  await say("Ravi stop the report for now", [{ kind: "pause", person: "Ravi Kumar", itemId: report, title: "", due: null }]);
  assert.equal(status(report).status, "paused");
  assert.equal(onTracker(report), false);
  assert.ok(!g.store.activeItems(ravi.id).some((i) => i.id === report)); // not chased or asked about
  await say("Ravi, carry on with the report", [{ kind: "resume", person: "Ravi Kumar", itemId: report, title: "", due: null }]);
  assert.equal(status(report).status, "open");

  // "Do not work on this at all": off the tracker for good, but kept in memory.
  const deck = item("Sales deck");
  await say("Ravi, drop the sales deck completely", [{ kind: "cancel", person: "Ravi Kumar", itemId: deck, title: "", due: null }]);
  assert.equal(status(deck).status, "dropped");
  assert.match(status(deck).lastUpdate, /Sujith|cto/i);

  // "That is done" and "this first".
  const mail = item("Mail setup");
  await say("mail setup is done, thanks Ravi", [{ kind: "done", person: "Ravi Kumar", itemId: mail, title: "", due: null }]);
  assert.equal(status(mail).status, "done");
  const port = g.store.addItem(ravi.id, "SQL port fix", "open", "", "2026-11-16", "2026-11-20");
  await say("Ravi, SQL port fix first, today", [{ kind: "prioritise", person: "Ravi Kumar", itemId: port, title: "", due: "2026-11-16" }]);
  assert.equal(status(port).due, "2026-11-16");

  // The instruction itself is also kept with what was said in the group today.
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.match(g.brain.groupAsks.at(-1)!.memory, /SQL port fix first, today/);
});

test("who a message says is on leave, sick or away today", () => {
  const people = [
    { id: 1, name: "Priya Rao" },
    { id: 2, name: "Ravi Kumar" },
    { id: 3, name: "Nazia Mohammed" },
  ];
  const [priya, , nazia] = people;
  assert.deepEqual(leaveToday("Ravi is on leave today", nazia!, people), [2]);
  assert.deepEqual(leaveToday("I am on sick leave", priya!, people), [1]);
  assert.deepEqual(leaveToday("I'm sick today, will update tomorrow", priya!, people), [1]);
  assert.deepEqual(leaveToday("Priya is away today and Ravi Kumar is on annual leave", nazia!, people), [1, 2]);
  assert.deepEqual(leaveToday("Ravi will be on leave tomorrow", nazia!, people), []);
  assert.deepEqual(leaveToday("could not finish, was unwell", priya!, people), []);
  assert.deepEqual(leaveToday("Ravi is back from leave", nazia!, people), []);
  assert.deepEqual(leaveToday("please leave it with me", priya!, people), []);
  assert.deepEqual(leaveToday("the client is on leave today", priya!, people), []);
});

test("someone said to be on leave in the group is not chased, and the admins are told", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const groupId = g.store.groups()[0]!.id;
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  await g.engine.groupLine(groupId, PRIYA, "Ravi is on sick leave today", "2026-11-16T17:05:00Z");
  assert.match(g.admin.at(-1)!, /Priya Rao says Ravi Kumar is on leave today/);
  assert.match(g.admin.at(-1)!, /leave ravi off/i);

  g.setDate("2026-11-16T17:50:00Z"); // past the 45-minute chase
  await g.engine.tick();
  assert.equal(g.sent.filter((s) => s.mentions?.some((m) => m.userId === "u-ravi") && /Any update/.test(s.text)).length, 0);
  // Said again later: the admins are not told twice.
  const told = g.admin.length;
  await g.engine.groupLine(groupId, PRIYA, "Ravi is on leave today", "2026-11-16T17:55:00Z");
  assert.equal(g.admin.length, told);
});

test("someone who says one to one they are on leave today is not chased or asked again", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleMessage(t.id(PRIYA), "I am on annual leave today");
  assert.match(t.to(CTO).at(-1)!, /priya says they are on leave today/i);
  assert.equal(await t.engine.chaseFollowUps(0), 0);
});

test("the end-of-day ask is about what is complete, what will finish before logging off, and what carries over", async () => {
  const g = groupSetup(); // 17:00, the end of the day
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.match(g.sent.at(-1)!.text, /what is complete so far/);
  assert.match(g.sent.at(-1)!.text, /before you log off/);
  assert.match(g.sent.at(-1)!.text, /carries over to tomorrow morning/);

  const lines = roleAskLines([{ name: "Priya Rao", profile: "Recruiter" }], "review").join("\n");
  assert.match(lines, /carries over to tomorrow/);
  // One to one and in follow-ups, the conversation is briefed the same way.
  assert.match(ROUND_BRIEF.review, /complete so far/);
  assert.match(ROUND_BRIEF.review, /before they log off/);
  assert.match(ROUND_BRIEF.review, /carries over to tomorrow morning/);
});

test("a group or person another sender already follows cannot be added a second time", async () => {
  const g = groupSetup();
  g.engine.followedElsewhere = {
    group: (chatId) => (chatId === "g-rec" ? "Mahanth M" : undefined),
    person: (email) => (email === "ravi@acme.test" ? "Mahanth M" : undefined),
  };
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  assert.match(g.admin.at(-1)!, /Recruitment Team is already followed by Mahanth M/);
  assert.equal(g.store.groups().length, 0);

  await g.engine.handleMessage(g.cto, "add ravi@acme.test");
  assert.match(g.admin.at(-1)!, /ravi@acme.test is already followed by Mahanth M/);
  assert.equal(g.store.personByEmail("ravi@acme.test")?.nominated ?? false, false);

  // Anyone not followed elsewhere is added as usual.
  await g.engine.handleMessage(g.cto, `add ${PRIYA}`);
  assert.equal(g.store.personByEmail(PRIYA)!.nominated, true);
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

  // Same day again, asked by hand: only what is new is read, and nothing is.
  await r.engine.handleMessage(r.cto, "ask priya");
  assert.equal(r.brain.revisions.length, 0);
  const priya2 = r.store.personByEmail(PRIYA)!;
  await r.engine.handleDirectReply(priya2.id, "done");

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
      // Like Teams: with a "since", only what came after it.
      history: async (chat, _max, since) =>
        chat === "g-rec" && !since
          ? [{ fromSender: false, from: "Priya Rao", text: "Initech wants 5 profiles by Friday", at: "2026-11-16T09:00:00.000Z" }]
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
  assert.equal(g.sent.at(-1)!.text, "Hi team, website updates and launch date?\n{@0} {@1} please share your update.");
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
  assert.match(t.to(CTO).at(-1)!, /priya will be asked automatically at \*\*18:00 UTC\*\* \(end-of-day update\)/);

  await t.engine.handleMessage(t.id(CTO), "schedule");
  assert.match(t.to(CTO).at(-1)!, /Own times, Monday to Friday:\n- \*\*priya\*\*: evening 18:00 UTC/);

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
  assert.match(g.admin.at(-1)!, /Recruitment Team will be asked automatically at \*\*17:00 UTC\*\*/);
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
  assert.match(json, /"cmd":"scheduleFor","who":"priya@acme\.test","field":"p\d+m","zoneField":"p\d+mz","slot":"morning"/);
  assert.equal(cardToCommand({ cmd: "scheduleFor", who: PRIYA, field: "p2e", slot: "evening", p2e: "17:30" }), `schedule ${PRIYA} evening 17:30`);
  assert.equal(cardToCommand({ cmd: "scheduleFor", who: "", field: "allm", slot: "morning", allm: "09:30" }), "schedule morning 09:30");
  assert.equal(
    cardToCommand({ cmd: "scheduleFor", who: "", field: "allm", zoneField: "allmz", slot: "morning", allm: "09:30", allmz: "Asia/Kolkata" }),
    "schedule morning 09:30 Asia/Kolkata",
  );
  assert.equal(cardToCommand({ cmd: "scheduleFor", who: PRIYA, field: "p2", p2: "10:30" }), `schedule ${PRIYA} 10:30`);
  assert.equal(cardToCommand({ cmd: "scheduleOffFor", who: PRIYA }), `schedule ${PRIYA} off`);

  await b.engine.handleMessage(b.id(CTO), "schedule 17:00");
  await b.engine.handleMessage(b.id(CTO), `schedule ${PRIYA} 10:30`);
  json = b.edits.at(-1)!.json;
  assert.match(json, /"text":"ON · End of day: everyone is asked at 17:00 UTC, Monday to Friday"[^}]*"color":"Good"/);
  assert.match(json, /"text":"ON · Morning 10:30 UTC, Monday to Friday","color":"Good"/);
  assert.match(json, /"title":"Times: Morning 10:30 UTC"/);
  assert.match(json, /"type":"Input.ChoiceSet","id":"p\d+mz","label":"Timezone"/);
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
  assert.equal(
    post.text,
    "Hi team, end of day: what is complete so far, will the rest be done before you log off, and what carries over to tomorrow morning? Any blockers?\n{@0} TalentFlow app - where are we?\n{@1} please share your update.",
  );
  assert.deepEqual(post.mentions, [
    { userId: "u-ravi", name: "Ravi Kumar" },
    { userId: "u-priya", name: "Priya Rao" },
  ]);

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

  // Ravi never replied at all: that is the group chase's job, not this one (he is only tagged in the ask itself).
  assert.ok(!inGroup().slice(1).some((s) => s.mentions?.some((m) => m.userId === "u-ravi")));
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
  assert.equal(
    post.text,
    "Hi team, end of day: what is complete so far, will the rest be done before you log off, and what carries over to tomorrow morning? Any blockers?\n{@0} please share your update.",
  );
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
  await g.engine.handleGroupReply("u-ravi", "sent 3 submissions");
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
  const groupId = g.store.groups()[0]!.id;

  // The assistant's own posts are recognised, with or without the tag in front.
  assert.equal(g.engine.isAssistantGroupText(groupId, "Hi team, end of day: what is complete so far, will the rest be done before you log off, and what carries over to tomorrow morning? Any blockers? Priya Rao Ravi Kumar please share your update."), true);
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
  assert.match(t.to(CTO).at(-1)!, /Morning plan: everyone is asked automatically at \*\*09:30 UTC\*\*/);
  assert.match(t.to(CTO).at(-1)!, /End-of-day update: everyone is asked automatically at \*\*17:30 UTC\*\*/);

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
  assert.match(html, /<td>Priya<\/td><td>Client deck &lt;v2&gt;<\/td><td>14 Nov<\/td><td>-<\/td><td>🔴 Overdue<\/td>/);
  assert.match(html, /<td>Ravi<\/td><td>No items yet<\/td>/);

  await t.engine.refreshTrackers(); // nothing changed: nothing sent
  assert.equal(t.created.length + t.updated.length, 1);

  await t.engine.handleMessage(t.cto, "ask group Recruitment Team");
  await reportItem(t, "Landing page", "blocked", "");
  await t.engine.refreshTrackers();
  assert.equal(t.created.length, 1); // still one message
  assert.equal(t.updated.at(-1)!.id, "trk-1");
  assert.match(t.updated.at(-1)!.html, /Landing page<\/td><td>-<\/td><td>-<\/td><td>🟠 Blocked/);
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
      // One follow-up per person per day in a group, so each reply is on a day of its own.
      g.setDate(`2026-11-${16 + i}T17:00:00Z`);
      Math.random = real;
      await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
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

// ---------- building a tracker from the whole chat history ----------

test("a tracker is built from the group's history: items by person, progress and what is done, pinned", async () => {
  const t = trackerSetup();
  const direct = (t.engine as unknown as { direct: Record<string, unknown> }).direct;
  direct.history = async () => [
    { fromSender: true, from: "Sujith", text: "status of the website?", at: "2026-11-10T09:00:00Z" },
    { fromSender: false, from: "Ravi Kumar", text: "80% done, live by friday", at: "2026-11-10T09:05:00Z" },
    { fromSender: true, from: "Sujith", text: "Team tracker · updated ...", at: "2026-11-11T09:00:00Z" },
  ];
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  t.brain.trackerEntries = [
    { person: "Ravi Kumar", title: "Website", status: "open", due: "2026-11-14", progress: 80, doneSoFar: "80% done, live by Friday (10 Nov)" },
    { person: "Priya", title: "LinkedIn posts", status: "done", due: "", progress: -1, doneSoFar: "posted" },
    { person: "Nobody Here", title: "Ghost task", status: "open", due: "", progress: -1, doneSoFar: "" },
  ];
  t.brain.nextActions = [{ type: "build_tracker", target: "Recruitment Team", text: "" }];
  await t.engine.handleMessage(t.cto, "build a tracker for the Recruitment Team group from the whole chat and track it daily");

  // The tracker table itself is not fed back in as history.
  assert.doesNotMatch(t.brain.trackerInputs[0]!.transcript, /Team tracker/);
  assert.match(t.brain.trackerInputs[0]!.transcript, /2026-11-10 Ravi Kumar: 80% done, live by friday/);

  const ravi = t.store.personByEmail("ravi@acme.test")!;
  assert.deepEqual(
    t.store.activeItems(ravi.id).map((i) => [i.title, i.due, i.progress]),
    [["Website", "2026-11-14", 80]],
  );
  const html = (t.updated.at(-1) ?? t.created.at(-1))!.html;
  assert.match(html, /<th>Progress<\/th><th>Status<\/th><th>Done so far<\/th>/);
  assert.match(html, /Website<\/td><td>14 Nov<\/td><td>▰▰▰▰▱ 80%<\/td><td>🔴 Overdue<\/td><td>80% done, live by Friday \(10 Nov\)/);
  assert.match(html, /LinkedIn posts<\/td><td>-<\/td><td>▰▰▰▰▰ 100%<\/td><td>✅ Done/);
  assert.doesNotMatch(html, /Ghost task/);
  assert.match(t.admin.at(-1)!, /Built the tracker for \*\*Recruitment Team\*\*[\s\S]*Ravi Kumar: 1/);

  // Built again: the same work is updated, not added twice.
  t.brain.trackerEntries = [{ person: "Ravi", title: "website", status: "open", due: "", progress: 90, doneSoFar: "90%" }];
  await t.engine.handleMessage(t.cto, "tracker build Recruitment Team");
  assert.deepEqual(
    t.store.activeItems(ravi.id).map((i) => [i.title, i.due, i.progress]),
    [["Website", "2026-11-14", 90]],
  );
});

// ---------- adding a group or a person always starts with the whole history ----------

test("adding a group reads every member's chat afresh, the group's history, and builds the tracker", async () => {
  const t = trackerSetup();
  const direct = (t.engine as unknown as { direct: Record<string, unknown> }).direct;
  const reads: { chat: string; max: number; since?: string }[] = [];
  direct.history = async (chat: string, max: number, since?: string) => {
    reads.push({ chat, max, since });
    return chat === "g-rec"
      ? [{ fromSender: false, from: "Ravi Kumar", text: "website 80% done", at: "2026-11-10T09:00:00Z" }]
      : Array.from({ length: 10 }, (_, i) => ({ fromSender: i % 2 === 0, from: "", text: `msg ${i}`, at: "2026-11-01T09:00:00Z" }));
  };
  t.brain.trackerEntries = [{ person: "Ravi Kumar", title: "Website", status: "open", due: "", progress: 80, doneSoFar: "80% done" }];

  // Someone already known: the old note must not stop a fresh read.
  const priya = t.store.upsertPerson(PRIYA);
  t.store.set(`rapport:${priya.id}`, "OLD NOTE");

  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  assert.ok(t.admin.some((m) => /Reading its whole chat history, and each person's whole chat with you/.test(m)));
  assert.deepEqual(reads.filter((r) => r.chat.startsWith("chat-")).map((r) => r.chat).sort(), [`chat-${PRIYA}`, "chat-ravi@acme.test"]);
  assert.ok(reads.every((r) => !r.since && r.max === config.historyMessages)); // whole history, not just what is new
  assert.equal(t.store.get(`rapport:${priya.id}`), "Casual. Says 'pls share'.");
  assert.equal(t.created.length, 1); // the tracker was built and posted
  assert.match(t.admin.at(-1)!, /Now following[\s\S]*Ravi Kumar: one-to-one chat read[\s\S]*Built the tracker/);
  assert.equal(t.sent.filter((s) => s.to === "g-rec").length, 0); // no question posted

  // Removed and added again: the whole read happens again.
  const before = reads.length;
  await t.engine.handleMessage(t.cto, "remove group Recruitment Team");
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  assert.ok(reads.length >= before + 3);
});

test("adding a person reads their whole one-to-one chat and tracks the work in it", async () => {
  const r = rapportSetup(SOME_CHAT);
  r.brain.trackerEntries = [{ person: "Priya Rao", title: "Client deck", status: "open", due: "2026-11-20", progress: 40, doneSoFar: "draft shared" }];
  await r.engine.handleMessage(r.cto, `add ${PRIYA}`);
  const priya = r.store.personByEmail(PRIYA)!;
  assert.deepEqual(r.store.activeItems(priya.id).map((i) => [i.title, i.due, i.progress]), [["Client deck", "2026-11-20", 40]]);
  assert.equal(r.store.get(`rapport:${priya.id}`), "Casual. Says 'pls share'.");
});

test("a group with nothing open in its history gets no empty tracker posted", async () => {
  const t = trackerSetup();
  const direct = (t.engine as unknown as { direct: Record<string, unknown> }).direct;
  direct.history = async () => [];
  t.brain.trackerEntries = [];
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  assert.equal(t.created.length, 0);
  assert.match(t.admin.at(-1)!, /found no open work/);
});

// ---------- office hours and the timezone ----------

test("outside office hours replies are recorded but not answered; inside they are", async () => {
  const t = setup();
  await onboard(t);
  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleMessage(t.id(CTO), "hours 9 to 5"); // means 09:00-17:00
  assert.match(t.to(CTO).at(-1)!, /Office hours: \*\*09:00 to 17:00 UTC\*\*/);
  t.setDate("2026-11-16T10:00:00Z");
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  const toPriya = () => t.to(PRIYA).length;
  const asked = toPriya();

  t.setDate("2026-11-16T18:30:00Z"); // after hours
  await t.engine.handleMessage(t.id(PRIYA), "late update");
  assert.equal(toPriya(), asked);
  const priya = t.store.personByEmail(PRIYA)!;
  assert.ok(t.store.messages(t.store.checkin(priya.id, "2026-11-16")!.id).some((m) => m.text === "late update"));
  assert.equal(await t.engine.chaseFollowUps(0), 0);
  assert.equal(await t.engine.runReminders(0), 0);

  t.setDate("2026-11-16T16:00:00Z"); // back inside hours
  await t.engine.handleMessage(t.id(PRIYA), "one more thing");
  assert.equal(toPriya(), asked + 1);
});

test("scheduled messages to staff wait for office hours; the weekend is quiet", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "hours 09:30-18:30");
  await t.engine.handleMessage(t.id(CTO), "schedule morning 08:00"); // before office hours
  t.setDate("2026-11-17T08:00:00Z");
  await t.engine.tick();
  assert.equal(t.brain.turns.length, 0);
  t.setDate("2026-11-21T12:00:00Z"); // Saturday, inside the hours
  assert.equal(await t.engine.chaseFollowUps(0), 0);
});

test("the timezone can be set from the chat, and office hours are read in it", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "timezone India");
  assert.match(t.to(CTO).at(-1)!, /Asia\/Kolkata/);
  await t.engine.handleMessage(t.id(CTO), "hours 09:30-18:30");
  // 17:00 UTC is 22:30 in India: outside hours.
  assert.match(t.to(CTO).at(-1)!, /outside office hours now/);
  t.setDate("2026-11-16T05:00:00Z"); // 10:30 in India
  await t.engine.handleMessage(t.id(CTO), "hours");
  assert.match(t.to(CTO).at(-1)!, /It is office hours now/);
  await t.engine.handleMessage(t.id(CTO), "timezone Mars/Olympus");
  assert.match(t.to(CTO).at(-1)!, /do not know the timezone/);
  await t.engine.handleMessage(t.id(CTO), "hours off");
  assert.match(t.to(CTO).at(-1)!, /No office hours are set/);
});

// ---------- a timezone on each schedule ----------

test("a timezone is taken out of a phrase only when it is clearly one", () => {
  assert.deepEqual(takeZone("morning 09:30 India"), { rest: "morning 09:30", zone: "Asia/Kolkata" });
  assert.deepEqual(takeZone("priya evening 6pm UK time"), { rest: "priya evening 6pm", zone: "Europe/London" });
  assert.deepEqual(takeZone("09:30 Asia/Dubai"), { rest: "09:30", zone: "Asia/Dubai" });
  assert.deepEqual(takeZone("santosh morning 10:00"), { rest: "santosh morning 10:00", zone: "" });
});

test("each schedule runs in its own timezone, whatever the bot's own is", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "schedule morning 09:30 India"); // 04:00 UTC
  assert.match(t.to(CTO).at(-1)!, /\*\*09:30 India time\*\*/);
  await t.engine.handleMessage(t.id(CTO), "schedule priya evening 18:00 UK");
  assert.match(t.to(CTO).at(-1)!, /\*\*18:00 UK time\*\*/);

  t.setDate("2026-11-17T04:00:00Z"); // 09:30 in India
  await t.engine.tick();
  assert.equal(t.brain.turns.at(-1)?.round, "plan");
  assert.equal(t.brain.turns.length, 1);
});

test("office hours can carry their own timezone too", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.handleMessage(t.id(CTO), "hours 09:30-18:30 India");
  assert.match(t.to(CTO).at(-1)!, /09:30 to 18:30 India time/);
  t.setDate("2026-11-16T05:00:00Z"); // 10:30 in India
  await t.engine.handleMessage(t.id(CTO), "hours");
  assert.match(t.to(CTO).at(-1)!, /It is office hours now/);
  t.setDate("2026-11-16T14:00:00Z"); // 19:30 in India
  await t.engine.handleMessage(t.id(CTO), "hours");
  assert.match(t.to(CTO).at(-1)!, /outside office hours now/);
});

// ---------- the sent log, and approve-first drafts ----------

test("what is sent as the sender is logged and can be deleted", async () => {
  const g = groupSetup();
  const removed: string[] = [];
  const direct = (g.engine as unknown as { direct: Record<string, unknown> }).direct;
  let n = 0;
  const realSend = direct.send as (...a: unknown[]) => Promise<void>;
  direct.send = async (...a: unknown[]) => {
    await realSend(...a);
    return `msg-${++n}`;
  };
  direct.unsend = async (_chat: string, id: string) => void removed.push(id);
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const log = g.store.sentSince("");
  assert.equal(log.length, 1);
  assert.equal(log[0]!.label, "Recruitment Team");
  assert.equal(log[0]!.messageId, "msg-1");

  await g.engine.handleMessage(g.cto, `unsend ${log[0]!.id}`);
  assert.deepEqual(removed, ["msg-1"]);
  assert.match(g.admin.at(-1)!, /Deleted/);
  assert.equal(g.store.sentById(log[0]!.id)!.deleted, true);
});

test("on approve-first, messages become drafts: sent as edited, or skipped and taken out of the record", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "approve Recruitment Team");
  assert.match(g.admin.at(-1)!, /come to you first as a draft/);

  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.equal(g.sent.length, 0); // held
  const draftMsg = g.admin.find((m) => /Draft for Recruitment Team/.test(m))!;
  const id = Number(/draft send (\d+)/.exec(draftMsg)![1]);

  await g.engine.handleMessage(g.cto, `draft send ${id}\nHi team, quick update today please`);
  assert.equal(g.sent.length, 1);
  assert.equal(g.sent[0]!.text, "Hi team, quick update today please");
  const priya = g.store.personByEmail(PRIYA)!;
  assert.equal(g.store.messages(g.store.checkin(priya.id, "2026-11-16")!.id)[0]!.text, "Hi team, quick update today please");
  await g.engine.handleMessage(g.cto, `draft send ${id}`);
  assert.match(g.admin.at(-1)!, /already sent/);

  g.brain.doneAfterUserMessages = 99;
  await g.engine.handleGroupReply("u-priya", "3 submissions");
  const second = Number(/draft send (\d+)/.exec(g.admin.filter((m) => /Draft for/.test(m)).at(-1)!)![1]);
  await g.engine.handleMessage(g.cto, `draft skip ${second}`);
  assert.equal(g.sent.length, 1); // nothing more went out
  assert.ok(!g.store.messages(g.store.checkin(priya.id, "2026-11-16")!.id).some((m) => m.role === "bot" && m.text.includes("Question")));
});

test("approve-first is on for the rest of the day a group is added, then off by itself", async () => {
  const g = groupSetup();
  (g.engine as unknown as { config: Config }).config.approveFirstDay = true;
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.equal(g.sent.length, 0);
  g.setDate("2026-11-17T17:00:00Z");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.equal(g.sent.length, 1);
});

// ---------- work blocked on a colleague, and escalation ----------

test("work blocked on a colleague: shown on the tracker, and the colleague is asked in the group the next day", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  const ravi = t.store.personByEmail("ravi@acme.test")!;
  const id = t.store.addItem(ravi.id, "Website content", "blocked", "waiting for copy", "2026-11-16");
  t.store.setItemFlow(id, "blocked", "Priya", "2026-11-16");

  assert.equal(await t.engine.chaseDependencies(), 0); // not on the day it was raised
  await t.engine.refreshTrackers();
  assert.match(t.created.at(-1)!.html, /🟠 Blocked · waiting on Priya/);

  t.setDate("2026-11-17T10:00:00Z");
  assert.equal(await t.engine.chaseDependencies(), 1);
  const ask = t.sent.at(-1)!;
  assert.equal(ask.to, "g-rec");
  assert.deepEqual(ask.mentions, [{ userId: "u-priya", name: "Priya Rao" }]);
  assert.match(ask.text, /Website content/);
  assert.equal(await t.engine.chaseDependencies(), 0); // once a day

  // Priya's answer in the group is a check-in about that work.
  const priya = t.store.personByEmail(PRIYA)!;
  const checkin = t.store.checkin(priya.id, "2026-11-17")!;
  assert.equal(checkin.status, "open");
  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleGroupReply("u-priya", "will send tomorrow");
  assert.match(t.brain.turns.at(-1)!.topic!, /Ravi is waiting on them for "Website content"/);
});

test("blocked two days or overdue three days goes to the admins once, with a suggestion", async () => {
  const t = setup();
  await onboard(t);
  const priya = t.store.personByEmail(PRIYA)!;
  const a = t.store.addItem(priya.id, "Client deck", "blocked", "waiting", "2026-11-12");
  t.store.setItemFlow(a, "blocked", "Ravi", "2026-11-12");
  t.store.addItem(priya.id, "Landing page", "open", "half", "2026-11-01", "2026-11-10");
  t.store.addItem(priya.id, "Fresh task", "open", "", "2026-11-16", "2026-11-15"); // one day late: not yet

  t.setDate("2026-11-16T11:00:00Z");
  assert.equal(await t.engine.escalate(), 2);
  const msg = t.to(CTO).at(-1)!;
  assert.match(msg, /Needs a decision\*\* \(2\)/);
  assert.match(msg, /"Client deck" blocked 4 days, waiting on Ravi\. Suggest: ask Ravi for a firm date/);
  assert.match(msg, /"Landing page" 6 days overdue/);
  assert.equal(await t.engine.escalate(), 0); // once a day
  t.setDate("2026-11-17T11:00:00Z");
  assert.equal(await t.engine.escalate(), 0); // and each item once per reason
});

// ---------- leave, holidays and meetings ----------

test("people out of office, on leave or on a holiday are not asked or chased", async () => {
  const g = groupSetup();
  const direct = (g.engine as unknown as { direct: Record<string, unknown> }).direct;
  direct.awayBetween = async () => new Map([[PRIYA, [["2026-11-16T00:00:00Z", "2026-11-17T00:00:00Z"]]]]);
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.refreshLeave();
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.match(g.admin.at(-1)!, /\(1 people\)/); // only Ravi
  await g.engine.handleMessage(g.cto, "ask priya");
  assert.match(g.admin.at(-1)!, /on leave today/);

  await g.engine.handleMessage(g.cto, "leave ravi until 2026-11-20");
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  assert.equal(await g.engine.startCheckin(ravi, true), false);
  await g.engine.handleMessage(g.cto, "leave ravi off");
  assert.match(g.admin.at(-1)!, /is back/);

  await g.engine.handleMessage(g.cto, "holiday 2026-11-16 Founders Day");
  await g.engine.handleMessage(g.cto, "ask ravi");
  assert.match(g.admin.at(-1)!, /on leave today \(Founders Day\)/);
});

test("before a meeting you get a brief; after it, action items in its chat go on the tracker", async () => {
  const g = groupSetup();
  const toCto: string[] = [];
  const direct = (g.engine as unknown as { direct: Record<string, unknown> }).direct;
  direct.senderEmail = CTO;
  direct.meetings = async () => [
    { id: "m1", subject: "Weekly touchbase", start: "2026-11-16T17:20:00Z", end: "2026-11-16T17:50:00Z", attendees: [PRIYA, CTO], chatId: "" },
    { id: "m2", subject: "Website review", start: "2026-11-16T15:00:00Z", end: "2026-11-16T16:00:00Z", attendees: ["ravi@acme.test"], chatId: "19:meeting_x@thread.v2" },
  ];
  direct.history = async (chat: string) =>
    chat === "19:meeting_x@thread.v2"
      ? [
          { fromSender: true, from: "", text: "ravi pls finish the QA fixes by friday", at: "2026-11-16T15:30:00Z" },
          { fromSender: false, from: "Ravi Kumar", text: "ok will do", at: "2026-11-16T15:31:00Z" },
        ]
      : [];
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  g.admin.length = 0;
  const priya = g.store.personByEmail(PRIYA)!;
  g.store.addItem(priya.id, "Client deck", "open", "half done", "2026-11-10", "2026-11-12");
  g.brain.trackerEntries = [{ person: "Ravi Kumar", title: "QA fixes", status: "open", due: "2026-11-20", progress: -1, doneSoFar: "agreed in review" }];

  await g.engine.meetingCycle();
  const brief = g.admin.find((m) => /Before "Weekly touchbase"/.test(m))!;
  assert.match(brief, /\*\*Priya Rao\*\*\n- Client deck \(overdue, due 2026-11-12\)/);
  assert.match(g.admin.find((m) => /From "Website review"/.test(m))!, /Ravi Kumar: QA fixes \(due 2026-11-20\)/);
  const ravi = g.store.personByEmail("ravi@acme.test")!;
  assert.deepEqual(g.store.activeItems(ravi.id).map((i) => i.title), ["QA fixes"]);

  const before = g.admin.length;
  await g.engine.meetingCycle(); // within ten minutes: nothing again
  assert.equal(g.admin.length, before);
  void toCto;
});

// ---------- weekly report, recurring issues, AI use, backups ----------

test("the digest is given the week's blockers and pain points, to spot what keeps coming up", async () => {
  const t = setup();
  await onboard(t);
  await t.engine.runCheckins();
  t.brain.nextNote = {
    note: { summary: "x", progressVsTargets: "", blockers: ["Client portal down"], painPoints: ["Slow VPN"], helpNeeded: [], followUpTomorrow: [] },
    items: [],
  };
  await t.engine.handleMessage(t.id(PRIYA), "portal down again");
  await t.engine.runDigest();
  assert.match(t.brain.digestRecent.at(-1)!, /2026-11-16 priya: Client portal down/);
  assert.match(t.brain.digestRecent.at(-1)!, /Slow VPN/);
});

test("the weekly report goes out on Friday after hours, once, with plan against done", async () => {
  const t = setup();
  await onboard(t);
  const priya = t.store.personByEmail(PRIYA)!;
  t.store.addItem(priya.id, "Client deck", "done", "sent", "2026-11-17");
  t.setDate("2026-11-19T18:30:00Z"); // Thursday
  await t.engine.runWeekly();
  assert.equal(t.brain.weeklyInputs.length, 0);
  t.setDate("2026-11-20T18:30:00Z"); // Friday evening
  await t.engine.runWeekly();
  await t.engine.runWeekly();
  assert.equal(t.brain.weeklyInputs.length, 1);
  assert.equal(t.brain.weeklyInputs[0]!.from, "2026-11-16");
  assert.match(t.brain.weeklyInputs[0]!.material, /Finished this week: Client deck/);
  assert.equal(t.to(CTO).at(-1), "WEEKLY");
});

test("AI use is counted per day and model, and shown with a cost", async () => {
  const t = setup();
  await t.engine.handleInstall(t.id(CTO));
  t.engine.recordUsage("claude-haiku-5-5", 200_000, 10_000);
  t.engine.recordUsage("claude-sonnet-5-5", 10_000, 2_000);
  await t.engine.handleMessage(t.id(CTO), "usage");
  // 0.2M x $0.10 + 0.01M x $0.50 + 0.01M x $2 + 0.002M x $10 = 0.02 + 0.005 + 0.02 + 0.02
  assert.match(t.to(CTO).at(-1)!, /2026-11-16: ≈ \$0\.07 \(haiku-5-5 200k in, 10k out · sonnet-5-5 10k in, 2k out\)/);
});

test("a daily backup is made and only the last two weeks are kept", async () => {
  const { mkdtempSync, readdirSync, rmSync } = await import("node:fs");
  const { tmpdir } = await import("node:os");
  const path = await import("node:path");
  const dir = mkdtempSync(path.join(tmpdir(), "mands-"));
  try {
    const store = new Store(dir);
    store.set("x", "1");
    for (let d = 1; d <= 16; d++) store.backup(`2026-11-${String(d).padStart(2, "0")}`);
    const files = readdirSync(path.join(dir, "backups")).sort();
    assert.equal(files.length, 14);
    assert.equal(files[0], "mands-2026-11-03.db");
    store.close();
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ---------- TalentFlow figures ----------

test("with TalentFlow connected, a recruiter's real figures go into the check-in and the weekly report", async () => {
  const t = setup();
  await onboard(t);
  const calls: string[] = [];
  t.engine.ats = {
    stats: async (email, from) => {
      calls.push(`${email} ${from}`);
      return { l1_select: 1, submitted_to_client: 3 };
    },
  };
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.equal(
    t.brain.turns.at(-1)!.facts,
    "Today so far: 3 submitted to client, 1 L1 select.\nYesterday (2026-11-15): 3 submitted to client, 1 L1 select.",
  );
  assert.match(calls[0]!, /^priya@acme\.test 2026-11-16T00:00:00\.000Z$/);
  assert.match(calls[1]!, /^priya@acme\.test 2026-11-15T00:00:00\.000Z$/);

  t.brain.doneAfterUserMessages = 99;
  await t.engine.handleMessage(t.id(PRIYA), "sent 5 today");
  assert.equal(calls.length, 2); // fetched at most every 15 minutes

  t.setDate("2026-11-20T18:30:00Z");
  await t.engine.runWeekly(true);
  assert.match(t.brain.weeklyInputs.at(-1)!.material, /In TalentFlow this week: 3 submitted to client, 1 L1 select\./);
});

test("if TalentFlow cannot be reached the check-in goes ahead without figures", async () => {
  const t = setup();
  await onboard(t);
  t.engine.ats = { stats: async () => { throw new Error("down"); } };
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.equal(t.brain.turns.at(-1)!.facts, undefined);
  assert.equal(t.to(PRIYA).length > 0, true);
});

test("someone who is not a TalentFlow recruiter gets no figures, not 'nothing recorded'", async () => {
  const t = setup();
  await onboard(t);
  t.engine.ats = { stats: async () => null };
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.equal(t.brain.turns.at(-1)!.facts, undefined);
});

test("with a target for the month, progress against it goes into the check-in", async () => {
  const t = setup();
  await onboard(t);
  const froms: string[] = [];
  t.engine.ats = {
    stats: async (_email, from) => {
      froms.push(from);
      return from.startsWith("2026-11-01") ? { submitted_to_client: 7 } : { submitted_to_client: 2 };
    },
    targets: async (_email, month) => (month === "2026-11" ? { submitted_to_client: 20, onboarded: 1 } : null),
  };
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.equal(
    t.brain.turns.at(-1)!.facts,
    "Today so far: 2 submitted to client.\nYesterday (2026-11-15): 2 submitted to client.\nThis month against target: submitted to client 7 of 20, joined 0 of 1.",
  );
  assert.deepEqual(froms, ["2026-11-16T00:00:00.000Z", "2026-11-15T00:00:00.000Z", "2026-11-01T00:00:00.000Z"]);
});

test("a TalentFlow days behind is flagged in the check-in, worked out once a day, and the digest sees it", async () => {
  const t = setup();
  await onboard(t);
  let lastCalls = 0;
  t.engine.ats = {
    stats: async () => ({}),
    lastUpdate: async () => {
      lastCalls++;
      return "2026-11-12T10:00:00.000Z";
    },
  };
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  const facts = t.brain.turns.at(-1)!.facts!;
  assert.match(facts, /^Today so far: nothing recorded\./);
  assert.match(facts, /Last TalentFlow update: 2026-11-12, 4 days ago\. TalentFlow is behind\./);

  await t.engine.runDigest();
  assert.equal(lastCalls, 1); // once a day, though the digest fetched today's figures afresh
  const priya = t.brain.digestCalls.at(-1)!.find((p) => p.person.email === "priya@acme.test")!;
  assert.match(priya.talentflow!, /TalentFlow is behind/);
});

test("an update yesterday is not flagged as behind", async () => {
  const t = setup();
  await onboard(t);
  t.engine.ats = { stats: async () => ({}), lastUpdate: async () => "2026-11-15T16:00:00.000Z" };
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.doesNotMatch(t.brain.turns.at(-1)!.facts!, /behind/);
});

test("someone with two TalentFlow recruiter records under one email gets the work from both", async () => {
  const fake = {
    findApp: async () => ({ id: "app1", name: "TalentFlow", slug: "mats" }),
    users: async () => [],
    list: async (_app: string, entity: string, opts: { q?: { id?: { $in: string[] } } } = {}) => {
      if (entity === "Recruiter") return [{ id: "r-old", email: "Priya@Acme.test" }, { id: "r-new", email: "priya@acme.test" }];
      if (entity === "Candidate") return (opts.q?.id?.$in ?? []).map((id) => ({ id, full_name: `Candidate ${id}` }));
      if (entity === "Requirement" && opts.q?.id) return (opts.q.id.$in ?? []).map((id) => ({ id, job_title: `Role ${id}` }));
      if (entity !== "CandidateApplication") return [];
      const sent = [{ status: "Submitted to Client", changed_at: "2026-11-16T05:00:00Z" }];
      return [
        { recruiter_id: "r-old", candidate_id: "c1", requirement_id: "q1", status_history: sent },
        { recruiter_id: "r-new", candidate_id: "c2", requirement_id: "q2", status_history: sent },
      ];
    },
  };
  const tf = new Base44TalentFlow(fake as unknown as Base44, "mats");
  const team = await tf.team([PRIYA], "2026-11-16T00:00:00.000Z", "2026-11-01T00:00:00.000Z", "2026-11-16T12:00:00.000Z");
  const f = team.get(PRIYA)!;
  assert.equal(f.day.submitted_to_client, 2);
  assert.deepEqual(f.toClientToday, [
    { candidate: "Candidate c1", requirement: "Role q1" },
    { candidate: "Candidate c2", requirement: "Role q2" },
  ]);
});

test("TalentFlow figures count each move into a status inside the window, from Base44", async () => {
  const calls: { entity: string; q?: unknown }[] = [];
  const fake = {
    findApp: async (ref: string) => (ref === "mats" ? { id: "app1", name: "TalentFlow", slug: "mats" } : undefined),
    users: async () => [],
    list: async (_app: string, entity: string, opts: { q?: unknown } = {}) => {
      calls.push({ entity, q: opts.q });
      if (entity === "Recruiter") return [{ id: "r1", email: "Priya@Acme.test " }];
      return [
        {
          status_history: [
            { status: "Applied", changed_at: "2026-11-15T09:00:00Z" },
            { status: "Submitted to Client", changed_at: "2026-11-16T05:00:00Z" },
            { status: "L1 Interview Scheduled", changed_at: "2026-11-16T07:00:00Z" },
          ],
        },
        { status_history: [{ status: "Submitted to Client", changed_at: "2026-11-16T08:00:00Z" }, { status: "Client Hold", changed_at: "2026-11-16T09:00:00Z" }] },
      ];
    },
  };
  const tf = new Base44TalentFlow(fake as unknown as Base44, "mats");
  const counts = await tf.stats("priya@acme.test", "2026-11-16T00:00:00.000Z", "2026-11-16T12:00:00.000Z");
  assert.deepEqual(counts, { submitted_to_client: 2, l1_scheduled: 1, client_hold: 1 });
  assert.equal(describeCounts(counts!), "2 submitted to client, 1 on client hold, 1 L1 scheduled");
  assert.deepEqual(calls.at(-1)!.q, { recruiter_id: { $in: ["r1"] }, updated_date: { $gte: "2026-11-16T00:00:00" } });
  assert.equal(await tf.stats("nobody@acme.test", "2026-11-16T00:00:00.000Z", "2026-11-16T12:00:00.000Z"), null);
  assert.equal(calls.filter((c) => c.entity === "Recruiter").length, 1); // recruiters read once, not per call
});

// ---------- held drafts: nothing counts until it has really gone out ----------

function draftIds(admin: string[]): number[] {
  return admin.flatMap((m) => [...m.matchAll(/draft send (\d+)/g)].map((x) => Number(x[1])));
}

test("a group ask held for approval opens nothing; replies are not taken as answers until it is sent", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "approve Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.match(g.admin.at(-1)!, /Drafted the message for \*\*Recruitment Team\*\*/);
  const priya = g.store.personByEmail(PRIYA)!;
  assert.equal(g.store.checkin(priya.id, "2026-11-16"), undefined); // nothing opened
  assert.equal(g.engine.listeningGroups().length, 0);

  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  assert.match(g.admin.at(-1)!, /already waiting for your approval/); // no second draft

  const [id] = draftIds(g.admin);
  await g.engine.handleMessage(g.cto, `draft skip ${id}`);
  assert.equal(g.store.checkin(priya.id, "2026-11-16"), undefined);
  assert.equal(g.engine.listeningGroups().length, 0);
  await g.engine.handleGroupReply("u-priya", "random chat", "m1", g.store.groups()[0]!.id);
  assert.equal(g.brain.turns.length, 0);

  // Sent this time: now it is recorded and the group is read.
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
  const second = draftIds(g.admin).at(-1)!;
  await g.engine.handleMessage(g.cto, `draft send ${second}`);
  assert.equal(g.store.checkin(priya.id, "2026-11-16")!.status, "open");
  assert.equal(g.engine.listeningGroups().length, 1);
});

test("a private follow-up to a group on approve-first is held too, and the conversation waits for it", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "ask group Recruitment Team"); // sent: not on approve yet
  await g.engine.handleMessage(g.cto, "approve Recruitment Team");
  g.brain.doneAfterUserMessages = 99;
  g.brain.privateNext = true;
  await g.engine.handleGroupReply("u-priya", "was unwell", "m1");
  assert.equal(g.sent.filter((s) => s.to === `chat-${PRIYA}`).length, 0); // held, not sent one to one
  const priya = g.store.personByEmail(PRIYA)!;
  const checkin = g.store.checkin(priya.id, "2026-11-16")!;
  assert.ok(!g.store.messages(checkin.id).some((m) => m.text === "Private question"));

  const turns = g.brain.turns.length;
  await g.engine.handleGroupReply("u-priya", "also the deck is late", "m2");
  assert.equal(g.brain.turns.length, turns); // recorded, not answered while the reply waits
  assert.equal(await g.engine.chaseFollowUps(0), 0);

  const id = draftIds(g.admin).at(-1)!;
  await g.engine.handleMessage(g.cto, `draft send ${id}`);
  assert.equal(g.sent.at(-1)!.to, `chat-${PRIYA}`);
  assert.ok(g.store.messages(checkin.id).some((m) => m.role === "bot" && m.text === "Private question"));
});

test("a skipped chase is not drafted again, and a draft cannot be sent twice", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  await t.engine.handleMessage(t.id(CTO), `add ${PRIYA}`);
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  await t.engine.handleMessage(t.id(CTO), "approve priya");
  assert.equal(await t.engine.runReminders(0), 1);
  assert.equal(await t.engine.runReminders(0), 0); // waiting: not drafted again
  const id = draftIds(t.to(CTO)).at(-1)!;
  await t.engine.handleMessage(t.id(CTO), `draft skip ${id}`);
  assert.equal(await t.engine.runReminders(0), 0); // skipped counts as reminded
  await t.engine.handleMessage(t.id(CTO), `draft send ${id}`);
  assert.match(t.to(CTO).at(-1)!, /already skipped/);
});

test("unsend removes exactly what that message wrote into the record", async () => {
  const t = setup({ personal: true });
  await t.engine.handleInstall(t.id(CTO));
  const direct = (t.engine as unknown as { direct: Record<string, unknown> }).direct;
  let n = 0;
  const realSend = direct.send as (...a: unknown[]) => Promise<void>;
  direct.send = async (...a: unknown[]) => {
    await realSend(...a);
    return `m${++n}`;
  };
  direct.unsend = async () => undefined;
  await t.engine.handleMessage(t.id(CTO), `add ${PRIYA}`);
  await t.engine.handleMessage(t.id(CTO), "add ravi@acme.test");
  await t.engine.handleMessage(t.id(CTO), "ask all");
  await t.engine.runReminders(0); // the same chase text can go to both
  const priya = t.store.personByEmail(PRIYA)!;
  const ravi = t.store.personByEmail("ravi@acme.test")!;
  const priyaRows = t.store.messages(t.store.checkin(priya.id, "2026-11-16")!.id).map((m) => m.id);
  const chaseToPriya = t.store.sentSince("").find((m) => m.recordIds.length && m.recordIds.every((id) => priyaRows.includes(id)) && t.store.messages(t.store.checkin(priya.id, "2026-11-16")!.id).find((x) => x.id === m.recordIds[0])!.text === t.store.messages(t.store.checkin(priya.id, "2026-11-16")!.id).at(-1)!.text)!;
  const raviBefore = t.store.messages(t.store.checkin(ravi.id, "2026-11-16")!.id).length;
  await t.engine.handleMessage(t.id(CTO), `unsend ${chaseToPriya.id}`);
  assert.ok(!t.store.messages(t.store.checkin(priya.id, "2026-11-16")!.id).some((m) => chaseToPriya.recordIds.includes(m.id!)));
  assert.equal(t.store.messages(t.store.checkin(ravi.id, "2026-11-16")!.id).length, raviBefore); // untouched
});

test("a dependency chase sent one to one is read one to one", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  const ravi = t.store.personByEmail("ravi@acme.test")!;
  const priya = t.store.personByEmail(PRIYA)!;
  t.store.upsertPerson(PRIYA, { chatId: `chat-${PRIYA}` });
  t.store.set(`aadOf:${priya.id}`, ""); // cannot be tagged in the group
  const id = t.store.addItem(ravi.id, "Website content", "blocked", "waiting", "2026-11-15");
  t.store.setItemFlow(id, "blocked", "Priya", "2026-11-15");
  assert.equal(await t.engine.chaseDependencies(), 1);
  assert.equal(t.sent.at(-1)!.to, `chat-${PRIYA}`);
  const checkin = t.store.checkin(priya.id, "2026-11-16")!;
  assert.equal(t.store.get(`via:${checkin.id}`), "");
  assert.ok(t.engine.openDirectCheckins().some((c) => c.person.id === priya.id));
});

test("a colleague is chased only when the name can be only one person", async () => {
  const t = trackerSetup();
  await t.engine.handleMessage(t.cto, "add group Recruitment Team");
  t.store.upsertPerson("priya.k@acme.test", { name: "Priya Kumar" });
  t.store.setNominated(t.store.personByEmail("priya.k@acme.test")!.id, true);
  const ravi = t.store.personByEmail("ravi@acme.test")!;
  const a = t.store.addItem(ravi.id, "Copy", "blocked", "w", "2026-11-15");
  t.store.setItemFlow(a, "blocked", "Priya", "2026-11-15"); // two Priyas
  const b = t.store.addItem(ravi.id, "Signoff", "blocked", "w", "2026-11-15");
  t.store.setItemFlow(b, "blocked", "Rahul Sharma", "2026-11-15"); // someone at the client
  assert.equal(await t.engine.chaseDependencies(), 0);
});

test("out of office counts only while it lasts", async () => {
  const g = groupSetup();
  const direct = (g.engine as unknown as { direct: Record<string, unknown> }).direct;
  direct.awayBetween = async () => new Map([[PRIYA, [["2026-11-16T20:00:00Z", "2026-11-16T23:00:00Z"]]]]);
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.refreshLeave();
  await g.engine.handleMessage(g.cto, "ask priya"); // 17:00: not away yet
  assert.match(g.admin.at(-1)!, /Asked Priya Rao/);
});

test("asking for the weekly report by hand does not cancel Friday's", async () => {
  const t = setup();
  await onboard(t);
  t.setDate("2026-11-20T10:00:00Z");
  await t.engine.handleMessage(t.id(CTO), "weekly");
  t.setDate("2026-11-20T18:30:00Z");
  await t.engine.runWeekly();
  assert.equal(t.brain.weeklyInputs.length, 2);
});

// ---------- hourly progress, end of day, TalentFlow history ----------

test("every TalentFlow status is counted, including holds, hires, internal rejects and duplicates", () => {
  const at = "2026-11-20T09:00:00.000Z";
  const rows = ["Applied", "Client Hold", "Hired", "Screen Reject - Internal", "Client Duplicate", "Internal Duplicate"].map((status) => ({
    status_history: [{ status, changed_at: at }],
  }));
  const counts = countMoves(rows as never, Date.parse("2026-11-20T00:00:00Z"), Date.parse("2026-11-21T00:00:00Z"));
  assert.deepEqual(counts, { added: 1, client_hold: 1, hired: 1, internal_reject: 1, client_duplicate: 1, internal_duplicate: 1 });
  assert.match(describeCounts(counts), /^1 candidates added \(not yet sent to the client\), 1 internal screen reject/);
});

test("roles are read from the start of a profile", () => {
  assert.equal(roleOf("Recruiter. Does submissions only."), "Recruiter");
  assert.equal(roleOf("account owner for TCS"), "Account Owner");
  assert.equal(roleOf("Account Manager for Deloitte"), "Account Manager");
  assert.equal(roleOf("Recruitment Director. Owns the flow."), "Recruitment Director");
  assert.equal(roleOf("HR Manager. Oversees blockers."), "HR Manager");
  assert.equal(roleOf("HR. Not involved in recruitment work. Never ask for updates."), "HR Manager");
  assert.equal(roleOf("Developer"), null);
});

for (const [who, profile] of [
  ["the recruitment director", "Recruitment Director. Responsible for the team's overall performance."],
  ["HR", "HR. Not involved in recruitment work; in the group only to stay aware. Never ask for updates."],
  ["someone marked never to be asked", "Observer. Never ask for updates."],
] as const) {
  test(`${who} is not asked, chased or put on the tracker in the group`, async () => {
    const g = groupSetup();
    await g.engine.handleMessage(g.cto, "add group Recruitment Team");
    g.store.setProfile(g.store.personByEmail("ravi@acme.test")!.id, profile);
    await g.engine.handleMessage(g.cto, "ask group Recruitment Team");
    const ravi = g.store.personByEmail("ravi@acme.test")!;
    const priya = g.store.personByEmail(PRIYA)!;
    assert.equal(g.store.checkin(ravi.id, "2026-11-16"), undefined);
    assert.ok(g.store.checkin(priya.id, "2026-11-16"));
    assert.ok(!g.brain.groupAsks.at(-1)!.members.some((m) => m.name.startsWith("Ravi")));

    g.setDate("2026-11-16T17:50:00Z"); // past the chase
    await g.engine.tick();
    const chase = g.sent.find((s) => s.to === "g-rec" && s.text.includes("Any update?"));
    assert.deepEqual(chase?.mentions, [{ userId: "u-priya", name: "Priya Rao" }]);
  });
}

test("hourly progress and the end-of-day summary are posted in the group from TalentFlow", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "profile priya: Recruiter. Does submissions only.");
  await g.engine.handleMessage(g.cto, "profile ravi: Account Manager for TCS.");
  const posts: string[] = [];
  (g.engine as unknown as { direct: { postHtml: (c: string, h: string) => Promise<string> } }).direct.postHtml = async (c, h) => {
    posts.push(`${c}|${h}`);
    return `m${posts.length}`;
  };
  g.engine.ats = {
    stats: async () => ({}),
    team: async () =>
      new Map([
        [PRIYA, { day: { added: 5, submitted_to_client: 2, l1_select: 1 }, month: { added: 30, submitted_to_client: 9, onboarded: 1 }, lastToday: "2026-11-16T10:30:00.000Z", toClientToday: [{ candidate: "Asha Kumar", requirement: "ServiceNow Lead" }, { candidate: "Ben <Roy>", requirement: "SAP FICO" }] }],
        ["ravi@acme.test", { day: { new_requirements: 1, new_spots: 3 }, month: {}, lastToday: "2026-11-16T07:00:00.000Z" }],
      ]),
  };
  await g.engine.handleMessage(g.cto, "hourly Recruitment Team 11:00-17:00");
  assert.match(g.admin.at(-1)!, /Every hour from \*\*11:00 to 17:00 UTC\*\*/);
  await g.engine.handleMessage(g.cto, "clockout Recruitment Team 18:00");
  assert.match(g.admin.at(-1)!, /Clock-out for Recruitment Team is \*\*18:00 UTC\*\*/);

  g.setDate("2026-11-16T10:02:00Z");
  await g.engine.tick(); // before the first hour
  assert.equal(posts.length, 0);
  g.setDate("2026-11-16T11:02:00Z");
  await g.engine.tick();
  await g.engine.tick(); // once an hour
  assert.equal(posts.length, 1);
  assert.match(posts[0]!, /^g-rec\|<p><b>Team progress at 11:02<\/b><\/p>/);
  assert.match(posts[0]!, /Priya Rao<\/td><td>Recruiter<\/td><td>2 of 6<\/td><td>Asha Kumar – ServiceNow Lead<br>Ben &lt;Roy&gt; – SAP FICO<\/td><td>1<\/td><td>9 · 0 \(0% of 20%\) · 1 of 3<\/td><td>-<\/td><td>10:30<\/td>/);
  assert.match(posts[0]!, /Ravi Kumar<\/td><td>Account Manager<\/td><td>-<\/td><td>-<\/td><td>0<\/td><td>-<\/td><td>1 · 3 · 0<\/td>/);
  assert.match(posts[0]!, /<th>Submitted to client today<\/th><th>Candidate – requirement<\/th>/);
  assert.match(posts[0]!, /Team today: 2 of 6 submitted to client \(33%\), 1 L1 selects, 1 new requirements \(3 spots\)/);
  assert.match(posts[0]!, /No TalentFlow update in the last 2 hours: Ravi Kumar\./);
  assert.equal(g.engine.isGroupReport("m1"), true);
  assert.equal(g.sent.length, 0); // the posts are not chat messages, and nothing else was sent

  g.setDate("2026-11-16T18:03:00Z");
  await g.engine.tick();
  assert.equal(posts.length, 2);
  assert.match(posts[1]!, /<b>End of day summary, 16 Nov<\/b>/);
  g.setDate("2026-11-16T18:09:00Z");
  await g.engine.tick(); // once a day, and no hourly post after clock-out
  assert.equal(posts.length, 2);
});

test("the end-of-day summary carries a line per person and the director's team target met", async () => {
  const g = groupSetup();
  g.members.push({ userId: "u-madan", email: "madan@acme.test", name: "Madan Mani" });
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "profile priya: Recruiter.");
  await g.engine.handleMessage(g.cto, "profile madan: Recruitment Director.");
  const posts: string[] = [];
  (g.engine as unknown as { direct: { postHtml: (c: string, h: string) => Promise<string> } }).direct.postHtml = async (_c, h) => {
    posts.push(h);
    return "x";
  };
  const asked: { name: string; figures: string }[][] = [];
  (g.brain as unknown as { groupDayLines: (i: { people: { name: string; figures: string }[] }) => Promise<string> }).groupDayLines = async (i) => {
    asked.push(i.people);
    return "Priya Rao: 3 submissions; 3 more needed tomorrow.\nRavi Kumar: no update.";
  };
  g.engine.ats = {
    stats: async () => ({}),
    team: async () => new Map([[PRIYA, { day: { added: 7, submitted_to_client: 3 }, month: { onboarded: 2 }, lastToday: null }]]),
  };
  await g.engine.handleMessage(g.cto, "clockout Recruitment Team 18:00");
  g.setDate("2026-11-16T18:00:00Z");
  await g.engine.tick();
  assert.equal(posts.length, 1);
  assert.match(
    posts[0]!,
    /Madan Mani \(Recruitment Director\): 0 of 1 people reached 75% of today's submission target\. Below 75%: Priya Rao 50%\. Successful today only when everyone is at 75% or more: not yet\. Team: 50% of today's submissions, 67% of the month's joins so far\./,
  );
  assert.match(posts[0]!, /Priya Rao: 3 submissions; 3 more needed tomorrow\.<br>Ravi Kumar: no update\./);
  assert.doesNotMatch(posts[0]!, /<td>Madan Mani/); // the director is measured by the team, not a row
  assert.deepEqual(asked[0]!.map((p) => p.name), ["Priya Rao", "Ravi Kumar"]);
  assert.equal(asked[0]![0]!.figures, "7 candidates added (not yet sent to the client), 3 submitted to client");
});

test("'progress' posts the TalentFlow table in a group straight away", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "profile priya: Recruiter. Does submissions only.");
  const posts: string[] = [];
  (g.engine as unknown as { direct: { postHtml: (c: string, h: string) => Promise<string> } }).direct.postHtml = async (c, h) => {
    posts.push(`${c}|${h}`);
    return "m1";
  };
  g.engine.ats = { stats: async () => ({}), team: async () => new Map([[PRIYA, { day: { submitted_to_client: 1 }, month: {}, lastToday: null }]]) };
  await g.engine.handleMessage(g.cto, "progress Recruitment Team");
  assert.equal(posts.length, 1);
  assert.match(posts[0]!, /^g-rec|<p><b>Team progress at /);
  assert.match(g.admin.at(-1)!, /Posted the progress table in Recruitment Team/);
});

test("the board shows group chats first and individual chats below them", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "hourly Recruitment Team 11:00-17:00");
  const card = JSON.stringify((g.engine as unknown as { boardCard: () => unknown }).boardCard());
  const groups = card.indexOf("Group chats (1)");
  const people = card.indexOf("Individual chats");
  assert.ok(groups > 0 && people > groups, "groups come before individual chats");
  assert.ok(card.indexOf("Priya Rao") > people);
  assert.match(card, /Hourly progress 11:00 to 17:00/);
});

test("a question reads what was said in the groups since they were last read, the same day", async () => {
  const g = groupSetup();
  let lines: { fromSender: boolean; from: string; text: string; at: string }[] = [];
  (g.engine as unknown as { direct: { history: (c: string, m: number, s?: string) => Promise<typeof lines> } }).direct.history = async (_c, _m, since) =>
    lines.filter((l) => !since || l.at > since);
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  const before = g.brain.revisions.length;
  lines = [{ fromSender: false, from: "Madan Mani", text: "we can connect tomorrow on the targets", at: "2026-11-16T17:30:00.000Z" }];
  g.setDate("2026-11-16T17:40:00Z");
  await g.engine.handleMessage(g.cto, "what did madan say in the recruitment group about targets?");
  assert.equal(g.brain.revisions.length, before + 1);
  assert.match(g.brain.revisions.at(-1)!.transcript, /Madan Mani: we can connect tomorrow on the targets/);
  assert.equal(g.brain.questions.length, 1);

  await g.engine.handleMessage(g.cto, "and what else?");
  assert.equal(g.brain.revisions.length, before + 1); // nothing new: no second read
});

test("a new blocker in the group goes to the director, tagged, with someone else tagged for information, once", async () => {
  const g = groupSetup();
  g.members.push({ userId: "u-madan", email: "madan@acme.test", name: "Madan Mani" });
  g.members.push({ userId: "u-ceo", email: "ceo@acme.test", name: "Mahanth M" });
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "blockers Recruitment Team to madan fyi mahanth");
  assert.match(g.admin.at(-1)!, /I tag Madan Mani in the group to look into it, with Mahanth M tagged for information/);

  const priya = g.store.personByEmail(PRIYA)!;
  const old = g.store.addItem(priya.id, "Old issue from April", "blocked", "x", "2026-04-10");
  g.store.setItemFlow(old, "blocked", "", "2026-04-10");
  const item = g.store.addItem(priya.id, "No SAP requirements to work on", "blocked", "no reqs", "2026-11-16");
  g.store.setItemFlow(item, "blocked", "Madan", "2026-11-16");
  const before = g.sent.length;
  await g.engine.tick();
  assert.equal(g.sent.length, before + 1);
  const post = g.sent.at(-1)!;
  assert.equal(post.to, "g-rec");
  assert.equal(post.text, "{@0} please look into these blockers today: {@1}: No SAP requirements to work on (waiting on Madan). {@2} for your information.");
  assert.deepEqual(post.mentions!.map((m) => m.name), ["Madan Mani", "Priya Rao", "Mahanth M"]);

  await g.engine.tick(); // once only
  assert.equal(g.sent.length, before + 1);
});

test("the whole of TalentFlow is analysed and fed to answers and each person's brief", async () => {
  const t = setup();
  await onboard(t);
  t.engine.ats = {
    stats: async () => ({}),
    overview: async () => "Whole team, all time: 3000 submitted to client.",
    trackRecord: async (email) => (email === PRIYA ? "Submitted to client: 140." : null),
  };
  const briefsFor: (string | undefined)[] = [];
  (t.brain as unknown as Record<string, unknown>).talentflowOverview = async (i: { stats: string; targets: string }) =>
    `Overall: ${i.stats} Targets seen: ${/20% of submissions/.test(i.targets)}`;
  (t.brain as unknown as Record<string, unknown>).trackBrief = async (i: { record: string; team?: string }) => {
    briefsFor.push(i.team);
    return `Strengths: ${i.record}`;
  };
  await t.engine.handleMessage(t.id(CTO), "talentflow");
  assert.match(t.to(CTO).at(-1)!, /\*\*The whole of TalentFlow\*\*\nOverall: Whole team, all time: 3000 submitted to client\. Targets seen: true/);
  assert.deepEqual(briefsFor, ["Whole team, all time: 3000 submitted to client."]);

  await t.engine.handleMessage(t.id(CTO), "who is behind on submissions?");
  const material = t.brain.questions.at(-1)!.material;
  assert.match(material, /# TalentFlow, the whole team[\s\S]*## Analysis\nOverall: Whole team/);
  assert.match(material, /## Their track record in TalentFlow\nStrengths: Submitted to client: 140\./);
});

test("after clock-out the group is not chased", async () => {
  const g = groupSetup();
  await g.engine.handleMessage(g.cto, "add group Recruitment Team");
  await g.engine.handleMessage(g.cto, "clockout Recruitment Team 18:00");
  g.setDate("2026-11-16T17:30:00Z");
  await g.engine.handleMessage(g.cto, "ask group recruitment team");
  const asked = g.sent.length;
  g.setDate("2026-11-16T18:30:00Z"); // well past the chase time, but past clock-out too
  await g.engine.tick();
  assert.equal(g.sent.length, asked);
});

test("the TalentFlow history becomes a private brief that the check-ins see", async () => {
  const t = setup();
  await onboard(t);
  t.engine.ats = {
    stats: async () => ({}),
    trackRecord: async (email) => (email === PRIYA ? "Submitted to client: 140." : null),
  };
  (t.brain as unknown as { trackBrief: (i: { record: string }) => Promise<string> }).trackBrief = async ({ record }) => `Strengths: ${record}`;
  await t.engine.handleMessage(t.id(CTO), "talentflow");
  assert.match(t.to(CTO).at(-1)!, /\*\*TalentFlow track record\*\*[\s\S]*Strengths: Submitted to client: 140\./);
  await t.engine.handleMessage(t.id(CTO), "ask priya");
  assert.match(t.brain.turns.at(-1)!.facts!, /track record in TalentFlow \(private[\s\S]*Strengths: Submitted to client: 140\./);
});

test("a track record counts the funnel, merges client spellings and covers requirements owned", () => {
  const data = {
    clients: [
      { id: "c1", client_name: "Tech Mahindra" },
      { id: "c2", client_name: "TECH  MAHINDRA" },
    ],
    reqs: [{ id: "r1", client_id: "c1", job_title: "SN Dev", account_manager_id: "u1", positions_count: 2, status: "Open", created_date: "2026-11-02T10:00:00" }],
    apps: [
      {
        recruiter_id: "x",
        requirement_id: "r1",
        client_id: "c2",
        status: "Onboarded",
        status_history: [
          { status: "Submitted to Client", changed_at: "2026-11-03T10:00:00Z" },
          { status: "L1 Select", changed_at: "2026-11-05T10:00:00Z" },
          { status: "Onboarded", changed_at: "2026-11-10T00:00:00Z" },
        ],
      },
      {
        recruiter_id: "x",
        requirement_id: "r1",
        client_id: "c1",
        status: "L1 Reject",
        status_history: [
          { status: "Submitted to Client", changed_at: "2026-11-04T10:00:00Z" },
          { status: "L1 Reject", changed_at: "2026-11-06T10:00:00Z" },
        ],
      },
    ],
  };
  const text = describeTrackRecord(data, "x", "u1", "a@b.test", new Date("2026-11-19T00:00:00Z"));
  assert.match(text, /Submitted to client: 2\. Reached L1: 2\. L1 selects: 1 \(50% of submissions\)\. Final select or offered: 1\. Joined: 1 \(50%\)\./);
  assert.match(text, /Typical time from submission to L1 select: 2 days\./);
  assert.match(text, /2026-11: 2\/1\/1\./);
  assert.match(text, /Closed \(joined\) for: Tech Mahindra \(2 sub, 1 L1 sel 50%, 1 joined\)\./);
  assert.match(text, /Requirements brought in: 1 \(2 spots\), 1 still open or in progress\./);
  assert.match(text, /On their requirements: 2 submissions, 1 L1 selects, 1 joined \(50% of spots filled\)\./);
});
