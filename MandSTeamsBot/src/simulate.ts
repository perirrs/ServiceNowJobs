/**
 * Try the bot in your terminal, with real Claude and no Teams or Azure setup.
 * You play a nominated employee. Needs ANTHROPIC_API_KEY (in .env or the environment).
 *
 *   npm run simulate
 *
 * Commands while chatting:
 *   /nextday   close today (writes the digest) and start tomorrow's check-in
 *   /digest    show today's leadership digest
 *   /ask ...   ask a question as the CTO/CEO
 *   /quit
 */
import { createInterface } from "node:readline/promises";
import { ClaudeBrain } from "./ai.js";
import { loadConfig } from "./config.js";
import { Store } from "./db.js";
import { Engine } from "./engine.js";
import type { Identity } from "./engine.js";

const ADMIN = "cto@sim.local";
const STAFF = "staff@sim.local";

const config = { ...loadConfig(), adminEmails: [ADMIN] };
const store = new Store(":memory:");
const rl = createInterface({ input: process.stdin, output: process.stdout });

let dayOffset = 0;
const now = () => new Date(Date.now() + dayOffset * 86_400_000);

const engine = new Engine(
  store,
  new ClaudeBrain(config.model, config.companyName),
  {
    send: async (conversationId, text) => {
      const who = conversationId === "conv-admin" ? "BOT → leadership" : "BOT";
      console.log(`\n\x1b[36m${who}:\x1b[0m ${text}\n`);
    },
  },
  config,
  now,
);

const identity = (email: string, conv: string, name: string): Identity => ({
  teamsUserId: `user-${email}`,
  conversationId: conv,
  name,
  lookupEmail: async () => email,
});
const admin = identity(ADMIN, "conv-admin", "The CTO");
const staff = identity(STAFF, "conv-staff", "Sam Example");

const profile =
  (await rl.question(
    "Describe the employee's role, responsibilities and targets\n(Enter for a sample recruiter): ",
  )) ||
  "Senior recruiter on the ServiceNow desk. Sources and submits candidates to client roles, arranges interviews, manages offers. Targets: 10 submissions and 3 first interviews a week, 2 placements a month.";

await engine.handleInstall(admin);
await engine.handleMessage(admin, `nominate ${STAFF}`);
await engine.handleMessage(admin, `profile ${STAFF}: ${profile}`);
await engine.handleInstall(staff);
await engine.runCheckins();

for (;;) {
  const line = (await rl.question("\x1b[33mYou:\x1b[0m ")).trim();
  if (!line) continue;
  if (line === "/quit") break;
  if (line === "/digest") {
    await engine.runDigest();
  } else if (line === "/nextday") {
    await engine.runDigest();
    dayOffset++;
    console.log(`\n--- next day (${now().toDateString()}) ---`);
    await engine.runCheckins();
  } else if (line.startsWith("/ask ")) {
    await engine.handleMessage(admin, line.slice(5));
  } else {
    await engine.handleMessage(staff, line);
  }
}
rl.close();
store.close();
