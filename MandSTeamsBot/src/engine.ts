import { displayName, renderItems, renderNote } from "./ai.js";
import type { AdminAction, Brain, CheckinContext, DigestPerson, Round } from "./ai.js";
import { todayIn } from "./config.js";
import type { Config } from "./config.js";
import type { ChatMessage, Checkin, Group, Person, Store } from "./db.js";

/** Sends a chat message to a Teams conversation. Implemented by teams.ts, faked in tests. */
export interface Messenger {
  send(conversationId: string, text: string): Promise<void>;
  /** Send an Adaptive Card and return the new message's id. Optional: without it the board falls back to text. */
  sendCard?(conversationId: string, card: Record<string, unknown>): Promise<string | void>;
  /** Replace the card in a message sent earlier. */
  updateCard?(conversationId: string, messageId: string, card: Record<string, unknown>): Promise<void>;
}

/**
 * Present when check-ins are sent from a real person's Teams account rather
 * than from the bot. Nominees are then reached in that person's own chats.
 */
export interface DirectChannel {
  senderName: string;
  /** Find or create the one-to-one chat with a colleague. */
  connect(
    email: string,
  ): Promise<{ conversationId: string; name: string } | { notFound: true; suggestions: string[] }>;
  send(
    conversationId: string,
    text: string,
    mentions?: { userId: string; name: string }[],
    /** The message being answered, shown quoted above the reply. */
    quote?: { messageId: string; preview: string; userId: string; name: string },
  ): Promise<void>;
  /** Post the tracker table in a group chat and pin it. Returns the new message's id. */
  createTracker?(chatId: string, html: string): Promise<string>;
  /** Replace the tracker table in place. */
  updateTracker?(chatId: string, messageId: string, html: string): Promise<void>;
  /** Read every watched chat now, so a decision to chase is made on what has really been said. */
  sync?(): Promise<void>;
  /** The sender's named group chats whose name contains the query. */
  findGroups?(query: string): Promise<{ chatId: string; name: string }[]>;
  /** Everyone in a group chat except the sender. */
  groupMembers?(chatId: string): Promise<{ userId: string; email: string; name: string }[]>;
  /**
   * Messages in one of the sender's chats, oldest first: the most recent `max`,
   * or with `since` only those after that time.
   */
  history?(
    conversationId: string,
    max: number,
    since?: string,
  ): Promise<{ fromSender: boolean; from: string; text: string; at: string }[]>;
}

/** Who a Teams activity came from. */
export interface Identity {
  teamsUserId: string;
  conversationId: string;
  name?: string;
  /** Looks the user's email up in Teams. Only called the first time we see someone. */
  lookupEmail: () => Promise<string | undefined>;
}

const ADMIN_HELP = `**Daily Check-in: what you can type**

- **pause**: stop sending anything to anyone, at once. **resume** starts again.
- **board** (or **menu**): the team board, with who has replied, who is blocked, and buttons for everything below
- **add** name@company.com: start asking someone for updates
- **remove** name: stop asking them (their notes are kept)
- **tracker build** Group Name: go through the group's whole chat history and build its tracker: each piece of work by person, what is done so far, progress and due date.
- **tracker** Group Name: post (or refresh) the tracker table in that group: who is doing what, due dates and status, pinned at the top and kept up to date by me. **tracker Group Name off** stops it.
- **add group** Group Name: follow a Teams group chat. Updates are asked for in the group, and follow-up questions stay there with the person tagged; only something that should not be said in front of the group goes to them one to one. **ask group** Name asks now, **remove group** Name stops.
- **ask** name: ask them for an update right now. **ask all** asks everyone.
- **profile** name: their role, responsibilities and targets, so the questions fit their job
- **style** name: what I learned from your past chat about how you write to them. **style name refresh** reads the chat again.
- **greeting** name Hi Anna: how to open messages to that person. I learn this from your past chat; use this to set or correct it. **greeting name off** goes back to a plain Hi.
- **list**: everyone you have added and where they stand today
- **report** name: their recent notes and open items
- **digest**: today's summary of everyone, right now
- **schedule morning 09:30** and **schedule evening 17:30**: the two daily messages, Monday to Friday. The morning one asks each person what they will work on today; the evening one checks what happened with that plan and what is blocking them. **schedule off** stops both, **schedule morning off** one of them, and **schedule** alone shows the settings.
- **schedule digest 19:30**: send the daily digest automatically at that time. **schedule digest off** stops it.
- **schedule priya morning 10:00**: give one person, or one group, a time of their own for that message instead of the general one. **schedule priya off** removes theirs.

Anything else you type is treated as a question about the team's notes, for example: _who has been blocked for more than two days?_

A first name or part of an email is enough once the person is added.`;

/** "17:00", "1700", "5pm", "5:30 pm" -> "17:00". Null if it is not a time. */
export function parseTime(input: string): string | null {
  const m = /^(\d{1,2})(?:[:.]?(\d{2}))?\s*(am|pm)?$/i.exec(input.trim());
  if (!m) return null;
  let hour = Number(m[1]);
  const minute = Number(m[2] ?? 0);
  const half = m[3]?.toLowerCase();
  if (half === "pm" && hour < 12) hour += 12;
  if (half === "am" && hour === 12) hour = 0;
  if (hour > 23 || minute > 59) return null;
  return `${String(hour).padStart(2, "0")}:${String(minute).padStart(2, "0")}`;
}

/** Turn a button press on the menu card into the command it stands for. */
export function cardToCommand(value: unknown): string {
  const v = (value ?? {}) as Record<string, unknown>;
  const str = (k: string) => (typeof v[k] === "string" ? (v[k] as string).trim() : "");
  switch (v.cmd) {
    case "add":
      return `add ${str("newEmail")}`;
    case "ask":
      return `ask ${str("who")}`;
    case "remove":
      return `remove ${str("who")}`;
    case "askAll":
      return "ask all";
    case "addGroup":
      return `add group ${str("groupName")}`;
    case "askGroup":
      return `ask group ${str("who")}`;
    case "removeGroup":
      return `remove group ${str("who")}`;
    case "digest":
      return "digest";
    case "list":
      return "list";
    case "details":
      return `report ${str("who")}`;
    case "refresh":
      return "refresh";
    case "pause":
      return "pause";
    case "resume":
      return "resume";
    case "schedule":
      return `schedule ${str("time")}`;
    case "scheduleOff":
      return "schedule off";
    case "scheduleFor":
      return ["schedule", str("who"), str("slot"), str(str("field"))].filter(Boolean).join(" ");
    case "scheduleOffFor":
      return ["schedule", str("who"), "off"].filter(Boolean).join(" ");
    default:
      return "board";
  }
}

const MESSAGE_LIMIT = 12000;

/** Split long text at paragraph breaks so each part fits in one Teams message. */
export function chunk(text: string, limit = MESSAGE_LIMIT): string[] {
  const parts: string[] = [];
  let current = "";
  for (const para of text.split(/\n{2,}/)) {
    const candidate = current ? `${current}\n\n${para}` : para;
    if (candidate.length <= limit) {
      current = candidate;
      continue;
    }
    if (current) parts.push(current);
    current = para;
    while (current.length > limit) {
      parts.push(current.slice(0, limit));
      current = current.slice(limit);
    }
  }
  if (current) parts.push(current);
  return parts;
}

function daysBefore(date: string, days: number): string {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() - days);
  return d.toISOString().slice(0, 10);
}

/** The two daily messages. Morning asks for the plan; evening reviews the day. */
const SLOTS = ["morning", "evening"] as const;
type Slot = (typeof SLOTS)[number];
const roundOf = (slot: Slot): Round => (slot === "morning" ? "plan" : "review");
/** A time before noon belongs to the morning message, anything later to the evening one. */
const slotOf = (time: string): Slot => (time < "12:00" ? "morning" : "evening");
const SLOT_LABEL: Record<Slot, string> = { morning: "morning plan", evening: "end-of-day update" };

/** Chases, sent from the sender's account. Picked at random, never the same one twice running. */
const FIRST_CHASES = [
  "Any update? Please share when you can.",
  "hi, any update today?",
  "waiting for your update",
  "pls share the update when free",
  "any update on this?",
  "need your update for today",
];
const FOLLOWUP_CHASES = ["Any update on this?", "?", "any update?", "waiting on this one", "pls check above", "hello?"];

const clamp = (n: number, low: number, high: number) => Math.min(high, Math.max(low, n));
/** Plus or minus a quarter, so replies do not arrive like clockwork. */
const jitter = (ms: number) => Math.round(ms * (0.75 + Math.random() * 0.5));

/** What a person wrote that is waiting for an answer. Several quick messages are taken together. */
interface Incoming {
  text: string;
  fromGroup: boolean;
  /** Teams id of their (last) message, for quoting in a group reply. */
  messageId: string;
  /** Our own record ids of these messages. */
  ids: number[];
}

/** A reply that has been written but not yet sent. */
interface Draft {
  turn: { reply: string; done: boolean; private?: boolean };
  incoming: Incoming;
  /** How many messages the person had sent when this was written. */
  userCount: number;
}

/** A date the model gave, kept only if it really is YYYY-MM-DD. */
/** A progress figure the model gave, kept only if it is 0 to 100; done is always 100. */
const validProgress = (value: number | undefined, status: string): number | null =>
  status === "done" ? 100 : typeof value === "number" && value >= 0 && value <= 100 ? Math.round(value) : null;

/** "▰▰▰▱▱ 60%" */
const progressBar = (p: number) => `${"▰".repeat(Math.round(p / 20))}${"▱".repeat(5 - Math.round(p / 20))} ${p}%`;

const validDate = (value: string | undefined) => (value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : "");

export class Engine {
  /** One queue per person, so two quick messages are handled in order. */
  private queues = new Map<number, Promise<unknown>>();

  constructor(
    private store: Store,
    private brain: Brain,
    private messenger: Messenger,
    private config: Config,
    private now: () => Date = () => new Date(),
    private log: (msg: string, err?: unknown) => void = (msg, err) =>
      err ? console.error(msg, err) : console.log(msg),
    private direct?: DirectChannel,
  ) {
    this.migrateSchedules();
  }

  /** Times saved before there were two daily messages: file each under morning or evening. */
  private migrateSchedules(): void {
    for (const key of this.store.keys("schedule:")) {
      const parts = key.split(":");
      const legacy =
        key === "schedule:checkin" || ((parts[1] === "person" || parts[1] === "group") && parts.length === 3);
      if (!legacy) continue;
      const time = this.store.get(key) ?? "";
      if (time) {
        const target = key === "schedule:checkin" ? `schedule:${slotOf(time)}` : `${key}:${slotOf(time)}`;
        if (!this.store.get(target)) this.store.set(target, time);
      }
      this.store.set(key, "");
    }
  }

  /** What a conversation started now is for, when nobody said: plan in the morning, review late in the day. */
  private roundNow(): Round {
    const { minutes } = this.localNow();
    return minutes < 12 * 60 ? "plan" : minutes >= 15 * 60 ? "review" : "update";
  }

  /**
   * Whether this person should be asked now. Never while they are in the
   * middle of replying. On a schedule, once per kind of conversation per
   * day; by hand, whenever asked.
   */
  private dueFor(person: Person, date: string, round: Round, force: boolean): boolean {
    const c = this.store.checkin(person.id, date);
    if (!c) return true;
    if (c.status === "open" && this.sinceAsk(c.id).some((m) => m.role === "user")) return false;
    return force || !this.store.get(`round:${c.id}:${round}`);
  }

  private today(): string {
    return todayIn(this.config.timezone, this.now());
  }

  /** Sending as a real person without mentioning automation: write and behave as that person would. */
  private get personal(): boolean {
    return !!this.direct && !this.config.discloseAutomation;
  }

  private async notifyAdmins(text: string): Promise<void> {
    for (const email of this.config.adminEmails) {
      const admin = this.store.personByEmail(email);
      if (!admin?.conversationId) continue;
      try {
        for (const part of chunk(text)) await this.messenger.send(admin.conversationId, part);
      } catch (err) {
        this.log(`Could not notify ${email}`, err);
      }
    }
  }

  private isAdmin(person: Person): boolean {
    return this.config.adminEmails.includes(person.email);
  }

  /**
   * Where a nominee's check-in goes: the sender's own chat with them in
   * send-as-user mode, otherwise their chat with the bot. Null if unreachable.
   */
  private address(person: Person): string | null {
    return this.direct ? person.chatId : person.conversationId;
  }

  /** True when an admin has told the bot to send nothing to anyone. Admin replies still work. */
  private get paused(): boolean {
    return this.store.get("paused") === "1";
  }

  /** Message a nominee as part of their check-in. */
  private async say(person: Person, text: string): Promise<void> {
    if (this.paused) throw new Error("Paused: nothing is sent to staff");
    const to = this.address(person);
    if (!to) throw new Error(`No chat to reach ${person.email} in`);
    const channel = this.direct ?? this.messenger;
    for (const part of chunk(text)) await channel.send(to, part);
  }

  /** Message an admin in their chat with the bot. One person can be both admin and nominee. */
  private async sayAdmin(admin: Person, text: string): Promise<void> {
    if (!admin.conversationId) return;
    for (const part of chunk(text)) await this.messenger.send(admin.conversationId, part);
  }

