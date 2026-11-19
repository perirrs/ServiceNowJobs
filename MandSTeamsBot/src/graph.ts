/**
 * Sends and reads Teams chat messages as a signed-in person (Microsoft Graph,
 * delegated permissions), so check-ins arrive from that person's own account.
 *
 * The sign-in token is cached in DATA_DIR/graph-token.json. Anyone who can read
 * that file can read and send that person's Teams chats, so protect it.
 */
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { PublicClientApplication } from "@azure/msal-node";
import type { ICachePlugin } from "@azure/msal-node";

const GRAPH = "https://graph.microsoft.com/v1.0";
export const GRAPH_SCOPES = ["Chat.ReadWrite", "User.ReadBasic.All"];

export interface ChatLine {
  id: string;
  createdAt: string;
  fromUserId: string;
  fromName: string;
  text: string;
}

export function textToHtml(text: string): string {
  return text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/\n/g, "<br>");
}

export function htmlToText(html: string): string {
  return html
    .replace(/<br\s*\/?>/gi, "\n")
    .replace(/<\/(p|div|li)>/gi, "\n")
    .replace(/<[^>]+>/g, "")
    .replace(/&nbsp;/g, " ")
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'")
    .replace(/&amp;/g, "&")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

function tokenCache(dataDir: string): ICachePlugin {
  const file = path.join(dataDir, "graph-token.json");
  return {
    beforeCacheAccess: async (ctx) => {
      if (existsSync(file)) ctx.tokenCache.deserialize(readFileSync(file, "utf8"));
    },
    afterCacheAccess: async (ctx) => {
      if (!ctx.cacheHasChanged) return;
      mkdirSync(dataDir, { recursive: true });
      writeFileSync(file, ctx.tokenCache.serialize(), { mode: 0o600 });
    },
  };
}

class GraphError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

type GraphUser = { id: string; displayName: string; mail?: string | null; userPrincipalName: string };
const USER_FIELDS = "$select=id,displayName,mail,userPrincipalName";

export class GraphChat {
  private pca: PublicClientApplication;
  private me?: { id: string; email: string; name: string };

  constructor(clientId: string, tenantId: string, dataDir: string) {
    this.pca = new PublicClientApplication({
      auth: { clientId, authority: `https://login.microsoftonline.com/${tenantId}` },
      cache: { cachePlugin: tokenCache(dataDir) },
    });
  }

  /** One-time interactive sign-in. Prints a code to enter at microsoft.com/devicelogin. */
  async signIn(show: (message: string) => void): Promise<string> {
    const result = await this.pca.acquireTokenByDeviceCode({
      scopes: GRAPH_SCOPES,
      deviceCodeCallback: (info) => show(info.message),
    });
    if (!result?.account) throw new Error("Sign-in did not complete");
    return result.account.username;
  }

  private async token(): Promise<string> {
    const [account] = await this.pca.getTokenCache().getAllAccounts();
    if (!account) throw new Error("Nobody is signed in for sending. Run: npm run signin");
    const result = await this.pca.acquireTokenSilent({ account, scopes: GRAPH_SCOPES });
    return result.accessToken;
  }

  private async call<T>(method: string, url: string, body?: unknown): Promise<T> {
    let res: Response;
    for (let attempt = 1; ; attempt++) {
      res = await fetch(url.startsWith("http") ? url : `${GRAPH}${url}`, {
        method,
        headers: {
          Authorization: `Bearer ${await this.token()}`,
          ...(body ? { "Content-Type": "application/json" } : {}),
        },
        body: body ? JSON.stringify(body) : undefined,
      });
      // Graph throttles chat sends; it says how long to wait in Retry-After.
      if (![429, 503, 504].includes(res.status) || attempt >= 4) break;
      const wait = Math.min(Number(res.headers.get("retry-after")) || 5 * attempt, 60);
      console.warn(`Graph asked us to slow down (${res.status}); retrying in ${wait}s`);
      await new Promise((r) => setTimeout(r, wait * 1000));
    }
    if (!res.ok) {
      throw new GraphError(
        `Graph ${method} ${url.split("?")[0]} failed: ${res.status} ${(await res.text()).slice(0, 300)}`,
        res.status,
      );
    }
    return (await res.json()) as T;
  }

  /** The signed-in person the messages are sent as. */
  async whoAmI(): Promise<{ id: string; email: string; name: string }> {
    if (!this.me) {
      const r = await this.call<{ id: string; mail?: string; userPrincipalName: string; displayName: string }>(
        "GET",
        "/me?$select=id,mail,userPrincipalName,displayName",
      );
      this.me = { id: r.id, email: (r.mail ?? r.userPrincipalName).toLowerCase(), name: r.displayName };
    }
    return this.me;
  }

  /** Look a colleague up by sign-in name first, then by mail address. */
  private async findUser(email: string): Promise<GraphUser | undefined> {
    try {
      return await this.call<GraphUser>("GET", `/users/${encodeURIComponent(email)}?${USER_FIELDS}`);
    } catch (err) {
      if (!(err instanceof GraphError) || err.status !== 404) throw err;
    }
    const quoted = email.replace(/'/g, "''");
    const byMail = await this.call<{ value: GraphUser[] }>(
      "GET",
      `/users?$filter=${encodeURIComponent(`mail eq '${quoted}'`)}&${USER_FIELDS}`,
    );
    return byMail.value[0];
  }

  /** People whose name or address starts like the one that was not found. */
  private async suggest(email: string): Promise<string[]> {
    const stem = email.split("@")[0]!.replace(/'/g, "''").slice(0, 5);
    if (stem.length < 3) return [];
    const filter = `startswith(displayName,'${stem}') or startswith(mail,'${stem}') or startswith(userPrincipalName,'${stem}')`;
    const r = await this.call<{ value: GraphUser[] }>(
      "GET",
      `/users?$filter=${encodeURIComponent(filter)}&$top=5&${USER_FIELDS}`,
    );
    return r.value.map((u) => `${u.displayName} (${(u.mail ?? u.userPrincipalName).toLowerCase()})`);
  }

  /**
   * Find (or create) the one-to-one chat with a colleague. If nobody has that
   * address, returns likely matches instead.
   */
  async connect(
    email: string,
  ): Promise<{ conversationId: string; name: string } | { notFound: true; suggestions: string[] }> {
    const me = await this.whoAmI();
    const user = await this.findUser(email);
    if (!user) return { notFound: true, suggestions: await this.suggest(email).catch(() => []) };
    const member = (id: string) => ({
      "@odata.type": "#microsoft.graph.aadUserConversationMember",
      roles: ["owner"],
      "user@odata.bind": `${GRAPH}/users('${id}')`,
    });
    // Graph returns the existing chat if these two people already have one.
    const chat = await this.call<{ id: string }>("POST", "/chats", {
      chatType: "oneOnOne",
      members: [member(me.id), member(user.id)],
    });
    return { conversationId: chat.id, name: user.displayName };
  }

  /** Returns the id of the sent message. */
  async send(chatId: string, text: string, mentions: { userId: string; name: string }[] = []): Promise<string> {
    // @mentions go in front of the text, each tied to the person's directory id.
    const tags = mentions.map((m, i) => `<at id="${i}">${textToHtml(m.name)}</at>`).join(" ");
    const msg = await this.call<{ id: string }>("POST", `/chats/${encodeURIComponent(chatId)}/messages`, {
      body: { contentType: "html", content: tags ? `${tags} ${textToHtml(text)}` : textToHtml(text) },
      ...(mentions.length
        ? {
            mentions: mentions.map((m, i) => ({
              id: i,
              mentionText: m.name,
              mentioned: { user: { id: m.userId, displayName: m.name, userIdentityType: "aadUser" } },
            })),
          }
        : {}),
    });
    return msg.id;
  }

  /** The signed-in person's named group chats whose name contains `query`. */
  async findGroups(query: string): Promise<{ chatId: string; name: string }[]> {
    type Page = { value: { id: string; topic?: string | null }[]; "@odata.nextLink"?: string };
    const found: { chatId: string; name: string }[] = [];
    const q = query.trim().toLowerCase();
    let url: string | undefined = `/me/chats?$filter=${encodeURIComponent("chatType eq 'group'")}&$top=50`;
    for (let page = 0; url && page < 6; page++) {
      const r: Page = await this.call<Page>("GET", url);
      for (const chat of r.value) {
        if (chat.topic && chat.topic.toLowerCase().includes(q)) found.push({ chatId: chat.id, name: chat.topic });
      }
      url = r["@odata.nextLink"];
    }
    return found;
  }

  /** Everyone in a group chat except the signed-in person. People without an email (guests) are left out. */
  async groupMembers(chatId: string): Promise<{ userId: string; email: string; name: string }[]> {
    const me = await this.whoAmI();
    const r = await this.call<{ value: { userId?: string; email?: string | null; displayName?: string }[] }>(
      "GET",
      `/chats/${encodeURIComponent(chatId)}/members`,
    );
    return r.value
      .filter((m) => m.userId && m.email && m.userId !== me.id)
      .map((m) => ({ userId: m.userId!, email: m.email!.toLowerCase(), name: m.displayName ?? m.email! }));
  }

  /**
   * The most recent messages in a chat (up to `max`), oldest first. With
   * `since`, only messages after that time.
   */
  async history(chatId: string, max = 500, since = ""): Promise<ChatLine[]> {
    type Page = {
      value: {
        id: string;
        messageType: string;
        createdDateTime: string;
        deletedDateTime?: string | null;
        from?: { user?: { id?: string; displayName?: string } | null } | null;
        body?: { content?: string };
      }[];
      "@odata.nextLink"?: string;
    };
    const lines: ChatLine[] = [];
    let url: string | undefined = `/chats/${encodeURIComponent(chatId)}/messages?$top=50`;
    let reachedOlder = false;
    while (url && lines.length < max && !reachedOlder) {
      const page: Page = await this.call<Page>("GET", url);
      for (const m of page.value) {
        // Pages run newest first, so once we pass `since` the rest is older still.
        if (since && m.createdDateTime <= since) {
          reachedOlder = true;
          break;
        }
        const text = htmlToText(m.body?.content ?? "");
        if (m.messageType !== "message" || m.deletedDateTime || !m.from?.user?.id || !text) continue;
        lines.push({
          id: m.id,
          createdAt: m.createdDateTime,
          fromUserId: m.from.user.id,
          fromName: m.from.user.displayName ?? "",
          text,
        });
      }
      url = page["@odata.nextLink"];
    }
    return lines.slice(0, max).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }

  /** Messages in a chat created after `since`, oldest first. */
  async messagesSince(chatId: string, since: string): Promise<ChatLine[]> {
    const r = await this.call<{
      value: {
        id: string;
        messageType: string;
        createdDateTime: string;
        deletedDateTime?: string | null;
        from?: { user?: { id?: string } | null } | null;
        body?: { content?: string };
      }[];
    }>("GET", `/chats/${encodeURIComponent(chatId)}/messages?$top=30`);
    return r.value
      .filter((m) => m.messageType === "message" && !m.deletedDateTime && m.createdDateTime > since && m.from?.user?.id)
      .map((m) => ({
        id: m.id,
        createdAt: m.createdDateTime,
        fromUserId: m.from!.user!.id!,
        fromName: "",
        text: htmlToText(m.body?.content ?? ""),
      }))
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }
}
