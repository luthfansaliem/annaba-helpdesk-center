#!/usr/bin/env node
/**
 * Product-demo seed: fills a fresh DEVELOPMENT helpdesk so that every screen has data and every flow can be walked through live.
 * Runs the modules in scripts/demo/ in order, all through the public API as the demo cast (see docs/69-demo-guide.md).
 *
 *   node scripts/demo-seed.mjs                       # API on http://localhost:5080
 *   node scripts/demo-seed.mjs --only setup,spaces   # run some modules only
 *   node scripts/demo-seed.mjs --list                # show the modules
 *
 * Needs an API started with Api:SeedDevUsers=true and Api:SeedDemoUsers=true (Development), on an EMPTY database: it is meant to be run once.
 * `scripts/demo-up.ps1` does the whole thing (database, API, web, seed).
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
if (!["localhost", "127.0.0.1", "[::1]"].includes(host) && !args.includes("--yes")) { console.error(`Refusing to write demo data to ${API}: this is for a local development API (pass --yes if you are sure).`); process.exit(2); }

const d = connect({ api: API });
d.ctx = {};
console.log(`Demo data for ${API}`);
const started = Date.now();
for (const key of ORDER) {
  if (only && !only.includes(key)) continue;
  let mod;
  try { mod = await import(FILES[key]); } catch (e) { if (e.code === "ERR_MODULE_NOT_FOUND" && String(e.message).includes(FILES[key].slice(2))) { console.log(`[${key}] not written yet, skipped`); continue; } throw e; }
  console.log(`[${key}]`);
  const t = Date.now();
  try { await mod.run(d); } catch (e) { console.error(`  FAILED: ${e.message}`); if (!args.includes("--keep-going")) process.exit(1); }
  console.log(`  (${((Date.now() - t) / 1000).toFixed(1)} s)`);
}
console.log(`Done in ${((Date.now() - started) / 1000).toFixed(0)} s.`);
