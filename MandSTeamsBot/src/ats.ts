/**
 * A recruiter's real figures from TalentFlow (a Base44 app), so the check-in
 * can take them as given instead of asking for them.
 *
 * TalentFlow keeps one CandidateApplication per candidate per requirement,
 * owned by a Recruiter, with a status_history of { status, changed_at }. A
 * figure is the number of moves into a status inside the window: three moves
 * to "Submitted to Client" today is "3 submitted to client". Monthly targets
 * come from TeamMemberTarget, by the team member's email and "YYYY-MM".
 */
import type { Base44 } from "./base44.js";
import { describeOverview, describeTrackRecord, utc } from "./trackrecord.js";
import type { HistoryPerson } from "./trackrecord.js";

/** TalentFlow status -> the figure it counts towards. Statuses not listed are not counted. */
const STATUS_BUCKET: Record<string, string> = {
  Applied: "added",
  "Screen Reject - Internal": "internal_reject",
  "Internal Duplicate": "internal_duplicate",
  "Submitted to Client": "submitted_to_client",
  "Client Duplicate": "client_duplicate",
  "Client Hold": "client_hold",
  "Screen Reject - Client": "client_reject",
  "L1 Interview Scheduled": "l1_scheduled",
  "L1 Select": "l1_select",
  "L1 Reject": "l1_reject",
  "L2 Interview Scheduled": "l2_scheduled",
  "L2 Select": "l2_select",
  "L2 Reject": "l2_reject",
  "L3 Interview Scheduled": "l3_scheduled",
  "L3 Select": "l3_select",
  "L3 Reject": "l3_reject",
  "Final Select": "final_select",
  Offered: "offered",
  Hired: "hired",
  Onboarded: "onboarded",
  "Candidate Backed Out": "backed_out",
};

/** Plain names for the figures, in the order they are shown. */
const BUCKET_NAMES: Record<string, string> = {
  // TalentFlow calls a candidate added against a requirement a "submission"; sending it on to the client is a later step.
  added: "submissions (candidates added)",
  internal_reject: "internal screen reject",
  internal_duplicate: "internal duplicate",
  submitted_to_client: "submitted to client",
  client_duplicate: "client duplicate",
  client_hold: "on client hold",
  client_reject: "rejected by client on screening",
  l1_scheduled: "L1 scheduled",
  l1_select: "L1 select",
  l1_reject: "L1 reject",
  l2_scheduled: "L2 scheduled",
  l2_select: "L2 select",
  l2_reject: "L2 reject",
  l3_scheduled: "L3 scheduled",
  l3_select: "L3 select",
  l3_reject: "L3 reject",
  final_select: "final select",
  offered: "offered",
  hired: "hired",
  onboarded: "joined",
  backed_out: "backed out",
  new_requirements: "new requirements",
  new_spots: "new spots",
  new_spocs: "new SPOCs",
};

/** TeamMemberTarget field -> figure. */
const TARGET_FIELDS: Record<string, string> = {
  submitted_to_client_target: "submitted_to_client",
  l1_select_target: "l1_select",
  l2_select_target: "l2_select",
  l3_select_target: "l3_select",
  final_select_target: "final_select",
  offered_target: "offered",
  onboarding_target: "onboarded",
};

export interface Ats {
  /** Figures in [fromIso, toIso), or null when the person is not in the ATS. */
  stats(email: string, fromIso: string, toIso: string): Promise<Record<string, number> | null>;
  /** This person's targets for a "YYYY-MM" month, or null when none are set. */
  targets?(email: string, month: string): Promise<Record<string, number> | null>;
  /** When they last changed anything in the ATS (ISO, UTC), or null if never or not in it. */
  lastUpdate?(email: string): Promise<string | null>;
  /** Everyone's figures for today and for the month so far, read in one pass. */
  team?(emails: string[], dayFromIso: string, monthFromIso: string, toIso: string): Promise<Map<string, TeamFigures>>;
  /** A person's whole history in the ATS, as plain text, or null if they are not in it. */
  trackRecord?(email: string): Promise<string | null>;
  /** The whole ATS on one page: team trend, every client, one line for every person. */
  overview?(): Promise<string>;
}

