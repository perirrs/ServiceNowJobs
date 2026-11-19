import { mkdirSync } from "node:fs";
import path from "node:path";
import { DatabaseSync } from "node:sqlite";

export interface Person {
  id: number;
  email: string;
  name: string | null;
  teamsUserId: string | null;
  conversationId: string | null;
  nominated: boolean;
  /** Free-text brief written by an admin: role, responsibilities, targets. */
  profile: string | null;
}

export type CheckinStatus = "open" | "completed" | "no_response";

export interface Checkin {
  id: number;
  personId: number;
  date: string;
  status: CheckinStatus;
  reminded: boolean;
}

export interface ChatMessage {
  role: "bot" | "user";
  text: string;
  at: string;
}

export type ItemStatus = "open" | "blocked" | "done" | "dropped";

/** A tracked piece of work. Open and blocked items are the bot's memory between days. */
export interface WorkItem {
  id: number;
  personId: number;
  title: string;
  status: ItemStatus;
  lastUpdate: string;
  firstSeen: string;
  lastSeen: string;
}

/** The structured note the AI writes at the end of each person's check-in. */
export interface NoteContent {
  summary: string;
  progressVsTargets: string;
  blockers: string[];
  painPoints: string[];
  helpNeeded: string[];
  followUpTomorrow: string[];
}

export interface Note extends NoteContent {
  personId: number;
  date: string;
}

type Row = Record<string, unknown>;

const SCHEMA = `
CREATE TABLE IF NOT EXISTS people (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  email TEXT NOT NULL UNIQUE,
  name TEXT,
  teams_user_id TEXT UNIQUE,
  conversation_id TEXT,
  nominated INTEGER NOT NULL DEFAULT 0,
  profile TEXT,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE TABLE IF NOT EXISTS checkins (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  person_id INTEGER NOT NULL REFERENCES people(id),
  date TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'open',
  reminded INTEGER NOT NULL DEFAULT 0,
  UNIQUE (person_id, date)
);
CREATE TABLE IF NOT EXISTS messages (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  checkin_id INTEGER NOT NULL REFERENCES checkins(id),
  role TEXT NOT NULL,
  text TEXT NOT NULL,
  at TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE TABLE IF NOT EXISTS notes (
  checkin_id INTEGER PRIMARY KEY REFERENCES checkins(id),
  person_id INTEGER NOT NULL REFERENCES people(id),
  date TEXT NOT NULL,
  content TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  person_id INTEGER NOT NULL REFERENCES people(id),
  title TEXT NOT NULL,
  status TEXT NOT NULL,
  last_update TEXT NOT NULL,
  first_seen TEXT NOT NULL,
  last_seen TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS kv (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS digests (
  date TEXT PRIMARY KEY,
  text TEXT NOT NULL,
  created_at TEXT NOT NULL DEFAULT (datetime('now'))
);
`;

function toPerson(r: Row): Person {
  return {
    id: r.id as number,
    email: r.email as string,
    name: (r.name as string | null) ?? null,
    teamsUserId: (r.teams_user_id as string | null) ?? null,
    conversationId: (r.conversation_id as string | null) ?? null,
    nominated: r.nominated === 1,
    profile: (r.profile as string | null) ?? null,
  };
}

function toCheckin(r: Row): Checkin {
  return {
    id: r.id as number,
    personId: r.person_id as number,
    date: r.date as string,
    status: r.status as CheckinStatus,
    reminded: r.reminded === 1,
  };
}

function toItem(r: Row): WorkItem {
  return {
    id: r.id as number,
    personId: r.person_id as number,
    title: r.title as string,
    status: r.status as ItemStatus,
    lastUpdate: r.last_update as string,
    firstSeen: r.first_seen as string,
    lastSeen: r.last_seen as string,
  };
}

function toNote(r: Row): Note {
  return {
    ...(JSON.parse(r.content as string) as NoteContent),
    personId: r.person_id as number,
    date: r.date as string,
  };
}

