/**
 * A person's whole history in TalentFlow, as short plain lines: their funnel
 * and conversion rates, who they closed for, which roles and skills they
 * succeed with, the monthly trend, and (for account owners and managers) the
 * requirements, spots and SPOCs they brought in and how those filled.
 */

export interface HistoryApplication {
  recruiter_id?: string;
  requirement_id?: string;
  client_id?: string;
  status?: string;
  status_history?: { status?: string; changed_at?: string }[];
}

export interface HistoryRequirement {
  id: string;
  account_manager_id?: string;
  client_id?: string;
  job_title?: string;
  required_skills?: unknown;
  positions_count?: number;
  status?: string;
  created_date?: string;
}

export interface HistoryClient {
  id: string;
  client_name?: string;
  account_name?: string;
  status?: string;
  created_by?: string;
  created_date?: string;
}

/** TalentFlow stores times in UTC without a zone. */
export function utc(at: string | undefined): number {
  if (!at) return NaN;
  return Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(at) ? at : `${at}Z`);
}

const AFTER_SUBMISSION = new Set([
  "Submitted to Client", "Screen Reject - Client", "Client Duplicate",
  "L1 Interview Scheduled", "L1 completed and Feedback Pending", "L1 Interview", "L1 Select", "L1 Reject",
  "L2 Interview Scheduled", "L2 completed and Feedback Pending", "L2 Interview", "L2 Select", "L2 Reject",
  "L3 Interview Scheduled", "L3 completed and Feedback Pending", "L3 Interview", "L3 Select", "L3 Reject",
  "Final Select", "Offered", "Candidate Backed Out", "Onboarded", "Client Withdrawn", "Client Hold",
]);
const L1_SELECTED = new Set([
  "L1 Select", "L2 Interview Scheduled", "L2 completed and Feedback Pending", "L2 Interview", "L2 Select", "L2 Reject",
  "L3 Interview Scheduled", "L3 completed and Feedback Pending", "L3 Interview", "L3 Select", "L3 Reject",
  "Final Select", "Offered", "Onboarded",
]);
const L1_REACHED = new Set(["L1 Interview Scheduled", "L1 completed and Feedback Pending", "L1 Interview", "L1 Reject", ...L1_SELECTED]);

interface Funnel {
  submitted: number;
  l1: number;
  l1Select: number;
  final: number;
  joined: number;
  backedOut: number;
}

const emptyFunnel = (): Funnel => ({ submitted: 0, l1: 0, l1Select: 0, final: 0, joined: 0, backedOut: 0 });

function addToFunnel(f: Funnel, statuses: Set<string>): void {
  const has = (set: Set<string>) => [...statuses].some((s) => set.has(s));
  if (!has(AFTER_SUBMISSION)) return;
  f.submitted++;
  if (has(L1_REACHED)) f.l1++;
  if (has(L1_SELECTED)) f.l1Select++;
  if (statuses.has("Final Select") || statuses.has("Offered") || statuses.has("Onboarded")) f.final++;
  if (statuses.has("Onboarded")) f.joined++;
  if (statuses.has("Candidate Backed Out")) f.backedOut++;
}

const pct = (n: number, of: number) => (of ? `${Math.round((n / of) * 100)}%` : "-");

function topFunnels(map: Map<string, Funnel>, by: (f: Funnel) => number, n: number, min = 1): string {
  return [...map]
    .filter(([, f]) => by(f) >= min)
    .sort(([, a], [, b]) => by(b) - by(a))
    .slice(0, n)
    .map(([k, f]) => `${k} (${f.submitted} sub, ${f.l1Select} L1 sel ${pct(f.l1Select, f.submitted)}, ${f.joined} joined)`)
    .join("; ");
}

const statusesOf = (a: HistoryApplication) =>
  new Set<string>([...(a.status_history ?? []).map((h) => h.status ?? ""), a.status ?? ""].filter(Boolean));

