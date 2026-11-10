#!/usr/bin/env node
/**
 * Load test for the GA helpdesk API (PRD §10: p95 read < 300 ms, write < 500 ms, ticket list < 1 s, dashboards < 2 s).
 * Talks to the API directly (it sends the shared proxy key itself) and signs in once per role, then runs concurrent virtual clients against a mix of
 * realistic requests for a fixed time. Prints a table of percentiles per request kind and exits 1 when a budget is broken.
 *
 *   API=http://localhost:5190 PROXY_KEY=... node scripts/load-test.mjs --clients 40 --seconds 30 [--think 1000] [--only text]
 *   (--clients without --think is a stress test: every client asks again the moment it is answered; with --think each stands for one person who pauses.)
 *
 * Seed the database first with scripts/load-seed.sql and give the agent and requester accounts a login (see docs/35). Use a throw-away environment:
 * the test creates tickets and comments.
 */
const args = process.argv.slice(2);
const opt = (n, d) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : d; };
const API = (process.env.API ?? "http://localhost:5190").replace(/\/$/, "");
const KEY = process.env.PROXY_KEY ?? "load-proxy-key-0123456789-abcdefghijklmnop";
const PASSWORD = process.env.DEMO_PASSWORD ?? "Passw0rd!demo";
const CLIENTS = Number(opt("--clients", "40")); const SECONDS = Number(opt("--seconds", "30"));
const WARMUP = Number(opt("--warmup", "5"));
const THINK = Number(opt("--think", "0")); // milliseconds a virtual person pauses between requests (0 = as fast as possible)
const ONLY = opt("--only", ""); const JSON_OUT = args.includes("--json");

const ACCOUNTS = { admin: "sarah.wijaya@annabadigital.io", agent: "agent1@load.test", requester: "req1@load.test" };

