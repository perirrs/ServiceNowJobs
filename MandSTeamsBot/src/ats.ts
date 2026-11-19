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

/** TalentFlow status -> the figure it counts towards. Statuses not listed are not counted. */
const STATUS_BUCKET: Record<string, string> = {
  Applied: "added",
  "Submitted to Client": "submitted_to_client",
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
  Onboarded: "onboarded",
  "Candidate Backed Out": "backed_out",
};

/** Plain names for the figures, in the order they are shown. */
const BUCKET_NAMES: Record<string, string> = {
  added: "new candidates added",
  submitted_to_client: "submitted to client",
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
  onboarded: "joined",
  backed_out: "backed out",
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
}

interface Application {
  status_history?: { status?: string; changed_at?: string }[];
}

export class Base44TalentFlow implements Ats {
  private recruiters?: { at: number; byEmail: Map<string, string> };
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

  async stats(email: string, fromIso: string, toIso: string): Promise<Record<string, number> | null> {
    const id = await this.recruiterId(email);
    if (!id) return null;
    const from = Date.parse(fromIso);
    const to = Date.parse(toIso);
    // Only applications touched since the window opened can have moved inside it.
    // TalentFlow stores updated_date in UTC without a zone, so compare without one.
    const rows = await this.b44.list<Application>(await this.app(), "CandidateApplication", {
      q: { recruiter_id: id, updated_date: { $gte: new Date(from).toISOString().slice(0, 19) } },
      fields: ["status_history"],
    });
    return countMoves(rows, from, to);
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
