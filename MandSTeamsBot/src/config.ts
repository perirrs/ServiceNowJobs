export interface Config {
  port: number;
  adminEmails: string[];
  companyName: string;
  timezone: string;
  /** Minutes to wait for a reply before sending the one chase message. */
  chaseAfterMinutes: number;
  maxBotTurns: number;
  dataDir: string;
  model: string;
  /** The stronger model for the jobs where quality matters most: the digest, the weekly report, tracker building, questions. */
  smartModel: string;
  /** The strongest model, for the weekly analysis of the whole ATS. */
  analysisModel: string;
  /** One Base44 key for every Base44 app the user holds; empty: no Base44 portals. */
  base44Key: string;
  /** TalentFlow's Base44 app (slug, name or id). */
  talentflowApp: string;
  /** Send check-ins from a signed-in person's own Teams account instead of from the bot. */
  sendAsUser: boolean;
  pollSeconds: number;
  /** Minutes of silence after which a conversation with replies is closed and summarised. */
  idleCloseMinutes: number;
  /** How many of the most recent messages to read from a chat when first learning about a person or group. */
  historyMessages: number;
  /** How much chat the model reads at once; a longer history is read in parts of this size, oldest first. */
  historyChunkChars?: number;
  /** Chat history is read newest to oldest in batches of this many messages (500). */
  historyBatch?: number;
  /** Minutes after a group is asked during which replies in the group count as updates. */
  groupWindowMinutes: number;
  /**
   * In send-as-user mode: true tells colleagues the check-in is automated;
   * false writes as the sender in the first person and never mentions automation.
   */
  discloseAutomation: boolean;
  /** Wait as long as a person would take to read and type before each reply. */
  humanDelay: boolean;
  /** On the day a person or group is added, messages to them wait for the sender's approval. */
  approveFirstDay: boolean;
}

export function loadConfig(env: NodeJS.ProcessEnv = process.env): Config {
  const adminEmails = (env.ADMIN_EMAILS ?? "")
    .split(",")
    .map((e) => e.trim().toLowerCase())
    .filter(Boolean);

  return {
    port: Number(env.PORT ?? 3978),
    adminEmails,
    companyName: env.COMPANY_NAME ?? "the company",
    timezone: env.TIMEZONE ?? Intl.DateTimeFormat().resolvedOptions().timeZone,
    chaseAfterMinutes: Number(env.CHASE_AFTER_MINUTES ?? 45),
    maxBotTurns: Number(env.MAX_BOT_TURNS ?? 6),
    dataDir: env.DATA_DIR ?? "./data",
    model: env.CLAUDE_MODEL ?? "claude-opus-5-5",
    smartModel: env.CLAUDE_SMART_MODEL ?? "claude-sonnet-5-5",
    analysisModel: env.CLAUDE_ANALYSIS_MODEL ?? "claude-opus-5-5",
    base44Key: env.BASE44_API_KEY ?? "",
    talentflowApp: env.TALENTFLOW_APP ?? "mats",
    sendAsUser: env.SEND_AS_USER === "true",
    pollSeconds: Math.max(10, Number(env.POLL_SECONDS ?? 20)),
    discloseAutomation: env.DISCLOSE_AUTOMATION !== "false",
    humanDelay: env.HUMAN_DELAY !== "false",
    approveFirstDay: env.APPROVE_FIRST_DAY !== "false",
    idleCloseMinutes: Number(env.IDLE_CLOSE_MINUTES ?? 30),
    groupWindowMinutes: Number(env.GROUP_WINDOW_MINUTES ?? 120),
    // The whole history by default. The ceiling only guards against a runaway read.
    // The whole chat history: long histories are read in parts, so there is no need to cut them short.
    historyMessages: Number(env.HISTORY_MESSAGES ?? 1_000_000),
    historyChunkChars: Number(env.HISTORY_CHUNK_CHARS ?? 250_000),
    // Histories are read newest to oldest in batches of this many messages.
    historyBatch: Number(env.HISTORY_BATCH ?? 500),
  };
}

/** Today's date (YYYY-MM-DD) in the configured timezone. */
export function todayIn(timezone: string, now: Date = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: timezone,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(now);
}