async function signIn(email) {
  const jar = new Map();
  const absorb = (res) => { for (const c of res.headers.getSetCookie?.() ?? []) { const [pair] = c.split(";"); const i = pair.indexOf("="); jar.set(pair.slice(0, i), pair.slice(i + 1)); } };
  const base = { "X-Internal-Proxy-Key": KEY, Origin: "http://localhost:3190" };
  const login = await fetch(`${API}/api/auth/login`, { method: "POST", headers: { ...base, "Content-Type": "application/json" }, body: JSON.stringify({ email, password: PASSWORD }) });
  absorb(login);
  if (!login.ok) throw new Error(`sign-in as ${email} failed (${login.status}): ${(await login.text()).slice(0, 200)}`);
  const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join("; ");
  const csrf = (await (await fetch(`${API}/api/auth/csrf`, { headers: { ...base, Cookie: cookie() } })).json()).token;
  return {
    async call(method, path, body) {
      const headers = { ...base, Cookie: cookie() };
      if (body !== undefined) headers["Content-Type"] = "application/json";
      if (method !== "GET") headers["X-CSRF-Token"] = csrf;
      const t0 = performance.now();
      const res = await fetch(`${API}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
      const text = await res.text(); const ms = performance.now() - t0;
      return { ok: res.ok, status: res.status, ms, text };
    },
  };
}

const pct = (a, p) => { if (!a.length) return 0; const s = [...a].sort((x, y) => x - y); return s[Math.min(s.length - 1, Math.ceil((p / 100) * s.length) - 1)]; };

async function main() {
  const who = {}; for (const [k, e] of Object.entries(ACCOUNTS)) who[k] = await signIn(e);
  // what to ask for: some real ticket ids, a service to raise requests against
  const sample = JSON.parse((await who.admin.call("GET", "/api/tickets?pageSize=100&status=")).text).data.map((t) => t.id);
  const mine = JSON.parse((await who.requester.call("GET", "/api/tickets/mine?pageSize=50")).text).data.map((t) => t.id);
  const catalog = JSON.parse((await who.requester.call("GET", "/api/portal/catalog")).text);
  const words = ["printer", "leak", "lamp", "chair", "badge", "courier", "toner", "elevator"];
  const pick = (a) => a[Math.floor(Math.random() * a.length)];

  // budgets in ms (PRD §10): [read 300 | write 500 | list 1000 | dashboard 2000]
  const scenarios = [
    { name: "queue (open, 50 rows) — supervisor", weight: 6, budget: 1000, run: () => who.admin.call("GET", "/api/tickets?status=open&pageSize=50") },
    { name: "queue (any status, page 20) — supervisor", weight: 2, budget: 1000, run: () => who.admin.call("GET", `/api/tickets?status=&pageSize=50&page=${1 + Math.floor(Math.random() * 20)}`) },
    { name: "queue search — supervisor", weight: 3, budget: 1000, run: () => who.admin.call("GET", `/api/tickets?status=&pageSize=50&q=${pick(words)}`) },
    { name: "queue by SLA state, due first", weight: 2, budget: 1000, run: () => who.admin.call("GET", "/api/tickets?status=open&sla=breached&sort=due&pageSize=50") },
    { name: "queue — agent (team scope)", weight: 6, budget: 1000, run: () => who.agent.call("GET", "/api/tickets?status=open&pageSize=50") },
    { name: "my tickets — requester", weight: 6, budget: 300, run: () => who.requester.call("GET", "/api/tickets/mine?pageSize=20") },
    { name: "ticket detail", weight: 8, budget: 300, run: () => who.admin.call("GET", `/api/tickets/${pick(sample)}`) },
    { name: "ticket detail — requester", weight: 3, budget: 300, run: () => who.requester.call("GET", `/api/tickets/${pick(mine)}`) },
    { name: "operations dashboard", weight: 1, budget: 2000, run: () => who.admin.call("GET", "/api/reports/operations?days=30") },
    { name: "SLA report", weight: 1, budget: 2000, run: () => who.admin.call("GET", "/api/reports/sla?days=30") },
    { name: "bell: unread count", weight: 8, budget: 300, run: () => who.requester.call("GET", "/api/notifications/unread-count") },
    { name: "notifications list", weight: 2, budget: 300, run: () => who.admin.call("GET", "/api/notifications?pageSize=20") },
    { name: "approvals count", weight: 4, budget: 300, run: () => who.requester.call("GET", "/api/approvals/mine/count") },
    { name: "service catalog", weight: 3, budget: 300, run: () => who.requester.call("GET", "/api/portal/catalog") },
    { name: "saved views", weight: 1, budget: 300, run: () => who.admin.call("GET", "/api/ticket-views") },
    { name: "WRITE: raise a request", weight: 2, budget: 500, run: () => { const s = pick(catalog.services); return who.requester.call("POST", `/api/portal/services/${s.id}/requests`, { subject: `Load ${Date.now()}`, answers: {} }); }, okStatus: [201, 422] },
    { name: "WRITE: comment", weight: 2, budget: 500, run: () => who.requester.call("POST", `/api/tickets/${pick(mine)}/comments`, { body: "Any news?", internal: false }), okStatus: [200, 409] },
  ].filter((s) => !ONLY || s.name.toLowerCase().includes(ONLY.toLowerCase()));
  const bag = scenarios.flatMap((s) => Array(s.weight).fill(s));

  const stats = new Map(scenarios.map((s) => [s.name, { ms: [], errors: 0, bad: [] }]));
  const stop = Date.now() + SECONDS * 1000; let total = 0;
  const warmUntil = Date.now() + WARMUP * 1000; // connections are opened and caches fill during the first seconds; they are not measured
  async function client() {
    if (THINK > 0) await new Promise((r) => setTimeout(r, Math.random() * THINK * 2)); // people do not all start in the same instant
    while (Date.now() < stop) {
      const s = pick(bag); const st = stats.get(s.name);
      try {
        const r = await s.run(); total++;
        if (r.ok || (s.okStatus ?? []).includes(r.status)) { if (Date.now() >= warmUntil) st.ms.push(r.ms); } else { st.errors++; if (st.bad.length < 2) st.bad.push(`${r.status} ${r.text.slice(0, 120)}`); }
      } catch (e) { st.errors++; if (st.bad.length < 2) st.bad.push(String(e)); }
      if (THINK > 0) await new Promise((r) => setTimeout(r, THINK * (0.5 + Math.random())));
    }
  }
  const t0 = performance.now();
  await Promise.all(Array.from({ length: CLIENTS }, client));
  const secs = (performance.now() - t0) / 1000;

  const rows = scenarios.map((s) => { const st = stats.get(s.name); return { name: s.name, n: st.ms.length, errors: st.errors, p50: pct(st.ms, 50), p95: pct(st.ms, 95), p99: pct(st.ms, 99), max: Math.max(0, ...st.ms), budget: s.budget, pass: st.errors === 0 && pct(st.ms, 95) <= s.budget, bad: st.bad }; });
  if (JSON_OUT) console.log(JSON.stringify({ clients: CLIENTS, seconds: secs, requests: total, rps: total / secs, rows }, null, 1));
  else {
    console.log(`\n${CLIENTS} clients for ${secs.toFixed(0)} s: ${total} requests, ${(total / secs).toFixed(0)} req/s\n`);
    const w = Math.max(...rows.map((r) => r.name.length));
    console.log(`${"request".padEnd(w)}  ${"n".padStart(6)} ${"err".padStart(4)} ${"p50".padStart(6)} ${"p95".padStart(6)} ${"p99".padStart(6)} ${"max".padStart(6)}  budget`);
    for (const r of rows) console.log(`${r.name.padEnd(w)}  ${String(r.n).padStart(6)} ${String(r.errors).padStart(4)} ${r.p50.toFixed(0).padStart(6)} ${r.p95.toFixed(0).padStart(6)} ${r.p99.toFixed(0).padStart(6)} ${r.max.toFixed(0).padStart(6)}  ${r.pass ? "ok" : "OVER"} (${r.budget} ms)`);
    for (const r of rows.filter((x) => x.bad.length)) console.log(`  ! ${r.name}: ${r.bad.join(" | ")}`);
  }
  process.exit(rows.every((r) => r.pass) ? 0 : 1);
}
main().catch((e) => { console.error(e.message); process.exit(2); });
