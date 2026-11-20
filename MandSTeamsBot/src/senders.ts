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
