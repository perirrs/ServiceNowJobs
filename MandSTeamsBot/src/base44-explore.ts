/**
 * Read-only look at a Base44 app: its entities, their fields and how many
 * records each holds. Prints no record contents, only field names, allowed
 * values and counts.
 *
 *   npm run base44            (all your apps)
 *   npm run base44 -- mats    (one app's entities)
 */
import { Base44 } from "./base44.js";

const key = process.env.BASE44_API_KEY ?? "";
if (!key) {
  console.error("Put BASE44_API_KEY in the settings file first.");
  process.exit(1);
}
const b = new Base44(key);
const ref = process.argv[2] ?? process.env.TALENTFLOW_APP ?? "";

const apps = await b.apps();
console.log(`${apps.length} apps:`);
for (const a of apps) console.log(`  ${a.id}  ${a.slug ?? "-"}  ${a.name}`);
if (!ref) process.exit(0);

const app = await b.findApp(ref);
if (!app) {
  console.error(`No app matches "${ref}".`);
  process.exit(1);
}
console.log(`\n${app.name} (${app.id}) entities:`);
for (const s of await b.schemas(app.id)) {
  if (s.entity_name === "User") continue;
  let n = "?";
  try {
    n = String(await b.count(app.id, s.entity_name));
  } catch (err) {
    n = `count failed: ${(err as Error).message.slice(0, 60)}`;
  }
  console.log(`\n## ${s.entity_name} (${n} records)`);
  for (const [field, p] of Object.entries(s.entity_schema.properties ?? {})) {
    const extra = p.enum ? ` one of: ${p.enum.join(" | ")}` : p.format ? ` (${p.format})` : "";
    console.log(`  ${field}: ${p.type ?? "?"}${extra}`);
  }
}
