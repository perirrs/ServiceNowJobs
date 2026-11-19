export interface Config {
  port: number;
  adminEmails: string[];
  companyName: string;
  timezone: string;
  checkinCron: string;
  reminderCron: string;
  digestCron: string;
  maxBotTurns: number;
  dataDir: string;
  model: string;
  /** Send check-ins from a signed-in person's own Teams account instead of from the bot. */
  sendAsUser: boolean;
  pollSeconds: number;
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
    checkinCron: env.CHECKIN_CRON ?? "0 17 * * 1-5",
    reminderCron: env.REMINDER_CRON ?? "0 18 * * 1-5",
    digestCron: env.DIGEST_CRON ?? "30 19 * * 1-5",
    maxBotTurns: Number(env.MAX_BOT_TURNS ?? 6),
    dataDir: env.DATA_DIR ?? "./data",
    model: env.CLAUDE_MODEL ?? "claude-opus-5-5",
    sendAsUser: env.SEND_AS_USER === "true",
    pollSeconds: Math.max(10, Number(env.POLL_SECONDS ?? 20)),
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
