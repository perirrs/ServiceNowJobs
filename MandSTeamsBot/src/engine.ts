import { displayName, renderItems, renderNote } from "./ai.js";
import type { AdminAction, Brain, CheckinContext, DigestPerson, Round, TrackerEntry } from "./ai.js";
import { todayIn } from "./config.js";
import type { Config } from "./config.js";
import type { ChatMessage, Checkin, Group, Person, Store, WorkItem } from "./db.js";
import { describeCounts, describeProgress } from "./ats.js";
import type { Ats } from "./ats.js";

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
  ): Promise<string | void>;
  /** The sender's email, so their own copy of the log and drafts goes to them. */
  senderEmail?: string;
  /** Delete a message the sender sent. */
  unsend?(conversationId: string, messageId: string): Promise<void>;
  /** Who is out of office between two times (by email), from Outlook. */
  awayBetween?(emails: string[], startIso: string, endIso: string): Promise<Map<string, [string, string][]>>;
  /** The sender's meetings between two times. */
  meetings?(startIso: string, endIso: string): Promise<{ id: string; subject: string; start: string; end: string; attendees: string[]; chatId: string }[]>;
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

- **hours 09:30-18:30**: office hours, Monday to Friday. Outside them I send nothing to anyone (no replies, chases or scheduled messages), but still record what people write. **hours off** allows any time.
- **timezone India**: the time I work in, for office hours and schedules.
- **sent**: everything I sent as you today, each with a Delete button. You also get this list once a day after office hours.
- **approve** Name: every message to that person or group comes to you first as a draft to send, edit or skip. **approve Name off** stops it. On the day someone is added this is on by default.
- **leave** priya until 2026-11-25 (or **today**, or **off**): I will not ask or chase them. People out of office in Outlook are skipped automatically.
- **holiday** 2026-12-25 Christmas: nobody is asked that day. **holidays** lists them.
- Before a meeting with people I follow, you get a short brief on each of them; after it, action items agreed in the meeting chat go on the tracker.
- **weekly**: the week's report now (plan against done, what keeps coming up). It also arrives on Fridays after office hours.
- **usage**: what the AI has cost each day this week. Today's figure is also on the board.
- **pause**: stop sending anything to anyone, at once. **resume** starts again.
- **board** (or **menu**): the team board, with who has replied, who is blocked, and buttons for everything below
- **add** name@company.com: start asking someone for updates. I first read your whole chat history with them and put their work on the tracker.
- **remove** name: stop asking them (their notes are kept)
- **tracker build** Group Name: go through the group's whole chat history and build its tracker: each piece of work by person, what is done so far, progress and due date.
- **tracker** Group Name: post (or refresh) the tracker table in that group: who is doing what, due dates and status, pinned at the top and kept up to date by me. **tracker Group Name off** stops it.
- **add group** Group Name: follow a Teams group chat. I first read the group's whole chat history and each member's whole chat with you, then build and pin the tracker. Updates are asked for in the group, and follow-up questions stay there with the person tagged; only something that should not be said in front of the group goes to them one to one. **ask group** Name asks now, **remove group** Name stops.
- **ask** name: ask them for an update right now. **ask all** asks everyone.
- **profile** name: their role, responsibilities and targets, so the questions fit their job
- **style** name: what I learned from your past chat about how you write to them. **style name refresh** reads the chat again.
- **greeting** name Hi Anna: how to open messages to that person. I learn this from your past chat; use this to set or correct it. **greeting name off** goes back to a plain Hi.
- **list**: everyone you have added and where they stand today
- **report** name: their recent notes and open items
- **digest**: today's summary of everyone, right now
- **schedule morning 09:30 India** and **schedule evening 17:30 India**: add a timezone (India, UK, Dubai, US...) to any time; without one, my own timezone is used. the two daily messages, Monday to Friday. The morning one asks each person what they will work on today; the evening one checks what happened with that plan and what is blocking them. **schedule off** stops both, **schedule morning off** one of them, and **schedule** alone shows the settings.
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
    case "draftSend":
      return `draft send ${v.id}\n${str(str("field"))}`;
    case "draftSkip":
      return `draft skip ${v.id}`;
    case "unsend":
      return `unsend ${v.id}`;
    case "resume":
      return "resume";
    case "schedule":
      return `schedule ${str("time")}`;
    case "scheduleOff":
      return "schedule off";
    case "scheduleFor":
      return ["schedule", str("who"), str("slot"), str(str("field")), str(str("zoneField"))].filter(Boolean).join(" ");
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

/**
 * What follows from a message once it has really gone out: recording it,
 * opening the conversation it starts, setting the flags that stop it being
 * sent again. Held with a draft, and applied only when the draft is sent.
 */
type Effect =
  | { kind: "record"; checkinId: number; text: string }
  | { kind: "kv"; key: string; value: string }
  | { kind: "cursor"; checkinId: number }
  | { kind: "reminded"; checkinId: number }
  | { kind: "finish"; checkinId: number }
  | { kind: "openAsk"; personId: number; date: string; text: string; round: Round; topic: string }
  | {
      kind: "groupAsk";
      groupId: number;
      date: string;
      text: string;
      round: Round;
      asks: { personId: number; topic: string }[];
    }
  | { kind: "depAsk"; helperId: number; date: string; groupId: number; text: string; topic: string };

/** A message on its way to staff from the sender's account. */
interface Outgoing {
  chatId: string;
  text: string;
  mentions?: { userId: string; name: string }[];
  quote?: { messageId: string; preview: string; userId: string; name: string };
  /** Who it is for, as the sender would say it. */
  label: string;
  /** The text as written into the check-in record, when that differs from what is sent. */
  recorded?: string;
  /** Applied once it is really sent. */
  effects?: Effect[];
  /** Applied if it is held and then skipped or left to expire, so the same thing is not drafted again. */
  onSkip?: Effect[];
  /** Set to the draft's id while it waits, so nothing piles up behind it. */
  waitKey?: string;
  /** Another chat whose approve-first setting also covers this message (a group, for a private follow-up). */
  alsoApprove?: string;
}

/** The message as it reads in Teams, with each tag shown as "@Name". */
function shown(out: Outgoing): string {
  let text = out.text;
  (out.mentions ?? []).forEach((m, i) => (text = text.replace(`{@${i}}`, `@${m.name}`)));
  return text;
}

/** Whole days from one YYYY-MM-DD date to another. */
const daysBetween = (from: string, to: string) => Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);

/** Dollars per million tokens, input and output. */
const PRICES: Record<string, [number, number]> = {
  "claude-haiku-5-5": [0.1, 0.5],
  "claude-sonnet-5-5": [2, 10],
  "claude-opus-5-5": [4, 20],
  "claude-fable-5-2": [8, 40],
};

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

const ZONE_TABLE: Record<string, string> = {
  india: "Asia/Kolkata", ist: "Asia/Kolkata", kolkata: "Asia/Kolkata", hyderabad: "Asia/Kolkata", bangalore: "Asia/Kolkata",
  bengaluru: "Asia/Kolkata", mumbai: "Asia/Kolkata", delhi: "Asia/Kolkata", chennai: "Asia/Kolkata",
  uk: "Europe/London", london: "Europe/London", gmt: "Europe/London", bst: "Europe/London", england: "Europe/London",
  dubai: "Asia/Dubai", uae: "Asia/Dubai", gst: "Asia/Dubai",
  us: "America/New_York", usa: "America/New_York", est: "America/New_York", "new york": "America/New_York",
  singapore: "Asia/Singapore", sgt: "Asia/Singapore",
};
const ZONE_WORDS = new Set(Object.keys(ZONE_TABLE));

/** The timezones offered on the board, with plain names. */
const ZONE_CHOICES = [
  { zone: "Asia/Kolkata", label: "India time" },
  { zone: "Europe/London", label: "UK time" },
  { zone: "Asia/Dubai", label: "Dubai time" },
  { zone: "Asia/Singapore", label: "Singapore time" },
  { zone: "America/New_York", label: "US Eastern time" },
  { zone: "UTC", label: "UTC" },
];

/** "Asia/Kolkata" -> "India time"; anything else -> "Asia/Tokyo time". */
export function zoneLabel(zone: string): string {
  return ZONE_CHOICES.find((z) => z.zone === zone)?.label ?? `${zone} time`;
}

/**
 * Take a timezone out of a phrase: "morning 09:30 India" -> { rest: "morning 09:30", zone: "Asia/Kolkata" }.
 * Only clear zone words count (India, IST, UK, Dubai...) or names with a slash, so a person's name is never taken for one.
 */
export function takeZone(text: string): { rest: string; zone: string } {
  const words = text.split(/\s+/).filter(Boolean);
  for (let size = 2; size >= 1; size--) {
    for (let i = 0; i + size <= words.length; i++) {
      const candidate = words.slice(i, i + size).join(" ").replace(/^\(|\)$|,$/g, "");
      const plain = candidate.toLowerCase().replace(/\s+time$/, "");
      if (!ZONE_WORDS.has(plain) && !candidate.includes("/") && plain !== "utc") continue;
      const zone = parseZone(plain === "utc" ? "UTC" : candidate.includes("/") ? candidate : plain);
      if (!zone) continue;
      return { rest: [...words.slice(0, i), ...words.slice(i + size)].join(" "), zone };
    }
  }
  return { rest: text.trim(), zone: "" };
}

