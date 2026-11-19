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
  /** Send check-ins from a signed-in person's own Teams account instead of from the bot. */
  sendAsUser: boolean;
  pollSeconds: number;
  /** Minutes of silence after which a conversation with replies is closed and summarised. */
  idleCloseMinutes: number;
  /** How many of the most recent messages to read from a chat when first learning about a person or group. */
  historyMessages: number;
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
    sendAsUser: env.SEND_AS_USER === "true",
    pollSeconds: Math.max(10, Number(env.POLL_SECONDS ?? 20)),
    discloseAutomation: env.DISCLOSE_AUTOMATION !== "false",
    humanDelay: env.HUMAN_DELAY !== "false",
    approveFirstDay: env.APPROVE_FIRST_DAY !== "false",
    idleCloseMinutes: Number(env.IDLE_CLOSE_MINUTES ?? 30),
    groupWindowMinutes: Number(env.GROUP_WINDOW_MINUTES ?? 120),
    // The whole history by default. The ceiling only guards against a runaway read.
    historyMessages: Number(env.HISTORY_MESSAGES ?? 20000),
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
