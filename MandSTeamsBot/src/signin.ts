/**
 * One-time sign-in for send-as-user mode. Run it yourself, as the person the
 * check-ins should come from:
 *
 *   npm run signin
 *
 * For an extra sender (listed in EXTRA_SENDERS), say who is signing in, so their
 * sign-in is kept apart from the main sender's:
 *
 *   node --env-file=.env dist/signin.js --as mahanth.m@mergencorp.com
 *
 * It prints a code to enter at microsoft.com/devicelogin. The resulting token
 * is saved in the data folder (graph-token.json) and refreshed automatically.
 */
import { rmSync } from "node:fs";
import path from "node:path";
import { loadConfig } from "./config.js";
import { GraphChat } from "./graph.js";
import { senderDataDir } from "./senders.js";

const config = loadConfig();
const { CLIENT_ID, TENANT_ID } = process.env;
if (!CLIENT_ID || !TENANT_ID) {
  console.error("CLIENT_ID and TENANT_ID must be set in .env first.");
  process.exit(1);
}

const asIndex = process.argv.indexOf("--as");
const as = asIndex > 0 ? (process.argv[asIndex + 1] ?? "").trim().toLowerCase() : "";
if (asIndex > 0 && !as.includes("@")) {
  console.error("Give the email of the person signing in: --as name@company.com");
  process.exit(1);
}
const dataDir = as ? senderDataDir(config.dataDir, as) : config.dataDir;

const graph = new GraphChat(CLIENT_ID, TENANT_ID, dataDir);
await graph.signIn((message) => console.log(`\n${message}\n`));
const me = await graph.whoAmI();
if (as && me.email.toLowerCase() !== as) {
  // The wrong person signed in: their sign-in must not be kept under someone else's name.
  rmSync(path.join(dataDir, "graph-token.json"), { force: true });
  console.error(`That sign-in was for ${me.email}, not ${as}, so it was not kept. Run this again and sign in as ${as}.`);
  process.exit(1);
}
console.log(`Signed in. Check-ins will be sent as ${me.name} <${me.email}>.`);
