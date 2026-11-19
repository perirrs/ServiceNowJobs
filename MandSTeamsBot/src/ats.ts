/**
 * Reads a recruiter's real figures from TalentFlow, so the check-in can take
 * them as given instead of asking for them.
 *
 * Expects GET {ATS_URL}?email=&from=&to= (ISO date-times, UTC) with a bearer
 * token, answering { counts: { submitted_to_client: 3, l1_interview: 1, ... } }:
 * TalentFlow's metric buckets, counted for submissions owned by that recruiter.
 */

/** Plain names for TalentFlow's metric buckets. */
const BUCKET_NAMES: Record<string, string> = {
  submitted_to_client: "submitted to client",
  l1_interview: "L1 interview",
  l2_interview: "L2 interview",
  l3_interview: "L3 interview",
  final_select: "final select",
  offered: "offered",
  onboarded: "joined",
};

export interface Ats {
  stats(email: string, fromIso: string, toIso: string): Promise<Record<string, number>>;
}

export class TalentFlowAts implements Ats {
  constructor(
    private url: string,
    private token: string,
  ) {}

  async stats(email: string, fromIso: string, toIso: string): Promise<Record<string, number>> {
    const query = new URLSearchParams({ email, from: fromIso, to: toIso });
    const res = await fetch(`${this.url}?${query}`, {
      headers: { Authorization: `Bearer ${this.token}`, Accept: "application/json" },
      signal: AbortSignal.timeout(15_000),
    });
    if (res.status === 404) return {}; // not a TalentFlow user: nothing to report
    if (!res.ok) throw new Error(`TalentFlow answered ${res.status}`);
    const body = (await res.json()) as { counts?: Record<string, unknown> };
    const counts: Record<string, number> = {};
    for (const [bucket, n] of Object.entries(body.counts ?? {})) {
      if (typeof n === "number" && n > 0) counts[bucket] = n;
    }
    return counts;
  }
}

/** "3 submitted to client, 1 L1 interview", or "" when there is nothing. */
export function describeCounts(counts: Record<string, number>): string {
  return Object.entries(counts)
    .filter(([, n]) => n > 0)
    .map(([bucket, n]) => `${n} ${BUCKET_NAMES[bucket] ?? bucket.replace(/_/g, " ")}`)
    .join(", ");
}