  private enqueue<T>(personId: number, job: () => Promise<T>): Promise<T> {
    const prev = this.queues.get(personId) ?? Promise.resolve();
    const next = prev.catch(() => undefined).then(job);
    this.queues.set(personId, next);
    return next;
  }

  private welcomeText(person: Person): string {
    const first = person.name?.split(" ")[0];
    if (this.direct) {
      return `Hi${first ? ` ${first}` : ""}, a heads-up from ${this.direct.senderName}: from now on a daily check-in will arrive in this chat each working day. It is sent automatically by an AI assistant from my account, so those messages are not me typing. It asks what you got done, how earlier things turned out, and anything that's in your way. It usually takes a couple of minutes.

Your answers are summarised each day for me and the CEO, so we can see where people need help. If you need me personally, just say so here and I'll pick it up myself.`;
    }
    return `Hi${first ? ` ${first}` : ""}, I'm the daily check-in assistant for ${this.config.companyName}. Each working day I'll message you here for a quick update on your work: what you got done, how earlier things turned out, and anything that's in your way. It usually takes a couple of minutes.

So you know how it's used: your answers are summarised each day for the CTO and CEO, so they can see where people need help. You can also message me at any time to add to your update.`;
  }

  // ---------- identity ----------

  /**
   * Work out who a Teams user is. Returns undefined if we cannot tell
   * (no email available), in which case the caller should do nothing.
   */
  private async identify(id: Identity): Promise<{ person: Person; firstContact: boolean } | undefined> {
    const known = this.store.personByTeamsId(id.teamsUserId);
    if (known) {
      if (known.conversationId !== id.conversationId || (!known.name && id.name)) {
        return {
          person: this.store.upsertPerson(known.email, {
            name: known.name ?? id.name,
            conversationId: id.conversationId,
          }),
          firstContact: false,
        };
      }
      return { person: known, firstContact: false };
    }

    const email = (await id.lookupEmail())?.toLowerCase();
    if (!email) {
      this.log(`Could not resolve an email for Teams user ${id.teamsUserId}`);
      return undefined;
    }
    // Only keep records for people we have a reason to know: admins and nominees.
    const existing = this.store.personByEmail(email);
    if (!existing && !this.config.adminEmails.includes(email)) {
      return {
        person: {
          id: 0,
          email,
          name: id.name ?? null,
          teamsUserId: id.teamsUserId,
          conversationId: id.conversationId,
          chatId: null,
          nominated: false,
          profile: null,
        },
        firstContact: true,
      };
    }
    const person = this.store.upsertPerson(email, {
      name: id.name,
      teamsUserId: id.teamsUserId,
      conversationId: id.conversationId,
    });
    return { person, firstContact: true };
  }

  // ---------- inbound from Teams ----------

  /** The bot was installed for (or opened by) a user. */
  async handleInstall(id: Identity): Promise<void> {
    const who = await this.identify(id);
    if (!who?.firstContact) return;
    const { person } = who;
    if (this.isAdmin(person)) await this.sayAdmin(person, ADMIN_HELP);
    else if (person.nominated) await this.say(person, this.welcomeText(person));
    // Everyone else hears nothing: an org-wide install should not message the whole company.
  }

  async handleMessage(id: Identity, text: string): Promise<void> {
    const who = await this.identify(id);
    if (!who) return;
    const { person, firstContact } = who;

    if (this.isAdmin(person)) {
      await this.enqueue(person.id, () => this.handleAdmin(person, text.trim()));
      await this.refreshBoards();
      return;
    }
    if (!person.nominated) {
      await this.messenger.send(
        id.conversationId,
        `Thanks for getting in touch. I run daily check-ins for people nominated by ${this.config.companyName}'s leadership, and you're not on that list at the moment, so there's nothing you need to do here.`,
      );
      return;
    }
    if (firstContact) {
      await this.say(person, this.welcomeText(person));
      return;
    }
    await this.enqueue(person.id, () => this.handleUpdate(person, text.trim()));
    await this.refreshBoards();
  }

  // ---------- the check-in conversation ----------

  private context(person: Person, checkin: Checkin, finalTurn = false, fromGroup = false, opening = false): CheckinContext {
    return {
      date: checkin.date,
      person,
      activeItems: this.store.activeItems(person.id),
      recentNotes: this.store.recentNotes(person.id, 5, checkin.date),
      history: this.store.messages(checkin.id),
      finalTurn,
      fromGroup,
      rapport: this.personal ? this.memoryFor(person) : undefined,
      topic: this.store.get(`topic:${checkin.id}`) || undefined,
      round: (this.store.get(`kind:${checkin.id}`) as Round | undefined) || undefined,
      opening,
      greeting: this.personal ? this.store.get(`greeting:${person.id}`) || undefined : undefined,
    };
  }

  /** What the assistant remembers about a person: their own memory, plus that of any group they are in. */
  private memoryFor(person: Person): string | undefined {
    const parts = [this.store.get(`rapport:${person.id}`) || ""];
    for (const groupId of this.store.groupsOf(person.id)) {
      const group = this.store.groups().find((g) => g.id === groupId);
      const memory = this.store.get(`gmemory:${groupId}`);
      if (group && memory) parts.push(`From the group chat "${group.name}":\n${memory}`);
    }
    return parts.filter(Boolean).join("\n\n") || undefined;
  }

  /**
   * Make sure the memory of this person is ready and current before asking
   * them anything: read their chat history the first time, and after that
   * fold in whatever has been said in the chat since the last look, once a day.
   */
  private async refreshMemory(person: Person): Promise<void> {
    if (!this.personal || !this.direct?.history || !person.chatId) return;
    const key = `rapport:${person.id}`;
    if (this.store.get(key) === undefined) {
      await this.learnRapport(person);
      return;
    }
    if (this.store.get(`memday:${person.id}`) === this.today()) return;
    try {
      const since = this.store.get(`memcursor:${person.id}`) || "";
      const own = new Set(this.store.assistantTexts(person.id).map((t) => t.replace(/\s+/g, " ").trim()));
      const fresh = (await this.direct.history(person.chatId, this.config.historyMessages, since)).filter(
        (m) => !(m.fromSender && own.has(m.text.replace(/\s+/g, " ").trim())),
      );
      if (fresh.length) {
        const transcript = fresh
          .map((m) => `${m.at.slice(0, 10)} ${m.fromSender ? this.direct!.senderName : displayName(person)}: ${m.text.slice(0, 300)}`)
          .join("\n");
        const revised = await this.brain.reviseMemory({
          about: `${displayName(person)}, and how ${this.direct.senderName} works with them`,
          senderName: this.direct.senderName,
          memory: this.store.get(key) || "",
          transcript,
          today: this.today(),
        });
        if (revised) this.store.set(key, revised);
      }
      this.store.set(`memcursor:${person.id}`, this.now().toISOString());
      this.store.set(`memday:${person.id}`, this.today());
    } catch (err) {
      this.log(`Could not update the memory of ${person.email}`, err);
    }
  }

  /** The same for a group chat: its whole recent history the first time, then what is new each day. */
  private async refreshGroupMemory(group: Group): Promise<void> {
    if (!this.personal || !this.direct?.history) return;
    if (this.store.get(`gmemday:${group.id}`) === this.today()) return;
    try {
      const since = this.store.get(`gmemcursor:${group.id}`) || "";
      const fresh = (await this.direct.history(group.chatId, this.config.historyMessages, since)).filter(
        (m) => !(m.fromSender && m.text.startsWith("Team tracker")),
      );
      if (fresh.length) {
        const revised = await this.brain.reviseMemory({
          about: `the group chat "${group.name}" and the work of each person in it`,
          senderName: this.direct.senderName,
          memory: this.store.get(`gmemory:${group.id}`) || "",
          transcript: fresh
            .map((m) => `${m.at.slice(0, 10)} ${m.fromSender ? this.direct!.senderName : m.from || "Someone"}: ${m.text.slice(0, 300)}`)
            .join("\n"),
          today: this.today(),
        });
        if (revised) this.store.set(`gmemory:${group.id}`, revised);
      }
      this.store.set(`gmemcursor:${group.id}`, this.now().toISOString());
      this.store.set(`gmemday:${group.id}`, this.today());
    } catch (err) {
      this.log(`Could not update the memory of group ${group.name}`, err);
    }
  }

  /**
   * Read the sender's past chat with this person once, and keep a short note of
   * how the sender writes to them. Done the first time they are asked for an
   * update; after that the stored note is reused unless `force` is set.
   */
  private async learnRapport(person: Person, force = false): Promise<string | undefined> {
    const key = `rapport:${person.id}`;
    const known = this.store.get(key);
    if (known !== undefined && !force) return known || undefined;
    if (!this.personal || !this.direct?.history || !person.chatId) return undefined;
    try {
      // Leave out what the assistant itself sent, so it learns the person's style and not its own.
      const own = new Set(this.store.assistantTexts(person.id).map((t) => t.replace(/\s+/g, " ").trim()));
      const chat = (await this.direct.history(person.chatId, this.config.historyMessages)).filter(
        (m) => !(m.fromSender && own.has(m.text.replace(/\s+/g, " ").trim())),
      );
      this.store.set(`memcursor:${person.id}`, this.now().toISOString());
      this.store.set(`memday:${person.id}`, this.today());
      // Too little to learn from: remember that, so we do not look again every time.
      let text = "";
      // Enough to learn a tone from, or at least enough to know what is going on.
      if (chat.filter((m) => m.fromSender).length >= 3 || chat.length >= 8) {
        const learned = await this.brain.learnRapport(person, this.direct.senderName, chat, this.today());
        text = learned.brief;
        // A greeting the admin set by hand is never overwritten by what was learned.
        const address = learned.address.replace(/^hi\b[\s,]*/i, "").replace(/[\n,.!]+/g, " ").trim().slice(0, 30);
        if (address && this.store.get(`greeting:${person.id}`) === undefined) {
          this.store.set(`greeting:${person.id}`, `Hi ${address}`);
        }
      }
      this.store.set(key, text);
      return text || undefined;
    } catch (err) {
      this.log(`Could not read the chat history with ${person.email}`, err);
      return undefined;
    }
  }

  /** Send today's opening question. Returns false if the person cannot be reached or already has one. */
  async startCheckin(person: Person, force = false, topic = "", round: Round = this.roundNow()): Promise<boolean> {
    if (this.paused) return false;
    if (!this.address(person)) return false;
    const date = this.today();
    const existing = this.store.checkin(person.id, date);
    if (!this.dueFor(person, date, round, force)) return false;

    await this.refreshMemory(person);
    const checkin = this.store.openCheckin(person.id, date);
    this.beginAsk(checkin);
    this.store.set(`via:${checkin.id}`, "");
    this.store.set(`topic:${checkin.id}`, topic);
    this.store.set(`kind:${checkin.id}`, round);
    try {
      const turn = await this.brain.nextTurn(this.context(person, checkin, false, false, true));
      // Deliver first, record second: the record must only show what was really sent.
      await this.say(person, turn.reply);
      this.store.addMessage(checkin.id, "bot", turn.reply);
      this.store.set(`round:${checkin.id}:${round}`, "1");
    } catch (err) {
      // Nothing reached the person, so leave today free for another attempt.
      if (existing) this.store.setCheckinStatus(checkin.id, existing.status);
      else this.store.discardEmptyCheckin(checkin.id);
      throw err;
    }
    return true;
  }

  /**
   * A colleague wrote in their one-to-one chat with the sender. Only counts
   * while today's check-in is open: outside it, the chat is ordinary conversation.
   */
  async handleDirectReply(personId: number, text: string): Promise<void> {
    const person = this.store.personById(personId);
    if (!person?.nominated) return;
    await this.enqueue(person.id, async () => {
      const checkin = this.store.checkin(person.id, this.today());
      if (checkin?.status === "open") await this.handleUpdate(person, text.trim());
    });
    await this.refreshBoards();
  }

  /** The sender typed in the chat themselves: the assistant stays quiet there for the rest of the day. */
  manualTakeover(personId: number): void {
    const checkin = this.store.checkin(personId, this.today());
    if (checkin?.status === "open") this.store.set(`takeover:${checkin.id}`, "manual");
  }

  /** People whose check-in is open today and who are reached through the direct channel. */
  openDirectCheckins(): { person: Person; checkin: Checkin }[] {
    if (!this.direct) return [];
    return this.store
      .checkinsOn(this.today())
      .filter((c) => c.status === "open")
      // Someone asked in a group is answered in the group. Their one-to-one chat
      // with the sender is their own business unless a private follow-up is waiting there.
      .filter((c) => !this.store.get(`via:${c.id}`) || this.store.get(`lastwhere:${c.id}`) === "direct")
      .map((checkin) => ({ person: this.store.personById(checkin.personId), checkin }))
      .filter((x): x is { person: Person; checkin: Checkin } => !!x.person?.nominated && !!x.person.chatId);
  }

  /** Whether a message seen in a group, sent from the sender's account, is one the assistant posted. */
  isAssistantGroupText(groupId: number, text: string): boolean {
    const norm = (t: string) => t.replace(/@/g, "").replace(/\s+/g, " ").trim();
    const seen = norm(text);
    const date = this.today();
    for (const member of this.store.groupMembers(groupId)) {
      const checkin = this.store.checkin(member.id, date);
      if (!checkin) continue;
      for (const m of this.store.messages(checkin.id)) {
        const recorded = norm(m.text);
        // A follow-up is recorded without the tag in front of it, hence the suffix match.
        if (m.role === "bot" && recorded && (seen === recorded || seen.endsWith(` ${recorded}`))) return true;
      }
    }
    return false;
  }