/** "India", "IST", "uk", "Asia/Dubai" -> an IANA timezone name, or null. */
export function parseZone(input: string): string | null {
  const plain = input.trim().toLowerCase();
  const known = ZONE_TABLE;
  const zone = known[plain] ?? input.trim();
  if (plain === "utc") return "UTC";
  try {
    new Intl.DateTimeFormat("en-GB", { timeZone: zone });
    return zone;
  } catch {
    return null;
  }
}

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
    this.migrateAskMarks();
  }

  /** Ask marks used to count messages; they now hold the id of the last message before the ask. */
  private migrateAskMarks(): void {
    if (this.store.get("askmark-ids")) return;
    for (const key of this.store.keys("askmark:")) {
      const count = Number(this.store.get(key) || 0);
      const messages = this.store.messages(Number(key.slice("askmark:".length)));
      this.store.set(key, String(count > 0 ? (messages[count - 1]?.id ?? messages.at(-1)?.id ?? 0) : 0));
    }
    this.store.set("askmark-ids", "1");
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

  /** The timezone everything runs on: set from the chat, otherwise from TIMEZONE. */
  private get tz(): string {
    return this.store.get("timezone") || this.config.timezone;
  }

  private today(): string {
    return todayIn(this.tz, this.now());
  }

  /** Office hours as minutes from midnight, or null when the bot may speak at any time. */
  private officeHours(): { start: number; end: number; zone: string; text: string } | null {
    const value = this.store.get("hours") || "";
    const m = /^(\d\d):(\d\d)-(\d\d):(\d\d)(?:@(.+))?$/.exec(value);
    if (!m) return null;
    const zone = m[5] || this.tz;
    return {
      start: Number(m[1]) * 60 + Number(m[2]),
      end: Number(m[3]) * 60 + Number(m[4]),
      zone,
      text: `${m[1]}:${m[2]} to ${m[3]}:${m[4]} ${zoneLabel(zone)}`,
    };
  }

  /**
   * Whether staff may be messaged now. Outside office hours (and at the
   * weekend, when hours are set) what people write is still recorded, but
   * nothing is sent to them: no replies, chases or scheduled messages.
   */
  private inOfficeHours(): boolean {
    const hours = this.officeHours();
    if (!hours) return true;
    const { minutes, weekend } = this.localNow(hours.zone);
    if (weekend) return false;
    return hours.start <= hours.end
      ? minutes >= hours.start && minutes < hours.end
      : minutes >= hours.start || minutes < hours.end; // a window that runs past midnight
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
  private async say(
    person: Person,
    text: string,
    opts: Pick<Outgoing, "effects" | "onSkip" | "waitKey" | "alsoApprove"> = {},
  ): Promise<"sent" | "held"> {
    if (this.paused) throw new Error("Paused: nothing is sent to staff");
    const to = this.address(person);
    if (!to) throw new Error(`No chat to reach ${person.email} in`);
    const parts = chunk(text);
    let result: "sent" | "held" = "sent";
    for (let i = 0; i < parts.length; i++) {
      const last = i === parts.length - 1;
      if (this.direct) {
        result = await this.sendAsSender({ chatId: to, text: parts[i]!, label: displayName(person), ...(last ? opts : { alsoApprove: opts.alsoApprove }) });
      } else {
        await this.messenger.send(to, parts[i]!);
        if (last) await this.applyEffects(opts.effects);
      }
    }
    return result;
  }

  /**
   * The one way anything reaches staff from the sender's account. Logged, so
   * the sender can see and delete it; held as a draft for approval when that
   * chat is still on approve-first.
   */
  private async sendAsSender(out: Outgoing): Promise<"sent" | "held"> {
    if (this.paused) throw new Error("Paused: nothing is sent to staff");
    if (this.needsApproval(out.chatId) || (out.alsoApprove && this.needsApproval(out.alsoApprove))) {
      const id = await this.queueDraft(out);
      if (out.waitKey) this.store.set(out.waitKey, String(id));
      return "held";
    }
    await this.sendNow(out);
    return "sent";
  }

  /** Send, then apply what follows from it. With `edited`, the record shows the edited text. */
  private async sendNow(out: Outgoing, edited?: string): Promise<void> {
    const id = await this.direct!.send(out.chatId, out.text, out.mentions, out.quote);
    const recordIds = await this.applyEffects(out.effects, edited);
    this.store.logSent(out.chatId, typeof id === "string" ? id : "", edited ?? shown(out), out.label, this.now().toISOString(), recordIds);
  }

  /** Apply what follows from a message that went out. Returns the ids of the record rows written. */
  private async applyEffects(effects: Effect[] = [], edited?: string): Promise<number[]> {
    const ids: number[] = [];
    for (const e of effects) {
      switch (e.kind) {
        case "record":
          ids.push(this.store.addMessage(e.checkinId, "bot", edited ?? e.text));
          break;
        case "kv":
          this.store.set(e.key, e.value);
          break;
        case "cursor":
          // Their one-to-one chat is watched only from this message on.
          this.store.set(`cursor:${e.checkinId}`, new Date(this.now().getTime() - 3000).toISOString());
          break;
        case "reminded":
          this.store.markReminded(e.checkinId);
          break;
        case "finish": {
          const checkin = this.store.checkinById(e.checkinId);
          const person = checkin ? this.store.personById(checkin.personId) : undefined;
          if (!checkin || !person || checkin.status !== "open") break;
          try {
            await this.finish(person, checkin);
          } catch (err) {
            // The closing line did go out. It needs no chase; the note is tried again when the chat is next closed.
            this.log(`Could not write the note for ${person.email}`, err);
            this.store.set(`midchase:${checkin.id}`, "1");
          }
          break;
        }
        case "openAsk": {
          const checkin = this.store.openCheckin(e.personId, e.date);
          this.beginAsk(checkin);
          this.store.set(`via:${checkin.id}`, "");
          this.store.set(`topic:${checkin.id}`, e.topic);
          this.store.set(`kind:${checkin.id}`, e.round);
          ids.push(this.store.addMessage(checkin.id, "bot", edited ?? e.text));
          this.store.set(`round:${checkin.id}:${e.round}`, "1");
          break;
        }
        case "groupAsk": {
          for (const ask of e.asks) {
            const checkin = this.store.openCheckin(ask.personId, e.date);
            this.beginAsk(checkin);
            ids.push(this.store.addMessage(checkin.id, "bot", edited ?? e.text));
            this.store.set(`via:${checkin.id}`, String(e.groupId));
            this.store.set(`kind:${checkin.id}`, e.round);
            this.store.set(`round:${checkin.id}:${e.round}`, "1");
            this.store.set(`topic:${checkin.id}`, ask.topic);
          }
          // Start reading from this ask, unless the group is already being read:
          // then carry on from where the reading had got to, so nothing is skipped.
          if (!this.store.get(`gasked:${e.groupId}`)) this.store.set(`gcursor:${e.groupId}`, "");
          this.store.set(`gasked:${e.groupId}`, this.now().toISOString());
          this.store.set(`gchased:${e.groupId}`, "");
          break;
        }
        case "depAsk": {
          // Their answer is a check-in like any other, about this piece of work.
          const existing = this.store.checkin(e.helperId, e.date);
          if (existing?.status === "open") break;
          const checkin = this.store.openCheckin(e.helperId, e.date);
          this.beginAsk(checkin);
          this.store.set(`via:${checkin.id}`, e.groupId ? String(e.groupId) : "");
          this.store.set(`kind:${checkin.id}`, "update");
          this.store.set(`topic:${checkin.id}`, e.topic);
          ids.push(this.store.addMessage(checkin.id, "bot", edited ?? e.text));
          break;
        }
      }
    }
    return ids;
  }

  /** True while the draft named under `key` is still waiting and under three hours old. A stale key is cleared. */
  private isWaiting(key: string): boolean {
    const id = Number(this.store.get(key) || 0);
    if (!id) return false;
    const draft = this.store.draft(id);
    if (draft?.status === "waiting" && this.now().getTime() - Date.parse(draft.at) <= 3 * 3_600_000) return true;
    this.store.set(key, "");
    return false;
  }

  /** Close drafts nobody acted on within three hours, as if skipped. */
  private async expireDrafts(): Promise<void> {
    for (const d of this.store.staleDrafts(new Date(this.now().getTime() - 3 * 3_600_000).toISOString())) {
      this.store.setDraftStatus(d.id, "expired");
      const out = JSON.parse(d.payload) as Outgoing;
      await this.applyEffects(out.onSkip);
      if (out.waitKey && this.store.get(out.waitKey) === String(d.id)) this.store.set(out.waitKey, "");
    }
  }

  /** On approve-first: today only, after being added, or always if set so. */
  private needsApproval(chatId: string): boolean {
    const setting = this.store.get(`approve:${chatId}`) || "";
    return setting === "always" || setting === this.today();
  }

  private chatLabel(chatId: string): string {
    const group = this.store.groups().find((g) => g.chatId === chatId);
    if (group) return group.name;
    const person = this.store.nominees().find((p) => p.chatId === chatId);
    return person ? displayName(person) : "a chat";
  }

  /** The admin the sender is, who gets the drafts and the log. Falls back to every admin. */
  private senderAdmins(): Person[] {
    const email = this.direct?.senderEmail?.toLowerCase();
    const admins = this.config.adminEmails
      .map((e) => this.store.personByEmail(e))
      .filter((p): p is Person => !!p?.conversationId);
    const own = admins.filter((p) => p.email === email);
    return own.length ? own : admins;
  }

  private async toSender(text: string, card?: Record<string, unknown>): Promise<void> {
    for (const admin of this.senderAdmins()) {
      try {
        if (card && this.messenger.sendCard) await this.messenger.sendCard(admin.conversationId!, card);
        else await this.sayAdmin(admin, text);
      } catch (err) {
        this.log(`Could not reach ${admin.email}`, err);
      }
    }
  }

  private async queueDraft(out: Outgoing): Promise<number> {
    const id = this.store.addDraft(out.chatId, JSON.stringify(out), out.label, this.now().toISOString());
    const text = shown(out);
    await this.toSender(`Draft for ${out.label}, not sent yet: "${text}". Send **draft send ${id}** or **draft skip ${id}**.`, {
      type: "AdaptiveCard",
      $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
      version: "1.4",
      body: [
        { type: "TextBlock", text: `Draft for ${out.label}`, weight: "Bolder", wrap: true },
        { type: "TextBlock", text: "Not sent yet. Change it if you like, then send or skip.", isSubtle: true, spacing: "None", wrap: true, size: "Small" },
        { type: "Input.Text", id: `d${id}`, value: text, isMultiline: true },
      ],
      actions: [
        { type: "Action.Submit", title: "Send", data: { cmd: "draftSend", id, field: `d${id}` } },
        { type: "Action.Submit", title: "Skip", data: { cmd: "draftSkip", id } },
      ],
    });
    return id;
  }

  /** Blockers and pain points from the last seven days, for spotting what keeps coming up. */
  private recentIssues(): string {
    const since = new Date(Date.parse(`${this.today()}T00:00:00Z`) - 6 * 86_400_000).toISOString().slice(0, 10);
    const lines: string[] = [];
    for (const n of this.store.notesSince(since)) {
      const person = this.store.personById(n.personId);
      for (const x of [...n.blockers, ...n.painPoints]) lines.push(`- ${n.date} ${person ? displayName(person) : "?"}: ${x}`);
    }
    return lines.slice(0, 80).join("\n");
  }

  /** The Friday report. Runs once on Fridays after office hours (or 18:00), or whenever asked. */
  async runWeekly(force = false): Promise<string> {
    const date = this.today();
    if (!force) {
      if (this.store.get("weekly") === date) return "";
      const hours = this.officeHours();
      const now = this.localNow(hours?.zone ?? this.tz);
      const friday = new Date(`${date}T12:00:00Z`).getUTCDay() === 5;
      if (!friday || now.minutes < (hours ? hours.end : 18 * 60)) return "";
      // At most one try every 30 minutes if writing it fails.
      const tried = this.store.get("weeklytry");
      if (tried && this.now().getTime() - Date.parse(tried) < 30 * 60_000) return "";
      this.store.set("weeklytry", this.now().toISOString());
    }
    const day = new Date(`${date}T12:00:00Z`).getUTCDay() || 7; // Monday = 1
    const from = new Date(Date.parse(`${date}T00:00:00Z`) - (day - 1) * 86_400_000).toISOString().slice(0, 10);
    const people = this.store.nominees().filter((p) => !this.isAdmin(p));
    if (!people.length) return "";

    // Real figures for the week from TalentFlow, when connected.
    const weekFacts = new Map<number, string>();
    if (this.ats) {
      // Everyone at once, and never more than ten seconds in all, so a slow ATS cannot hold up the minute's other jobs.
      const ats = this.ats;
      const fromIso = this.localMidnightIso(from);
      const all = Promise.all(
        people.map(async (p) => {
          try {
            const counts = await ats.stats(p.email, fromIso, this.now().toISOString());
            if (counts) weekFacts.set(p.id, describeCounts(counts) || "nothing recorded");
          } catch (err) {
            this.log(`Could not read TalentFlow for ${p.email}`, err);
          }
        }),
      );
      await Promise.race([all, new Promise((resolve) => setTimeout(resolve, 10_000))]);
    }
    const rows = people.map((p) => {
      const checkins = this.store.checkinsBetween(p.id, from, date);
      const notes = this.store.notesSince(from).filter((n) => n.personId === p.id);
      const planned = notes.reduce((sum, n) => sum + (n.planToday?.length ?? 0), 0);
      const active = this.store.activeItems(p.id);
      return [
        `# ${displayName(p)}${p.profile ? ` (${p.profile})` : ""}`,
        `Replied on ${new Set(checkins.filter((c) => c.replied).map((c) => c.date)).size} of ${new Set(checkins.map((c) => c.date)).size} days asked.`,
        weekFacts.get(p.id) ? `In TalentFlow this week: ${weekFacts.get(p.id)}.` : "",
        `Planned in the mornings: ${planned} things. Finished this week: ${this.store.doneSince(p.id, from).map((i) => i.title).join("; ") || "none recorded"}.`,
        `Overdue now: ${active.filter((i) => i.due && i.due < date).map((i) => i.title).join("; ") || "none"}. Blocked now: ${active.filter((i) => i.status === "blocked").map((i) => `${i.title}${i.waitingOn ? ` (waiting on ${i.waitingOn})` : ""}`).join("; ") || "none"}.`,
        notes.length ? `Notes:\n${notes.map((n) => `## ${n.date}\n${renderNote(n)}`).join("\n")}` : "No notes this week.",
      ]
        .filter(Boolean)
        .join("\n");
    });
    try {
      const report = await this.brain.weeklyReport({ from, to: date, material: rows.join("\n\n") });
      await this.notifyAdmins(report);
      if (!force) this.store.set("weekly", date);
      return report;
    } catch (err) {
      this.log("Could not write the weekly report", err);
      return "";
    }
  }

  /** Midnight at the start of a YYYY-MM-DD day in the bot's timezone, as an ISO time in UTC. */
  private localMidnightIso(date: string): string {
    const now = this.now();
    let offset = this.localNow().minutes - (now.getUTCHours() * 60 + now.getUTCMinutes());
    if (offset > 720) offset -= 1440;
    if (offset < -720) offset += 1440;
    return new Date(Date.parse(`${date}T00:00:00Z`) - offset * 60_000).toISOString();
  }

  /** A copy of the database every day, keeping two weeks. */
  private backupDaily(): void {
    const date = this.today();
    if (this.store.get("backupday") === date) return;
    this.store.set("backupday", date);
    try {
      this.store.backup(date);
    } catch (err) {
      this.log("Could not back up the database", err);
    }
  }

  /** Tokens used by the AI, kept per day and per model. */
  recordUsage(model: string, input: number, output: number): void {
    const key = `usage:${this.today()}:${model}`;
    const [i, o] = (this.store.get(key) || "0,0").split(",").map(Number);
    this.store.set(key, `${(i ?? 0) + input},${(o ?? 0) + output}`);
  }

  /** "≈ $0.04 (claude-haiku-5-5 210k in, 12k out)" for a day. */
  private usageLine(date: string): string {
    const rows = this.store.keys(`usage:${date}:`).map((k) => {
      const model = k.slice(`usage:${date}:`.length);
      const [i, o] = (this.store.get(k) || "0,0").split(",").map(Number);
      const [pi, po] = PRICES[model] ?? [2, 10];
      return { model, i: i ?? 0, o: o ?? 0, cost: ((i ?? 0) * pi + (o ?? 0) * po) / 1_000_000 };
    });
    if (!rows.length) return "";
    const k = (n: number) => (n >= 1000 ? `${Math.round(n / 1000)}k` : String(n));
    const total = rows.reduce((sum, r) => sum + r.cost, 0);
    return `≈ $${total.toFixed(2)} (${rows.map((r) => `${r.model.replace("claude-", "")} ${k(r.i)} in, ${k(r.o)} out`).join(" · ")})`;
  }

  /** TalentFlow, when connected. */
  ats?: Ats;

  /**
   * What TalentFlow shows for this person: today's figures (fetched at most
   * every 15 minutes, or now when `fresh`), and once a day yesterday's
   * figures, when they last updated it, and this month against target.
   */
  private async refreshAtsFacts(person: Person, fresh = false): Promise<void> {
    if (!this.ats || this.isAdmin(person)) return;
    const stamp = this.store.get(`atsat:${person.id}`);
    if (!fresh && stamp && this.now().getTime() - Date.parse(stamp) < 15 * 60_000) return;
    this.store.set(`atsat:${person.id}`, this.now().toISOString());
    try {
      // From midnight today in the bot's timezone, as UTC.
      const midnight = new Date(this.now().getTime() - this.localNow().minutes * 60_000);
      midnight.setUTCSeconds(0, 0);
      const nowIso = this.now().toISOString();
      const counts = await this.ats.stats(person.email, midnight.toISOString(), nowIso);
      if (!counts) {
        // Not in TalentFlow: nothing to say, rather than "nothing recorded".
        this.store.set(`atsfacts:${person.id}`, "");
        return;
      }
      const text = describeCounts(counts);
      const lines = [text ? `Today so far: ${text}.` : "Today so far: nothing recorded."];
      const daily = await this.atsDaily(person, nowIso);
      if (daily) lines.push(daily);
      this.store.set(`atsfacts:${person.id}`, lines.join("\n"));
    } catch (err) {
      this.log(`Could not read TalentFlow for ${person.email}`, err);
      this.store.set(`atsfacts:${person.id}`, "");
    }
  }

  /**
   * The slower-moving part of what TalentFlow shows, worked out once a day:
   * yesterday's figures, how long since the last update, and this month
   * against target. Kept for the day so a busy morning does not hit the limit.
   */
  private async atsDaily(person: Person, nowIso: string): Promise<string> {
    const ats = this.ats!;
    const date = this.today();
    const key = `atsdaily:${person.id}`;
    const saved = this.store.get(key);
    if (saved?.startsWith(`${date}|`)) return saved.slice(date.length + 1);

    const lines: string[] = [];
    const yesterday = new Date(Date.parse(`${date}T12:00:00Z`) - 86_400_000).toISOString().slice(0, 10);
    const y = await ats.stats(person.email, this.localMidnightIso(yesterday), this.localMidnightIso(date));
    if (y) lines.push(`Yesterday (${yesterday}): ${describeCounts(y) || "nothing recorded"}.`);
    if (ats.lastUpdate) {
      const last = await ats.lastUpdate(person.email);
      const lastDay = last ? this.localDate(last) : "";
      const days = lastDay ? Math.round((Date.parse(`${date}T00:00:00Z`) - Date.parse(`${lastDay}T00:00:00Z`)) / 86_400_000) : -1;
      if (!last) lines.push("Last TalentFlow update: never.");
      else if (days >= 2) lines.push(`Last TalentFlow update: ${lastDay}, ${days} days ago. TalentFlow is behind.`);
    }
    // Progress against this month's targets, when TalentFlow has them.
    const month = date.slice(0, 7);
    const targets = ats.targets ? await ats.targets(person.email, month) : null;
    if (targets) {
      const monthCounts = (await ats.stats(person.email, this.localMidnightIso(`${month}-01`), nowIso)) ?? {};
      lines.push(`This month against target: ${describeProgress(monthCounts, targets)}.`);
    }
    const text = lines.join("\n");
    this.store.set(key, `${date}|${text}`);
    return text;
  }

  /** The YYYY-MM-DD date of an ISO time in the bot's timezone. */
  private localDate(iso: string): string {
    return new Intl.DateTimeFormat("en-CA", { timeZone: this.tz, year: "numeric", month: "2-digit", day: "2-digit" }).format(new Date(iso));
  }

  /** A public holiday today, by name, or "". */
  private holidayToday(): string {
    return this.store.get(`holiday:${this.today()}`) || "";
  }

  /** On leave today: a holiday, out of office in Outlook, or marked on leave by an admin. */
  private isAway(person: Person): boolean {
    const date = this.today();
    if (this.holidayToday()) return true;
    // Out of office now, by the time ranges read from Outlook.
    const oof = this.store.get(`oof:${person.id}`) || "";
    if (oof.startsWith("[")) {
      const now = this.now().toISOString();
      if ((JSON.parse(oof) as [string, string][]).some(([start, end]) => start <= now && now < end)) return true;
    } else if (oof === date) {
      return true;
    }
    const until = this.store.get(`leave:${person.id}`) || "";
    return !!until && until >= date;
  }

  /** Read out-of-office from Outlook for everyone followed, every two hours. */
  async refreshLeave(): Promise<void> {
    if (!this.direct?.awayBetween) return;
    const last = this.store.get("leavecheck");
    if (last && this.now().getTime() - Date.parse(last) < 2 * 3_600_000) return;
    this.store.set("leavecheck", this.now().toISOString());
    const people = this.store.nominees().filter((p) => !this.isAdmin(p));
    if (!people.length) return;
    try {
      const start = this.now();
      const away = await this.direct.awayBetween(
        people.map((p) => p.email),
        start.toISOString(),
        new Date(start.getTime() + 10 * 3_600_000).toISOString(),
      );
      for (const p of people) this.store.set(`oof:${p.id}`, JSON.stringify(away.get(p.email) ?? []));
    } catch (err) {
      this.log("Could not read out-of-office from Outlook", err);
    }
  }

  // ---------- meetings: a brief before, action items after ----------

  /** What to know about someone before a meeting: open work, flags, and what to ask. No AI call. */
  private personBrief(p: Person): string {
    const date = this.today();
    const items = this.store.activeItems(p.id);
    const flag = (i: WorkItem) =>
      i.status === "blocked"
        ? ` (blocked${i.waitingOn ? `, waiting on ${i.waitingOn}` : ""})`
        : i.due && i.due < date
          ? ` (overdue, due ${i.due})`
          : i.due === date
            ? " (due today)"
            : "";
    const note = this.store.recentNotes(p.id, 1)[0];
    return [
      `**${displayName(p)}**${this.isAway(p) ? " (on leave)" : ""}`,
      items.length ? items.slice(0, 5).map((i) => `- ${i.title}${flag(i)}`).join("\n") : "- nothing open on the tracker",
      note ? `Last update (${note.date}): ${note.summary}` : "",
      note?.followUpTomorrow.length ? `Worth asking: ${note.followUpTomorrow.slice(0, 2).join("; ")}` : "",
    ]
      .filter(Boolean)
      .join("\n");
  }

  /**
   * Every ten minutes: a brief on the people in any meeting starting within
   * half an hour, and after a meeting with a chat, the action items agreed
   * in that chat put on the tracker.
   */
  async meetingCycle(): Promise<void> {
    if (!this.direct?.meetings) return;
    const last = this.store.get("meetcheck");
    const now = this.now();
    if (last && now.getTime() - Date.parse(last) < 10 * 60_000) return;
    this.store.set("meetcheck", now.toISOString());
    let meetings;
    try {
      meetings = await this.direct.meetings(new Date(now.getTime() - 4 * 3_600_000).toISOString(), new Date(now.getTime() + 45 * 60_000).toISOString());
    } catch (err) {
      this.log("Could not read the calendar", err);
      return;
    }
    const followed = (m: { attendees: string[] }) =>
      this.store.nominees().filter((p) => !this.isAdmin(p) && m.attendees.includes(p.email));

    for (const m of meetings) {
      const startsIn = (Date.parse(m.start) - now.getTime()) / 60_000;
      const endedAgo = (now.getTime() - Date.parse(m.end)) / 60_000;
      const people = followed(m);
      if (!people.length) continue;

      if (startsIn > 0 && startsIn <= 35 && !this.store.get(`meetprep:${m.id}`)) {
        this.store.set(`meetprep:${m.id}`, "1");
        await this.toSender(`**Before "${m.subject}"** at ${this.clock(m.start.replace("T", " ").slice(0, 19))}\n\n${people.map((p) => this.personBrief(p)).join("\n\n")}`);
      }

      if (endedAgo >= 10 && endedAgo <= 240 && m.chatId && this.direct.history && !this.store.get(`meetdone:${m.id}`)) {
        this.store.set(`meetdone:${m.id}`, "1");
        try {
          const since = new Date(Date.parse(m.start) - 15 * 60_000).toISOString();
          const lines = await this.direct.history(m.chatId, 2000, since);
          if (lines.length < 2) continue;
          const entries = await this.brain.extractTracker({
            groupName: `meeting "${m.subject}" on ${this.today()}`,
            today: this.today(),
            transcript: this.transcriptOf(lines),
            people: people.map((p) => ({ name: displayName(p), role: p.profile ?? "", memory: "" })),
          });
          const counts = this.applyTrackerEntries(people, entries);
          const found = entries.filter((e) => people.some((p) => displayName(p).toLowerCase().split(" ")[0] === e.person.toLowerCase().split(" ")[0]));
          if ([...counts.values()].some((n) => n > 0)) {
            await this.toSender(
              `**From "${m.subject}"**: put on the tracker\n\n${found.map((e) => `- ${e.person}: ${e.title}${e.due ? ` (due ${e.due})` : ""}`).join("\n")}`,
            );
          }
        } catch (err) {
          this.log(`Could not read the chat of "${m.subject}"`, err);
        }
      }
    }
  }

  /** A colleague named in a "waiting on", if they are one of the people followed. */
  private resolveColleague(name: string): Person | undefined {
    const want = name.trim().toLowerCase();
    if (!want) return undefined;
    const people = this.store.nominees();
    // Only a match that can be only one person: a full name, else a first name or email
    // name that nobody else shares. Anything else is left alone rather than guessed.
    const only = (list: Person[]) => (list.length === 1 ? list[0] : undefined);
    const first = want.split(/\s+/)[0];
    const full = people.filter((p) => displayName(p).toLowerCase() === want);
    if (full.length) return only(full);
    if (want.includes(" ")) return undefined; // a full name that is not one of ours, e.g. someone at the client
    return only(
      people.filter(
        (p) => displayName(p).toLowerCase().split(/\s+/)[0] === first || p.email.split("@")[0]!.split(".")[0] === first,
      ),
    );
  }

  /**
   * Work blocked on a colleague: ask that colleague, once a day, in a group
   * both are in (or one to one if there is none). Their answer is then
   * followed up like any other check-in.
   */
  async chaseDependencies(): Promise<number> {
    if (this.paused || !this.direct || !this.inOfficeHours()) return 0;
    const date = this.today();
    let chased = 0;
    for (const item of this.store.allActiveItems()) {
      if (item.status !== "blocked" || !item.waitingOn) continue;
      if (!item.blockedSince || item.blockedSince >= date) continue; // from the day after it was raised
      if (this.store.get(`depchase:${item.id}`) === date) continue;
      const owner = this.store.personById(item.personId);
      const helper = this.resolveColleague(item.waitingOn);
      if (!owner?.nominated || !helper || helper.id === owner.id || this.isAdmin(helper) || this.isAway(helper)) continue;

      const ownerName = displayName(owner).split(" ")[0]!;
      const pool = [
        `{@0} ${item.title}: ${ownerName} is waiting on you for this. When can you send it?`,
        `{@0} any update on ${item.title}? ${ownerName} needs it to move ahead`,
        `{@0} ${ownerName} is blocked on ${item.title} from your side. pls share by when`,
      ];
      const text = pool[Math.floor(Math.random() * pool.length)]!;
      const shared = this.store.groupsOf(owner.id).find((g) => this.store.groupsOf(helper.id).includes(g));
      const group = this.store.groups().find((g) => g.id === shared);
      const userId = this.store.get(`aadOf:${helper.id}`);
      this.store.set(`depchase:${item.id}`, date);
      const topic = `${ownerName} is waiting on them for "${item.title}". Find out when it will be ready.`;
      // Where it goes decides where the answer is read: the group only if they can be tagged there.
      const inGroup = !!group && !!userId;
      if (!inGroup && !helper.chatId) continue;
      const out: Outgoing = inGroup
        ? { chatId: group!.chatId, text, mentions: [{ userId: userId!, name: displayName(helper) }], label: `${displayName(helper)} in ${group!.name}` }
        : { chatId: helper.chatId!, text: text.replace("{@0} ", ""), label: displayName(helper) };
      out.effects = [{ kind: "depAsk", helperId: helper.id, date, groupId: inGroup ? group!.id : 0, text: shown(out), topic }];
      try {
        await this.sendAsSender(out);
      } catch (err) {
        this.log(`Could not ask ${helper.email} about ${item.title}`, err);
        continue;
      }
      chased++;
    }
    return chased;
  }

  /**
   * Once a day: anything blocked two days or more, or overdue three days or
   * more, goes to the admins with a suggested next step. Each item at most
   * once for each reason.
   */
  async escalate(): Promise<number> {
    const date = this.today();
    if (this.store.get("escday") === date) return 0;
    const hours = this.officeHours();
    const { minutes, weekend } = this.localNow(hours?.zone ?? this.tz);
    if (weekend || minutes < (hours ? hours.start + 60 : 10 * 60)) return 0;
    this.store.set("escday", date);

    const lines: string[] = [];
    for (const item of this.store.allActiveItems()) {
      const owner = this.store.personById(item.personId);
      if (!owner?.nominated) continue;
      const who = displayName(owner);
      if (item.status === "blocked" && item.blockedSince && daysBetween(item.blockedSince, date) >= 2 && !this.store.get(`esc:${item.id}:blocked`)) {
        this.store.set(`esc:${item.id}:blocked`, date);
        const days = daysBetween(item.blockedSince, date);
        lines.push(
          item.waitingOn
            ? `- **${who}**: "${item.title}" blocked ${days} days, waiting on ${item.waitingOn}. Suggest: ask ${item.waitingOn.split(" ")[0]} for a firm date, or decide whether ${who.split(" ")[0]} can go ahead without it.`
            : `- **${who}**: "${item.title}" blocked ${days} days. Suggest: find out what exactly is stopping it, and who can remove it.`,
        );
      }
      if (item.due && daysBetween(item.due, date) >= 3 && !this.store.get(`esc:${item.id}:overdue`)) {
        this.store.set(`esc:${item.id}:overdue`, date);
        lines.push(`- **${who}**: "${item.title}" ${daysBetween(item.due, date)} days overdue (was due ${item.due}). Suggest: agree a new date with ${who.split(" ")[0]}, or drop it.`);
      }
    }
    if (lines.length) await this.notifyAdmins(`**Needs a decision** (${lines.length})\n\n${lines.join("\n")}`);
    return lines.length;
  }

  /** A card listing what was sent in the sender's name today, each with a Delete button. */
  private sentCard(): Record<string, unknown> | null {
    const since = new Date(this.now().getTime() - 36 * 3_600_000).toISOString();
    const today = this.today();
    const rows = this.store.sentSince(since).filter((m) => todayIn(this.tz, new Date(m.at)) === today).slice(0, 25);
    if (!rows.length) return null;
    return {
      type: "AdaptiveCard",
      $schema: "http://adaptivecards.io/schemas/adaptive-card.json",
      version: "1.4",
      body: [
        { type: "TextBlock", text: `Sent as you today (${rows.length})`, weight: "Bolder", size: "Medium" },
        ...rows.map((m) => ({
          type: "ColumnSet",
          separator: true,
          columns: [
            {
              type: "Column",
              width: "stretch",
              items: [
                { type: "TextBlock", text: `${this.clock(m.at.replace("T", " ").slice(0, 19))} · ${m.label}${m.deleted ? " · deleted" : ""}`, isSubtle: true, size: "Small", wrap: true },
                { type: "TextBlock", text: m.text.length > 160 ? `${m.text.slice(0, 159)}…` : m.text, wrap: true, spacing: "None" },
              ],
            },
            {
              type: "Column",
              width: "auto",
              verticalContentAlignment: "Center",
              items: m.deleted || !m.messageId ? [] : [{ type: "ActionSet", actions: [{ type: "Action.Submit", title: "Delete", data: { cmd: "unsend", id: m.id } }] }],
            },
          ],
        })),
      ],
    };
  }

  /** Once a day, after office hours (or 19:00), the sender gets the list of what went out in their name. */
  private async pushSentLog(): Promise<void> {
    if (!this.direct) return;
    const today = this.today();
    if (this.store.get("sentlog") === today) return;
    const hours = this.officeHours();
    const { minutes, weekend } = this.localNow(hours?.zone ?? this.tz);
    if (weekend || minutes < (hours ? hours.end : 19 * 60)) return;
    this.store.set("sentlog", today);
    const card = this.sentCard();
    if (card) await this.toSender("Here is what I sent as you today. Send **sent** to see it again.", card);
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
      facts: this.ats ? this.store.get(`atsfacts:${person.id}`) || undefined : undefined,
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
    if (this.isWaiting(`pwait:${person.id}`)) return false; // an opening is already waiting for approval
    if (!this.address(person)) return false;
    if (this.isAway(person)) return false;
    const date = this.today();
    const existing = this.store.checkin(person.id, date);
    if (!this.dueFor(person, date, round, force)) return false;

    await this.refreshMemory(person);
    await this.refreshAtsFacts(person);
    // The opening is written against a provisional record, which is then put back:
    // the ask itself is recorded only when the message really goes out.
    const keys = ["askmark", "via", "topic", "kind", "takeover", "midchase", "lastwhere"];
    const before = existing ? keys.map((k) => [k, this.store.get(`${k}:${existing.id}`) ?? ""] as const) : [];
    const checkin = this.store.openCheckin(person.id, date);
    this.beginAsk(checkin);
    this.store.set(`via:${checkin.id}`, "");
    this.store.set(`topic:${checkin.id}`, topic);
    this.store.set(`kind:${checkin.id}`, round);
    let reply: string;
    try {
      reply = (await this.brain.nextTurn(this.context(person, checkin, false, false, true))).reply;
    } finally {
      if (existing) {
        this.store.setCheckinStatus(checkin.id, existing.status);
        for (const [k, v] of before) this.store.set(`${k}:${existing.id}`, v);
        if (existing.reminded) this.store.markReminded(existing.id);
      } else {
        this.store.discardEmptyCheckin(checkin.id);
      }
    }
    await this.say(person, reply, {
      waitKey: `pwait:${person.id}`,
      effects: [{ kind: "openAsk", personId: person.id, date, text: reply, round, topic }],
    });
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
    // Paused, or outside office hours: what they said is kept, and nothing is sent back.
    if (this.paused || !this.inOfficeHours()) return;
    // A reply is waiting for approval: what they say now is recorded, and answered after that.
    if (this.isWaiting(`draftwait:${checkin.id}`)) return;

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
    if (this.store.get(`takeover:${checkin.id}`) || this.paused || !this.inOfficeHours()) return null;
    if (this.isWaiting(`draftwait:${checkin.id}`)) return null;
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
    await this.refreshAtsFacts(person);
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
    // Recording it, and closing the conversation when this was the last word, happen only
    // once it has really gone out (straight away, or when its draft is sent).
    const follows: Effect[] = [{ kind: "record", checkinId: checkin.id, text: turn.reply }];
    if (turn.done) follows.push({ kind: "finish", checkinId: checkin.id });
    try {
      // Answer where they wrote. In a group that means in the group, tagged,
      // unless this reply has to stay out of it.
      const inGroup =
        incoming.fromGroup &&
        !turn.private &&
        (await this.sayInGroup(person, checkin, turn.reply, { id: incoming.messageId, text: incoming.text }, [
          { kind: "kv", key: `lastwhere:${checkin.id}`, value: "group" },
          ...follows,
        ]));
      if (!inGroup) {
        const group = incoming.fromGroup ? this.store.groups().find((g) => String(g.id) === this.store.get(`via:${checkin.id}`)) : undefined;
        await this.say(person, turn.reply, {
          waitKey: `draftwait:${checkin.id}`,
          // A private follow-up to a group conversation is covered by the group's approve-first.
          alsoApprove: group?.chatId,
          effects: [
            { kind: "kv", key: `lastwhere:${checkin.id}`, value: "direct" },
            ...(incoming.fromGroup ? [{ kind: "cursor" as const, checkinId: checkin.id }] : []),
            ...follows,
          ],
        });
      }
    } catch (err) {
      this.log(`Check-in reply failed for ${person.email}`, err);
      await this.replyFailed(person);
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
    // Everything after the last message so far belongs to this ask.
    this.store.set(`askmark:${checkin.id}`, String(this.store.messages(checkin.id).at(-1)?.id ?? 0));
    for (const flag of ["takeover", "midchase", "lastwhere"]) this.store.set(`${flag}:${checkin.id}`, "");
    this.store.resetReminded(checkin.id);
    const waiting = this.pending.get(checkin.id);
    if (waiting) clearTimeout(waiting.timer);
    this.pending.delete(checkin.id);
  }

  /** The messages of a check-in since it was last asked. */
  private sinceAsk(checkinId: number): ChatMessage[] {
    const mark = Number(this.store.get(`askmark:${checkinId}`) || 0);
    return this.store.messages(checkinId).filter((m) => (m.id ?? 0) > mark);
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
      const id = updated
        ? change.itemId!
        : this.store.addItem(person.id, change.title, change.status, change.update, checkin.date, validDate(change.due), validProgress(change.progress, change.status));
      this.store.setItemFlow(id, change.status, change.waitingOn ?? "", checkin.date);
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
  private async sayInGroup(
    person: Person,
    checkin: Checkin,
    text: string,
    replyingTo?: { id: string; text: string },
    effects: Effect[] = [],
    onSkip: Effect[] = [],
  ): Promise<boolean> {
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
    await this.sendAsSender({
      chatId: group.chatId,
      text: body,
      mentions: layout === "quoteOnly" ? [] : [{ userId, name }],
      quote: layout === "tagOnly" ? undefined : quote,
      label: `${name} in ${group.name}`,
      recorded: text,
      effects,
      onSkip,
      waitKey: `draftwait:${checkin.id}`,
    });
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
    if (this.paused || !this.inOfficeHours()) return 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open") continue;
      const messages = this.sinceAsk(checkin.id);
      if (!this.awaitingFollowUp(checkin, messages)) continue;
      const lastAt = Date.parse(`${messages.at(-1)!.at.replace(" ", "T")}Z`);
      if (minMinutes > 0 && this.now().getTime() - lastAt < minMinutes * 60_000) continue;
      const person = this.store.personById(checkin.personId);
      if (!person?.nominated || !this.address(person) || this.isAway(person)) continue;
      if (this.isWaiting(`draftwait:${checkin.id}`)) continue;
      try {
        const nudge = this.nudgeText("followup");
        const follows: Effect[] = [
          { kind: "record", checkinId: checkin.id, text: nudge },
          { kind: "kv", key: `midchase:${checkin.id}`, value: "1" },
        ];
        const onSkip: Effect[] = [{ kind: "kv", key: `midchase:${checkin.id}`, value: "1" }];
        const inGroup =
          this.store.get(`lastwhere:${checkin.id}`) === "group" && (await this.sayInGroup(person, checkin, nudge, undefined, follows, onSkip));
        if (!inGroup) await this.say(person, nudge, { effects: follows, onSkip, waitKey: `draftwait:${checkin.id}` });
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
    if (this.isAway(target)) return `${name} is on leave today${this.holidayToday() ? ` (${this.holidayToday()})` : ""}, so I have not asked.`;
    if (!this.address(target)) return `${name} is not connected yet.`;
    return this.enqueue(target.id, async () => {
      const checkin = this.store.checkin(target.id, this.today());
      if (checkin?.status === "open") {
        if (topic) this.store.set(`topic:${checkin.id}`, topic);
        if (this.sinceAsk(checkin.id).some((m) => m.role === "user")) {
          return `${name} is in the middle of replying. Their summary will appear here when the conversation finishes.`;
        }
        if (this.isWaiting(`draftwait:${checkin.id}`)) return `A message to ${name} is already waiting for your approval.`;
        const nudge = this.nudgeText();
        const result = await this.say(target, nudge, {
          effects: [{ kind: "record", checkinId: checkin.id, text: nudge }, { kind: "reminded", checkinId: checkin.id }],
          onSkip: [{ kind: "reminded", checkinId: checkin.id }],
          waitKey: `draftwait:${checkin.id}`,
        });
        return result === "held" ? `Drafted a reminder to ${name} for your approval.` : `Asked ${name} again.`;
      }
      if (this.isWaiting(`pwait:${target.id}`)) return `A message to ${name} is already waiting for your approval.`;
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

  private async addGroup(query: string, progress?: (message: string) => Promise<void>): Promise<string> {
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
    if (this.config.approveFirstDay && this.store.get(`approve:${group.chatId}`) !== "always") {
      this.store.set(`approve:${group.chatId}`, this.today());
    }
    const members = await this.syncGroup(group);
    const tracked = members.filter((p) => !this.isAdmin(p));
    if (progress) {
      await progress(
        `Found **${group.name}** (${members.length} people). Reading its whole chat history, and each person's whole chat with you, before anything else. This can take a few minutes.`,
      );
    }

    // Always from scratch, even if this group or these people were followed before:
    // the whole history is read again, so nothing said in the meantime is missed.
    const read: string[] = [];
    if (this.personal) {
      for (let i = 0; i < tracked.length; i++) {
        let member = tracked[i]!;
        try {
          if (!member.chatId && this.direct.connect) {
            const chat = await this.direct.connect(member.email);
            if (!("notFound" in chat)) member = this.store.upsertPerson(member.email, { chatId: chat.conversationId });
          }
          const learned = await this.learnRapport(member, true);
          read.push(`- ${displayName(member)}: ${learned ? "one-to-one chat read" : "no one-to-one chat with you to read"}`);
        } catch (err) {
          this.log(`Could not read the chat with ${member.email}`, err);
          read.push(`- ${displayName(member)}: could not read the one-to-one chat`);
        }
      }
    }
    for (const key of ["gmemory", "gmemcursor", "gmemday"]) this.store.set(`${key}:${group.id}`, "");
    await this.refreshGroupMemory(group);

    let tracker = "";
    try {
      tracker = this.direct.history && this.direct.createTracker ? await this.buildTracker(group, true) : "";
    } catch (err) {
      this.log(`Could not build the tracker for ${group.name}`, err);
      tracker = "I could not build the tracker. The server log has the details; send **tracker build** with the group's name to try again.";
    }

    return [
      `Now following **${group.name}** (${members.length} people: ${members.map(displayName).join(", ")}). No question has been posted there.`,
      read.length ? `Read the group's whole chat history, and:\n${read.join("\n")}` : "",
      tracker,
      `Send **ask group ${group.name}** to ask now. The question and any follow-ups stay in the group, with people tagged.`,
    ]
      .filter(Boolean)
      .join("\n\n");
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
    if (this.isWaiting(`gwait:${group.id}`)) return { asked: 0, message: `A message for ${group.name} is already waiting for your approval.` };
    const date = this.today();
    const due = (await this.syncGroup(group)).filter((p) => {
      // Admins in the group see the question like everyone else, but are not
      // chased or followed up: the summaries are for them.
      if (this.isAdmin(p)) return false;
      // On the group's schedule, someone with a time of their own for this message waits for it.
      const slot = round === "plan" ? "morning" : round === "review" ? "evening" : "";
      if (!force && slot && this.store.get(`schedule:person:${p.id}:${slot}`)) return false;
      if (this.isAway(p)) return false;
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
    const result = await this.sendAsSender({
      chatId: group.chatId,
      text: lines((i) => `{@${i}}`),
      mentions: mentioned.map((t) => ({ userId: t.userId, name: displayName(t.person) })),
      label: group.name,
      waitKey: `gwait:${group.id}`,
      effects: [
        {
          kind: "groupAsk",
          groupId: group.id,
          date,
          text,
          round,
          asks: due.map((person) => {
            const own = tagged.find((t) => t.person.id === person.id)?.question;
            return { personId: person.id, topic: [topic, own ? `In the group they were asked directly: "${own}"` : ""].filter(Boolean).join("\n") };
          }),
        },
      ],
    });
    if (result === "held") {
      return { asked: 0, message: `Drafted the message for **${group.name}** (${due.length} people) for your approval: "${text}"` };
    }
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
      const unanswered = this.store.groupMembers(group.id).filter((p) => {
        const c = this.store.checkin(p.id, date);
        return c?.status === "open" && this.store.get(`via:${c.id}`) === String(group.id) && !this.sinceAsk(c.id).some((m) => m.role === "user");
      });
      const silent = unanswered.filter((p) => !this.isAway(p));

      if (
        silent.length &&
        !this.store.get(`gchased:${group.id}`) &&
        !this.isWaiting(`gchasewait:${group.id}`) &&
        minutes >= this.config.chaseAfterMinutes &&
        this.inOfficeHours()
      ) {
        const nudge = this.nudgeText();
        const mentions = silent
          .map((p) => ({ userId: this.store.get(`aadOf:${p.id}`) ?? "", name: displayName(p) }))
          .filter((m) => m.userId);
        const effects: Effect[] = [{ kind: "kv", key: `gchased:${group.id}`, value: "1" }];
        for (const p of silent) {
          const c = this.store.checkin(p.id, date)!;
          effects.push({ kind: "record", checkinId: c.id, text: nudge }, { kind: "reminded", checkinId: c.id });
        }
        try {
          await this.sendAsSender({
            chatId: group.chatId,
            text: nudge,
            mentions,
            label: group.name,
            effects,
            onSkip: [{ kind: "kv", key: `gchased:${group.id}`, value: "1" }],
            waitKey: `gchasewait:${group.id}`,
          });
        } catch (err) {
          this.log(`Could not chase in ${group.name}`, err);
        }
      }

      if (minutes >= this.config.groupWindowMinutes) {
        // Stop reading the group. Whoever said nothing (on leave or not) is marked as not having replied.
        for (const p of unanswered) this.closeUnanswered(this.store.checkin(p.id, date)!);
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
        const name = `${esc((p.name ?? p.email).split(" ")[0]!)}${this.isAway(p) ? " (on leave)" : ""}`;
        const items = this.store.trackerItems(p.id, date).slice(0, 8);
        rows += items.length;
        if (!items.length) return `<tr><td>${name}</td><td>No items yet</td><td>-</td><td>-</td><td>-</td><td>-</td></tr>`;
        return items
          .map((i, n) => {
            const status =
              i.status === "done"
                ? "✅ Done"
                : i.status === "blocked"
                  ? `🟠 Blocked${i.waitingOn ? ` · waiting on ${esc(i.waitingOn.split(" ")[0]!)}` : ""}`
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
  /** Put tracker entries from the model onto the right people's items. Returns how many each got. */
  private applyTrackerEntries(members: Person[], entries: TrackerEntry[]): Map<number, number> {
    const date = this.today();
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
      let id = existing?.id;
      if (existing) this.store.updateItem(person.id, existing.id, entry.status, entry.doneSoFar, date, due, progress);
      else id = this.store.addItem(person.id, entry.title.trim(), entry.status, entry.doneSoFar, date, due, progress);
      this.store.setItemFlow(id!, entry.status, entry.waitingOn ?? "", date);
      counts.set(person.id, (counts.get(person.id) ?? 0) + 1);
    }
    return counts;
  }

  private transcriptOf(lines: { fromSender: boolean; from: string; text: string; at: string }[], other = "Someone"): string {
    return lines
      .filter((m) => !(m.fromSender && m.text.startsWith("Team tracker")))
      .map((m) => `${m.at.slice(0, 10)} ${m.fromSender ? this.direct!.senderName : m.from || other}: ${m.text.slice(0, 400)}`)
      .join("\n");
  }

  /**
   * Read a group's whole chat history and put every piece of work in it on
   * the tracker, by person: what is done so far, progress and due date.
   * Items already tracked are updated rather than duplicated. With
   * `skipIfEmpty`, nothing is posted when nothing was found.
   */
  private async buildTracker(group: Group, skipIfEmpty = false): Promise<string> {
    if (!this.direct?.history || !this.direct.createTracker) {
      return "Building a tracker needs messages to be sent from your own Teams account.";
    }
    if (this.paused) return `Paused: nothing was posted in ${group.name}. Send **resume** first.`;
    const members = (await this.syncGroup(group)).filter((p) => !this.isAdmin(p));
    if (!members.length) return `I found nobody in ${group.name} to track.`;

    const lines = await this.direct.history(group.chatId, this.config.historyMessages);
    const entries = await this.brain.extractTracker({
      groupName: group.name,
      today: this.today(),
      transcript: this.transcriptOf(lines),
      people: members.map((p) => ({
        name: displayName(p),
        role: p.profile ?? "",
        memory: this.store.get(`rapport:${p.id}`) || "",
      })),
    });
    const counts = this.applyTrackerEntries(members, entries);
    const found = [...counts.values()].reduce((sum, n) => sum + n, 0);
    const summary = members.map((p) => `- ${displayName(p)}: ${counts.get(p.id) ?? 0}`).join("\n");
    if (skipIfEmpty && !found) {
      return `I found no open work in ${group.name}'s history, so there is no tracker yet. It appears as soon as people report.`;
    }

    this.store.set(`trackeroff:${group.id}`, "");
    this.store.set(`trackerhash:${group.id}`, "");
    await this.refreshTrackers(group);
    return this.store.get(`tracker:${group.id}`)
      ? `Built the tracker for **${group.name}** from ${lines.length} messages of its chat history, and pinned it at the top of that chat.\n\nItems found:\n${summary}\n\nI keep it up to date as people reply, and the morning and evening messages follow up on what is due or overdue on it.`
      : `I read ${lines.length} messages of ${group.name} and found:\n${summary}\n\nbut could not post the tracker there. The server log has the details.`;
  }

  /** The work in someone's one-to-one chat with the sender, put on their items. */
  private async trackFromOwnChat(person: Person): Promise<number> {
    if (!this.direct?.history || !person.chatId) return 0;
    const lines = await this.direct.history(person.chatId, this.config.historyMessages);
    if (!lines.length) return 0;
    const entries = await this.brain.extractTracker({
      groupName: `one-to-one chat between ${this.direct.senderName} and ${displayName(person)}`,
      today: this.today(),
      transcript: this.transcriptOf(lines, displayName(person)),
      people: [{ name: displayName(person), role: person.profile ?? "", memory: this.store.get(`rapport:${person.id}`) || "" }],
    });
    return this.applyTrackerEntries([person], entries).get(person.id) ?? 0;
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
        timeZone: this.tz,
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

  private localNow(zone: string = this.tz): { date: string; minutes: number; weekend: boolean } {
    const parts = new Intl.DateTimeFormat("en-GB", {
      timeZone: zone,
      hour: "2-digit",
      minute: "2-digit",
      hour12: false,
      weekday: "short",
    }).formatToParts(this.now());
    const get = (type: string) => parts.find((p) => p.type === type)?.value ?? "";
    return {
      date: todayIn(zone, this.now()),
      minutes: (Number(get("hour")) % 24) * 60 + Number(get("minute")),
      weekend: ["Sat", "Sun"].includes(get("weekday")),
    };
  }

  private hoursStatus(): string {
    const hours = this.officeHours();
    return hours
      ? `Office hours: **${hours.text}**, Monday to Friday. Outside them I send nothing to anyone, but I still record what people write. ${this.inOfficeHours() ? "It is office hours now." : "It is outside office hours now, so I am quiet."}`
      : `No office hours are set: I may message people at any time (${this.tz} time). Set them with **hours 09:30-18:30**.`;
  }

  /** "09:30" or "09:30@Asia/Kolkata" as shown to a person: "09:30 India time". */
  private showTime(setting: string | undefined): string {
    if (!setting) return "";
    const [time, zone] = setting.split("@") as [string, string | undefined];
    return `${time} ${zoneLabel(zone || this.tz)}`;
  }

  private scheduleStatus(): string {
    const morning = this.showTime(this.store.get("schedule:morning"));
    const evening = this.showTime(this.store.get("schedule:evening"));
    const digest = this.showTime(this.store.get("schedule:digest"));
    return [
      morning ? `Morning plan: everyone is asked automatically at **${morning}**.` : "",
      evening ? `End-of-day update: everyone is asked automatically at **${evening}**.` : "",
      morning || evening
        ? `Monday to Friday.${morning && evening ? "" : ` No ${morning ? "end-of-day" : "morning"} message is set.`}`
        : "No automatic asking: updates are only requested when you say **ask**.",
      digest ? `Daily digest at **${digest}**.` : "No automatic digest: send **digest** when you want one.",
      this.ownTimes(),
    ]
      .filter(Boolean)
      .join("\n");
  }

  /** Groups and people with times of their own, one line each. */
  private ownTimes(): string {
    const lines: string[] = [];
    const add = (name: string, kind: string) => {
      const times = SLOTS.map((slot) => [slot, this.showTime(this.store.get(`schedule:${kind}:${slot}`))] as const)
        .filter(([, t]) => t)
        .map(([slot, t]) => `${slot} ${t}`);
      if (times.length) lines.push(`- **${name}**: ${times.join(", ")}`);
    };
    for (const g of this.store.groups()) add(g.name, `group:${g.id}`);
    for (const p of this.store.nominees()) add(displayName(p), `person:${p.id}`);
    return lines.length ? `Own times, Monday to Friday:\n${lines.join("\n")}` : "";
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
        timeZone: this.tz,
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
    await this.pushSentLog();
    await this.expireDrafts();
    await this.chaseDependencies();
    await this.escalate();
    await this.refreshLeave();
    await this.meetingCycle();
    await this.runWeekly();
    this.backupDaily();
    await this.refreshBoards();

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
      const setting = this.store.get(`schedule:${kind}`);
      if (!setting) continue;
      // Each schedule runs in its own timezone, or the bot's when none was given.
      const [at, zone] = setting.split("@") as [string, string | undefined];
      const { date, minutes, weekend } = this.localNow(zone || this.tz);
      if (weekend) continue;
      const due = Number(at.slice(0, 2)) * 60 + Number(at.slice(3));
      // A ten-minute window, so a restart just after the time still runs it, once.
      if (minutes < due || minutes > due + 10 || this.store.get(`ran:${kind}`) === date) continue;
      // The digest goes to you; everything else goes to staff and waits for office hours.
      if (kind !== "digest" && (!this.inOfficeHours() || this.holidayToday())) continue;
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
      timeZone: this.tz,
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
      morning: this.showTime(this.store.get(`schedule:${prefix}morning`)),
      evening: this.showTime(this.store.get(`schedule:${prefix}evening`)),
    });
    const rawTime = (prefix: string, slot: string) => (this.store.get(`schedule:${prefix}${slot}`) || "").split("@")[0];
    const zoneOf = (prefix: string, slot: string) => (this.store.get(`schedule:${prefix}${slot}`) || "").split("@")[1] || this.tz;
    const timesText = (t: { morning: string; evening: string }) =>
      [t.morning && `Morning ${t.morning}`, t.evening && `Evening ${t.evening}`].filter(Boolean).join(" · ");
    const scheduleLine = (prefix: string) => {
      const t = times(prefix);
      return t.morning || t.evening
        ? [{ type: "TextBlock", text: `ON · ${timesText(t)}, Monday to Friday`, color: "Good", weight: "Bolder", size: "Small", spacing: "None", wrap: true }]
        : [];
    };
    // A choice of timezone beside each time.
    const zonePicker = (id: string, current: string) => ({
      type: "Input.ChoiceSet",
      id,
      label: "Timezone",
      value: current,
      choices: [...new Set([current, ...ZONE_CHOICES.map((z) => z.zone)])].map((zone) => ({ title: zoneLabel(zone), value: zone })),
    });
    // A button that opens the two time pickers, morning and evening. `who` is empty for everyone.
    const timePicker = (field: string, who: string, prefix: string, title = "") => {
      const t = times(prefix);
      return {
        type: "Action.ShowCard",
        title: title || (t.morning || t.evening ? `Times: ${timesText(t)}` : "Set times"),
        card: {
          type: "AdaptiveCard",
          body: [
            { type: "Input.Time", id: `${field}m`, label: "Morning: ask for the plan for the day", ...(rawTime(prefix, "morning") ? { value: rawTime(prefix, "morning") } : {}) },
            zonePicker(`${field}mz`, zoneOf(prefix, "morning")),
            { type: "Input.Time", id: `${field}e`, label: "Evening: ask for the end-of-day update", ...(rawTime(prefix, "evening") ? { value: rawTime(prefix, "evening") } : {}) },
            zonePicker(`${field}ez`, zoneOf(prefix, "evening")),
          ],
          actions: [
            submit("Save morning", { cmd: "scheduleFor", who, field: `${field}m`, zoneField: `${field}mz`, slot: "morning" }),
            submit("Save evening", { cmd: "scheduleFor", who, field: `${field}e`, zoneField: `${field}ez`, slot: "evening" }),
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
      if (this.isAway(person)) return { group: 5, person, line: this.holidayToday() ? `Holiday: ${this.holidayToday()}.` : "On leave today." };
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
    const usage = this.usageLine(this.today());
    if (usage) body.push({ type: "TextBlock", text: `AI use today: ${usage}`, wrap: true, size: "Small", isSubtle: true, spacing: "Large", separator: true });
    const hours = this.officeHours();
    body.push({
      type: "TextBlock",
      text: hours
        ? `ON · Office hours ${hours.text}, Monday to Friday${this.inOfficeHours() ? "" : " · quiet now"}`
        : `OFF · No office hours: I may message people at any time (${this.tz})`,
      wrap: true,
      size: "Small",
      spacing: "Large",
      separator: true,
      ...(hours ? { color: "Good", weight: "Bolder" } : { isSubtle: true }),
    });
    const morningAt = this.store.get("schedule:morning");
    const eveningAt = this.store.get("schedule:evening");
    const digestAt = this.store.get("schedule:digest");
    body.push(
      {
        type: "TextBlock",
        text: morningAt ? `ON · Morning plan: everyone is asked at ${this.showTime(morningAt)}, Monday to Friday` : "OFF · No morning plan message",
        wrap: true,
        size: "Small",
        spacing: "Large",
        separator: true,
        ...(morningAt ? { color: "Good", weight: "Bolder" } : { isSubtle: true }),
      },
      {
        type: "TextBlock",
        text: eveningAt ? `ON · End of day: everyone is asked at ${this.showTime(eveningAt)}, Monday to Friday` : "OFF · No end-of-day message",
        wrap: true,
        size: "Small",
        spacing: "None",
        ...(eveningAt ? { color: "Good", weight: "Bolder" } : { isSubtle: true }),
      },
      {
        type: "TextBlock",
        text: digestAt ? `ON · Daily digest at ${this.showTime(digestAt)}` : "OFF · No automatic digest",
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
    if (this.paused || !this.inOfficeHours()) return 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open" || checkin.reminded) continue;
      if (this.store.get(`takeover:${checkin.id}`)) continue;
      if (this.store.get(`via:${checkin.id}`)) continue; // asked in a group: chased there, together
      const messages = this.sinceAsk(checkin.id);
      if (!messages.length || messages.some((m) => m.role === "user")) continue;
      const sentAt = Date.parse(`${messages[0]!.at.replace(" ", "T")}Z`);
      if (minMinutes > 0 && this.now().getTime() - sentAt < minMinutes * 60_000) continue;
      const person = this.store.personById(checkin.personId);
      if (!person?.nominated || this.isAway(person)) continue;
      if (this.isWaiting(`draftwait:${checkin.id}`)) continue;
      try {
        const nudge = this.nudgeText();
        await this.say(person, nudge, {
          effects: [{ kind: "record", checkinId: checkin.id, text: nudge }, { kind: "reminded", checkinId: checkin.id }],
          onSkip: [{ kind: "reminded", checkinId: checkin.id }],
          waitKey: `draftwait:${checkin.id}`,
        });
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
    // What TalentFlow shows for each person at the end of the day, to set against what they said.
    if (this.ats) for (const person of this.store.nominees()) await this.refreshAtsFacts(person, true);
    const people: DigestPerson[] = this.store.nominees().map((person) => {
      const note = notes.get(person.id) ?? null;
      return {
        person,
        note,
        talentflow: (this.ats && !this.isAdmin(person) && this.store.get(`atsfacts:${person.id}`)) || undefined,
        activeItems: this.store.activeItems(person.id),
        missing: note ? null : this.address(person) ? "no_response" : "not_connected",
      };
    });

    const text = people.length
      ? await this.brain.digest(date, people, this.recentIssues())
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
    if (["help", "hi", "hello", "menu", "board", "refresh", "list", "digest", "pause", "resume", "weekly", "usage", "sent"].includes(verb) && arg) verb = "";
    if (verb === "office") {
      verb = "hours";
    }
    if (verb === "stop") verb = "pause";
    if (verb === "add") verb = "nominate";
    if (verb === "delete") verb = "remove";
    if (verb === "checkin") verb = "ask";
    // Short one-liners are commands. Anything longer or on several lines is
    // free-form and may hold several instructions, so it is interpreted.
    const short = !text.includes("\n") && text.split(/\s+/).length <= 6;
    if (!short && !["profile", "greeting", "style", "schedule", "tracker", "draft"].includes(verb)) verb = "";

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

        case "hours": {
          // A timezone can be given with the hours: "hours 09:30-18:30 India".
          const { rest: hoursText, zone: hoursZone } = takeZone(arg);
          const value = hoursText.toLowerCase().trim();
          if (!value) return await reply(this.hoursStatus());
          if (/^(off|none|any|always|24\/7|anytime)$/.test(value)) {
            this.store.set("hours", "");
            return await reply(this.hoursStatus());
          }
          const parts = value.split(/\s*(?:-|–|to|till|until)\s*/).filter(Boolean);
          const start = parts[0] ? parseTime(parts[0]) : null;
          let end = parts[1] ? parseTime(parts[1]) : null;
          // "9 to 6" means six in the evening.
          if (start && end && end <= start && !/am|pm/.test(parts[1]!) && Number(end.slice(0, 2)) < 12) {
            end = `${String(Number(end.slice(0, 2)) + 12).padStart(2, "0")}${end.slice(2)}`;
          }
          if (!start || !end || start === end) {
            return await reply("I did not understand those hours. Try **hours 09:30-18:30**, or **hours off** to allow any time.");
          }
          this.store.set("hours", `${start}-${end}${hoursZone ? `@${hoursZone}` : ""}`);
          return await reply(this.hoursStatus());
        }

        case "timezone": {
          if (!arg) return await reply(`I am working in **${this.tz}** time. Change it with **timezone India**, **timezone UK** or a name like **timezone Asia/Dubai**.`);
          const zone = parseZone(arg);
          if (!zone) return await reply(`I do not know the timezone "${arg}". Try **timezone India**, **timezone UK**, or a name like **Asia/Kolkata**.`);
          this.store.set("timezone", zone);
          return await reply(`Now working in **${zone}** time. Schedules and office hours are read in this time from now on.\n\n${this.hoursStatus()}`);
        }

        case "sent": {
          const card = this.sentCard();
          if (!card) return await reply("Nothing has been sent as you today.");
          if (admin.conversationId && this.messenger.sendCard) return void (await this.messenger.sendCard(admin.conversationId, card));
          return await reply(this.store.sentSince("").slice(0, 25).map((m) => `- ${m.label}: ${m.text}`).join("\n"));
        }

        case "unsend": {
          const message = this.store.sentById(Number(arg));
          if (!message) return await reply("I could not find that message.");
          if (message.deleted) return await reply("That message is already deleted.");
          if (!this.direct?.unsend || !message.messageId) return await reply("I cannot delete that one; please delete it in Teams.");
          try {
            await this.direct.unsend(message.chatId, message.messageId);
          } catch (err) {
            this.log("Could not delete a message", err);
            return await reply(`Teams would not let me delete it. Please delete it yourself in the chat with ${message.label}.`);
          }
          this.store.markSentDeleted(message.id);
          this.store.deleteMessages(message.recordIds);
          return await reply(`Deleted: "${message.text}" (${message.label}).`);
        }

        case "draft": {
          const m = /^(send|skip)\s+(\d+)\s*([\s\S]*)$/i.exec(arg);
          if (!m) return await reply("Use **draft send 12** or **draft skip 12**.");
          const draft = this.store.draft(Number(m[2]));
          if (!draft) return await reply("I could not find that draft.");
          if (draft.status !== "waiting") return await reply(`That draft was already ${draft.status}.`);
          const out = JSON.parse(draft.payload) as Outgoing;
          const original = shown(out);
          const release = () => {
            if (out.waitKey && this.store.get(out.waitKey) === String(draft.id)) this.store.set(out.waitKey, "");
          };
          if (m[1]!.toLowerCase() === "skip") {
            // Nothing was recorded for it, so there is nothing to undo; only stop it being drafted again.
            this.store.setDraftStatus(draft.id, "skipped");
            await this.applyEffects(out.onSkip);
            release();
            return await reply(`Skipped. Nothing was sent to ${draft.label}.`);
          }
          if (this.paused) return await reply("Paused: send **resume** first.");
          if (this.now().getTime() - Date.parse(draft.at) > 3 * 3_600_000) {
            this.store.setDraftStatus(draft.id, "expired");
            await this.applyEffects(out.onSkip);
            release();
            return await reply("That draft is more than three hours old, so I have not sent it.");
          }
          const edited = m[3]!.trim();
          let final = out;
          let recordAs: string | undefined;
          if (edited && edited !== original) {
            // Keep a tag only where its "@Name" is still in the text.
            const mentions = (out.mentions ?? []).filter((x) => edited.includes(`@${x.name}`));
            let text = edited;
            mentions.forEach((x, i) => (text = text.replace(`@${x.name}`, `{@${i}}`)));
            final = { ...out, text, mentions };
            recordAs = edited;
          }
          // Claimed before sending, so a second press (or the other admin) cannot send it twice.
          this.store.setDraftStatus(draft.id, "sending");
          try {
            await this.sendNow(final, recordAs);
          } catch (err) {
            this.store.setDraftStatus(draft.id, "waiting");
            this.log("Could not send a draft", err);
            return await reply("Teams did not take it. The draft is still waiting; try again in a moment.");
          }
          this.store.setDraftStatus(draft.id, "sent");
          release();
          return await reply(`Sent to ${draft.label}.`);
        }

        case "approve": {
          const value = arg.trim();
          if (!value) {
            const on = this.store
              .keys("approve:")
              .filter((k) => this.needsApproval(k.slice("approve:".length)))
              .map((k) => this.chatLabel(k.slice("approve:".length)));
            return await reply(
              on.length
                ? `Messages to these wait for your approval: ${on.join(", ")}.`
                : "Nothing is waiting for approval: messages go out automatically.",
            );
          }
          const mode = /\b(off|no)$/i.test(value) ? "" : /\btoday$/i.test(value) ? this.today() : "always";
          const who = value.replace(/\s+(on|off|always|today|no|yes)$/i, "").replace(/^group\s+/i, "").trim();
          const people = this.store.findNominees(who);
          const group = this.resolveGroup(who);
          const chatId = people.length === 1 ? people[0]!.chatId : typeof group !== "string" ? group.chatId : null;
          const label = people.length === 1 ? displayName(people[0]!) : typeof group !== "string" ? group.name : who;
          if (!chatId) return await reply(`I could not match "${who}" to one person or group.`);
          this.store.set(`approve:${chatId}`, mode);
          return await reply(
            mode === ""
              ? `Messages to ${label} now go out without asking you.`
              : mode === "always"
                ? `Every message to ${label} will come to you first as a draft, until you send **approve ${label} off**.`
                : `Messages to ${label} come to you as drafts for the rest of today.`,
          );
        }

        case "leave": {
          // "leave priya until 2026-11-25", "leave priya today", "leave priya off"
          const m = /^(.+?)\s+(off|today|until\s+(\d{4}-\d{2}-\d{2})|till\s+(\d{4}-\d{2}-\d{2}))$/i.exec(arg);
          if (!m) return await reply("Use **leave priya today**, **leave priya until 2026-11-25** or **leave priya off**.");
          const target = this.resolveOne(m[1]!);
          if (typeof target === "string") return await reply(target);
          const word = m[2]!.toLowerCase();
          const until = word === "off" ? "" : word === "today" ? this.today() : (m[3] ?? m[4])!;
          this.store.set(`leave:${target.id}`, until);
          return await reply(until ? `${displayName(target)} is on leave until ${until}. I will not ask or chase them until then.` : `${displayName(target)} is back: I will ask them as usual.`);
        }

        case "holiday":
        case "holidays": {
          const m = /^(off\s+)?(\d{4}-\d{2}-\d{2})\s*(.*)$/i.exec(arg);
          if (!m) {
            const list = this.store
              .keys("holiday:")
              .filter((k) => this.store.get(k) && k.slice(8) >= this.today())
              .sort()
              .map((k) => `- ${k.slice(8)}: ${this.store.get(k)}`);
            return await reply(list.length ? `**Holidays**\n${list.join("\n")}` : "No holidays set. Add one with **holiday 2026-12-25 Christmas**.");
          }
          this.store.set(`holiday:${m[2]}`, m[1] ? "" : m[3]!.trim() || "Holiday");
          return await reply(m[1] ? `${m[2]} is no longer a holiday.` : `${m[2]} is a holiday (${m[3]!.trim() || "Holiday"}): nobody is asked or chased that day.`);
        }

        case "weekly": {
          await reply("Putting this week's report together. It will arrive here shortly.");
          const report = await this.runWeekly(true);
          if (!report) await reply("There was nothing to report on yet.");
          return;
        }

        case "usage": {
          const lines: string[] = [];
          for (let i = 0; i < 7; i++) {
            const d = new Date(Date.parse(`${this.today()}T00:00:00Z`) - i * 86_400_000).toISOString().slice(0, 10);
            const line = this.usageLine(d);
            if (line) lines.push(`- ${d}: ${line}`);
          }
          return await reply(lines.length ? `**AI use, last 7 days**\n${lines.join("\n")}` : "No AI use recorded yet.");
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
          // A timezone can be given anywhere: "schedule morning 09:30 India".
          const taken = takeZone(words.join(" "));
          const zone = taken.zone;
          words = taken.rest.split(/\s+/).filter(Boolean);
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
          this.store.set(`schedule:${name}`, zone ? `${time}@${zone}` : time);
          // A time that has already passed today starts tomorrow, rather than firing at once.
          const there = this.localNow(zone || this.tz);
          if (there.minutes > Number(time.slice(0, 2)) * 60 + Number(time.slice(3))) {
            this.store.set(`ran:${name}`, there.date);
          }
          if (!label) return await reply(this.scheduleStatus());
          return await reply(
            `${label} will be asked automatically at **${this.showTime(this.store.get(`schedule:${name}`))}** (${SLOT_LABEL[chosen]}), Monday to Friday, instead of at the general time.`,
          );
        }

        case "nominate": {
          if (/^group\b/i.test(arg)) return await reply(await this.addGroup(arg.replace(/^group\s*/i, "").trim(), reply));
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
          if (this.config.approveFirstDay && person.chatId && this.store.get(`approve:${person.chatId}`) !== "always") {
            this.store.set(`approve:${person.chatId}`, this.today());
          }
          let history = "";
          if (this.personal && person.chatId) {
            // Always from scratch, even if they were followed before.
            await reply(`Reading your whole chat history with ${displayName(person)} first. This can take a minute.`);
            try {
              const learned = await this.learnRapport(person, true);
              const items = await this.trackFromOwnChat(person);
              history = learned || items
                ? `Read your whole chat with them and found ${items} piece${items === 1 ? "" : "s"} of work to track.`
                : "There is no chat history with them to read yet.";
            } catch (err) {
              this.log(`Could not read the chat with ${person.email}`, err);
              history = "I could not read your chat history with them; the server log has the details.";
            }
          }
          if (this.address(person)) {
            if (!this.personal) await this.say(person, this.welcomeText(person));
            this.store.set(awaitingKey, String(person.id));
            return await reply(
              `${displayName(person)} is nominated. ${history ? `${history} ` : ""}${
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
          await reply(await this.addGroup(target, reply));
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
        case "set_hours":
          await command(`hours ${detail || target}`);
          break;
        case "set_timezone":
          await command(`timezone ${detail || target}`);
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
