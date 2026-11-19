/**
 * A read-only client for Base44's Apps API. One key reaches every Base44 app
 * the user holds (TalentFlow is one of them), so each portal is named by its
 * app (id, slug or name) and shares the key.
 *
 * https://docs.base44.com/api-reference/list-entity-records
 */

const BASE = "https://app.base44.com/api";

export interface Base44App {
  id: string;
  name: string;
  slug: string | null;
}

export interface EntitySchema {
  entity_name: string;
  entity_schema: { properties?: Record<string, { type?: string; enum?: unknown[]; format?: string; description?: string }> };
}

export type Base44Query = Record<string, unknown>;

/** Base44 allows 70 list and count calls a minute per app; stay under that. */
const PER_MINUTE = 60;

export class Base44 {
  private calls: number[] = [];

  constructor(
    private key: string,
    private sleep: (ms: number) => Promise<void> = (ms) => new Promise((r) => setTimeout(r, ms)),
  ) {}

  /** Wait until a call fits in the last minute's allowance. */
  private async slot(): Promise<void> {
    for (;;) {
      const now = Date.now();
      this.calls = this.calls.filter((t) => now - t < 60_000);
      if (this.calls.length < PER_MINUTE) {
        this.calls.push(now);
        return;
      }
      await this.sleep(60_000 - (now - this.calls[0]!) + 50);
    }
  }

  private async get<T>(path: string, params: Record<string, string> = {}): Promise<T> {
    const query = new URLSearchParams(params).toString();
    let res: Response;
    for (let attempt = 0; ; attempt++) {
      await this.slot();
      res = await fetch(`${BASE}${path}${query ? `?${query}` : ""}`, {
        headers: { Authorization: `Bearer ${this.key}`, Accept: "application/json" },
        signal: AbortSignal.timeout(20_000),
      });
      if ((res.status !== 429 && res.status !== 503) || attempt >= 1) break;
      // Too many calls, or the database is busy: wait as asked (at most 30 seconds), once.
      const wait = Math.min(30, Number(res.headers.get("retry-after")) || 5);
      await this.sleep(wait * 1000);
    }
    if (!res.ok) {
      const body = await res.text().catch(() => "");
      throw new Error(`Base44 answered ${res.status} for ${path}: ${body.slice(0, 200)}`);
    }
    return (await res.json()) as T;
  }

  apps(): Promise<Base44App[]> {
    return this.get<Base44App[]>("/apps", { fields: "id,name,slug" });
  }

  /** The app whose id, slug or name is `ref` ("mats", "mats.base44.com" and the id all work). */
  async findApp(ref: string): Promise<Base44App | undefined> {
    const want = ref.trim().toLowerCase().replace(/^https?:\/\//, "").replace(/\.base44\.(com|app)\/?$/, "");
    const apps = await this.apps();
    return (
      apps.find((a) => a.id.toLowerCase() === want) ??
      apps.find((a) => (a.slug ?? "").toLowerCase() === want) ??
      apps.find((a) => a.name.toLowerCase() === want)
    );
  }

  async schemas(appId: string): Promise<EntitySchema[]> {
    return (await this.get<{ schemas: EntitySchema[] }>(`/apps/${appId}/entity-schemas`)).schemas;
  }

  async count(appId: string, entity: string, q?: Base44Query): Promise<number> {
    const params: Record<string, string> = q ? { q: JSON.stringify(q) } : {};
    return (await this.get<{ count: number }>(`/apps/${appId}/entities/${entity}/count`, params)).count;
  }

  /** Every matching record, following the cursor, up to `max`. */
  async list<T = Record<string, unknown>>(
    appId: string,
    entity: string,
    opts: { q?: Base44Query; fields?: string[]; max?: number; sort?: string } = {},
  ): Promise<T[]> {
    const out: T[] = [];
    const max = opts.max ?? 5000;
    let cursor: string | null = null;
    do {
      const params: Record<string, string> = { limit: String(Math.min(1000, max - out.length)) };
      if (cursor) params.cursor = cursor;
      else {
        if (opts.q) params.q = JSON.stringify(opts.q);
        if (opts.fields?.length) params.fields = opts.fields.join(",");
        if (opts.sort) params.sort = opts.sort;
      }
      const page: { items: T[]; next_cursor: string | null; has_more: boolean } = await this.get(
        `/apps/${appId}/entities/${entity}/v2/list`,
        params,
      );
      out.push(...page.items);
      cursor = page.has_more ? page.next_cursor : null;
    } while (cursor && out.length < max);
    return out;
  }
}
