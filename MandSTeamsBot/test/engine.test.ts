import assert from "node:assert/strict";
import { test } from "node:test";
import type { Brain, CheckinContext, DigestPerson, NoteResult, Turn } from "../src/ai.js";
import type { Config } from "../src/config.js";
import { Store } from "../src/db.js";
import { chunk, Engine } from "../src/engine.js";
import type { Identity } from "../src/engine.js";

const CTO = "cto@acme.test";
const CEO = "ceo@acme.test";
const PRIYA = "priya@acme.test";

const config: Config = {
  port: 0,
  adminEmails: [CTO, CEO],
  companyName: "Acme",
  timezone: "UTC",
  checkinCron: "",
  reminderCron: "",
  digestCron: "",
  maxBotTurns: 3,
  dataDir: ":memory:",
  model: "fake",
  sendAsUser: false,
  pollSeconds: 20,
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

  async nextTurn(ctx: CheckinContext): Promise<Turn> {
    if (this.failNextTurn) {
      this.failNextTurn = false;
      throw new Error("boom");
    }
    this.turns.push(ctx);
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
}

function setup(opts: { direct?: boolean } = {}) {
  const store = new Store(":memory:");
  const brain = new FakeBrain();
  const sent: { to: string; text: string }[] = [];
  let date = new Date("2026-11-16T17:00:00Z");
  const engine = new Engine(
    store,
    brain,
    { send: async (to, text) => void sent.push({ to, text }) },
    config,
    () => date,
    () => undefined,
    opts.direct
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
  assert.match(t.to(PRIYA).at(-1)!, /recorded/);

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
  assert.equal(priya.conversationId, `chat-${PRIYA}`);
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
