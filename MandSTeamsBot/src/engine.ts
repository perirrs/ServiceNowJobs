import { displayName, renderItems, renderNote } from "./ai.js";
import type { Brain, CheckinContext, DigestPerson } from "./ai.js";
import { todayIn } from "./config.js";
import type { Config } from "./config.js";
import type { Checkin, Person, Store } from "./db.js";

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
  send(conversationId: string, text: string): Promise<void>;
  /** Recent messages in the sender's chat with a colleague, oldest first. */
  history?(conversationId: string): Promise<{ fromSender: boolean; text: string }[]>;
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

- **board** (or **menu**): the team board, with who has replied, who is blocked, and buttons for everything below
- **add** name@company.com: start asking someone for updates
- **remove** name: stop asking them (their notes are kept)
- **ask** name: ask them for an update right now. **ask all** asks everyone.
- **profile** name: their role, responsibilities and targets, so the questions fit their job
- **style** name: what I learned from your past chat about how you write to them. **style name refresh** reads the chat again.
- **list**: everyone you have added and where they stand today
- **report** name: their recent notes and open items
- **digest**: today's summary of everyone, right now
- **schedule 17:00**: ask everyone automatically at that time, Monday to Friday. **schedule off** stops it. **schedule** alone shows the current setting.
- **schedule digest 19:30**: send the daily digest automatically at that time. **schedule digest off** stops it.

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
    case "digest":
      return "digest";
    case "list":
      return "list";
    case "details":
      return `report ${str("who")}`;
    case "refresh":
      return "refresh";
    case "schedule":
      return `schedule ${str("time")}`;
    case "scheduleOff":
      return "schedule off";
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
  ) {}

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

  /** Message a nominee as part of their check-in. */
  private async say(person: Person, text: string): Promise<void> {
    const to = this.address(person);
    if (!to) return;
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

  private context(person: Person, checkin: Checkin, finalTurn = false): CheckinContext {
    return {
      date: checkin.date,
      person,
      activeItems: this.store.activeItems(person.id),
      recentNotes: this.store.recentNotes(person.id, 5, checkin.date),
      history: this.store.messages(checkin.id),
      finalTurn,
      rapport: this.personal ? this.store.get(`rapport:${person.id}`) || undefined : undefined,
    };
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
      const chat = (await this.direct.history(person.chatId)).filter(
        (m) => !(m.fromSender && own.has(m.text.replace(/\s+/g, " ").trim())),
      );
      // Too little to learn from: remember that, so we do not look again every time.
      const text = chat.filter((m) => m.fromSender).length >= 3
        ? await this.brain.learnRapport(person, this.direct.senderName, chat)
        : "";
      this.store.set(key, text);
      return text || undefined;
    } catch (err) {
      this.log(`Could not read the chat history with ${person.email}`, err);
      return undefined;
    }
  }

  /** Send today's opening question. Returns false if the person cannot be reached or already has one. */
  async startCheckin(person: Person, force = false): Promise<boolean> {
    if (!this.address(person)) return false;
    const date = this.today();
    const existing = this.store.checkin(person.id, date);
    if (existing && !force) return false;
    if (existing?.status === "open") return false;

    await this.learnRapport(person);
    const checkin = this.store.openCheckin(person.id, date);
    try {
      const turn = await this.brain.nextTurn(this.context(person, checkin));
      // Deliver first, record second: the record must only show what was really sent.
      await this.say(person, turn.reply);
      this.store.addMessage(checkin.id, "bot", turn.reply);
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
      .map((checkin) => ({ person: this.store.personById(checkin.personId), checkin }))
      .filter((x): x is { person: Person; checkin: Checkin } => !!x.person?.nominated && !!x.person.chatId);
  }

  private async handleUpdate(person: Person, text: string): Promise<void> {
    if (!text) return;
    const date = this.today();
    let checkin = this.store.checkin(person.id, date);
    // A message outside an open check-in (before the daily prompt, or after
    // closing) opens or reopens today's, so nothing a person says is lost.
    if (!checkin || checkin.status !== "open") checkin = this.store.openCheckin(person.id, date);
    this.store.addMessage(checkin.id, "user", text);
    // Recorded for tonight's note, but the human has taken over this chat.
    if (this.store.get(`takeover:${checkin.id}`)) return;

    try {
      const botTurns = this.store.messages(checkin.id).filter((m) => m.role === "bot").length;
      const finalTurn = botTurns >= this.config.maxBotTurns - 1;
      const turn = await this.brain.nextTurn(this.context(person, checkin, finalTurn));
      if (turn.handover || !turn.reply) {
        // Something only a person should answer: go quiet here and tell the admins.
        this.store.set(`takeover:${checkin.id}`, "handover");
        await this.notifyAdmins(
          `**${displayName(person)} needs a personal reply.** They wrote: "${text}"\n\nI have stopped replying in that chat for today. Anything else they say is still recorded for the note.`,
        );
        return;
      }
      await this.say(person, turn.reply);
      this.store.addMessage(checkin.id, "bot", turn.reply);
      if (turn.done) await this.finish(person, checkin);
    } catch (err) {
      this.log(`Check-in reply failed for ${person.email}`, err);
      // The message is already stored, so it still reaches tonight's note.
      if (this.personal) {
        await this.notifyAdmins(`I could not reply to ${displayName(person)} just now (their message is saved). The server log has the details.`);
      } else {
        await this.say(person, "Noted. I had trouble replying just now, so if there is more to add, send it and I will pick it up.");
      }
    }
  }

  /** Turn the day's conversation into a note and update the person's open items. */
  private async finish(person: Person, checkin: Checkin, announce = true): Promise<void> {
    const ctx = this.context(person, checkin);
    const { note, items } = await this.brain.writeNote(ctx);
    this.store.saveNote(checkin, note);
    for (const change of items) {
      const updated =
        change.itemId != null &&
        this.store.updateItem(person.id, change.itemId, change.status, change.update, checkin.date);
      if (!updated) {
        this.store.addItem(person.id, change.title, change.status, change.update, checkin.date);
      }
    }
    this.store.setCheckinStatus(checkin.id, "completed");
    if (announce) {
      await this.notifyAdmins(
        `**Update from ${displayName(person)}** (${checkin.date})\n\n${renderNote(note)}\n\n**Open items**\n${renderItems(this.store.activeItems(person.id))}`,
      );
    }
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
      const messages = this.store.messages(checkin.id);
      if (!messages.some((m) => m.role === "user")) continue;
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

  private nudgeText(): string {
    return this.personal
      ? "Any update? Please share when you can."
      : "A quick reminder on today's check-in. A line or two is enough.";
  }

  /**
   * Ask one person right now, whatever has happened today. Not asked yet, or
   * finished earlier: a new question. Asked but silent: ask again.
   */
  private async askNow(target: Person): Promise<string> {
    const name = displayName(target);
    if (!this.address(target)) return `${name} is not connected yet.`;
    return this.enqueue(target.id, async () => {
      const checkin = this.store.checkin(target.id, this.today());
      if (checkin?.status === "open") {
        if (this.store.messages(checkin.id).some((m) => m.role === "user")) {
          return `${name} is in the middle of replying. Their summary will appear here when the conversation finishes.`;
        }
        const nudge = this.nudgeText();
        await this.say(target, nudge);
        this.store.addMessage(checkin.id, "bot", nudge);
        this.store.markReminded(checkin.id);
        return `Asked ${name} again.`;
      }
      await this.startCheckin(target, true);
      return `Asked ${name} for an update. Their summary will appear here when the conversation finishes.`;
    });
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
    const ask = this.store.get("schedule:checkin");
    const digest = this.store.get("schedule:digest");
    return [
      ask
        ? `Asking everyone automatically at **${ask}**, Monday to Friday (${this.config.timezone} time).`
        : "No automatic asking: updates are only requested when you say **ask**.",
      digest ? `Daily digest at **${digest}**.` : "No automatic digest: send **digest** when you want one.",
    ].join("\n");
  }

  /** Runs once a minute: the chase, closing quiet conversations, and anything scheduled. */
  async tick(): Promise<void> {
    await this.runReminders(this.config.chaseAfterMinutes);
    await this.closeIdle(this.config.idleCloseMinutes);
    await this.refreshBoards();

    const { date, minutes, weekend } = this.localNow();
    if (weekend) return;
    const jobs: [string, () => Promise<unknown>][] = [
      ["checkin", () => this.runCheckins()],
      ["digest", () => this.runDigest()],
    ];
    for (const [kind, job] of jobs) {
      const at = this.store.get(`schedule:${kind}`);
      if (!at) continue;
      const due = Number(at.slice(0, 2)) * 60 + Number(at.slice(3));
      // A ten-minute window, so a restart just after the time still runs it, once.
      if (minutes < due || minutes > due + 10 || this.store.get(`ran:${kind}`) === date) continue;
      this.store.set(`ran:${kind}`, date);
      this.log(`[schedule] running ${kind} for ${date}`);
      await job();
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
      const messages = checkin ? this.store.messages(checkin.id) : [];
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
                submit("Remove", { cmd: "remove", who: person.email }),
              ],
            },
          ],
        });
      }
    });

    body.push({
      type: "TextBlock",
      text: this.scheduleStatus().replace(/\*\*/g, ""),
      wrap: true,
      isSubtle: true,
      size: "Small",
      spacing: "Large",
      separator: true,
    });

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
        submit("Digest now", { cmd: "digest" }),
        {
          type: "Action.ShowCard",
          title: "Schedule",
          card: {
            type: "AdaptiveCard",
            body: [{ type: "Input.Time", id: "time", label: "Ask everyone at this time, Monday to Friday" }],
            actions: [submit("Save time", { cmd: "schedule" }), submit("Turn off", { cmd: "scheduleOff" })],
          },
        },
        submit("Refresh", { cmd: "refresh" }),
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

  async runCheckins(): Promise<{ sent: number; unreachable: Person[] }> {
    let sent = 0;
    const unreachable: Person[] = [];
    for (const person of this.store.nominees()) {
      if (!this.address(person)) {
        unreachable.push(person);
        continue;
      }
      try {
        if (await this.enqueue(person.id, () => this.startCheckin(person))) sent++;
      } catch (err) {
        this.log(`Could not start check-in for ${person.email}`, err);
      }
    }
    return { sent, unreachable };
  }

  /**
   * One chase, and only one, to anyone who has not answered today's opening
   * question within `minMinutes` of it being sent.
   */
  async runReminders(minMinutes = 0): Promise<number> {
    let nudged = 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open" || checkin.reminded) continue;
      if (this.store.get(`takeover:${checkin.id}`)) continue;
      const messages = this.store.messages(checkin.id);
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
      const replied = this.store.messages(checkin.id).some((m) => m.role === "user");
      if (!replied) {
        this.store.setCheckinStatus(checkin.id, "no_response");
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
    if (["help", "hi", "hello", "menu", "board", "refresh", "list", "digest"].includes(verb) && arg) verb = "";
    if (verb === "add") verb = "nominate";
    if (verb === "delete") verb = "remove";
    if (verb === "checkin") verb = "ask";

    try {
      switch (verb) {
        case "help":
          return await reply(ADMIN_HELP);

        case "hi":
        case "hello":
        case "menu":
        case "board":
          return await this.showBoard(admin);

        case "refresh":
          // Nothing to say: the board is brought up to date after every command.
          return;

        case "schedule": {
          const digest = /^digest\b/i.test(arg);
          const key = digest ? "schedule:digest" : "schedule:checkin";
          const value = arg.replace(/^digest\s*/i, "").trim();
          if (!value) return await reply(this.scheduleStatus());
          if (/^(off|none|stop|no|remove)$/i.test(value)) {
            this.store.set(key, "");
            return await reply(this.scheduleStatus());
          }
          const time = parseTime(value);
          if (!time) return await reply("I did not understand that time. Try **schedule 17:00** or **schedule off**.");
          this.store.set(key, time);
          return await reply(this.scheduleStatus());
        }

        case "nominate": {
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
              ? `**How you write to ${displayName(target)}** (read once from your past chat, and reused)\n\n${learned}`
              : `There is not enough past chat with ${displayName(target)} to learn from, so I use the standard short, simple style.`,
          );
        }

        case "remove": {
          const target = this.resolveOne(arg);
          if (typeof target === "string") return await reply(target);
          this.store.setNominated(target.id, false);
          return await reply(`${displayName(target)} will no longer get check-ins. Their past notes are kept.`);
        }

        case "list": {
          const nominees = this.store.nominees();
          if (!nominees.length) return await reply("Nobody is nominated yet. Send **nominate name@company.com** to add someone.");
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
          return await reply(`**Nominated (${nominees.length})**\n\n${lines.join("\n")}`);
        }

        case "report": {
          const target = this.resolveOne(arg);
          if (typeof target === "string") return await reply(target);
          const notes = this.store.recentNotes(target.id, 5);
          const body = notes.length
            ? notes.map((n) => `**${n.date}**\n${renderNote(n)}`).join("\n\n")
            : "No check-in notes yet.";
          return await reply(
            `**${displayName(target)}**\n\n**Open items**\n${renderItems(this.store.activeItems(target.id))}\n\n${body}`,
          );
        }

        case "ask": {
          // "ask all", "ask priya", "ask mahanth for an update": ask now, even if already asked today.
          const nominees = this.store.nominees();
          const words = arg.toLowerCase().split(/[^a-z0-9@.]+/).filter((w) => w.length > 2);
          if (!arg || words.some((w) => ["all", "everyone", "everybody", "team"].includes(w))) {
            if (!nominees.length) return await reply("Nobody is added yet. Send **add name@company.com** first.");
            const results: string[] = [];
            for (const p of nominees) {
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
          if (!this.store.nominees().length) {
            return await reply(
              "There are no check-in notes yet because nobody is nominated. Start with **nominate name@company.com**, or send **help** for the full list of commands.",
            );
          }
          return await reply(await this.brain.answer(text, this.material()));
        }
      }
    } catch (err) {
      this.log(`Admin command failed: ${verb}`, err);
      await reply("That didn't work: something went wrong on my side. The server log has the details.");
    }
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
    return `Today is ${this.today()}. Notes cover the last 14 days.\n\n${sections.join("\n\n")}`;
  }
}
