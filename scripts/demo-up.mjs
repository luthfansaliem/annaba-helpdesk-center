#!/usr/bin/env node
/**
 * One command to get the demo helpdesk running on this machine, with data (see docs/69-demo-guide.md).
 *
 *   node scripts/demo-up.mjs --reset            # fresh database + API + web + all demo data (about 5-10 minutes the first time, includes `next build`)
 *   node scripts/demo-up.mjs                    # start API and web on the existing demo database (no seeding)
 *   node scripts/demo-up.mjs --reset --dev-web  # use `next dev` instead of a production build (slower pages, no build wait)
 *   node scripts/demo-up.mjs --seed             # (re)run the seed on a database that is still empty
 *   node scripts/demo-up.mjs --stop             # stop API and web
 *
 * Needs Docker Desktop running (PostgreSQL container annaba-digital-dev-db), .NET 10 SDK and Node. The demo uses its own database `annaba_demo`
 * (the normal dev database is untouched), API on :5080 and web on :3000. Seed data is relative to today: run `--reset` on the day of the demo.
 * Local development use only: it enables the dev accounts and the demo cast (Api:SeedDevUsers, Api:SeedDemoUsers), which Production refuses.
 */
import { spawn, spawnSync, execSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync, openSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2); const has = (f) => args.includes(f);
const KEY = "dev-proxy-key-0123456789abcdef0123456789abcdef";
const webPid = join(tmpdir(), "demo-web.pid"); const webLog = join(tmpdir(), "demo-web.log");
const run = (cmd, a, o = {}) => { const r = spawnSync(cmd, a, { cwd: root, stdio: "inherit", shell: process.platform === "win32" && cmd === "npm", ...o }); if (r.status) { console.error(`${cmd} ${a.join(" ")} failed`); process.exit(r.status); } };
const killPid = (file) => { if (!existsSync(file)) return; const pid = readFileSync(file, "utf8").trim(); try { execSync(process.platform === "win32" ? `taskkill /PID ${pid} /T /F` : `kill ${pid}`, { stdio: "ignore" }); } catch { /* gone */ } rmSync(file, { force: true }); };
/** Anything still listening on a port (a server started by hand earlier) would block ours. */
function freePort(port) {
  if (process.platform !== "win32") return;
  const out = spawnSync("netstat", ["-ano", "-p", "tcp"], { encoding: "utf8" }).stdout ?? "";
  for (const line of out.split("\n")) { const m = line.match(new RegExp(`:${port}\\s+\\S+\\s+LISTENING\\s+(\\d+)`)); if (m) try { execSync(`taskkill /PID ${m[1]} /T /F`, { stdio: "ignore" }); console.log(`freed port ${port} (pid ${m[1]})`); } catch { /* gone */ } }
}

if (has("--stop")) { run("node", ["scripts/demo/stack.mjs", "main", "--stop"]); killPid(webPid); freePort(3000); process.exit(0); }

// 1. database container
if (spawnSync("docker", ["info"], { stdio: "ignore" }).status) { console.error("Docker is not running. Start Docker Desktop and try again."); process.exit(1); }
run("docker", ["compose", "-f", "docker-compose.dev.yml", "up", "-d", "--wait"]);

// 2. API (own database annaba_demo)
killPid(webPid); freePort(5080); freePort(3000);
run("node", ["scripts/demo/stack.mjs", "main", "5080", ...(has("--reset") ? ["--reset"] : [])]);

// 3. seed
if (has("--reset") || has("--seed")) run("node", ["scripts/demo-seed.mjs", "--api", "http://localhost:5080"], { env: { ...process.env, DEMO_API_LOG: join(tmpdir(), "demo-api-main.log") } });

// 4. web
const webEnv = { ...process.env, API_URL: "http://localhost:5080", API_PROXY_KEY: KEY, PORT: "3000" };
if (!has("--dev-web")) { console.log("building the web app (production build)…"); run("npm", ["run", "build"], { env: webEnv }); }
const out = openSync(webLog, "w");
const child = spawn(process.platform === "win32" ? "npx.cmd" : "npx", has("--dev-web") ? ["next", "dev", "-p", "3000"] : ["next", "start", "-p", "3000"], { cwd: root, env: webEnv, detached: true, stdio: ["ignore", out, out], windowsHide: true, shell: process.platform === "win32" });
child.unref(); writeFileSync(webPid, String(child.pid));
for (let i = 0; i < 60; i++) { await new Promise((r) => setTimeout(r, 1000)); try { if ((await fetch("http://localhost:3000/login")).ok) break; } catch { /* not yet */ } }
console.log("\nDemo is up:  http://localhost:3000   (all accounts: password Passw0rd!demo)\nStart with the cast table in docs/69-demo-guide.md. Stop with: node scripts/demo-up.mjs --stop");