  /**
   * The sender wrote in a group themselves. The assistant steps back for the
   * people that message was aimed at (tagged or replied to); if it was aimed
   * at nobody in particular, for everyone there who is in the middle of a conversation.
   */
  senderSpokeInGroup(groupId: number, addressedUserIds: string[]): void {
    const date = this.today();
    const members = this.store.groupMembers(groupId);
    const aimedAt = members.filter((p) => addressedUserIds.includes(this.store.get(`aadOf:${p.id}`) ?? "-"));
    const targets = addressedUserIds.length
      ? aimedAt
      : members.filter((p) => {
          const c = this.store.checkin(p.id, date);
          return c?.status === "open" && this.store.messages(c.id).some((m) => m.role === "user");
        });
    for (const p of targets) this.manualTakeover(p.id);
  }

  /**
   * Conversations still open from an earlier day: summarise the ones with
   * replies, and mark the rest as unanswered, so nothing is left hanging.
   */
  async closePastDays(): Promise<number> {
    let closed = 0;
    for (const checkin of this.store.openCheckinsBefore(this.today())) {
      const person = this.store.personById(checkin.personId);
      const replied = this.sinceAsk(checkin.id).some((m) => m.role === "user");
      if (!person || !replied) {
        this.closeUnanswered(checkin);
        closed++;
        continue;
      }
      try {
        await this.enqueue(person.id, () => this.finish(person, checkin));
        closed++;
      } catch (err) {
        this.log(`Could not close the ${checkin.date} check-in for ${person.email}`, err);
        // Try again on later ticks, but not for ever.
        const tries = Number(this.store.get(`closefail:${checkin.id}`) || 0) + 1;
        this.store.set(`closefail:${checkin.id}`, String(tries));
        if (tries >= 5) this.store.setCheckinStatus(checkin.id, "completed");
      }
    }
    return closed;
  }

  /**
   * How long a person would take to read what was written, and to type a
   * reply, in milliseconds. A short "Ok, noted." goes out in about ten
   * seconds; a long reply takes a minute or more.
   */
  pace = {
    read: (text: string) => jitter(clamp(4000 + text.length * 40, 5000, 25_000)),
    type: (reply: string) => jitter(clamp(reply.length * 220, 4000, 100_000)),
  };

  /** Messages received and waiting to be "read", per check-in. A new message restarts the wait. */
  private pending = new Map<number, { timer: ReturnType<typeof setTimeout>; ids: number[]; text: string }>();

  private async handleUpdate(person: Person, text: string, fromGroup = false, messageId = ""): Promise<void> {
    if (!text) return;
    const date = this.today();
    let checkin = this.store.checkin(person.id, date);
    // A message outside an open check-in (before the daily prompt, or after
    // closing) opens or reopens today's, so nothing a person says is lost.
    if (!checkin || checkin.status !== "open") checkin = this.store.openCheckin(person.id, date);
    const recordId = this.store.addMessage(checkin.id, "user", text);
    // Recorded for tonight's note, but the human has taken over this chat.
    if (this.store.get(`takeover:${checkin.id}`)) return;
    // Paused: what they said is kept, and nothing is sent back.
    if (this.paused) return;

    if (!this.config.humanDelay) {
      const draft = await this.draftReply(person, checkin, { text, fromGroup, messageId, ids: [recordId] });
      if (draft) await this.deliverReply(person, checkin, draft);
      return;
    }

    // At a human pace: wait as long as it takes to read this, then answer. If
    // they write again in the meantime, start the wait again and answer it all at once.
    const waiting = this.pending.get(checkin.id);
    if (waiting) clearTimeout(waiting.timer);
    const incoming: Incoming = {
      text: waiting ? `${waiting.text}\n${text}` : text,
      fromGroup,
      messageId,
      ids: [...(waiting?.ids ?? []), recordId],
    };
    const checkinId = checkin.id;
    const timer = setTimeout(() => {
      this.pending.delete(checkinId);
      void this.answerAtHumanPace(person.id, checkinId, incoming);
    }, this.pace.read(incoming.text));
    this.pending.set(checkinId, { timer, ids: incoming.ids, text: incoming.text });
  }

  /** The check-in, if it is still one the assistant should be speaking in. */
  private liveCheckin(personId: number, checkinId: number): { person: Person; checkin: Checkin } | null {
    const person = this.store.personById(personId);
    const checkin = this.store.checkin(personId, this.today());
    if (!person || !checkin || checkin.id !== checkinId || checkin.status !== "open") return null;
    if (this.store.get(`takeover:${checkin.id}`) || this.paused) return null;
    return { person, checkin };
  }

  /** Write the reply, "type" it for a believable time, then send it if nothing has changed meanwhile. */
  private async answerAtHumanPace(personId: number, checkinId: number, incoming: Incoming): Promise<void> {
    try {
      const draft = await this.enqueue(personId, async () => {
        const live = this.liveCheckin(personId, checkinId);
        return live ? this.draftReply(live.person, live.checkin, incoming) : null;
      });
      if (!draft) return;
      // The typing time is spent outside the queue, so a message arriving now is not held up.
      await new Promise((resolve) => setTimeout(resolve, this.pace.type(draft.turn.reply)));
      await this.enqueue(personId, async () => {
        const live = this.liveCheckin(personId, checkinId);
        if (live) await this.deliverReply(live.person, live.checkin, draft);
      });
    } catch (err) {
      this.log(`Could not answer person ${personId}`, err);
    } finally {
      await this.refreshBoards().catch(() => undefined);
    }
  }

  /** Decide what to say back. Null when nothing should be sent (chatter, a handover, or a failure). */
  private async draftReply(person: Person, checkin: Checkin, incoming: Incoming): Promise<Draft | null> {
    try {
      // The cap is on follow-ups: what the assistant has said since the person first replied
      // to the latest ask. The opening question and any chase before that do not use it up.
      const soFar = this.sinceAsk(checkin.id);
      const firstReply = soFar.findIndex((m) => m.role === "user");
      const followUps = soFar.slice(firstReply).filter((m) => m.role === "bot").length;
      const finalTurn = followUps >= this.config.maxBotTurns - 1;
      const turn = await this.brain.nextTurn(this.context(person, checkin, finalTurn, incoming.fromGroup));
      if (turn.ignore) {
        // Group chatter, not an update: as if it was never said.
        for (const id of incoming.ids) this.store.removeMessage(id);
        return null;
      }
      if (turn.handover || !turn.reply) {
        // Something only a person should answer: go quiet here and tell the admins.
        this.store.set(`takeover:${checkin.id}`, "handover");
        await this.notifyAdmins(
          `**${displayName(person)} needs a personal reply.** They wrote: "${incoming.text}"\n\nI have stopped replying in that chat for today. Anything else they say is still recorded for the note.`,
        );
        return null;
      }
      const userCount = this.store.messages(checkin.id).filter((m) => m.role === "user").length;
      return { turn, incoming, userCount };
    } catch (err) {
      this.log(`Check-in reply failed for ${person.email}`, err);
      await this.replyFailed(person);
      return null;
    }
  }

  /** Send a drafted reply, unless the conversation moved on while it was being written. */
  private async deliverReply(person: Person, checkin: Checkin, draft: Draft): Promise<void> {
    const { turn, incoming } = draft;
    // They wrote again while this was being "typed": the newer round will answer everything.
    if (this.store.messages(checkin.id).filter((m) => m.role === "user").length !== draft.userCount) return;
    try {
      // Answer where they wrote. In a group that means in the group, tagged,
      // unless this reply has to stay out of it.
      const inGroup =
        incoming.fromGroup &&
        !turn.private &&
        (await this.sayInGroup(person, checkin, turn.reply, { id: incoming.messageId, text: incoming.text }));
      if (!inGroup) await this.say(person, turn.reply);
      this.store.set(`lastwhere:${checkin.id}`, inGroup ? "group" : "direct");
      if (incoming.fromGroup && !inGroup) {
        // Their one-to-one chat is watched only from this private message on, so
        // nothing said there earlier is mistaken for part of the check-in.
        this.store.set(`cursor:${checkin.id}`, new Date(this.now().getTime() - 3000).toISOString());
      }
      this.store.addMessage(checkin.id, "bot", turn.reply);
    } catch (err) {
      this.log(`Check-in reply failed for ${person.email}`, err);
      await this.replyFailed(person);
      return;
    }
    if (!turn.done) return;
    try {
      await this.finish(person, checkin);
    } catch (err) {
      // The closing line did go out. It needs no chase; the note is tried again when the chat is next closed.
      this.log(`Could not write the note for ${person.email}`, err);
      this.store.set(`midchase:${checkin.id}`, "1");
    }
  }

  private async replyFailed(person: Person): Promise<void> {
    // What they wrote is already stored, so it still reaches the note.
    try {
      if (this.personal) {
        await this.notifyAdmins(`I could not reply to ${displayName(person)} just now (their message is saved). The server log has the details.`);
      } else {
        await this.say(person, "Noted. I had trouble replying just now, so if there is more to add, send it and I will pick it up.");
      }
    } catch (err) {
      this.log(`Could not report the failed reply for ${person.email}`, err);
    }
  }

  /**
   * A new ask on a check-in that may have been used earlier today. Everything
   * that follows (chases, idle close, the follow-up cap) looks only at what is
   * said from here on, and flags left by the earlier round are cleared.
   */
  private beginAsk(checkin: Checkin): void {
    this.store.set(`askmark:${checkin.id}`, String(this.store.messages(checkin.id).length));
    for (const flag of ["takeover", "midchase", "lastwhere"]) this.store.set(`${flag}:${checkin.id}`, "");
    this.store.resetReminded(checkin.id);
    const waiting = this.pending.get(checkin.id);
    if (waiting) clearTimeout(waiting.timer);
    this.pending.delete(checkin.id);
  }

  /** The messages of a check-in since it was last asked. */
  private sinceAsk(checkinId: number): ChatMessage[] {
    return this.store.messages(checkinId).slice(Number(this.store.get(`askmark:${checkinId}`) || 0));
  }

  /** Close a check-in nobody answered: back to "completed" if an earlier round today already produced a note. */
  private closeUnanswered(checkin: Checkin): void {
    this.store.setCheckinStatus(checkin.id, this.store.hasNote(checkin.id) ? "completed" : "no_response");
  }

  /** Write the note and update the person's items from what has been said so far. */
  private async recordProgress(person: Person, checkin: Checkin) {
    const { note, items } = await this.brain.writeNote(this.context(person, checkin));
    this.store.saveNote(checkin, note);
    for (const change of items) {
      const updated =
        change.itemId != null &&
        this.store.updateItem(person.id, change.itemId, change.status, change.update, checkin.date, validDate(change.due), validProgress(change.progress, change.status));
      if (!updated) {
        this.store.addItem(person.id, change.title, change.status, change.update, checkin.date, validDate(change.due), validProgress(change.progress, change.status));
      }
    }
    this.store.set(`noted:${checkin.id}`, String(this.store.messages(checkin.id).filter((m) => m.role === "user").length));
    return note;
  }

