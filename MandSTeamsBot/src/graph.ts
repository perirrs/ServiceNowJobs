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
  /** Directory ids of the people this message tags or replies to. */
  addressedUserIds: string[];
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
    .replace(/&apos;/g, "'")
    .replace(/&#(\d+);/g, (_, n: string) => String.fromCodePoint(Number(n)))
    .replace(/&#x([0-9a-f]+);/gi, (_, n: string) => String.fromCodePoint(parseInt(n, 16)))
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

/** The message a reply answers, so Teams can show it quoted above the reply. */
export interface Quote {
  messageId: string;
  preview: string;
  userId: string;
  name: string;
}

/** A meeting in the sender's calendar. Times are ISO, UTC. */
export interface Meeting {
  id: string;
  subject: string;
  start: string;
  end: string;
  attendees: string[];
  chatId: string;
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

  private async token(scopes: string[] = GRAPH_SCOPES): Promise<string> {
    const [account] = await this.pca.getTokenCache().getAllAccounts();
    if (!account) throw new Error("Nobody is signed in for sending. Run: npm run signin");
    const result = await this.pca.acquireTokenSilent({ account, scopes });
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
      // A request that sends a message is only retried when Graph says it was refused for
      // rate (429). After a 503 or 504 the message may have been delivered, and a retry would send it twice.
      const retryable = method === "GET" ? [429, 503, 504] : [429];
      if (!retryable.includes(res.status) || attempt >= 4) break;
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
  async send(
    chatId: string,
    text: string,
    mentions: { userId: string; name: string }[] = [],
    quote?: Quote,
  ): Promise<string> {
    // Each @mention is tied to the person's directory id. A marker like {@0} in
    // the text says where that mention goes; with no markers they go in front.
    const tag = (i: number) => `<at id="${i}">${textToHtml(mentions[i]!.name)}</at>`;
    let content = textToHtml(text);
    if (mentions.some((_, i) => content.includes(`{@${i}}`))) {
      mentions.forEach((_, i) => (content = content.replace(`{@${i}}`, tag(i))));
    } else if (mentions.length) {
      content = `${mentions.map((_, i) => tag(i)).join(" ")} ${content}`;
    }
    const mentionList = mentions.length
      ? {
          mentions: mentions.map((m, i) => ({
            id: i,
            mentionText: m.name,
            mentioned: { user: { id: m.userId, displayName: m.name, userIdentityType: "aadUser" } },
          })),
        }
      : {};
    const post = (body: Record<string, unknown>) =>
      this.call<{ id: string }>("POST", `/chats/${encodeURIComponent(chatId)}/messages`, body);

    if (quote) {
      // A reply that shows the message it answers, as Teams' own "Reply" does.
      try {
        const msg = await post({
          body: { contentType: "html", content: `<attachment id="${quote.messageId}"></attachment>${content}` },
          attachments: [
            {
              id: quote.messageId,
              contentType: "messageReference",
              content: JSON.stringify({
                messageId: quote.messageId,
                messagePreview: quote.preview.slice(0, 200),
                messageSender: {
                  application: null,
                  device: null,
                  user: { userIdentityType: "aadUser", id: quote.userId, displayName: quote.name },
                },
              }),
            },
          ],
          ...mentionList,
        });
        return msg.id;
      } catch (err) {
        // Better an ordinary tagged message than none at all.
        if (!(err instanceof GraphError) || err.status >= 500 || err.status === 429) throw err;
        console.warn(`Teams would not accept the quoted reply (${err.status}); sending it without the quote.`);
      }
    }
    const msg = await post({ body: { contentType: "html", content }, ...mentionList });
    return msg.id;
  }

  /** Post a message whose body is already HTML (a table, for instance). Returns its id. */
  async sendHtml(chatId: string, html: string): Promise<string> {
    const msg = await this.call<{ id: string }>("POST", `/chats/${encodeURIComponent(chatId)}/messages`, {
      body: { contentType: "html", content: html },
    });
    return msg.id;
  }

  /** Calendar calls use their own permission, so the rest keeps working without it. */
  private async calendar<T>(method: string, path: string, body?: unknown): Promise<T> {
    const res = await fetch(`${GRAPH}${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${await this.token(["Calendars.Read"])}`,
        "Content-Type": "application/json",
        Prefer: 'outlook.timezone="UTC"',
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!res.ok) throw new GraphError(`Graph ${method} ${path.split("?")[0]} failed: ${res.status} ${(await res.text()).slice(0, 300)}`, res.status);
    return (await res.json()) as T;
  }

  /** When each person is out of office between two times, from their Outlook calendars, as [start, end] ISO pairs. Unknown means never. */
  async awayBetween(emails: string[], startIso: string, endIso: string): Promise<Map<string, [string, string][]>> {
    const away = new Map<string, [string, string][]>();
    for (let i = 0; i < emails.length; i += 20) {
      const r = await this.calendar<{
        value?: { scheduleId: string; scheduleItems?: { status: string; start?: { dateTime: string }; end?: { dateTime: string } }[] }[];
      }>(
        "POST",
        "/me/calendar/getSchedule",
        {
          schedules: emails.slice(i, i + 20),
          startTime: { dateTime: startIso.slice(0, 19), timeZone: "UTC" },
          endTime: { dateTime: endIso.slice(0, 19), timeZone: "UTC" },
          availabilityViewInterval: 60,
        },
      );
      for (const s of r.value ?? []) {
        away.set(
          s.scheduleId.toLowerCase(),
          (s.scheduleItems ?? [])
            .filter((x) => x.status === "oof" && x.start && x.end)
            .map((x): [string, string] => [`${x.start!.dateTime.slice(0, 19)}Z`, `${x.end!.dateTime.slice(0, 19)}Z`]),
        );
      }
    }
    return away;
  }

  /** The sender's meetings between two times, with attendees and the meeting's chat when it has one. */
  async meetings(startIso: string, endIso: string): Promise<Meeting[]> {
    const r = await this.calendar<{
      value?: {
        id: string;
        subject?: string;
        start?: { dateTime: string };
        end?: { dateTime: string };
        isCancelled?: boolean;
        attendees?: { emailAddress?: { address?: string } }[];
        onlineMeeting?: { joinUrl?: string } | null;
      }[];
    }>(
      "GET",
      `/me/calendarView?startDateTime=${encodeURIComponent(startIso)}&endDateTime=${encodeURIComponent(endIso)}&$select=id,subject,start,end,isCancelled,attendees,onlineMeeting&$top=50`,
    );
    return (r.value ?? [])
      .filter((e) => !e.isCancelled && e.start && e.end)
      .map((e) => ({
        id: e.id,
        subject: e.subject || "(no title)",
        start: `${e.start!.dateTime.slice(0, 19)}Z`,
        end: `${e.end!.dateTime.slice(0, 19)}Z`,
        attendees: (e.attendees ?? []).map((a) => (a.emailAddress?.address ?? "").toLowerCase()).filter(Boolean),
        chatId: /19:meeting_[^/?&]+@thread\.v2/.exec(decodeURIComponent(e.onlineMeeting?.joinUrl ?? ""))?.[0] ?? "",
      }));
  }

  /** Delete a message this person sent. */
  async softDelete(chatId: string, messageId: string): Promise<void> {
    const me = await this.whoAmI();
    const res = await fetch(
      `${GRAPH}/users/${me.id}/chats/${encodeURIComponent(chatId)}/messages/${encodeURIComponent(messageId)}/softDelete`,
      { method: "POST", headers: { Authorization: `Bearer ${await this.token()}` } },
    );
    if (!res.ok) {
      throw new GraphError(`Graph softDelete failed: ${res.status} ${(await res.text()).slice(0, 300)}`, res.status);
    }
  }

  /** Replace the body of a message this person sent earlier. */
  async updateHtml(chatId: string, messageId: string, html: string): Promise<void> {
    const res = await fetch(`${GRAPH}/chats/${encodeURIComponent(chatId)}/messages/${encodeURIComponent(messageId)}`, {
      method: "PATCH",
      headers: { Authorization: `Bearer ${await this.token()}`, "Content-Type": "application/json" },
      body: JSON.stringify({ body: { contentType: "html", content: html } }),
    });
    if (!res.ok) {
      throw new GraphError(`Graph PATCH message failed: ${res.status} ${(await res.text()).slice(0, 300)}`, res.status);
    }
  }

  /** Pin a message to the top of a chat. */
  async pin(chatId: string, messageId: string): Promise<void> {
    await this.call("POST", `/chats/${encodeURIComponent(chatId)}/pinnedMessages`, {
      "message@odata.bind": `${GRAPH}/chats/${chatId}/messages/${messageId}`,
    });
  }

  /**
   * The signed-in person's named group chats whose name contains `query`,
   * most recently active first. Where two chats share a name, only the most
   * recently active one is returned.
   */
  async findGroups(query: string): Promise<{ chatId: string; name: string }[]> {
    type Page = { value: { id: string; topic?: string | null; chatType?: string }[]; "@odata.nextLink"?: string };
    const found: { chatId: string; name: string }[] = [];
    const seen = new Set<string>();
    const q = query.trim().toLowerCase();
    let url: string | undefined = "/me/chats?$top=50&$orderby=lastMessagePreview/createdDateTime desc";
    for (let page = 0; url && page < 20; page++) {
      const r: Page = await this.call<Page>("GET", url);
      for (const chat of r.value) {
        const name = chat.topic?.trim();
        if (chat.chatType !== "group" || !name || !name.toLowerCase().includes(q)) continue;
        if (seen.has(name.toLowerCase())) continue;
        seen.add(name.toLowerCase());
        found.push({ chatId: chat.id, name });
      }
      url = r["@odata.nextLink"];
    }
    return found;
  }

  /**
   * Create a named group chat with the signed-in person and these colleagues.
   * Nothing is posted in it. Returns the new chat's id.
   */
  async createGroup(topic: string, emails: string[]): Promise<string> {
    const me = await this.whoAmI();
    const ids = new Set<string>([me.id]);
    for (const email of emails) {
      const user = await this.findUser(email);
      if (!user) throw new Error(`Nobody in the directory has the address ${email}`);
      ids.add(user.id);
    }
    const chat = await this.call<{ id: string }>("POST", "/chats", {
      chatType: "group",
      topic,
      members: [...ids].map((id) => ({
        "@odata.type": "#microsoft.graph.aadUserConversationMember",
        roles: ["owner"],
        "user@odata.bind": `${GRAPH}/users('${id}')`,
      })),
    });
    return chat.id;
  }

  /** A group chat's current name, as shown in Teams; null when it has none. */
  async groupName(chatId: string): Promise<string | null> {
    const chat = await this.call<{ topic?: string | null }>("GET", `/chats/${encodeURIComponent(chatId)}?$select=topic`);
    return chat.topic?.trim() || null;
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
          addressedUserIds: [],
        });
      }
      url = page["@odata.nextLink"];
    }
    return lines.slice(0, max).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }

  /** Messages in a chat created after `since`, oldest first. Pages back as far as needed. */
  async messagesSince(chatId: string, since: string): Promise<ChatLine[]> {
    type Raw = {
      id: string;
      messageType: string;
      createdDateTime: string;
      deletedDateTime?: string | null;
      from?: { user?: { id?: string } | null } | null;
      body?: { content?: string };
      mentions?: { mentioned?: { user?: { id?: string } | null } | null }[] | null;
      attachments?: { contentType?: string | null; content?: string | null }[] | null;
    };
    type Page = { value: Raw[]; "@odata.nextLink"?: string };
    // Who a message is aimed at: anyone it tags, and the author of a message it quotes.
    const addressed = (m: Raw): string[] => {
      const ids = (m.mentions ?? []).map((x) => x.mentioned?.user?.id ?? "");
      for (const a of m.attachments ?? []) {
        if (a.contentType !== "messageReference" || !a.content) continue;
        try {
          ids.push((JSON.parse(a.content) as { messageSender?: { user?: { id?: string } } }).messageSender?.user?.id ?? "");
        } catch {
          // An attachment we cannot read tells us nothing about who is addressed.
        }
      }
      return [...new Set(ids.filter(Boolean))];
    };

    const lines: ChatLine[] = [];
    let url: string | undefined = `/chats/${encodeURIComponent(chatId)}/messages?$top=50`;
    let reachedOlder = false;
    // Newest first: keep going until we pass `since`, so a busy chat after an outage loses nothing.
    for (let page = 0; url && !reachedOlder && page < 10; page++) {
      const r: Page = await this.call<Page>("GET", url);
      for (const m of r.value) {
        if (m.createdDateTime <= since) {
          reachedOlder = true;
          break;
        }
        if (m.messageType !== "message" || m.deletedDateTime || !m.from?.user?.id) continue;
        lines.push({
          id: m.id,
          createdAt: m.createdDateTime,
          fromUserId: m.from.user.id,
          fromName: "",
          text: htmlToText(m.body?.content ?? ""),
          addressedUserIds: addressed(m),
        });
      }
      url = r["@odata.nextLink"];
    }
    return lines.sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  }
}