export function describeTrackRecord(
  data: { apps: HistoryApplication[]; reqs: HistoryRequirement[]; clients: HistoryClient[] },
  recruiterId: string | undefined,
  userId: string | undefined,
  email: string,
  now: Date,
): string {
  // One spelling per client, whatever the case or spacing it was typed with.
  const spelling = new Map<string, string>();
  const clientName = new Map(
    data.clients.map((c) => {
      const name = (c.client_name || c.account_name || "").trim().replace(/\s+/g, " ");
      const key = name.toLowerCase();
      if (name && !spelling.has(key)) spelling.set(key, name);
      return [c.id, name ? spelling.get(key)! : "unknown client"] as const;
    }),
  );
  const reqById = new Map(data.reqs.map((r) => [r.id, r]));
  const month = (ms: number) => (Number.isFinite(ms) ? new Date(ms).toISOString().slice(0, 7) : "");
  const months: string[] = [];
  for (let i = 5; i >= 0; i--) {
    months.push(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - i, 1)).toISOString().slice(0, 7));
  }
  const lines: string[] = [];

  if (recruiterId) {
    const mine = data.apps.filter((a) => a.recruiter_id === recruiterId);
    const all = emptyFunnel();
    const byClient = new Map<string, Funnel>();
    const byRole = new Map<string, Funnel>();
    const bySkill = new Map<string, Funnel>();
    const byMonth = new Map<string, Funnel>();
    let first = Infinity;
    let last = -Infinity;
    const toL1Days: number[] = [];
    for (const a of mine) {
      const hist = a.status_history ?? [];
      const statuses = statusesOf(a);
      addToFunnel(all, statuses);
      for (const h of hist) {
        const at = Date.parse(h.changed_at ?? "");
        if (Number.isFinite(at)) {
          first = Math.min(first, at);
          last = Math.max(last, at);
        }
      }
      if (![...statuses].some((s) => AFTER_SUBMISSION.has(s))) continue;
      const submittedAt = Date.parse(hist.find((h) => AFTER_SUBMISSION.has(h.status ?? ""))?.changed_at ?? "");
      const selectedAt = Date.parse(hist.find((h) => L1_SELECTED.has(h.status ?? ""))?.changed_at ?? "");
      if (Number.isFinite(submittedAt) && Number.isFinite(selectedAt) && selectedAt >= submittedAt) {
        toL1Days.push((selectedAt - submittedAt) / 86_400_000);
      }
      const put = (map: Map<string, Funnel>, key: string) => {
        if (!key) return;
        const f = map.get(key) ?? emptyFunnel();
        addToFunnel(f, statuses);
        map.set(key, f);
      };
      const req = reqById.get(a.requirement_id ?? "");
      put(byClient, clientName.get(a.client_id ?? "") ?? clientName.get(req?.client_id ?? "") ?? "");
      put(byRole, (req?.job_title ?? "").trim());
      const skills = Array.isArray(req?.required_skills) ? (req!.required_skills as unknown[]) : [];
      for (const s of new Set(skills.map((x) => String(x).trim()).filter(Boolean))) put(bySkill, s);
      if (Number.isFinite(submittedAt)) put(byMonth, month(submittedAt));
    }
    if (mine.length) {
      lines.push(
        `Candidates worked: ${mine.length}. Submitted to client: ${all.submitted}. Reached L1: ${all.l1}. L1 selects: ${all.l1Select} (${pct(all.l1Select, all.submitted)} of submissions). Final select or offered: ${all.final}. Joined: ${all.joined} (${pct(all.joined, all.submitted)}). Backed out: ${all.backedOut}.`,
      );
      if (toL1Days.length) {
        const sorted = toL1Days.sort((x, y) => x - y);
        lines.push(`Typical time from submission to L1 select: ${Math.round(sorted[Math.floor(sorted.length / 2)]!)} days.`);
      }
      const trend = months
        .map((m) => {
          const f = byMonth.get(m) ?? emptyFunnel();
          return `${m}: ${f.submitted}/${f.l1Select}/${f.joined}`;
        })
        .join(", ");
      lines.push(`Last 6 months by month of submission (submitted / L1 select / joined): ${trend}.`);
      const closed = topFunnels(byClient, (f) => f.joined, 6);
      if (closed) lines.push(`Closed (joined) for: ${closed}.`);
      const clientsTop = topFunnels(byClient, (f) => f.submitted, 5);
      if (clientsTop) lines.push(`Most submissions to: ${clientsTop}.`);
      const roles = topFunnels(byRole, (f) => f.submitted, 5, 3);
      if (roles) lines.push(`Roles worked most: ${roles}.`);
      const rated = [...bySkill]
        .filter(([, f]) => f.submitted >= 5)
        .sort(([, a], [, b]) => b.l1Select / b.submitted - a.l1Select / a.submitted);
      if (rated.length) {
        const show = (list: [string, Funnel][]) => list.map(([k, f]) => `${k} ${pct(f.l1Select, f.submitted)} of ${f.submitted}`).join(", ");
        lines.push(`Skills with the best L1 select rate: ${show(rated.slice(0, 5))}.`);
        if (rated.length > 5) lines.push(`Skills with the weakest L1 select rate: ${show(rated.slice(-3).reverse())}.`);
      }
      if (Number.isFinite(first)) {
        lines.push(`Active in TalentFlow from ${new Date(first).toISOString().slice(0, 10)} to ${new Date(last).toISOString().slice(0, 10)}.`);
      }
    }
  }

  if (userId) {
    const owned = data.reqs.filter((r) => r.account_manager_id === userId);
    if (owned.length) {
      const spots = owned.reduce((n, r) => n + Math.max(1, Number(r.positions_count) || 1), 0);
      const perMonth = months.map((m) => `${m}: ${owned.filter((r) => month(utc(r.created_date)) === m).length}`).join(", ");
      const ownedIds = new Set(owned.map((r) => r.id));
      const onOwned = emptyFunnel();
      const byClient = new Map<string, Funnel>();
      const reqsByClient = new Map<string, number>();
      for (const r of owned) {
        const c = clientName.get(r.client_id ?? "") ?? "unknown client";
        reqsByClient.set(c, (reqsByClient.get(c) ?? 0) + 1);
      }
      for (const a of data.apps) {
        if (!ownedIds.has(a.requirement_id ?? "")) continue;
        const statuses = statusesOf(a);
        addToFunnel(onOwned, statuses);
        const c = clientName.get(reqById.get(a.requirement_id ?? "")?.client_id ?? "") ?? "unknown client";
        const f = byClient.get(c) ?? emptyFunnel();
        addToFunnel(f, statuses);
        byClient.set(c, f);
      }
      const open = owned.filter((r) => r.status === "Open" || r.status === "In Progress").length;
      lines.push(`Requirements brought in: ${owned.length} (${spots} spots), ${open} still open or in progress. Per month, last 6: ${perMonth}.`);
      lines.push(
        `On their requirements: ${onOwned.submitted} submissions, ${onOwned.l1Select} L1 selects, ${onOwned.joined} joined (${pct(onOwned.joined, spots)} of spots filled).`,
      );
      const top = [...reqsByClient]
        .sort(([, a], [, b]) => b - a)
        .slice(0, 6)
        .map(([c, n]) => {
          const f = byClient.get(c) ?? emptyFunnel();
          return `${c} ${n} reqs, ${f.submitted} sub, ${f.joined} joined`;
        })
        .join("; ");
      lines.push(`Requirements by client: ${top}.`);
    }
    const spocs = data.clients.filter((c) => (c.created_by ?? "").trim().toLowerCase() === email);
    if (spocs.length) {
      const perMonth = months.map((m) => `${m}: ${spocs.filter((c) => month(utc(c.created_date)) === m).length}`).join(", ");
      lines.push(`Client SPOCs added: ${spocs.length}. Per month, last 6: ${perMonth}.`);
    }
  }
  return lines.length ? lines.join("\n") : "No work recorded in TalentFlow.";
}