/** One person's figures for a team report. */
export interface TeamFigures {
  day: Record<string, number>;
  month: Record<string, number>;
  /** Their last change in the ATS today (ISO, UTC), or null. */
  lastToday: string | null;
}

interface Application {
  status_history?: { status?: string; changed_at?: string }[];
}

interface FullApplication extends Application {
  recruiter_id?: string;
  requirement_id?: string;
  client_id?: string;
  status?: string;
}

interface RequirementRow {
  id: string;
  account_manager_id?: string;
  client_id?: string;
  job_title?: string;
  required_skills?: unknown;
  positions_count?: number;
  status?: string;
  created_date?: string;
}

interface ClientRow {
  id: string;
  client_name?: string;
  account_name?: string;
  status?: string;
  created_by?: string;
  created_date?: string;
}


/** The time as TalentFlow stores it, for a $gte filter. */
const stored = (ms: number) => new Date(ms).toISOString().slice(0, 19);

/** Requirements, spots (positions) and SPOCs a person added in [from, to). */
function addOwnerCounts(
  out: Record<string, number>,
  reqs: RequirementRow[],
  clients: ClientRow[],
  userId: string | undefined,
  email: string,
  from: number,
  to: number,
): void {
  if (userId) {
    for (const r of reqs) {
      const at = utc(r.created_date);
      if (r.account_manager_id !== userId || !(at >= from && at < to)) continue;
      out.new_requirements = (out.new_requirements ?? 0) + 1;
      out.new_spots = (out.new_spots ?? 0) + Math.max(1, Number(r.positions_count) || 1);
    }
  }
  for (const c of clients) {
    const at = utc(c.created_date);
    if ((c.created_by ?? "").trim().toLowerCase() !== email || !(at >= from && at < to)) continue;
    out.new_spocs = (out.new_spocs ?? 0) + 1;
  }
}

export class Base44TalentFlow implements Ats {
  private recruiters?: { at: number; byEmail: Map<string, string> };
  private users?: { at: number; byEmail: Map<string, string> };
  private everything?: { at: number; apps: FullApplication[]; reqs: RequirementRow[]; clients: ClientRow[] };
  private appId?: string;

  constructor(
    private b44: Base44,
    /** TalentFlow's app: slug, name or id. */
    private appRef: string,
    private now: () => Date = () => new Date(),
  ) {}

  private async app(): Promise<string> {
    if (!this.appId) {
      const app = await this.b44.findApp(this.appRef);
      if (!app) throw new Error(`No Base44 app matches "${this.appRef}"`);
      this.appId = app.id;
    }
    return this.appId;
  }

  /** Recruiter ids by lower-case email, read at most hourly. */
  private async recruiterId(email: string): Promise<string | undefined> {
    if (!this.recruiters || this.now().getTime() - this.recruiters.at > 3_600_000) {
      const rows = await this.b44.list<{ id: string; email?: string }>(await this.app(), "Recruiter", { fields: ["email"] });
      const byEmail = new Map<string, string>();
      for (const r of rows) if (r.email) byEmail.set(r.email.trim().toLowerCase(), r.id);
      this.recruiters = { at: this.now().getTime(), byEmail };
    }
    return this.recruiters.byEmail.get(email.trim().toLowerCase());
  }

  /** TalentFlow user ids (account managers and owners own requirements by these) by lower-case email. */
  private async userId(email: string): Promise<string | undefined> {
    if (!this.users || this.now().getTime() - this.users.at > 3_600_000) {
      const rows = await this.b44.users<{ id: string; email?: string }>(await this.app());
      const byEmail = new Map<string, string>();
      for (const u of rows) if (u.email) byEmail.set(u.email.trim().toLowerCase(), u.id);
      this.users = { at: this.now().getTime(), byEmail };
    }
    return this.users.byEmail.get(email.trim().toLowerCase());
  }