export class Store {
  private db: DatabaseSync;

  /** Pass ":memory:" for tests. */
  constructor(dataDirOrMemory: string) {
    if (dataDirOrMemory === ":memory:") {
      this.db = new DatabaseSync(":memory:");
    } else {
      mkdirSync(dataDirOrMemory, { recursive: true });
      this.db = new DatabaseSync(path.join(dataDirOrMemory, "mands.db"));
    }
    this.db.exec("PRAGMA foreign_keys = ON;");
    this.db.exec(SCHEMA);
  }

  close(): void {
    this.db.close();
  }

  // ---- people ----

  personByEmail(email: string): Person | undefined {
    const r = this.db.prepare("SELECT * FROM people WHERE email = ?").get(email.toLowerCase());
    return r ? toPerson(r) : undefined;
  }

  personByTeamsId(teamsUserId: string): Person | undefined {
    const r = this.db.prepare("SELECT * FROM people WHERE teams_user_id = ?").get(teamsUserId);
    return r ? toPerson(r) : undefined;
  }

  personById(id: number): Person | undefined {
    const r = this.db.prepare("SELECT * FROM people WHERE id = ?").get(id);
    return r ? toPerson(r) : undefined;
  }

  /** Create the person if needed, and fill in whichever details are provided. */
  upsertPerson(
    email: string,
    details: { name?: string | null; teamsUserId?: string; conversationId?: string } = {},
  ): Person {
    const key = email.toLowerCase();
    this.db.prepare("INSERT OR IGNORE INTO people (email) VALUES (?)").run(key);
    this.db
      .prepare(
        `UPDATE people SET
           name = COALESCE(?, name),
           teams_user_id = COALESCE(?, teams_user_id),
           conversation_id = COALESCE(?, conversation_id)
         WHERE email = ?`,
      )
      .run(details.name ?? null, details.teamsUserId ?? null, details.conversationId ?? null, key);
    return this.personByEmail(key)!;
  }

  setNominated(personId: number, nominated: boolean): void {
    this.db.prepare("UPDATE people SET nominated = ? WHERE id = ?").run(nominated ? 1 : 0, personId);
  }

  setProfile(personId: number, profile: string): void {
    this.db.prepare("UPDATE people SET profile = ? WHERE id = ?").run(profile, personId);
  }

  nominees(): Person[] {
    return this.db
      .prepare("SELECT * FROM people WHERE nominated = 1 ORDER BY COALESCE(name, email)")
      .all()
      .map(toPerson);
  }

  /** Find nominees by email, or by a fragment of their name or email. */
  findNominees(query: string): Person[] {
    const q = query.trim().toLowerCase();
    const all = this.nominees();
    const exact = all.filter((p) => p.email === q);
    if (exact.length) return exact;
    return all.filter((p) => p.email.includes(q) || (p.name ?? "").toLowerCase().includes(q));
  }

  // ---- check-ins ----

  checkin(personId: number, date: string): Checkin | undefined {
    const r = this.db
      .prepare("SELECT * FROM checkins WHERE person_id = ? AND date = ?")
      .get(personId, date);
    return r ? toCheckin(r) : undefined;
  }

  openCheckin(personId: number, date: string): Checkin {
    this.db
      .prepare(
        `INSERT INTO checkins (person_id, date) VALUES (?, ?)
         ON CONFLICT (person_id, date) DO UPDATE SET status = 'open'`,
      )
      .run(personId, date);
    return this.checkin(personId, date)!;
  }

  setCheckinStatus(checkinId: number, status: CheckinStatus): void {
    this.db.prepare("UPDATE checkins SET status = ? WHERE id = ?").run(status, checkinId);
  }

  markReminded(checkinId: number): void {
    this.db.prepare("UPDATE checkins SET reminded = 1 WHERE id = ?").run(checkinId);
  }

