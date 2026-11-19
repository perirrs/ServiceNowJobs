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
    const res = await fetch(url.startsWith("http") ? url : `${GRAPH}${url}`, {
      method,
      headers: {
        Authorization: `Bearer ${await this.token()}`,
        ...(body ? { "Content-Type": "application/json" } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) {
      throw new Error(`Graph ${method} ${url.split("?")[0]} failed: ${res.status} ${(await res.text()).slice(0, 300)}`);
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

  /** Find (or create) the one-to-one chat with a colleague. */
  async connect(email: string): Promise<{ conversationId: string; name: string }> {
    const me = await this.whoAmI();
    const user = await this.call<{ id: string; displayName: string }>(
      "GET",
      `/users/${encodeURIComponent(email)}?$select=id,displayName`,
    );
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
  async send(chatId: string, text: string): Promise<string> {
    const msg = await this.call<{ id: string }>("POST", `/chats/${encodeURIComponent(chatId)}/messages`, {
      body: { contentType: "html", content: textToHtml(text) },
    });
    return msg.id;
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
        text: htmlToText(m.body?.content ?? ""),
      }))
      .sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }
}