  async stats(email: string, fromIso: string, toIso: string): Promise<Record<string, number> | null> {
    const key = email.trim().toLowerCase();
    const id = await this.recruiterId(key);
    const user = await this.userId(key);
    if (!id && !user) return null;
    const from = Date.parse(fromIso);
    const to = Date.parse(toIso);
    const app = await this.app();
    let counts: Record<string, number> = {};
    if (id) {
      // Only applications touched since the window opened can have moved inside it.
      const rows = await this.b44.list<Application>(app, "CandidateApplication", {
        q: { recruiter_id: id, updated_date: { $gte: stored(from) } },
        fields: ["status_history"],
      });
      counts = countMoves(rows, from, to);
    }
    if (user) {
      const reqs = await this.b44.list<RequirementRow>(app, "Requirement", {
        q: { account_manager_id: user, created_date: { $gte: stored(from) } },
        fields: ["account_manager_id", "positions_count", "created_date"],
      });
      const clients = await this.b44.list<ClientRow>(app, "Client", {
        q: { created_by: key, created_date: { $gte: stored(from) } },
        fields: ["created_by", "created_date"],
      });
      addOwnerCounts(counts, reqs, clients, user, key, from, to);
    }
    return counts;
  }

  async team(emails: string[], dayFromIso: string, monthFromIso: string, toIso: string): Promise<Map<string, TeamFigures>> {
    const app = await this.app();
    const dayFrom = Date.parse(dayFromIso);
    const monthFrom = Math.min(Date.parse(monthFromIso), dayFrom);
    const to = Date.parse(toIso);
    // Three reads for the whole team, whatever its size.
    const apps = await this.b44.list<FullApplication>(app, "CandidateApplication", {
      q: { updated_date: { $gte: stored(monthFrom) } },
      fields: ["recruiter_id", "status_history"],
    });
    const reqs = await this.b44.list<RequirementRow>(app, "Requirement", {
      q: { created_date: { $gte: stored(monthFrom) } },
      fields: ["account_manager_id", "positions_count", "created_date"],
    });
    const clients = await this.b44.list<ClientRow>(app, "Client", {
      q: { created_date: { $gte: stored(monthFrom) } },
      fields: ["created_by", "created_date"],
    });
    const out = new Map<string, TeamFigures>();
    for (const raw of emails) {
      const email = raw.trim().toLowerCase();
      const rid = await this.recruiterId(email);
      const uid = await this.userId(email);
      if (!rid && !uid) continue;
      const mine = rid ? apps.filter((a) => a.recruiter_id === rid) : [];
      const day = countMoves(mine, dayFrom, to);
      const month = countMoves(mine, monthFrom, to);
      addOwnerCounts(day, reqs, clients, uid, email, dayFrom, to);
      addOwnerCounts(month, reqs, clients, uid, email, monthFrom, to);
      // The latest thing they did today, of any kind.
      let last = -Infinity;
      for (const a of mine) for (const h of a.status_history ?? []) {
        const at = utc(h.changed_at);
        if (at >= dayFrom && at < to && at > last) last = at;
      }
      for (const r of reqs) if (uid && r.account_manager_id === uid) last = Math.max(last, utc(r.created_date) >= dayFrom ? utc(r.created_date) : -Infinity);
      for (const c of clients) if ((c.created_by ?? "").toLowerCase() === email) last = Math.max(last, utc(c.created_date) >= dayFrom ? utc(c.created_date) : -Infinity);
      out.set(email, { day, month, lastToday: Number.isFinite(last) ? new Date(last).toISOString() : null });
    }
    return out;
  }

  /** Every application, requirement and client, read at most every six hours. */
  private async all(): Promise<NonNullable<Base44TalentFlow["everything"]>> {
    if (!this.everything || this.now().getTime() - this.everything.at > 6 * 3_600_000) {
      const app = await this.app();
      const apps = await this.b44.list<FullApplication>(app, "CandidateApplication", {
        fields: ["recruiter_id", "requirement_id", "client_id", "status", "status_history"],
        max: 50_000,
      });
      const reqs = await this.b44.list<RequirementRow>(app, "Requirement", {
        fields: ["account_manager_id", "client_id", "job_title", "required_skills", "positions_count", "status", "created_date"],
        max: 50_000,
      });
      const clients = await this.b44.list<ClientRow>(app, "Client", {
        fields: ["client_name", "account_name", "status", "created_by", "created_date"],
        max: 50_000,
      });
      this.everything = { at: this.now().getTime(), apps, reqs, clients };
    }
    return this.everything;
  }

