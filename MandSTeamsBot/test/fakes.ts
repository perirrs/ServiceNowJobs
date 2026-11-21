import type { AdminAction, Brain, CheckinContext, DigestPerson, InstructionAction, InstructionInput, NoteResult, Turn } from "../src/ai.js";

/** Scripted stand-in for Claude that records what it was shown. */
export class FakeBrain implements Brain {
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
