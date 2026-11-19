/**
 * One-time sign-in for send-as-user mode. Run it yourself, as the person the
 * check-ins should come from:
 *
 *   npm run signin
 *
 * It prints a code to enter at microsoft.com/devicelogin. The resulting token
 * is saved in DATA_DIR/graph-token.json and refreshed automatically.
 */
import { loadConfig } from "./config.js";
import { GraphChat } from "./graph.js";

const config = loadConfig();
const { CLIENT_ID, TENANT_ID } = process.env;
if (!CLIENT_ID || !TENANT_ID) {
  console.error("CLIENT_ID and TENANT_ID must be set in .env first.");
  process.exit(1);
}

const graph = new GraphChat(CLIENT_ID, TENANT_ID, config.dataDir);
await graph.signIn((message) => console.log(`\n${message}\n`));
const me = await graph.whoAmI();
console.log(`Signed in. Check-ins will be sent as ${me.name} <${me.email}>.`);
