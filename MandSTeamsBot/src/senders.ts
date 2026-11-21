/**
 * More than one person can send check-ins from their own Teams account through the one bot.
 * The main sender is whoever signed in first (DATA_DIR/graph-token.json). Each extra sender
 * (EXTRA_SENDERS) has a data folder of its own, with their own sign-in, people, groups,
 * schedule and memory, and only starts once they have signed in.
 */
import path from "node:path";

export interface SenderKey {
  email: string;
  /** The first name, as typed in front of a message to talk to that sender's assistant: "mahanth: list". */
  key: string;
}

/** The extra senders' emails from the setting, without the main sender and without duplicates. */
export function extraSenders(setting: string, mainEmail: string): string[] {
  const main = mainEmail.trim().toLowerCase();
  const seen = new Set<string>();
  for (const raw of setting.split(",")) {
    const email = raw.trim().toLowerCase();
    if (email && email !== main) seen.add(email);
  }
  return [...seen];
}

/** Where an extra sender's sign-in and database are kept. */
export function senderDataDir(dataDir: string, email: string): string {
  return path.join(dataDir, "senders", email.trim().toLowerCase());
}

/**
 * Which sender's assistant a message to the bot is for. An extra sender's own messages go to
 * theirs; everyone else's to the main one (`senders[0]`). An admin can address another sender's
 * assistant by starting with that sender's first name: "mahanth: add group Sales".
 */
export function routeAdminMessage(
  text: string,
  fromEmail: string,
  senders: SenderKey[],
  isAdmin = false,
): { sender: string; text: string } {
  const from = fromEmail.trim().toLowerCase();
  if (isAdmin) {
    const m = /^\s*([a-z]+)\s*[:,]\s*([\s\S]*)$/i.exec(text);
    const named = m ? senders.find((s) => s.key === m[1]!.toLowerCase()) : undefined;
    if (named && m![2]!.trim()) return { sender: named.email, text: m![2]!.trim() };
  }
  const own = senders.find((s, i) => i > 0 && s.email === from);
  return { sender: (own ?? senders[0]!).email, text };
}

/** Typed messages that are commands: they always go to the admin's own assistant (or to the one they name). */
const COMMAND = /^\s*(help|menu|board|tracker|hours|office|timezone|approve|leave|holidays?|progress|hourly|clockout|blockers|talentflow|weekly|usage|pause|stop|resume|refresh|schedule|nominate|add|profile|style|greeting|remove|delete|list|report|ask|checkin|digest|post|reread)\b/i;

/**
 * Routes what an admin types to the right assistant. A message that names one ("mahanth: …"),
 * or a button for the other admin's groups, goes there, and for the next 30 minutes the admin's
 * typed replies ("no, don't send", "has he replied?") follow it there too. Commands and board
 * buttons without a name always go to the admin's own assistant.
 */
export class ReplyRouter {
  private last = new Map<string, { sender: string; at: number }>();

  constructor(
    private readonly senders: SenderKey[],
    private readonly windowMs = 30 * 60_000,
  ) {}

  route(text: string, fromEmail: string, isAdmin: boolean, typed: boolean, now = Date.now()): { sender: string; text: string } {
    const from = fromEmail.trim().toLowerCase();
    const routed = routeAdminMessage(text, from, this.senders, isAdmin);
    if (!isAdmin) return routed;
    const own = routeAdminMessage("", from, this.senders, false).sender;
    const named = /^\s*([a-z]+)\s*[:,]\s*\S/i.exec(text);
    if (named && this.senders.some((s) => s.key === named[1]!.toLowerCase())) {
      // Talking to another assistant opens the window; naming your own closes it.
      if (routed.sender !== own) this.last.set(from, { sender: routed.sender, at: now });
      else this.last.delete(from);
      return routed;
    }
    const last = this.last.get(from);
    if (typed && last && now - last.at <= this.windowMs && !COMMAND.test(text)) {
      last.at = now;
      return { sender: last.sender, text: text.trim() };
    }
    return routed;
  }
}

/** The word typed in front of a message to reach a sender's assistant: the first part of their email. */
export function senderKey(email: string): string {
  return email.trim().toLowerCase().split(/[@.]/)[0]!;
}

/**
 * An extra sender's assistant starts with the main sender's timezone and office hours, so "now"
 * means the same for both (the team is in one place). Whatever the extra sender set is kept.
 */
export function inheritSettings(
  main: { get(key: string): string | undefined },
  extra: { get(key: string): string | undefined; set(key: string, value: string): void },
): void {
  for (const key of ["timezone", "hours"]) {
    const value = main.get(key);
    if (value && extra.get(key) === undefined) extra.set(key, value);
  }
}

/** What the guard needs from each sender: their name, and what their assistant follows. */
export interface SenderFollows {
  name: string;
  email?: string;
  store: {
    groups(): { chatId: string; name?: string }[];
    personByEmail(email: string): { nominated: boolean } | undefined;
  };
}

/**
 * For one sender: the groups each other sender follows, so either admin can ask the other's
 * groups for an update from their own board (through "name: ask group …").
 */
export function othersGroupsFor(self: SenderFollows, all: SenderFollows[]): () => { name: string; key: string; groups: string[] }[] {
  return () =>
    all
      .filter((o) => o !== self)
      .map((o) => ({ name: o.name, key: senderKey(o.email ?? ""), groups: o.store.groups().map((g) => g.name ?? g.chatId) }));
}

/**
 * For one sender: which other sender already follows a group chat or a person, so their
 * assistant never adds someone another sender already asks. Undefined when nobody else does.
 */
export function followedElsewhereFor(
  self: SenderFollows,
  all: SenderFollows[],
): { group(chatId: string): string | undefined; person(email: string): string | undefined } {
  const others = all.filter((o) => o !== self);
  return {
    group: (chatId) => others.find((o) => o.store.groups().some((g) => g.chatId === chatId))?.name,
    person: (email) => others.find((o) => o.store.personByEmail(email.trim().toLowerCase())?.nominated)?.name,
  };
}
