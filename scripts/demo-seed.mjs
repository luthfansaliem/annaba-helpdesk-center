#!/usr/bin/env node
/**
 * Product-demo seed: fills an EMPTY helpdesk so that every screen has data and every flow can be walked through live.
 * Runs the modules in scripts/demo/ in order, all through the public API as the demo cast (see docs/69-demo-guide.md).
 *
 *   node scripts/demo-seed.mjs                       # local development API on http://localhost:5080 (dev accounts + demo cast seeded by the API)
 *   node scripts/demo-seed.mjs --only setup,spaces   # run some modules only
 *   node scripts/demo-seed.mjs --list                # show the modules
 *
 * Against a real deployment (the seed runner container of deploy/docker-compose.coolify.yml, see docs/69-demo-guide.md §8):
 *   DEMO_PASSWORD=<password for the demo accounts> DEMO_API=http://api:8080 DEMO_PROXY_KEY=<proxy key> DEMO_ORIGIN=https://<public origin> \
 *   PGHOST=db PGUSER=… PGPASSWORD=… PGDATABASE=… node scripts/demo-seed.mjs --yes --bootstrap --mute-mail
 *     --bootstrap   create the demo cast through invitations (no dev accounts exist in a real deployment); real accounts are never touched
 *     --mute-mail   while seeding, mark every queued e-mail as handled (a trigger on outbox_emails, removed at the end) so nothing is sent to the made-up addresses
 *
 * Meant to be run once on an empty database. `scripts/demo-up.mjs` does the whole local thing (database, API, web, seed).
 */
import { connect } from "./demo/lib.mjs";

const args = process.argv.slice(2);
const opt = (n, f) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : f; };
const API = opt("--api", process.env.DEMO_API ?? "http://localhost:5080");
if (opt("--db", null)) process.env.DEMO_DB = opt("--db");
const ORDER = ["setup", "spaces", "assets", "stock", "servicedesk", "admin", "history"];
const FILES = { setup: "./demo/01-setup.mjs", spaces: "./demo/02-spaces.mjs", assets: "./demo/03-assets.mjs", stock: "./demo/04-stock.mjs", servicedesk: "./demo/05-servicedesk.mjs", admin: "./demo/06-admin.mjs", history: "./demo/07-history.mjs" };

if (args.includes("--list")) { console.log(ORDER.join("\n")); process.exit(0); }
const only = opt("--only", null)?.split(","); if (only && !only.includes("setup") && !args.includes("--no-setup")) only.unshift("setup");   // everything hangs off the setup (it only creates what is missing)
const host = new URL(API).hostname;
if (!["localhost", "127.0.0.1", "[::1]"].includes(host) && !args.includes("--yes")) { console.error(`Refusing to write demo data to ${API}: pass --yes if you are sure.`); process.exit(2); }

const MUTE_ON = `CREATE OR REPLACE FUNCTION demo_mute_mail() RETURNS trigger AS $$ BEGIN NEW.sent_at := now(); NEW.last_error := 'not sent (demo seed)'; RETURN NEW; END $$ LANGUAGE plpgsql;
DROP TRIGGER IF EXISTS demo_mute_mail ON outbox_emails;
CREATE TRIGGER demo_mute_mail BEFORE INSERT ON outbox_emails FOR EACH ROW EXECUTE FUNCTION demo_mute_mail();`;
const MUTE_OFF = `DROP TRIGGER IF EXISTS demo_mute_mail ON outbox_emails; DROP FUNCTION IF EXISTS demo_mute_mail();`;

const d = connect({ api: API });
d.ctx = {};
console.log(`Demo data for ${API}`);
const started = Date.now();
let failed = false;
const mute = args.includes("--mute-mail");
try {
  if (mute) { d.sql(MUTE_ON); console.log("e-mail muted while seeding (nothing is sent)"); }
  if (args.includes("--bootstrap")) {
    console.log("[cast]");
    const { bootstrapCast } = await import("./demo/bootstrap-cast.mjs");
    await bootstrapCast(d);
  }
  for (const key of ORDER) {
    if (only && !only.includes(key)) continue;
    let mod;
    try { mod = await import(FILES[key]); } catch (e) { if (e.code === "ERR_MODULE_NOT_FOUND" && String(e.message).includes(FILES[key].slice(2))) { console.log(`[${key}] not written yet, skipped`); continue; } throw e; }
    console.log(`[${key}]`);
    const t = Date.now();
    try { await mod.run(d); } catch (e) { console.error(`  FAILED: ${e.message}`); if (!args.includes("--keep-going")) { failed = true; break; } }
    console.log(`  (${((Date.now() - t) / 1000).toFixed(1)} s)`);
  }
} catch (e) { console.error(`FAILED: ${e.message}`); failed = true; }
finally { if (mute) { try { d.sql(MUTE_OFF); console.log("e-mail unmuted"); } catch (e) { console.error(`COULD NOT REMOVE THE E-MAIL MUTE (trigger demo_mute_mail on outbox_emails): ${e.message}`); } } }
console.log(failed ? "Stopped on an error." : `Done in ${((Date.now() - started) / 1000).toFixed(0)} s.`);
process.exit(failed ? 1 : 0);