  /**
   * Conversations still going: once someone has said something new and then
   * paused for a couple of minutes, record it, so the tracker and the board
   * show it now rather than only when the conversation finishes.
   */
  async noteProgress(): Promise<number> {
    let noted = 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open") continue;
      const messages = this.store.messages(checkin.id);
      const said = messages.filter((m) => m.role === "user");
      if (!said.length || String(said.length) === this.store.get(`noted:${checkin.id}`)) continue;
      const lastAt = Date.parse(`${said.at(-1)!.at.replace(" ", "T")}Z`);
      if (this.now().getTime() - lastAt < 2 * 60_000) continue;
      const person = this.store.personById(checkin.personId);
      if (!person) continue;
      try {
        await this.enqueue(person.id, async () => {
          if (this.store.checkin(person.id, checkin.date)?.status === "open") await this.recordProgress(person, checkin);
        });
        noted++;
      } catch (err) {
        this.log(`Could not record progress for ${person.email}`, err);
      }
    }
    return noted;
  }

  /** Turn the day's conversation into a note and update the person's open items. */
  private async finish(person: Person, checkin: Checkin, announce = true): Promise<void> {
    const note = await this.recordProgress(person, checkin);
    this.store.setCheckinStatus(checkin.id, "completed");
    if (announce) {
      await this.notifyAdmins(
        `**${this.store.get(`kind:${checkin.id}`) === "plan" ? "Plan for today from" : "Update from"} ${displayName(person)}** (${checkin.date})\n\n${renderNote(note)}\n\n**Open items**\n${renderItems(this.store.activeItems(person.id), this.today())}`,
      );
    }
  }

  /**
   * Post a message for one person in the group their check-in came from,
   * with them tagged in front. False if that is not possible (no group, or
   * the person cannot be tagged), so the caller can fall back to one to one.
   */
  private async sayInGroup(person: Person, checkin: Checkin, text: string, replyingTo?: { id: string; text: string }): Promise<boolean> {
    const group = this.store.groups().find((g) => String(g.id) === this.store.get(`via:${checkin.id}`));
    const userId = this.store.get(`aadOf:${person.id}`);
    if (!group || !userId || !this.direct) return false;
    if (this.paused) throw new Error("Paused: nothing is sent to staff");
    const name = displayName(person);
    // A person replying does not lay every message out the same way. Each reply
    // keeps at least one of the tag or the quote, so it is clear who it is for.
    const quote = replyingTo?.id ? { messageId: replyingTo.id, preview: replyingTo.text, userId, name } : undefined;
    const roll = Math.random();
    let layout: "front" | "end" | "quoteOnly" | "tagOnly" =
      roll < 0.4 ? "front" : roll < 0.6 ? "end" : roll < 0.8 ? "quoteOnly" : "tagOnly";
    if (layout === "quoteOnly" && !quote) layout = "front";
    if (layout === "end" && !/[?]$/.test(text)) layout = "front";
    if (layout === this.store.get(`lastlayout:${person.id}`)) layout = layout === "front" ? (quote ? "quoteOnly" : "tagOnly") : "front";
    this.store.set(`lastlayout:${person.id}`, layout);

    const body = layout === "front" || layout === "tagOnly" ? `{@0} ${text}` : layout === "end" ? `${text} {@0}` : text;
    await this.direct.send(
      group.chatId,
      body,
      layout === "quoteOnly" ? [] : [{ userId, name }],
      layout === "tagOnly" ? undefined : quote,
    );
    return true;
  }

  /**
   * True when the person has replied before, the assistant then asked
   * something, and they have not answered or been chased about it yet.
   */
  private awaitingFollowUp(checkin: Checkin, messages: { role: "bot" | "user" }[]): boolean {
    return (
      messages.at(-1)?.role === "bot" &&
      messages.some((m) => m.role === "user") &&
      !this.store.get(`takeover:${checkin.id}`) &&
      !this.store.get(`midchase:${checkin.id}`)
    );
  }

  /**
   * One chase for a follow-up question left unanswered for `minMinutes`,
   * in the same place the question was asked. After it the conversation is allowed to go quiet and close.
   */
  async chaseFollowUps(minMinutes = 0): Promise<number> {
    let chased = 0;
    if (this.paused) return 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open") continue;
      const messages = this.sinceAsk(checkin.id);
      if (!this.awaitingFollowUp(checkin, messages)) continue;
      const lastAt = Date.parse(`${messages.at(-1)!.at.replace(" ", "T")}Z`);
      if (minMinutes > 0 && this.now().getTime() - lastAt < minMinutes * 60_000) continue;
      const person = this.store.personById(checkin.personId);
      if (!person?.nominated || !this.address(person)) continue;
      try {
        const nudge = this.nudgeText("followup");
        const inGroup =
          this.store.get(`lastwhere:${checkin.id}`) === "group" && (await this.sayInGroup(person, checkin, nudge));
        if (!inGroup) await this.say(person, nudge);
        this.store.addMessage(checkin.id, "bot", nudge);
        this.store.set(`midchase:${checkin.id}`, "1");
        chased++;
      } catch (err) {
        this.log(`Could not chase ${person.email}`, err);
      }
    }
    return chased;
  }

  /**
   * Close conversations that have gone quiet: if the person has replied at
   * least once and nothing has been said for `idleMinutes`, write the note
   * and post the summary rather than waiting for the evening digest.
   */
  async closeIdle(idleMinutes: number): Promise<number> {
    let closed = 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open") continue;
      const messages = this.sinceAsk(checkin.id);
      if (!messages.some((m) => m.role === "user")) continue;
      // A follow-up question still waiting for its one chase is not "quiet" yet.
      if (this.awaitingFollowUp(checkin, messages)) continue;
      const lastAt = Date.parse(`${messages.at(-1)!.at.replace(" ", "T")}Z`);
      if (idleMinutes > 0 && this.now().getTime() - lastAt < idleMinutes * 60_000) continue;
      const person = this.store.personById(checkin.personId);
      if (!person) continue;
      try {
        await this.enqueue(person.id, async () => {
          if (this.store.checkin(person.id, checkin.date)?.status === "open") await this.finish(person, checkin);
        });
        closed++;
      } catch (err) {
        this.log(`Could not close check-in for ${person.email}`, err);
      }
    }
    return closed;
  }

  /** A chase, worded differently from the one sent last time. */
  private nudgeText(kind: "first" | "followup" = "first"): string {
    if (!this.personal) {
      return kind === "first"
        ? "A quick reminder on today's check-in. A line or two is enough."
        : "A quick reminder on my last question, when you have a minute.";
    }
    const pool = kind === "first" ? FIRST_CHASES : FOLLOWUP_CHASES;
    const last = this.store.get(`lastnudge:${kind}`);
    const options = pool.filter((t) => t !== last);
    const text = options[Math.floor(Math.random() * options.length)] ?? pool[0]!;
    this.store.set(`lastnudge:${kind}`, text);
    return text;
  }

  /**
   * Ask one person right now, whatever has happened today. Not asked yet, or
   * finished earlier: a new question. Asked but silent: ask again.
   */
  private async askNow(target: Person, topic = ""): Promise<string> {
    const name = displayName(target);
    if (this.paused) return `Paused: ${name} was not asked. Send **resume** first.`;
    if (!this.address(target)) return `${name} is not connected yet.`;
    return this.enqueue(target.id, async () => {
      const checkin = this.store.checkin(target.id, this.today());
      if (checkin?.status === "open") {
        if (topic) this.store.set(`topic:${checkin.id}`, topic);
        if (this.sinceAsk(checkin.id).some((m) => m.role === "user")) {
          return `${name} is in the middle of replying. Their summary will appear here when the conversation finishes.`;
        }
        const nudge = this.nudgeText();
        await this.say(target, nudge);
        this.store.addMessage(checkin.id, "bot", nudge);
        this.store.markReminded(checkin.id);
        return `Asked ${name} again.`;
      }
      await this.startCheckin(target, true, topic);
      return `Asked ${name} for an update. Their summary will appear here when the conversation finishes.`;
    });
  }

  // ---------- group chats: asked together, followed up privately ----------

  private resolveGroup(query: string): Group | string {
    const q = query.trim().toLowerCase();
    const all = this.store.groups();
    const exact = all.filter((g) => g.name.toLowerCase() === q);
    const matches = exact.length ? exact : all.filter((g) => q && g.name.toLowerCase().includes(q));
    if (matches.length === 1) return matches[0]!;
    if (!matches.length) return `I am not following a group chat matching "${query}". Send **list** to see the groups.`;
    return `"${query}" matches several groups: ${matches.map((g) => g.name).join(", ")}. Use the full name.`;
  }

  private async addGroup(query: string): Promise<string> {
    if (!this.direct?.findGroups || !this.direct.groupMembers) {
      return "Group chats only work when messages are sent from your own Teams account.";
    }
    if (!query) return "Tell me the group chat's name, for example: **add group Recruitment Team**";
    const found = await this.direct.findGroups(query);
    const exact = found.filter((g) => g.name.toLowerCase() === query.toLowerCase());
    const matches = exact.length ? exact : found;
    if (!matches.length) {
      return `None of your group chats has a name containing "${query}". The chat needs a name in Teams before I can find it.`;
    }
    if (matches.length > 1) {
      return `Several group chats match:\n\n${matches.slice(0, 10).map((g) => `- ${g.name}`).join("\n")}\n\nSend the full name of the one you mean.`;
    }
    const group = this.store.addGroup(matches[0]!.chatId, matches[0]!.name);
    const members = await this.syncGroup(group);
    await this.refreshGroupMemory(group); // read the group's history now, before anything is asked
    return `Now following **${group.name}** (${members.length} people: ${members.map(displayName).join(", ")}). Nothing has been posted there yet.\n\nSend **ask group ${group.name}** to ask now. The question and any follow-ups stay in the group, with people tagged.`;
  }

  /** Bring our record of who is in the group in line with Teams. */
  private async syncGroup(group: Group): Promise<Person[]> {
    const members: Person[] = [];
    for (const m of await this.direct!.groupMembers!(group.chatId)) {
      const person = this.store.upsertPerson(m.email, { name: m.name });
      if (!person.nominated && !this.isAdmin(person)) this.store.setNominated(person.id, true);
      this.store.set(`aad:${m.userId}`, String(person.id));
      this.store.set(`aadOf:${person.id}`, m.userId);
      members.push(this.store.personById(person.id)!);
    }
    this.store.setGroupMembers(group.id, members.map((p) => p.id));
    return members;
  }

  /**
   * Post one request for updates in the group. Members not yet asked today
   * are covered; with `force`, so are those who finished earlier.
   */
  private async askGroup(
    group: Group,
    force = false,
    topic = "",
    round: Round = this.roundNow(),
  ): Promise<{ asked: number; message: string }> {
    if (this.paused) return { asked: 0, message: `Paused: nothing was posted in ${group.name}. Send **resume** first.` };
    const date = this.today();
    const due = (await this.syncGroup(group)).filter((p) => {
      // Admins in the group see the question like everyone else, but are not
      // chased or followed up: the summaries are for them.
      if (this.isAdmin(p)) return false;
      // On the group's schedule, someone with a time of their own for this message waits for it.
      const slot = round === "plan" ? "morning" : round === "review" ? "evening" : "";
      if (!force && slot && this.store.get(`schedule:person:${p.id}:${slot}`)) return false;
      // Asked by hand: everyone except people in the middle of replying.
      // On a schedule: once a day for each kind of message.
      return this.dueFor(p, date, round, force);
    });
    if (!due.length) {
      return {
        asked: 0,
        message: force
          ? `Everyone in ${group.name} is already replying, so I have not posted again.`
          : `Everyone in ${group.name} has already been asked today.`,
      };
    }
    // People being asked a second time today: they get tagged, so it reads as a chase.
    const again = due.filter((p) => {
      const c = this.store.checkin(p.id, date);
      return c?.status === "open" && this.store.get(`kind:${c.id}`) === round;
    });

    // Read before asking: the group's chat, and (for a small group) each member's own chat with the sender.
    await this.refreshGroupMemory(group);
    if (this.personal && due.length <= 12) {
      for (let i = 0; i < due.length; i++) {
        let member = due[i]!;
        try {
          if (!member.chatId) {
            const chat = await this.direct!.connect(member.email);
            if (!("notFound" in chat)) member = this.store.upsertPerson(member.email, { chatId: chat.conversationId });
          }
          await this.refreshMemory(member);
          due[i] = member;
        } catch (err) {
          this.log(`Could not read the chat with ${member.email}`, err);
        }
      }
    }

    let opening =
      round === "plan"
        ? "Hi team, please share what you will work on today."
        : "Hi team, please share today's update.";
    let tagged: { person: Person; userId: string; question: string }[] = [];
    if (this.personal || topic) {
      try {
        const composed = await this.brain.composeGroupAsk({
          groupName: group.name,
          topic,
          memory: this.store.get(`gmemory:${group.id}`) || "",
          today: date,
          round,
          members: due.map((p) => ({
            name: displayName(p),
            role: p.profile ?? "",
            memory: [
              this.store.get(`rapport:${p.id}`) || "",
              `On the tracker:\n${renderItems(this.store.activeItems(p.id), date)}`,
            ]
              .filter(Boolean)
              .join("\n\n"),
          })),
        });
        opening = composed.opening || opening;
        // Only people who are being asked, who can be tagged, and one question each.
        for (const q of composed.questions) {
          const person = due.find((p) => displayName(p).toLowerCase() === q.name.toLowerCase());
          const userId = person ? this.store.get(`aadOf:${person.id}`) : "";
          if (person && userId && !tagged.some((t) => t.person.id === person.id)) {
            tagged.push({ person, userId, question: q.question });
          }
        }
      } catch (err) {
        this.log(`Could not word the question for ${group.name}; using the standard one`, err);
        tagged = [];
      }
    }

    // Anyone being asked again who has no question of their own is tagged together on a last line.
    const alsoTag = again
      .filter((p) => !tagged.some((t) => t.person.id === p.id))
      .map((p) => ({ person: p, userId: this.store.get(`aadOf:${p.id}`) ?? "" }))
      .filter((t) => t.userId);
    const mentioned = [...tagged, ...alsoTag];

    // {@0}, {@1}... mark where each person's tag goes when the message is sent.
    const lines = (name: (i: number) => string) =>
      [
        opening,
        ...tagged.map((t, i) => `${name(i)} ${t.question}`),
        ...(alsoTag.length ? [`${alsoTag.map((_, i) => name(tagged.length + i)).join(" ")} please share your update.`] : []),
      ].join("\n");
    const text = lines((i) => `@${displayName(mentioned[i]!.person)}`); // as it reads, for our record
    await this.direct!.send(
      group.chatId,
      lines((i) => `{@${i}}`),
      mentioned.map((t) => ({ userId: t.userId, name: displayName(t.person) })),
    );
    for (const person of due) {
      const checkin = this.store.openCheckin(person.id, date);
      this.beginAsk(checkin);
      this.store.addMessage(checkin.id, "bot", text);
      this.store.set(`via:${checkin.id}`, String(group.id));
      this.store.set(`kind:${checkin.id}`, round);
      this.store.set(`round:${checkin.id}:${round}`, "1");
      const own = tagged.find((t) => t.person.id === person.id)?.question;
      this.store.set(
        `topic:${checkin.id}`,
        [topic, own ? `In the group they were asked directly: "${own}"` : ""].filter(Boolean).join("\n"),
      );
    }
    // Start reading from this ask, unless the group is already being read:
    // then carry on from where the reading had got to, so nothing is skipped.
    if (!this.store.get(`gasked:${group.id}`)) this.store.set(`gcursor:${group.id}`, "");
    this.store.set(`gasked:${group.id}`, this.now().toISOString());
    this.store.set(`gchased:${group.id}`, "");
    return {
      asked: due.length,
      message: `Posted in **${group.name}** (${due.length} people): "${text}"\n\nSummaries will appear here as each person finishes.`,
    };
  }

  /** Groups whose chat should be watched for replies right now, with when they were asked. */
  listeningGroups(): { group: Group; askedAt: string }[] {
    return this.store
      .groups()
      .map((group) => ({ group, askedAt: this.store.get(`gasked:${group.id}`) ?? "" }))
      .filter((g) => g.askedAt);
  }

  /** Someone wrote in a group chat that is being watched. `userId` is their directory id. */
  async handleGroupReply(userId: string, text: string, messageId = "", groupId?: number): Promise<void> {
    const person = this.store.personById(Number(this.store.get(`aad:${userId}`) || 0));
    if (!person?.nominated) return;
    await this.enqueue(person.id, async () => {
      const checkin = this.store.checkin(person.id, this.today());
      if (checkin?.status !== "open") return;
      // Only when they were asked in this very group. Someone asked one to one, or in
      // another group, is just talking here: it is not an answer to the assistant.
      const via = this.store.get(`via:${checkin.id}`);
      if (!via || (groupId !== undefined && via !== String(groupId))) return;
      // Follow-ups go to them one to one, so that chat has to exist.
      let current = person;
      if (!current.chatId && this.direct) {
        const chat = await this.direct.connect(current.email);
        if (!("notFound" in chat)) current = this.store.upsertPerson(current.email, { chatId: chat.conversationId });
      }
      await this.refreshMemory(current);
      await this.handleUpdate(current, text.trim(), true, messageId);
    });
    await this.refreshBoards();
  }

  /** The one chase in each group, and the end of each group's listening window. */
  private async runGroupTimers(): Promise<void> {
    if (this.paused) return;
    const date = this.today();
    for (const { group, askedAt } of this.listeningGroups()) {
      const minutes = (this.now().getTime() - Date.parse(askedAt)) / 60_000;
      const silent = this.store.groupMembers(group.id).filter((p) => {
        const c = this.store.checkin(p.id, date);
        return (
          c?.status === "open" &&
          this.store.get(`via:${c.id}`) === String(group.id) &&
          !this.sinceAsk(c.id).some((m) => m.role === "user")
        );
      });

      if (silent.length && !this.store.get(`gchased:${group.id}`) && minutes >= this.config.chaseAfterMinutes) {
        const nudge = this.nudgeText();
        const mentions = silent
          .map((p) => ({ userId: this.store.get(`aadOf:${p.id}`) ?? "", name: displayName(p) }))
          .filter((m) => m.userId);
        try {
          await this.direct!.send(group.chatId, nudge, mentions);
          this.store.set(`gchased:${group.id}`, "1");
          for (const p of silent) {
            const c = this.store.checkin(p.id, date)!;
            this.store.addMessage(c.id, "bot", nudge);
            this.store.markReminded(c.id);
          }
        } catch (err) {
          this.log(`Could not chase in ${group.name}`, err);
        }
      }

      if (minutes >= this.config.groupWindowMinutes) {
        // Stop reading the group. Whoever said nothing is marked as not having replied.
        for (const p of silent) this.closeUnanswered(this.store.checkin(p.id, date)!);
        // Follow-ups happen in the group, so keep reading it until every conversation from this ask is closed.
        const stillTalking = this.store.groupMembers(group.id).some((p) => {
          const c = this.store.checkin(p.id, date);
          return c?.status === "open" && this.store.get(`via:${c.id}`) === String(group.id);
        });
        if (!stillTalking) this.store.set(`gasked:${group.id}`, "");
      }
    }
  }

  // ---------- the tracker: one pinned table in each followed group, kept up to date ----------

  /** The table itself. `rows` is how many real items it holds. */
  private trackerTable(group: Group): { html: string; rows: number } {
    const date = this.today();
    const esc = (t: string) => t.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const day = (d: string) =>
      new Date(`${d}T12:00:00Z`).toLocaleDateString("en-GB", { day: "numeric", month: "short", timeZone: "UTC" });
    let rows = 0;
    const body = this.store
      .groupMembers(group.id)
      .filter((p) => !this.isAdmin(p))
      .map((p) => {
        const name = esc((p.name ?? p.email).split(" ")[0]!);
        const items = this.store.trackerItems(p.id, date).slice(0, 8);
        rows += items.length;
        if (!items.length) return `<tr><td>${name}</td><td>No items yet</td><td>-</td><td>-</td><td>-</td><td>-</td></tr>`;
        return items
          .map((i, n) => {
            const status =
              i.status === "done"
                ? "✅ Done"
                : i.status === "blocked"
                  ? "🟠 Blocked"
                  : i.due && i.due < date
                    ? "🔴 Overdue"
                    : i.due === date
                      ? "🟡 Due today"
                      : "🔵 In progress";
            const progress = i.status === "done" ? 100 : i.progress;
            const latest = i.lastUpdate.length > 90 ? `${i.lastUpdate.slice(0, 89).trimEnd()}…` : i.lastUpdate;
            return `<tr><td>${n === 0 ? name : ""}</td><td>${esc(i.title)}</td><td>${i.due ? day(i.due) : "-"}</td><td>${progress === null ? "-" : progressBar(progress)}</td><td>${status}</td><td>${esc(latest)}</td></tr>`;
          })
          .join("");
      })
      .join("");
    return {
      rows,
      html: `<table><tr><th>Who</th><th>Item</th><th>Due</th><th>Progress</th><th>Status</th><th>Done so far</th></tr>${body}</table>`,
    };
  }

  /**
   * Read a group's whole chat history and put every piece of work in it on
   * the tracker, by person: what is done so far, progress and due date.
   * Items already tracked are updated rather than duplicated.
   */
  private async buildTracker(group: Group): Promise<string> {
    if (!this.direct?.history || !this.direct.createTracker) {
      return "Building a tracker needs messages to be sent from your own Teams account.";
    }
    if (this.paused) return `Paused: nothing was posted in ${group.name}. Send **resume** first.`;
    const date = this.today();
    const members = (await this.syncGroup(group)).filter((p) => !this.isAdmin(p));
    if (!members.length) return `I found nobody in ${group.name} to track.`;

    const lines = (await this.direct.history(group.chatId, 2000)).filter(
      (m) => !(m.fromSender && m.text.startsWith("Team tracker")),
    );
    const transcript = lines
      .map((m) => `${m.at.slice(0, 10)} ${m.fromSender ? this.direct!.senderName : m.from || "Someone"}: ${m.text.slice(0, 400)}`)
      .join("\n");
    const entries = await this.brain.extractTracker({
      groupName: group.name,
      today: date,
      transcript,
      people: members.map((p) => ({
        name: displayName(p),
        role: p.profile ?? "",
        memory: this.store.get(`rapport:${p.id}`) || "",
      })),
    });

    const counts = new Map<number, number>();
    for (const entry of entries) {
      const wanted = entry.person.trim().toLowerCase();
      const person =
        members.find((p) => displayName(p).toLowerCase() === wanted) ??
        members.find((p) => displayName(p).toLowerCase().split(/\s+/)[0] === wanted.split(/\s+/)[0]);
      if (!person || !entry.title.trim()) continue;
      const due = validDate(entry.due);
      const progress = validProgress(entry.progress, entry.status);
      const existing = this.store.itemByTitle(person.id, entry.title);
      if (existing) this.store.updateItem(person.id, existing.id, entry.status, entry.doneSoFar, date, due, progress);
      else this.store.addItem(person.id, entry.title.trim(), entry.status, entry.doneSoFar, date, due, progress);
      counts.set(person.id, (counts.get(person.id) ?? 0) + 1);
    }

    this.store.set(`trackeroff:${group.id}`, "");
    this.store.set(`trackerhash:${group.id}`, "");
    await this.refreshTrackers(group);
    const posted = !!this.store.get(`tracker:${group.id}`);
    const summary = members.map((p) => `- ${displayName(p)}: ${counts.get(p.id) ?? 0}`).join("\n");
    return posted
      ? `Built the tracker for **${group.name}** from ${lines.length} messages of its chat history, and pinned it at the top of that chat.\n\nItems found:\n${summary}\n\nI keep it up to date as people reply, and the morning and evening messages follow up on what is due or overdue on it.`
      : `I read ${lines.length} messages of ${group.name} and found:\n${summary}\n\nbut could not post the tracker there. The server log has the details.`;
  }

  /**
   * Bring each group's tracker up to date. It is one message in the group,
   * pinned, and edited in place, so the team always has the current picture
   * in one spot. It carries titles, dates and status only: no notes and no
   * blocker details.
   */
  async refreshTrackers(only?: Group): Promise<void> {
    if (this.paused || !this.direct?.createTracker) return;
    for (const group of only ? [only] : this.store.groups()) {
      if (this.store.get(`trackeroff:${group.id}`)) continue;
      const { html, rows } = this.trackerTable(group);
      const existing = this.store.get(`tracker:${group.id}`);
      // Nothing to show yet: do not put an empty table in front of the team.
      if (!existing && !rows && !only) continue;
      if (this.store.get(`trackerhash:${group.id}`) === html) continue;

      const when = new Intl.DateTimeFormat("en-GB", {
        timeZone: this.config.timezone,
        weekday: "short",
        day: "numeric",
        month: "short",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
      }).format(this.now());
      const message = `<p><b>Team tracker</b> · updated ${when}</p>${html}`;
      try {
        if (existing && this.direct.updateTracker) {
          try {
            await this.direct.updateTracker(group.chatId, existing, message);
            this.store.set(`trackerhash:${group.id}`, html);
            continue;
          } catch (err) {
            this.log(`Could not edit the tracker in ${group.name}`, err);
            // Posting a fresh one each time would flood the group: at most one new table a day.
            if (this.store.get(`trackerday:${group.id}`) === this.today()) continue;
          }
        }
        const id = await this.direct.createTracker(group.chatId, message);
        this.store.set(`tracker:${group.id}`, id);
        this.store.set(`trackerday:${group.id}`, this.today());
        this.store.set(`trackerhash:${group.id}`, html);
      } catch (err) {
        this.log(`Could not post the tracker in ${group.name}`, err);
      }
    }
  }

  // ---------- the schedule, kept in the database and set from the chat ----------

  private localNow(): { date: string; minutes: number; weekend: boolean } {
    const parts = new Intl.DateTimeFormat("en-GB", {
      timeZone: this.config.timezone,
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
      weekday: "short",
    }).formatToParts(this.now());
    const get = (type: string) => parts.find((p) => p.type === type)?.value ?? "";
    return {
      date: this.today(),
      minutes: (Number(get("hour")) % 24) * 60 + Number(get("minute")),
      weekend: ["Sat", "Sun"].includes(get("weekday")),
    };
  }

  private scheduleStatus(): string {
    const morning = this.store.get("schedule:morning");
    const evening = this.store.get("schedule:evening");
    const digest = this.store.get("schedule:digest");
    return [
      morning ? `Morning plan: everyone is asked automatically at **${morning}**.` : "",
      evening ? `End-of-day update: everyone is asked automatically at **${evening}**.` : "",
      morning || evening
        ? `Monday to Friday, ${this.config.timezone} time.${morning && evening ? "" : ` No ${morning ? "end-of-day" : "morning"} message is set.`}`
        : "No automatic asking: updates are only requested when you say **ask**.",
      digest ? `Daily digest at **${digest}**.` : "No automatic digest: send **digest** when you want one.",
    ]
      .filter(Boolean)
      .join("\n");
  }

  /**
   * Called once when the bot starts. If it had been down for a while, tell
   * the admins: scheduled asks in that gap did not happen, and they would
   * otherwise have no way to know.
   */
  async announceStart(): Promise<void> {
    const last = this.store.get("heartbeat");
    const now = this.now();
    this.store.set("heartbeat", now.toISOString());
    if (!last || now.getTime() - Date.parse(last) < 10 * 60_000) return;
    const when = (d: Date) =>
      new Intl.DateTimeFormat("en-GB", {
        timeZone: this.config.timezone,
        weekday: "short",
        day: "numeric",
        month: "short",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
      }).format(d);
    await this.notifyAdmins(
      `I was offline from ${when(new Date(last))} to ${when(now)} and am running again. Anything scheduled in that time was skipped: send **ask all** if you want it now. Replies people sent meanwhile are being picked up.`,
    );
  }

  /** Runs once a minute: the chase, closing quiet conversations, and anything scheduled. */
  async tick(): Promise<void> {
    this.store.set("heartbeat", this.now().toISOString());
    try {
      await this.direct?.sync?.();
    } catch (err) {
      this.log("Could not read the chats before this tick", err);
    }
    await this.closePastDays();
    await this.runReminders(this.config.chaseAfterMinutes);
    await this.runGroupTimers();
    await this.chaseFollowUps(this.config.chaseAfterMinutes);
    await this.closeIdle(this.config.idleCloseMinutes);
    await this.noteProgress();
    await this.refreshTrackers();
    await this.refreshBoards();

    const { date, minutes, weekend } = this.localNow();
    if (weekend) return;
    type Job = [string, () => Promise<unknown>];
    const jobs: Job[] = [
      ["morning", () => this.runCheckins("morning")],
      ["evening", () => this.runCheckins("evening")],
      ["digest", () => this.runDigest()],
      ...this.store
        .groups()
        .flatMap((g) => SLOTS.map((slot): Job => [`group:${g.id}:${slot}`, () => this.askGroup(g, false, "", roundOf(slot))])),
      ...this.store
        .nominees()
        .flatMap((p) =>
          SLOTS.map((slot): Job => [
            `person:${p.id}:${slot}`,
            () => this.enqueue(p.id, () => this.startCheckin(p, false, "", roundOf(slot))),
          ]),
        ),
    ];
    for (const [kind, job] of jobs) {
      const at = this.store.get(`schedule:${kind}`);
      if (!at) continue;
      const due = Number(at.slice(0, 2)) * 60 + Number(at.slice(3));
      // A ten-minute window, so a restart just after the time still runs it, once.
      if (minutes < due || minutes > due + 10 || this.store.get(`ran:${kind}`) === date) continue;
      this.store.set(`ran:${kind}`, date);
      this.log(`[schedule] running ${kind} for ${date}`);
      try {
        await job();
      } catch (err) {
        this.log(`[schedule] ${kind} failed`, err);
        const failKey = `ranfail:${kind}:${date}`;
        const fails = Number(this.store.get(failKey) || 0) + 1;
        this.store.set(failKey, String(fails));
        if (fails < 3) {
          this.store.set(`ran:${kind}`, ""); // try again on the next tick, inside the window
        } else {
          await this.notifyAdmins(
            `A scheduled action failed three times and has been skipped for today (${kind}). Send **ask all** to do it by hand; the server log has the details.`,
          );
        }
      }
    }
  }

  // ---------- the status board ----------

  /** A stored message time (UTC) shown as HH:MM in the configured timezone. */
  private clock(at: string): string {
    return new Intl.DateTimeFormat("en-GB", {
      timeZone: this.config.timezone,
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
    }).format(new Date(`${at.replace(" ", "T")}Z`));
  }

  /**
   * One card showing where everyone stands today, most urgent first, with the
   * controls underneath. It is edited in place as things change, so a pinned
   * copy stays current.
   */
  private boardCard(): Record<string, unknown> {
    const date = this.today();
    const nominees = this.store.nominees();
    const todaysNotes = new Map(this.store.notesOn(date).map((n) => [n.personId, n]));
    const clip = (text: string, max = 120) => (text.length > max ? `${text.slice(0, max - 1).trimEnd()}…` : text);
    const submit = (title: string, data: Record<string, unknown>) => ({ type: "Action.Submit", title, data });

    // A time of their own, in green, for a person or group that has one.
    const times = (prefix: string) => ({
      morning: this.store.get(`schedule:${prefix}morning`) || "",
      evening: this.store.get(`schedule:${prefix}evening`) || "",
    });
    const timesText = (t: { morning: string; evening: string }) =>
      [t.morning && `Morning ${t.morning}`, t.evening && `Evening ${t.evening}`].filter(Boolean).join(" · ");
    const scheduleLine = (prefix: string) => {
      const t = times(prefix);
      return t.morning || t.evening
        ? [{ type: "TextBlock", text: `ON · ${timesText(t)}, Monday to Friday`, color: "Good", weight: "Bolder", size: "Small", spacing: "None", wrap: true }]
        : [];
    };
    // A button that opens the two time pickers, morning and evening. `who` is empty for everyone.
    const timePicker = (field: string, who: string, prefix: string, title = "") => {
      const t = times(prefix);
      return {
        type: "Action.ShowCard",
        title: title || (t.morning || t.evening ? `Times: ${timesText(t)}` : "Set times"),
        card: {
          type: "AdaptiveCard",
          body: [
            { type: "Input.Time", id: `${field}m`, label: "Morning: ask for the plan for the day", ...(t.morning ? { value: t.morning } : {}) },
            { type: "Input.Time", id: `${field}e`, label: "Evening: ask for the end-of-day update", ...(t.evening ? { value: t.evening } : {}) },
          ],
          actions: [
            submit("Save morning", { cmd: "scheduleFor", who, field: `${field}m`, slot: "morning" }),
            submit("Save evening", { cmd: "scheduleFor", who, field: `${field}e`, slot: "evening" }),
            submit("Turn off", { cmd: "scheduleOffFor", who }),
          ],
        },
      };
    };
    // Things of theirs that are past their date, in red under the person.
    const overdueLine = (person: Person) => {
      const late = this.store.activeItems(person.id).filter((i) => i.due && i.due < date);
      return late.length
        ? [{ type: "TextBlock", text: `OVERDUE · ${late.slice(0, 3).map((i) => `${clip(i.title, 60)} (due ${i.due})`).join("; ")}`, color: "Attention", size: "Small", spacing: "None", wrap: true }]
        : [];
    };

    const groups = [
      { title: "Needs you personally", color: "Attention" },
      { title: "Has a blocker", color: "Attention" },
      { title: "Asked, no reply yet", color: "Warning" },
      { title: "Replying now", color: "Accent" },
      { title: "Update received", color: "Good" },
      { title: "Not asked today", color: "Default" },
    ];

    const placed = nominees.slice(0, 30).map((person) => {
      const checkin = this.store.checkin(person.id, date);
      const messages = checkin ? this.sinceAsk(checkin.id) : [];
      const lastFromThem = messages.filter((m) => m.role === "user").at(-1);
      const note = todaysNotes.get(person.id);
      const blockedItem = this.store.activeItems(person.id).find((i) => i.status === "blocked");

      if (!this.address(person)) return { group: 5, person, line: "Not connected in Teams yet." };
      if (checkin?.status === "open" && this.store.get(`takeover:${checkin.id}`) === "handover") {
        return { group: 0, person, line: `They wrote: "${clip(lastFromThem?.text ?? "", 100)}"` };
      }
      if (note?.blockers.length) return { group: 1, person, line: clip(note.blockers.join("; ")) };
      if (checkin?.status === "open") {
        if (lastFromThem) return { group: 3, person, line: `${this.clock(lastFromThem.at)}: "${clip(lastFromThem.text, 100)}"` };
        return {
          group: 2,
          person,
          line: `Asked at ${this.clock(messages[0]?.at ?? "")}${checkin.reminded ? ", chased once" : ""}.`,
        };
      }
      if (checkin?.status === "no_response") return { group: 2, person, line: "Asked today, did not reply." };
      if (note) return { group: 4, person, line: clip(note.summary) };
      if (blockedItem) return { group: 1, person, line: clip(`Still blocked: ${blockedItem.title}. ${blockedItem.lastUpdate}`) };
      const last = this.store.recentNotes(person.id, 1)[0];
      return { group: 5, person, line: last ? `Last update ${last.date}: ${clip(last.summary, 90)}` : "No updates yet." };
    });

    const body: Record<string, unknown>[] = [
      { type: "TextBlock", text: "Team board", weight: "Bolder", size: "Large" },
      ...(this.paused
        ? [{ type: "TextBlock", text: "PAUSED · Nothing is being sent to anyone", color: "Attention", weight: "Bolder", wrap: true, spacing: "None" }]
        : []),
      {
        type: "TextBlock",
        spacing: "None",
        isSubtle: true,
        wrap: true,
        text: nominees.length
          ? groups
              .map((g, i) => ({ g, n: placed.filter((r) => r.group === i).length }))
              .filter((x) => x.n)
              .map((x) => `${x.n} ${x.g.title.toLowerCase()}`)
              .join(" · ")
          : "Nobody added yet. Use Add person below.",
      },
    ];

    groups.forEach((group, index) => {
      const rows = placed.filter((r) => r.group === index);
      if (!rows.length) return;
      body.push({
        type: "TextBlock",
        text: `${group.title} (${rows.length})`,
        weight: "Bolder",
        color: group.color,
        spacing: "Large",
        separator: true,
      });
      for (const { person, line } of rows) {
        body.push({
          type: "Container",
          spacing: "Small",
          items: [
            { type: "TextBlock", text: displayName(person), weight: "Bolder", wrap: true },
            { type: "TextBlock", text: line, wrap: true, maxLines: 2, spacing: "None", isSubtle: true },
            ...overdueLine(person),
            ...scheduleLine(`person:${person.id}:`),
            {
              type: "ActionSet",
              spacing: "Small",
              actions: [
                {
                  type: "Action.OpenUrl",
                  title: "Open chat",
                  url: `https://teams.microsoft.com/l/chat/0/0?users=${encodeURIComponent(person.email)}`,
                },
                submit("Ask now", { cmd: "ask", who: person.email }),
                submit("Details", { cmd: "details", who: person.email }),
                timePicker(`p${person.id}`, person.email, `person:${person.id}:`),
                submit("Remove", { cmd: "remove", who: person.email }),
              ],
            },
          ],
        });
      }
    });

    const groupList = this.store.groups();
    if (groupList.length) {
      body.push({ type: "TextBlock", text: `Group chats (${groupList.length})`, weight: "Bolder", spacing: "Large", separator: true });
      for (const g of groupList) {
        body.push({
          type: "Container",
          spacing: "Small",
          items: [
            { type: "TextBlock", text: g.name, weight: "Bolder", wrap: true },
            {
              type: "TextBlock",
              text: `${this.store.groupMembers(g.id).length} people · asked and followed up in the group`,
              wrap: true,
              spacing: "None",
              isSubtle: true,
            },
            ...scheduleLine(`group:${g.id}:`),
            {
              type: "ActionSet",
              spacing: "Small",
              actions: [
                submit("Ask group now", { cmd: "askGroup", who: g.name }),
                timePicker(`g${g.id}`, g.name, `group:${g.id}:`),
                submit("Remove group", { cmd: "removeGroup", who: g.name }),
              ],
            },
          ],
        });
      }
    }

    // The general settings: green when switched on, grey when off.
    const morningAt = this.store.get("schedule:morning");
    const eveningAt = this.store.get("schedule:evening");
    const digestAt = this.store.get("schedule:digest");
    body.push(
      {
        type: "TextBlock",
        text: morningAt ? `ON · Morning plan: everyone is asked at ${morningAt}, Monday to Friday` : "OFF · No morning plan message",
        wrap: true,
        size: "Small",
        spacing: "Large",
        separator: true,
        ...(morningAt ? { color: "Good", weight: "Bolder" } : { isSubtle: true }),
      },
      {
        type: "TextBlock",
        text: eveningAt ? `ON · End of day: everyone is asked at ${eveningAt}, Monday to Friday` : "OFF · No end-of-day message",
        wrap: true,
        size: "Small",
        spacing: "None",
        ...(eveningAt ? { color: "Good", weight: "Bolder" } : { isSubtle: true }),
      },
      {
        type: "TextBlock",
        text: digestAt ? `ON · Daily digest at ${digestAt}` : "OFF · No automatic digest",
        wrap: true,
        size: "Small",
        spacing: "None",
        ...(digestAt ? { color: "Good", weight: "Bolder" } : { isSubtle: true }),
      },
    );

    return {
      type: "AdaptiveCard",
      $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
      version: "1.4",
      body,
      actions: [
        submit("Ask everyone now", { cmd: "askAll" }),
        {
          type: "Action.ShowCard",
          title: "Add person",
          card: {
            type: "AdaptiveCard",
            body: [{ type: "Input.Text", id: "newEmail", placeholder: "name@company.com", label: "Their work email" }],
            actions: [submit("Add", { cmd: "add" })],
          },
        },
        {
          type: "Action.ShowCard",
          title: "Add group",
          card: {
            type: "AdaptiveCard",
            body: [{ type: "Input.Text", id: "groupName", placeholder: "Group chat name", label: "Name of the Teams group chat" }],
            actions: [submit("Add group", { cmd: "addGroup" })],
          },
        },
        submit("Digest now", { cmd: "digest" }),
        timePicker("all", "", "", "Schedule"),
        submit("Refresh", { cmd: "refresh" }),
        this.paused ? submit("Resume", { cmd: "resume" }) : submit("Pause all", { cmd: "pause" }),
      ],
    };
  }

  /** What each admin's board last showed, so it is only edited when something changed. */
  private shownBoards = new Map<number, string>();

  /** Post a fresh board to this admin. It becomes the one that is kept up to date. */
  private async showBoard(admin: Person): Promise<void> {
    if (!admin.conversationId || !this.messenger.sendCard) {
      await this.sayAdmin(admin, ADMIN_HELP);
      return;
    }
    const card = this.boardCard();
    const id = await this.messenger.sendCard(admin.conversationId, card);
    if (id) this.store.set(`board:${admin.id}`, id);
    this.shownBoards.set(admin.id, JSON.stringify(card));
  }

  /** Bring every admin's board up to date, editing the existing message in place. */
  async refreshBoards(): Promise<void> {
    if (!this.messenger.updateCard) return;
    const card = this.boardCard();
    const json = JSON.stringify(card);
    for (const email of this.config.adminEmails) {
      const admin = this.store.personByEmail(email);
      const messageId = admin ? this.store.get(`board:${admin.id}`) : undefined;
      if (!admin?.conversationId || !messageId || this.shownBoards.get(admin.id) === json) continue;
      try {
        await this.messenger.updateCard(admin.conversationId, messageId, card);
        this.shownBoards.set(admin.id, json);
      } catch (err) {
        this.log(`Could not refresh the board for ${email}`, err);
      }
    }
  }

  // ---------- scheduled jobs ----------

  async runCheckins(slot?: Slot): Promise<{ sent: number; unreachable: Person[] }> {
    const round = slot ? roundOf(slot) : this.roundNow();
    let sent = 0;
    const unreachable: Person[] = [];
    const failed: string[] = [];
    for (const group of this.store.groups()) {
      if (slot && this.store.get(`schedule:group:${group.id}:${slot}`)) continue; // has a time of its own
      try {
        sent += (await this.askGroup(group, false, "", round)).asked;
      } catch (err) {
        this.log(`Could not ask group ${group.name}`, err);
        failed.push(group.name);
      }
    }
    for (const person of this.store.nominees()) {
      if (this.store.groupsOf(person.id).length) continue; // asked with their group
      if (slot && this.store.get(`schedule:person:${person.id}:${slot}`)) continue; // has a time of their own
      if (!this.address(person)) {
        unreachable.push(person);
        continue;
      }
      try {
        if (await this.enqueue(person.id, () => this.startCheckin(person, false, "", round))) sent++;
      } catch (err) {
        this.log(`Could not start check-in for ${person.email}`, err);
        failed.push(displayName(person));
      }
    }
    if (failed.length) {
      await this.notifyAdmins(`I could not ask: ${failed.join(", ")}. Send **ask** with their name to try again; the server log has the details.`);
    }
    return { sent, unreachable };
  }

  /**
   * One chase, and only one, to anyone who has not answered today's opening
   * question within `minMinutes` of it being sent.
   */
  async runReminders(minMinutes = 0): Promise<number> {
    let nudged = 0;
    if (this.paused) return 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open" || checkin.reminded) continue;
      if (this.store.get(`takeover:${checkin.id}`)) continue;
      if (this.store.get(`via:${checkin.id}`)) continue; // asked in a group: chased there, together
      const messages = this.sinceAsk(checkin.id);
      if (!messages.length || messages.some((m) => m.role === "user")) continue;
      const sentAt = Date.parse(`${messages[0]!.at.replace(" ", "T")}Z`);
      if (minMinutes > 0 && this.now().getTime() - sentAt < minMinutes * 60_000) continue;
      const person = this.store.personById(checkin.personId);
      if (!person?.nominated) continue;
      try {
        const nudge = this.nudgeText();
        await this.say(person, nudge);
        this.store.addMessage(checkin.id, "bot", nudge);
        this.store.markReminded(checkin.id);
        nudged++;
      } catch (err) {
        this.log(`Could not remind ${person.email}`, err);
      }
    }
    return nudged;
  }

  /** Close the day: write any outstanding notes, build the digest, send it to the admins. */
  async runDigest(): Promise<string> {
    const date = this.today();

    for (const checkin of this.store.checkinsOn(date)) {
      if (checkin.status !== "open") continue;
      const person = this.store.personById(checkin.personId);
      if (!person) continue;
      const replied = this.sinceAsk(checkin.id).some((m) => m.role === "user");
      if (!replied) {
        this.closeUnanswered(checkin);
        continue;
      }
      try {
        await this.enqueue(person.id, () => this.finish(person, checkin, false));
      } catch (err) {
        this.log(`Could not write note for ${person.email}`, err);
      }
    }

    const notes = new Map(this.store.notesOn(date).map((n) => [n.personId, n]));
    const people: DigestPerson[] = this.store.nominees().map((person) => {
      const note = notes.get(person.id) ?? null;
      return {
        person,
        note,
        activeItems: this.store.activeItems(person.id),
        missing: note ? null : this.address(person) ? "no_response" : "not_connected",
      };
    });

    const text = people.length
      ? await this.brain.digest(date, people)
      : `**Daily digest: ${date}**\n\nNobody is nominated yet. Send **nominate name@company.com** to add someone.`;
    this.store.saveDigest(date, text);

    for (const email of this.config.adminEmails) {
      const admin = this.store.personByEmail(email);
      if (!admin?.conversationId) {
        this.log(`Digest not delivered to ${email}: they have not opened the bot in Teams yet`);
        continue;
      }
      try {
        await this.sayAdmin(admin, text);
      } catch (err) {
        this.log(`Could not deliver digest to ${email}`, err);
      }
    }
    return text;
  }

  // ---------- admin commands ----------

  private resolveOne(query: string): Person | string {
    const matches = this.store.findNominees(query);
    if (matches.length === 1) return matches[0]!;
    if (!matches.length) return `I don't have anyone nominated matching "${query}". Send **list** to see who is.`;
    return `"${query}" matches several people: ${matches.map((p) => p.email).join(", ")}. Use the full email.`;
  }

  private async handleAdmin(admin: Person, text: string): Promise<void> {
    const reply = (msg: string) => this.sayAdmin(admin, msg);
    // Right after a nomination, the admin's next plain message is taken as that
    // person's role profile. Any message uses the opportunity up.
    const awaitingKey = `awaitingProfile:${admin.id}`;
    const awaiting = Number(this.store.get(awaitingKey) || 0);
    this.store.set(awaitingKey, "");
    const nextSteps = (p: Person) =>
      `To ask ${displayName(p)} for an update right now, send **checkin ${p.email}**. Otherwise the first check-in goes out at the next scheduled time.`;
    const [, verbRaw = "", rest = ""] = /^(\S+)\s*([\s\S]*)$/.exec(text) ?? [];
    const arg = rest.trim();
    let verb = verbRaw.toLowerCase().replace(/[:,]$/, "");
    // "list" alone is a command; "list who is blocked" is a question.
    if (["help", "hi", "hello", "menu", "board", "refresh", "list", "digest", "pause", "resume"].includes(verb) && arg) verb = "";
    if (verb === "stop") verb = "pause";
    if (verb === "add") verb = "nominate";
    if (verb === "delete") verb = "remove";
    if (verb === "checkin") verb = "ask";
    // Short one-liners are commands. Anything longer or on several lines is
    // free-form and may hold several instructions, so it is interpreted.
    const short = !text.includes("\n") && text.split(/\s+/).length <= 6;
    if (!short && !["profile", "greeting", "style", "schedule", "tracker"].includes(verb)) verb = "";

    try {
      switch (verb) {
        case "help":
          return await reply(ADMIN_HELP);

        case "hi":
        case "hello":
        case "menu":
        case "board":
          return await this.showBoard(admin);

        case "tracker": {
          if (/^(build|rebuild)\b/i.test(arg)) {
            const group = this.resolveGroup(arg.replace(/^(build|rebuild)\s+/i, "").replace(/^(for\s+)?(the\s+)?group\s+/i, "").trim());
            if (typeof group === "string") return await reply(group);
            await reply(`Reading the whole chat history of ${group.name}. This takes a minute.`);
            return await reply(await this.buildTracker(group));
          }
          const off = /\boff$/i.test(arg);
          const on = /\bon$/i.test(arg);
          const group = this.resolveGroup(arg.replace(/\b(off|on)$/i, "").replace(/^group\s+/i, "").trim());
          if (typeof group === "string") {
            return await reply(arg ? group : "Tell me which group, for example: **tracker Recruitment Team** (add **off** to stop updating it).");
          }
          if (off) {
            this.store.set(`trackeroff:${group.id}`, "1");
            return await reply(`I will stop updating the tracker in ${group.name}. The message already there is left as it is.`);
          }
          if (on) this.store.set(`trackeroff:${group.id}`, "");
          if (!this.direct?.createTracker) return await reply("The tracker needs messages to be sent from your own Teams account.");
          if (this.store.get(`trackeroff:${group.id}`)) return await reply(`The tracker for ${group.name} is off. Send **tracker ${group.name} on** to start it.`);
          this.store.set(`trackerhash:${group.id}`, ""); // force a fresh copy
          await this.refreshTrackers(group);
          return await reply(
            this.store.get(`tracker:${group.id}`)
              ? `The tracker in **${group.name}** is up to date and pinned at the top of that chat. I update it as people report.`
              : `I could not post the tracker in ${group.name}. The server log has the details.`,
          );
        }

        case "pause":
          this.store.set("paused", "1");
          return await reply(
            "**Paused.** Nothing will be sent to anyone: no questions, replies, chases or scheduled asks. What people write is still recorded, and you can still ask me things. Send **resume** to start again.",
          );

        case "resume":
          this.store.set("paused", "");
          return await reply("**Running again.** Nothing that was skipped while paused is sent now; use **ask** if you want an update.");

        case "refresh":
          // Nothing to say: the board is brought up to date after every command.
          return;

        case "schedule": {
          let words = arg.split(/\s+/).filter(Boolean);
          if (!words.length) return await reply(this.scheduleStatus());
          const digest = /^digest$/i.test(words[0]!);
          if (digest) words = words.slice(1);

          // "morning" or "evening" anywhere says which of the two daily messages is meant.
          let slot: Slot | "" = "";
          words = words.filter((w) => {
            const plain = w.toLowerCase().replace(/[:,]$/, "");
            const named: Slot | "" = /^(morning|plan)$/.test(plain) ? "morning" : /^(evening|eod|review)$/.test(plain) ? "evening" : "";
            if (!named || slot) return true;
            slot = named;
            return false;
          });
          if (!words.length) return await reply(this.scheduleStatus());

          // The time (or "off") is the last word, or the last two ("5 pm"). Anything before it names a person or group.
          const isOff = (w: string) => /^(off|none|stop|no|remove)$/i.test(w);
          const isWhen = (w: string) => isOff(w) || parseTime(w) !== null;
          const tail = words.length > 1 && isWhen(words.slice(-2).join(" ")) && !isWhen(words.at(-1)!) ? 2 : 1;
          const when = words.slice(-tail).join(" ");
          const who = digest ? "" : words.slice(0, -tail).join(" ").replace(/^group\s+/i, "").replace(/[:,]$/, "").trim();

          let prefix = "";
          let label = "";
          if (who) {
            const people = this.store.findNominees(who);
            const group = this.resolveGroup(who);
            if (people.length === 1) {
              prefix = `person:${people[0]!.id}:`;
              label = displayName(people[0]!);
            } else if (typeof group !== "string") {
              prefix = `group:${group.id}:`;
              label = group.name;
            } else {
              return await reply(`I could not match "${who}" to one person or group. Send **list** to see who is added.`);
            }
          }

          if (isOff(when)) {
            if (digest) this.store.set("schedule:digest", "");
            else for (const s of slot ? [slot] : SLOTS) this.store.set(`schedule:${prefix}${s}`, "");
            if (!label) return await reply(this.scheduleStatus());
            return await reply(`${label} no longer has a ${slot ? `${slot} ` : ""}time of their own.`);
          }
          const time = parseTime(when);
          if (time === null) {
            return await reply(
              "I did not understand that time. Try **schedule morning 09:30**, **schedule evening 17:30**, **schedule priya morning 10:00** or **schedule off**.",
            );
          }
          // With no "morning" or "evening" given, a time before noon is the morning message.
          const chosen: Slot = slot || slotOf(time);
          const name = digest ? "digest" : `${prefix}${chosen}`;
          this.store.set(`schedule:${name}`, time);
          // A time that has already passed today starts tomorrow, rather than firing at once.
          if (this.localNow().minutes > Number(time.slice(0, 2)) * 60 + Number(time.slice(3))) {
            this.store.set(`ran:${name}`, this.today());
          }
          if (!label) return await reply(this.scheduleStatus());
          return await reply(
            `${label} will be asked automatically at **${time}** (${SLOT_LABEL[chosen]}), Monday to Friday, instead of at the general time.`,
          );
        }

        case "nominate": {
          if (/^group\b/i.test(arg)) return await reply(await this.addGroup(arg.replace(/^group\s*/i, "").trim()));
          const email = arg.split(/\s+/)[0]?.toLowerCase() ?? "";
          if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
            return await reply("Tell me who by work email, for example: **nominate priya@company.com**");
          }
          // Through the bot an admin's messages are commands, so they cannot also give updates there.
          if (!this.direct && this.config.adminEmails.includes(email)) {
            return await reply("That's an admin account. Admins receive the digest rather than check-ins.");
          }
          let chat: { conversationId: string; name: string } | undefined;
          if (this.direct) {
            const found = await this.direct.connect(email);
            if ("notFound" in found) {
              return await reply(
                `There is nobody in the organisation with the address ${email}, so nothing was sent.` +
                  (found.suggestions.length
                    ? ` Did you mean:\n\n${found.suggestions.map((s) => `- ${s}`).join("\n")}`
                    : " Check the spelling against their Teams profile."),
              );
            }
            chat = found;
          }
          const person = this.store.upsertPerson(email, chat && { name: chat.name, chatId: chat.conversationId });
          this.store.setNominated(person.id, true);
          this.store.set(`solo:${person.id}`, "1"); // added in their own right, not only through a group
          if (this.address(person)) {
            if (!this.personal) await this.say(person, this.welcomeText(person));
            this.store.set(awaitingKey, String(person.id));
            return await reply(
              `${displayName(person)} is nominated. ${
                this.personal
                  ? "Nothing has been sent to them yet."
                  : this.direct
                    ? "A heads-up explaining the automated check-ins has gone to them from your account. No update has been asked for yet."
                    : "I've told them how the check-ins work."
              }\n\nNow tell me about their role: reply with their role, responsibilities and targets in your own words, and I'll phrase the daily questions around it.`,
            );
          }
          this.store.set(awaitingKey, String(person.id));
          return await reply(
            `${email} is nominated. They are not connected to me yet: once the app is installed for them in Teams (or they open it and say hello) I'll introduce myself and start the daily check-ins.\n\nNow tell me about their role: reply with their role, responsibilities and targets in your own words.`,
          );
        }

        case "profile": {
          const split = /^([^:\n]+?)\s*:\s*([\s\S]+)$/.exec(arg);
          const target = this.resolveOne((split ? split[1]! : arg).trim());
          if (typeof target === "string") return await reply(target);
          if (!split) {
            return await reply(
              target.profile
                ? `**Profile for ${displayName(target)}**\n\n${target.profile}`
                : `No profile for ${displayName(target)} yet. Set one with **profile ${target.email}: role, responsibilities, targets**`,
            );
          }
          this.store.setProfile(target.id, split[2]!.trim());
          return await reply(
            `Saved. Questions for ${displayName(target)} will be phrased around this role and these targets.\n\n${nextSteps(target)}`,
          );
        }

        case "style": {
          const refresh = /\brefresh\b/i.test(arg);
          const target = this.resolveOne(arg.replace(/\brefresh\b/i, "").trim());
          if (typeof target === "string") return await reply(target);
          if (!this.personal) {
            return await reply("I only learn your writing style when messages are sent from your own account without mentioning automation.");
          }
          const learned = await this.learnRapport(target, refresh);
          return await reply(
            learned
              ? `**What I remember about ${displayName(target)}** (from your chat history, updated each day before asking)\n\nGreeting: "${this.store.get(`greeting:${target.id}`) || "Hi"}"\n\n${this.memoryFor(target) ?? learned}`
              : `There is not enough past chat with ${displayName(target)} to learn from, so I use the standard short, simple style.`,
          );
        }

        case "greeting": {
          const [who = "", ...rest] = arg.split(/\s+/);
          const target = this.resolveOne(who.replace(/:$/, ""));
          if (typeof target === "string") return await reply(target);
          const wanted = rest.join(" ").replace(/[,.!]+$/, "").trim();
          const key = `greeting:${target.id}`;
          if (!wanted) return await reply(`Messages to ${displayName(target)} open with "${this.store.get(key) || "Hi"}".`);
          // An empty value means "plain Hi, chosen on purpose", so learning will not replace it.
          this.store.set(key, /^(off|none|plain|hi)$/i.test(wanted) ? "" : wanted.slice(0, 40));
          return await reply(`Messages to ${displayName(target)} will open with "${this.store.get(key) || "Hi"}".`);
        }

        case "remove": {
          if (/^group\b/i.test(arg)) {
            const group = this.resolveGroup(arg.replace(/^group\s*/i, ""));
            if (typeof group === "string") return await reply(group);
            const members = this.store.groupMembers(group.id);
            this.store.removeGroup(group.id);
            // People who were only followed through this group stop being followed.
            for (const m of members) {
              if (!this.store.get(`solo:${m.id}`) && !this.store.groupsOf(m.id).length) this.store.setNominated(m.id, false);
            }
            return await reply(`${group.name} is removed. Nothing was posted in the group, and past notes are kept.`);
          }
          const target = this.resolveOne(arg);
          if (typeof target === "string") return await reply(target);
          this.store.set(`solo:${target.id}`, "");
          this.store.setNominated(target.id, false);
          return await reply(`${displayName(target)} will no longer get check-ins. Their past notes are kept.`);
        }

        case "list": {
          const nominees = this.store.nominees();
          const groupLines = this.store
            .groups()
            .map((g) => `- **${g.name}** (group chat): ${this.store.groupMembers(g.id).length} people`);
          if (!nominees.length && !groupLines.length) {
            return await reply("Nobody is added yet. Send **add name@company.com** or **add group Group Name**.");
          }
          const date = this.today();
          const lines = nominees.map((p) => {
            const c = this.store.checkin(p.id, date);
            const today = !this.address(p)
              ? "not connected in Teams yet"
              : !c
                ? "no check-in yet today"
                : c.status === "completed"
                  ? "update received"
                  : c.status === "no_response"
                    ? "no response"
                    : "check-in in progress";
            return `- **${displayName(p)}** (${p.email}): ${today}${p.profile ? "" : "; no role profile set"}`;
          });
          return await reply(
            `**People (${nominees.length})**\n\n${lines.join("\n")}` +
              (groupLines.length ? `\n\n**Groups (${groupLines.length})**\n\n${groupLines.join("\n")}` : ""),
          );
        }

        case "report": {
          const target = this.resolveOne(arg);
          if (typeof target === "string") return await reply(target);
          const notes = this.store.recentNotes(target.id, 5);
          const body = notes.length
            ? notes.map((n) => `**${n.date}**\n${renderNote(n)}`).join("\n\n")
            : "No check-in notes yet.";
          return await reply(
            `**${displayName(target)}**\n\n**Open items**\n${renderItems(this.store.activeItems(target.id), this.today())}\n\n${body}`,
          );
        }

        case "ask": {
          // "ask all", "ask priya", "ask mahanth for an update": ask now, even if already asked today.
          if (/^group\b/i.test(arg)) {
            const group = this.resolveGroup(arg.replace(/^group\s*/i, ""));
            if (typeof group === "string") return await reply(group);
            return await reply((await this.askGroup(group, true)).message);
          }
          const nominees = this.store.nominees();
          const words = arg.toLowerCase().split(/[^a-z0-9@.]+/).filter((w) => w.length > 2);
          if (!arg || words.some((w) => ["all", "everyone", "everybody", "team"].includes(w))) {
            if (!nominees.length && !this.store.groups().length) {
              return await reply("Nobody is added yet. Send **add name@company.com** or **add group Group Name** first.");
            }
            const results: string[] = [];
            for (const group of this.store.groups()) {
              try {
                results.push(`- ${(await this.askGroup(group, true)).message}`);
              } catch (err) {
                this.log(`Could not ask group ${group.name}`, err);
                results.push(`- Could not post in ${group.name}.`);
              }
            }
            for (const p of nominees) {
              if (this.store.groupsOf(p.id).length) continue; // asked with their group
              try {
                results.push(`- ${await this.askNow(p)}`);
              } catch (err) {
                this.log(`Could not ask ${p.email}`, err);
                results.push(`- Could not reach ${displayName(p)}.`);
              }
            }
            return await reply(results.join("\n"));
          }
          const named = nominees.filter((p) =>
            words.some(
              (w) =>
                p.email === w ||
                p.email.split("@")[0]!.split(".").includes(w) ||
                (p.name ?? "").toLowerCase().split(/\s+/).includes(w),
            ),
          );
          if (named.length !== 1) {
            return await reply(
              named.length
                ? `That matches several people: ${named.map((p) => p.email).join(", ")}. Use the full email.`
                : "I could not tell who you mean. Send **list** to see who is added, or **menu** for buttons.",
            );
          }
          return await reply(await this.askNow(named[0]!));
        }

        case "digest":
          await reply("Putting today's digest together. It will arrive here shortly.");
          await this.runDigest();
          return;

        default: {
          if (!text) return;
          const pending = awaiting ? this.store.personById(awaiting) : undefined;
          if (pending?.nominated) {
            this.store.setProfile(pending.id, text);
            return await reply(
              `Saved as the role profile for ${displayName(pending)}. To change it, send **profile ${pending.email}: ...**\n\n${nextSteps(pending)}`,
            );
          }
          const actions = await this.brain.interpret(text, {
            people: this.store.nominees().map((p) => ({ name: displayName(p), email: p.email })),
            groups: this.store.groups().map((g) => g.name),
          });
          return await this.runActions(admin, actions, text);
        }
      }
    } catch (err) {
      this.log(`Admin command failed: ${verb}`, err);
      await reply("That didn't work: something went wrong on my side. The server log has the details.");
    }
  }

  /** Carry out what a free-form admin message asked for, in order, and report on each part. */
  private async runActions(admin: Person, actions: AdminAction[], original: string): Promise<void> {
    const reply = (msg: string) => this.sayAdmin(admin, msg);
    const command = (text: string) => this.handleAdmin(admin, text);
    const roles: string[] = [];
    const problems: string[] = [];
    // Adding comes first, then roles, then asking, so that a question in the
    // same message already benefits from the roles given with it.
    const rank = (a: AdminAction) => (a.type.startsWith("add_") ? 0 : a.type === "set_role" || a.type === "set_greeting" ? 1 : 2);
    const ordered = actions.map((a, i) => ({ a, i })).sort((x, y) => rank(x.a) - rank(y.a) || x.i - y.i).map((x) => x.a);

    for (const action of ordered) {
      const target = action.target.trim();
      const detail = action.text.trim();
      switch (action.type) {
        case "add_person":
          await command(`add ${target}`);
          break;
        case "add_group":
          await reply(await this.addGroup(target));
          break;
        case "remove_person":
          await command(`remove ${target}`);
          break;
        case "remove_group":
          await command(`remove group ${target}`);
          break;
        case "ask_all":
          await command("ask all");
          break;
        case "ask_group": {
          const group = this.resolveGroup(target);
          if (typeof group === "string") await reply(group);
          else await reply((await this.askGroup(group, true, detail)).message);
          break;
        }
        case "ask_person": {
          const person = this.resolveOne(target);
          if (typeof person === "string") await reply(person);
          else await reply(await this.askNow(person, detail));
          break;
        }
        case "set_role": {
          const person = this.resolveOne(target);
          if (typeof person === "string") {
            problems.push(target);
          } else {
            this.store.setProfile(person.id, detail);
            roles.push(`${displayName(person)}: ${detail}`);
          }
          break;
        }
        case "set_greeting":
          await command(`greeting ${target} ${detail}`);
          break;
        case "schedule":
          await command(
            `schedule ${["", "ask", "all", "everyone"].includes(target.toLowerCase()) ? "" : `${target} `}${detail}`,
          );
          break;
        case "digest":
        case "list":
        case "board":
          await command(action.type);
          break;
        case "build_tracker": {
          const group = this.resolveGroup(target);
          if (typeof group === "string") {
            await reply(group);
          } else {
            await reply(`Reading the whole chat history of ${group.name}. This takes a minute.`);
            await reply(await this.buildTracker(group));
          }
          break;
        }
        case "question":
          if (!this.store.nominees().length) {
            await reply("There are no notes yet because nobody is added. Send **add name@company.com** or **add group Group Name**, or **help** for everything you can type.");
          } else {
            await reply(await this.brain.answer(detail || original, this.material()));
          }
          break;
      }
    }

    if (roles.length || problems.length) {
      await reply(
        (roles.length ? `Saved roles:\n${roles.map((r) => `- ${r}`).join("\n")}` : "") +
          (problems.length
            ? `${roles.length ? "\n\n" : ""}I could not match these to anyone I follow, so no role was saved: ${problems.join(", ")}.`
            : ""),
      );
    }
    // Adding a person normally makes the next message their role; not after a batch like this.
    this.store.set(`awaitingProfile:${admin.id}`, "");
  }

  /** Everything the AI may draw on to answer an admin's question. */
  private material(): string {
    const since = daysBefore(this.today(), 14);
    const notes = this.store.notesSince(since);
    const sections = this.store.nominees().map((p) => {
      const mine = notes.filter((n) => n.personId === p.id);
      const today = this.store.checkin(p.id, this.today());
      const live =
        today?.status === "open"
          ? this.store
              .messages(today.id)
              .map((m) => `${m.role === "bot" ? "Asked" : displayName(p)}: ${m.text}`)
              .join("\n")
          : "";
      return [
        `# ${displayName(p)} (${p.email})`,
        `Role and targets: ${p.profile ?? "(not provided)"}`,
        `Open items:\n${renderItems(this.store.activeItems(p.id))}`,
        this.store.get(`rapport:${p.id}`)
          ? `## Learned from the chat history with them (work, commitments and dates)\n${this.store.get(`rapport:${p.id}`)}`
          : "",
        live
          ? `## Today's conversation, still in progress (not yet summarised)\n${live}`
          : today?.status === "no_response"
            ? "## Today: asked, no reply"
            : !today
              ? "## Today: not asked yet"
              : "",
        mine.length
          ? mine.map((n) => `## ${n.date}\n${renderNote(n)}`).join("\n\n")
          : "(no finished check-in notes in the last 14 days)",
      ]
        .filter(Boolean)
        .join("\n\n");
    });
    const groupSections = this.store
      .groups()
      .map((g) => ({ g, memory: this.store.get(`gmemory:${g.id}`) }))
      .filter((x) => x.memory)
      .map(
        (x) =>
          `# Group chat: ${x.g.name}\nMembers: ${this.store.groupMembers(x.g.id).map(displayName).join(", ")}\n\n## Learned from the group's chat history (work, commitments and dates, by person)\n${x.memory}`,
      );
    return `Today is ${this.today()}. Check-in notes cover the last 14 days; what was learned from chat history goes back further.\n\n${[...sections, ...groupSections].join("\n\n")}`;
  }
}
