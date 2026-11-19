import { displayName, renderItems, renderNote } from "./ai.js";
import type { Brain, CheckinContext, DigestPerson } from "./ai.js";
import { todayIn } from "./config.js";
import type { Config } from "./config.js";
import type { Checkin, Person, Store } from "./db.js";

/** Sends a chat message to a Teams conversation. Implemented by teams.ts, faked in tests. */
export interface Messenger {
  send(conversationId: string, text: string): Promise<void>;
}

/** Who a Teams activity came from. */
export interface Identity {
  teamsUserId: string;
  conversationId: string;
  name?: string;
  /** Looks the user's email up in Teams. Only called the first time we see someone. */
  lookupEmail: () => Promise<string | undefined>;
}

const ADMIN_HELP = `**Check-in bot: admin commands**

- **nominate** name@company.com: start daily check-ins for someone
- **profile** name@company.com: their role, responsibilities and targets. The daily questions are phrased from this, so be specific (numbers help).
- **remove** name@company.com: stop their check-ins (notes are kept)
- **list**: everyone nominated and where they stand today
- **report** name@company.com: their recent notes and open items
- **checkin** name@company.com (or **checkin all**): run a check-in right now
- **digest**: produce today's digest right now

Anything else you type is treated as a question about the team's notes, for example: _who has been blocked for more than two days?_

A name or part of an email works wherever an email is shown, once the person is nominated.`;

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
  ) {}

  private today(): string {
    return todayIn(this.config.timezone, this.now());
  }

  private isAdmin(person: Person): boolean {
    return this.config.adminEmails.includes(person.email);
  }

  private async say(person: Person, text: string): Promise<void> {
    if (!person.conversationId) return;
    for (const part of chunk(text)) await this.messenger.send(person.conversationId, part);
  }

  private enqueue<T>(personId: number, job: () => Promise<T>): Promise<T> {
    const prev = this.queues.get(personId) ?? Promise.resolve();
    const next = prev.catch(() => undefined).then(job);
    this.queues.set(personId, next);
    return next;
  }

  private welcomeText(person: Person): string {
    const first = person.name?.split(" ")[0];
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
    if (this.isAdmin(person)) await this.say(person, ADMIN_HELP);
    else if (person.nominated) await this.say(person, this.welcomeText(person));
    // Everyone else hears nothing: an org-wide install should not message the whole company.
  }

  async handleMessage(id: Identity, text: string): Promise<void> {
    const who = await this.identify(id);
    if (!who) return;
    const { person, firstContact } = who;

    if (this.isAdmin(person)) {
      await this.enqueue(person.id, () => this.handleAdmin(person, text.trim()));
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
    };
  }

  /** Send today's opening question. Returns false if the person cannot be reached or already has one. */
  async startCheckin(person: Person, force = false): Promise<boolean> {
    if (!person.conversationId) return false;
    const date = this.today();
    const existing = this.store.checkin(person.id, date);
    if (existing && !force) return false;
    if (existing?.status === "open") return false;

    const checkin = this.store.openCheckin(person.id, date);
    const turn = await this.brain.nextTurn(this.context(person, checkin));
    this.store.addMessage(checkin.id, "bot", turn.reply);
    await this.say(person, turn.reply);
    return true;
  }

  private async handleUpdate(person: Person, text: string): Promise<void> {
    if (!text) return;
    const date = this.today();
    let checkin = this.store.checkin(person.id, date);
    // A message outside an open check-in (before the daily prompt, or after
    // closing) opens or reopens today's, so nothing a person says is lost.
    if (!checkin || checkin.status !== "open") checkin = this.store.openCheckin(person.id, date);
    this.store.addMessage(checkin.id, "user", text);

    try {
      const botTurns = this.store.messages(checkin.id).filter((m) => m.role === "bot").length;
      const finalTurn = botTurns >= this.config.maxBotTurns - 1;
      const turn = await this.brain.nextTurn(this.context(person, checkin, finalTurn));
      this.store.addMessage(checkin.id, "bot", turn.reply);
      await this.say(person, turn.reply);
      if (turn.done) await this.finish(person, checkin);
    } catch (err) {
      this.log(`Check-in reply failed for ${person.email}`, err);
      // The message is already stored, so it still reaches tonight's note.
      await this.say(person, "Thanks, I've recorded that. I had trouble replying just now, so if there's more to add, send it and I'll pick it up.");
    }
  }

  /** Turn the day's conversation into a note and update the person's open items. */
  private async finish(person: Person, checkin: Checkin): Promise<void> {
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
  }

  // ---------- scheduled jobs ----------

  async runCheckins(): Promise<{ sent: number; unreachable: Person[] }> {
    let sent = 0;
    const unreachable: Person[] = [];
    for (const person of this.store.nominees()) {
      if (!person.conversationId) {
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

  /** One gentle nudge to anyone who has not answered today's opening question. */
  async runReminders(): Promise<number> {
    let nudged = 0;
    for (const checkin of this.store.checkinsOn(this.today())) {
      if (checkin.status !== "open" || checkin.reminded) continue;
      if (this.store.messages(checkin.id).some((m) => m.role === "user")) continue;
      const person = this.store.personById(checkin.personId);
      if (!person?.nominated) continue;
      try {
        await this.say(person, "A quick nudge on today's check-in, whenever you have two minutes. A few lines is plenty.");
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
        await this.enqueue(person.id, () => this.finish(person, checkin));
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
        missing: note ? null : person.conversationId ? "no_response" : "not_connected",
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
        await this.say(admin, text);
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
    const reply = (msg: string) => this.say(admin, msg);
    const [, verbRaw = "", rest = ""] = /^(\S+)\s*([\s\S]*)$/.exec(text) ?? [];
    const arg = rest.trim();
    let verb = verbRaw.toLowerCase().replace(/[:,]$/, "");
    // "list" alone is a command; "list who is blocked" is a question.
    if (["help", "hi", "hello", "list", "digest"].includes(verb) && arg) verb = "";

    try {
      switch (verb) {
        case "help":
        case "hi":
        case "hello":
          return await reply(ADMIN_HELP);

        case "nominate": {
          const email = arg.split(/\s+/)[0]?.toLowerCase() ?? "";
          if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
            return await reply("Tell me who by work email, for example: **nominate priya@company.com**");
          }
          if (this.config.adminEmails.includes(email)) {
            return await reply("That's an admin account. Admins receive the digest rather than check-ins.");
          }
          const person = this.store.upsertPerson(email);
          this.store.setNominated(person.id, true);
          if (person.conversationId) {
            await this.say(person, this.welcomeText(person));
            return await reply(
              `${displayName(person)} is nominated and has been told how the check-ins work. Next, describe their role: **profile ${email}: ...**`,
            );
          }
          return await reply(
            `${email} is nominated. They are not connected to me yet: once the app is installed for them in Teams (or they open it and say hello) I'll introduce myself and start the daily check-ins. Next, describe their role: **profile ${email}: ...**`,
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
            `Saved. From the next check-in, questions for ${displayName(target)} will be phrased around this role and these targets.`,
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
            const today = !p.conversationId
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

        case "checkin": {
          if (!arg || arg.toLowerCase() === "all") {
            const { sent, unreachable } = await this.runCheckins();
            const note = unreachable.length
              ? ` Not connected yet: ${unreachable.map((p) => p.email).join(", ")}.`
              : "";
            return await reply(`Started ${sent} check-in${sent === 1 ? "" : "s"}.${note}`);
          }
          const target = this.resolveOne(arg);
          if (typeof target === "string") return await reply(target);
          if (!target.conversationId) return await reply(`${displayName(target)} is not connected to me in Teams yet.`);
          const started = await this.enqueue(target.id, () => this.startCheckin(target, true));
          return await reply(
            started
              ? `Check-in sent to ${displayName(target)}.`
              : `${displayName(target)} already has a check-in in progress.`,
          );
        }

        case "digest":
          await reply("Putting today's digest together. It will arrive here shortly.");
          await this.runDigest();
          return;

        default: {
          if (!text) return;
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
      return [
        `# ${displayName(p)} (${p.email})`,
        `Role and targets: ${p.profile ?? "(not provided)"}`,
        `Open items:\n${renderItems(this.store.activeItems(p.id))}`,
        mine.length
          ? mine.map((n) => `## ${n.date}\n${renderNote(n)}`).join("\n\n")
          : "(no check-in notes in the last 14 days)",
      ].join("\n\n");
    });
    return `Today is ${this.today()}. Notes cover the last 14 days.\n\n${sections.join("\n\n")}`;
  }
}
