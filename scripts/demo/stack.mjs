#!/usr/bin/env node
/**
 * Starts (or resets) one demo API against its own database in the dev Postgres container (annaba-digital-dev-db must be running).
 *
 *   node scripts/demo/stack.mjs <name> <port> [--reset] [--no-build]   # database annaba_demo_<name> (name "main" → annaba_demo), API on http://localhost:<port>
 *   node scripts/demo/stack.mjs <name> --stop
 *
 * --reset drops and recreates the database first (the API migrates and seeds the demo cast on start). The API runs detached; its log is
 * <tmp>/demo-api-<name>.log (Email:LogBodies is on, so one-time links appear there). Webhooks may target 127.0.0.1 (a local receiver), like in the tests.
 */
import { spawn, spawnSync, execSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync, openSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const [name, portArg, ...rest] = process.argv.slice(2);
if (!name) { console.error("usage: stack.mjs <name> <port> [--reset] [--stop] [--no-build]"); process.exit(2); }
const port = Number(portArg) || 5080;
const db = name === "main" ? "annaba_demo" : `annaba_demo_${name}`;
const pidFile = join(tmpdir(), `demo-api-${name}.pid`); const logFile = join(tmpdir(), `demo-api-${name}.log`);
const KEY = process.env.DEMO_PROXY_KEY ?? "dev-proxy-key-0123456789abcdef0123456789abcdef";

function stop() {
  if (!existsSync(pidFile)) return;
  const pid = readFileSync(pidFile, "utf8").trim();
  try { execSync(process.platform === "win32" ? `taskkill /PID ${pid} /T /F` : `kill ${pid}`, { stdio: "ignore" }); } catch { /* already gone */ }
  rmSync(pidFile, { force: true });
}
const psql = (sql) => spawnSync("docker", ["exec", "annaba-digital-dev-db", "psql", "-U", "annaba_digital", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c", sql], { encoding: "utf8" });

if (rest.includes("--stop") || portArg === "--stop") { stop(); console.log(`stopped ${name}`); process.exit(0); }
stop();
if (rest.includes("--reset")) {
  const a = psql(`DROP DATABASE IF EXISTS ${db} WITH (FORCE)`); const b = psql(`CREATE DATABASE ${db}`);
  if (a.status || b.status) { console.error(a.stderr || b.stderr); process.exit(1); }
  console.log(`database ${db} recreated`);
} else if (psql(`CREATE DATABASE ${db}`).status === 0) console.log(`database ${db} created`);

if (!rest.includes("--no-build")) { const r = spawnSync("dotnet", ["build", "api/src/AnnabaDigital.Api", "-v", "q", "-nologo"], { cwd: root, stdio: "inherit" }); if (r.status) process.exit(r.status); }

const env = {
  ...process.env, ASPNETCORE_ENVIRONMENT: "Development", Api__ProxyKey: KEY, Api__SeedDevUsers: "true", Api__SeedDemoUsers: "true", Webhooks__AllowPrivateTargets: "true",
  ConnectionStrings__Default: `Host=localhost;Port=5432;Database=${db};Username=annaba_digital;Password=annaba_digital_dev`,
};
const out = openSync(logFile, "w");
const child = spawn("dotnet", ["run", "--project", "api/src/AnnabaDigital.Api", "--no-build", "--no-launch-profile", "--urls", `http://localhost:${port}`], { cwd: root, env, detached: true, stdio: ["ignore", out, out], windowsHide: true });
child.unref(); writeFileSync(pidFile, String(child.pid));

for (let i = 0; i < 90; i++) {
  await new Promise((r) => setTimeout(r, 1000));
  try { const r = await fetch(`http://localhost:${port}/health`); if (r.ok) { console.log(`API "${name}" is up on http://localhost:${port} (database ${db}, log ${logFile})`); process.exit(0); } } catch { /* not yet */ }
}
console.error(`API did not come up; see ${logFile}`); process.exit(1);