  async overview(): Promise<string> {
    const app = await this.app();
    const recruiters = await this.b44.list<{ id: string; full_name?: string; email?: string; status?: string }>(app, "Recruiter", {
      fields: ["full_name", "email", "status"],
    });
    const users = await this.b44.users<{ id: string; full_name?: string; email?: string; app_role?: string; is_disabled?: boolean }>(app);
    const people = new Map<string, HistoryPerson>();
    for (const u of users) {
      const email = (u.email ?? "").trim().toLowerCase();
      if (!email || !u.app_role || !/Recruit|Account/.test(u.app_role)) continue;
      people.set(email, { name: u.full_name || email, email, role: u.app_role, userId: u.id, active: !u.is_disabled });
    }
    for (const r of recruiters) {
      const email = (r.email ?? "").trim().toLowerCase();
      if (!email) continue;
      const known = people.get(email);
      if (known) {
        known.recruiterId = r.id;
        known.name = r.full_name || known.name;
        known.active = known.active && r.status !== "Inactive";
      } else {
        people.set(email, { name: r.full_name || email, email, role: "Recruiter", recruiterId: r.id, active: r.status !== "Inactive" });
      }
    }
    return describeOverview(await this.all(), [...people.values()], this.now());
  }

  async trackRecord(email: string): Promise<string | null> {
    const key = email.trim().toLowerCase();
    const rid = await this.recruiterId(key);
    const uid = await this.userId(key);
    if (!rid && !uid) return null;
    return describeTrackRecord(await this.all(), rid, uid, key, this.now());
  }

  async lastUpdate(email: string): Promise<string | null> {
    const id = await this.recruiterId(email);
    if (!id) return null;
    const rows = await this.b44.list<{ updated_date?: string }>(await this.app(), "CandidateApplication", {
      q: { recruiter_id: id },
      fields: ["updated_date"],
      sort: "-updated_date",
      max: 1,
    });
    const at = rows[0]?.updated_date;
    if (!at) return null;
    // Stored in UTC without a zone.
    const iso = /[zZ]|[+-]\d\d:?\d\d$/.test(at) ? at : `${at}Z`;
    return Number.isNaN(Date.parse(iso)) ? null : new Date(iso).toISOString();
  }

  async targets(email: string, month: string): Promise<Record<string, number> | null> {
    const rows = await this.b44.list<Record<string, unknown>>(await this.app(), "TeamMemberTarget", {
      q: { user_email: email.trim().toLowerCase(), month_year: month },
      max: 1,
    });
    const row = rows[0];
    if (!row) return null;
    const out: Record<string, number> = {};
    for (const [field, bucket] of Object.entries(TARGET_FIELDS)) {
      const n = row[field];
      if (typeof n === "number" && n > 0) out[bucket] = n;
    }
    return Object.keys(out).length ? out : null;
  }
}

/** Moves into each counted status with changed_at in [from, to). */
export function countMoves(rows: Application[], from: number, to: number): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const row of rows) {
    for (const h of row.status_history ?? []) {
      const bucket = STATUS_BUCKET[h.status ?? ""];
      const at = Date.parse(h.changed_at ?? "");
      if (!bucket || !(at >= from && at < to)) continue;
      counts[bucket] = (counts[bucket] ?? 0) + 1;
    }
  }
  return counts;
}

const ORDER = Object.keys(BUCKET_NAMES);
const rank = (bucket: string) => (ORDER.includes(bucket) ? ORDER.indexOf(bucket) : ORDER.length);

/** "3 submitted to client, 1 L1 select", or "" when there is nothing. */
export function describeCounts(counts: Record<string, number>): string {
  return Object.entries(counts)
    .filter(([, n]) => n > 0)
    .sort(([a], [b]) => rank(a) - rank(b))
    .map(([bucket, n]) => `${n} ${BUCKET_NAMES[bucket] ?? bucket.replace(/_/g, " ")}`)
    .join(", ");
}

/** "submitted to client 4 of 10, joined 0 of 1": progress against each target. */
export function describeProgress(done: Record<string, number>, targets: Record<string, number>): string {
  return Object.entries(targets)
    .sort(([a], [b]) => rank(a) - rank(b))
    .map(([bucket, target]) => `${BUCKET_NAMES[bucket] ?? bucket} ${done[bucket] ?? 0} of ${target}`)
    .join(", ");
}