  checkinsOn(date: string): Checkin[] {
    return this.db.prepare("SELECT * FROM checkins WHERE date = ?").all(date).map(toCheckin);
  }

  addMessage(checkinId: number, role: "bot" | "user", text: string): void {
    this.db
      .prepare("INSERT INTO messages (checkin_id, role, text) VALUES (?, ?, ?)")
      .run(checkinId, role, text);
  }

  messages(checkinId: number): ChatMessage[] {
    return this.db
      .prepare("SELECT role, text, at FROM messages WHERE checkin_id = ? ORDER BY id")
      .all(checkinId)
      .map((r) => ({ role: r.role as "bot" | "user", text: r.text as string, at: r.at as string }));
  }

  // ---- notes ----

  saveNote(checkin: Checkin, content: NoteContent): void {
    this.db
      .prepare(
        `INSERT INTO notes (checkin_id, person_id, date, content) VALUES (?, ?, ?, ?)
         ON CONFLICT (checkin_id) DO UPDATE SET content = excluded.content`,
      )
      .run(checkin.id, checkin.personId, checkin.date, JSON.stringify(content));
  }

  /** Most recent notes first. */
  recentNotes(personId: number, limit: number, beforeDate?: string): Note[] {
    return this.db
      .prepare(
        `SELECT * FROM notes WHERE person_id = ? AND date < ?
         ORDER BY date DESC LIMIT ?`,
      )
      .all(personId, beforeDate ?? "9999-12-31", limit)
      .map(toNote);
  }

  notesOn(date: string): Note[] {
    return this.db.prepare("SELECT * FROM notes WHERE date = ?").all(date).map(toNote);
  }

  notesSince(date: string): Note[] {
    return this.db
      .prepare("SELECT * FROM notes WHERE date >= ? ORDER BY date DESC")
      .all(date)
      .map(toNote);
  }

  // ---- work items (the memory) ----

  /** Items still in play: open or blocked. */
  activeItems(personId: number): WorkItem[] {
    return this.db
      .prepare(
        "SELECT * FROM items WHERE person_id = ? AND status IN ('open', 'blocked') ORDER BY first_seen, id",
      )
      .all(personId)
      .map(toItem);
  }

  addItem(personId: number, title: string, status: ItemStatus, update: string, date: string): void {
    this.db
      .prepare(
        `INSERT INTO items (person_id, title, status, last_update, first_seen, last_seen)
         VALUES (?, ?, ?, ?, ?, ?)`,
      )
      .run(personId, title, status, update, date, date);
  }

  /** Returns false if the item does not belong to this person, so the AI cannot touch others' items. */
  updateItem(personId: number, itemId: number, status: ItemStatus, update: string, date: string): boolean {
    const res = this.db
      .prepare(
        "UPDATE items SET status = ?, last_update = ?, last_seen = ? WHERE id = ? AND person_id = ?",
      )
      .run(status, update, date, itemId, personId);
    return res.changes > 0;
  }

  // ---- small persistent flags and cursors ----

  get(key: string): string | undefined {
    const r = this.db.prepare("SELECT value FROM kv WHERE key = ?").get(key);
    return r ? (r.value as string) : undefined;
  }

  set(key: string, value: string): void {
    this.db
      .prepare("INSERT INTO kv (key, value) VALUES (?, ?) ON CONFLICT (key) DO UPDATE SET value = excluded.value")
      .run(key, value);
  }

  // ---- digests ----

  saveDigest(date: string, text: string): void {
    this.db
      .prepare(
        `INSERT INTO digests (date, text) VALUES (?, ?)
         ON CONFLICT (date) DO UPDATE SET text = excluded.text`,
      )
      .run(date, text);
  }

  digest(date: string): string | undefined {
    const r = this.db.prepare("SELECT text FROM digests WHERE date = ?").get(date);
    return r ? (r.text as string) : undefined;
  }
}
