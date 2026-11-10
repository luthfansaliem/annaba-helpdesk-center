#!/usr/bin/env node
/**
 * Fills a DEVELOPMENT or STAGING helpdesk with believable demo data through the public API, signed in as the seeded dev accounts
 * (so everything goes through the same validation, audit and notification paths as real use). It never touches the database directly.
 *
 *   node scripts/demo-data.mjs                       # against http://localhost:3000
 *   WEB=http://localhost:3100 node scripts/demo-data.mjs
 *   node scripts/demo-data.mjs --yes --web https://staging.example.com   # anything but localhost needs --yes
 *
 * Needs the dev accounts (Api:SeedDevUsers, Development only) with the default password, or DEMO_PASSWORD. Safe to run twice: demo tickets
 * carry the prefix "[Demo]" and nothing more is created once enough exist.
 */
const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 && args[i + 1] ? args[i + 1] : fallback; };

const WEB = (opt("--web", process.env.WEB ?? "http://localhost:3000")).replace(/\/$/, "");
const PASSWORD = process.env.DEMO_PASSWORD ?? "Passw0rd!demo";
const ADMIN = "sarah.wijaya@annabadigital.io";
const REQUESTER = "rizky.pratama@annabadigital.io";
const WANT = Number(opt("--tickets", "10"));
const PREFIX = "[Demo] ";

const host = new URL(WEB).hostname;
if (!["localhost", "127.0.0.1", "[::1]"].includes(host) && !flag("--yes")) {
  console.error(`Refusing to write demo data to ${WEB}. This is meant for development and staging; pass --yes if you are sure.`);
  process.exit(2);
}

/** A tiny signed-in client: keeps the session cookies, sends the CSRF token and the Origin the API expects. */
async function client(email) {
  const jar = new Map();
  const absorb = (res) => { for (const c of res.headers.getSetCookie?.() ?? []) { const [pair] = c.split(";"); const i = pair.indexOf("="); jar.set(pair.slice(0, i), pair.slice(i + 1)); } };
  const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join("; ");
  let csrf = null;
  async function call(method, path, body) {
    const headers = { Origin: WEB, Cookie: cookie() };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (method !== "GET") { csrf ??= (await (await fetch(`${WEB}/api/auth/csrf`, { headers: { Cookie: cookie() } })).json()).token; headers["X-CSRF-Token"] = csrf; }
    const res = await fetch(`${WEB}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
    absorb(res);
    const text = await res.text(); let data = null; try { data = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
    return { ok: res.ok, status: res.status, data, text };
  }
  const login = await fetch(`${WEB}/api/auth/login`, { method: "POST", headers: { "Content-Type": "application/json", Origin: WEB }, body: JSON.stringify({ email, password: PASSWORD }) });
  absorb(login);
  if (!login.ok) throw new Error(`Could not sign in as ${email} (${login.status}). Are the dev accounts seeded, and is DEMO_PASSWORD right?`);
  return { get: (p) => call("GET", p), post: (p, b) => call("POST", p, b ?? {}), put: (p, b) => call("PUT", p, b) };
}
const must = (r, what) => { if (!r.ok) throw new Error(`${what} failed (${r.status}): ${r.text.slice(0, 300)}`); return r.data; };

/** Plausible answers for any form, so the script works with whatever catalog the organisation has. */
function answersFor(fields, ctx) {
  const out = {};
  for (const f of fields) {
    switch (f.type) {
      case "text": out[f.key] = f.pattern ? undefined : "Demo"; break;
      case "long_text": out[f.key] = "Raised by the demo-data script to show how the helpdesk looks in use."; break;
      case "number": out[f.key] = f.min ?? 1; break;
      case "date": out[f.key] = new Date(Date.now() + 2 * 864e5).toISOString().slice(0, 10); break;
      case "datetime": out[f.key] = new Date(Date.now() + 2 * 864e5).toISOString(); break;
      case "select": out[f.key] = f.options?.[0]?.value; break;
      case "multi_select": out[f.key] = f.options?.[0] ? [f.options[0].value] : undefined; break;
      case "yes_no": out[f.key] = false; break;
      case "location": out[f.key] = ctx.placeId; break;
      case "user": out[f.key] = ctx.adminId; break;
      default: break; // files are skipped
    }
    if (out[f.key] === undefined) delete out[f.key];
  }
  return out;
}

const SUBJECTS = [
  "Air conditioning too warm in the meeting room", "Flickering light above desk 14", "Leaking tap in the pantry", "Please deliver contract to our partner",
  "Need a visitor badge for Thursday", "Chair with a broken wheel", "Restroom needs extra cleaning", "Order of A4 paper and pens",
  "Set up chairs for the all-hands", "Parking access for a new colleague", "Move the printer to the second floor", "Door lock on storage room is stuck",
];

async function main() {
  console.log(`Demo data for ${WEB}`);
  const admin = await client(ADMIN); const requester = await client(REQUESTER);
  const me = must(await admin.get("/api/auth/me"), "reading the administrator");

  // 1. a working setup (only what is missing)
  let status = must(await admin.get("/api/setup/status"), "reading the setup status");
  if (!status.ready) {
    const r = must(await admin.post("/api/setup/quick-start", { siteName: "Head Office Jakarta", teamName: "General Affairs", memberIds: [me.id], leadId: me.id }), "quick start");
    console.log(`  setup: created ${r.created.join(", ") || "nothing"}`);
  } else console.log("  setup: already complete");

  // 2. a few places to point requests at
  const places = must(await admin.get("/api/locations"), "reading locations"); const rows = Array.isArray(places) ? places : places.data;
  const site = rows.find((l) => l.type === "site");
  let building = rows.find((l) => l.name === "Tower A");
  if (site && !building) {
    building = must(await admin.post("/api/locations", { type: "building", parentId: site.id, code: "TWR-A", name: "Tower A" }), "creating Tower A");
    must(await admin.post("/api/locations", { type: "floor", parentId: building.id, code: "TWR-A-L3", name: "Level 3" }), "creating Level 3");
    console.log("  places: Tower A, Level 3");
  }
  const portalPlaces = must(await requester.get("/api/portal/locations"), "reading places");
  const placeId = (portalPlaces.find((p) => p.name === "Level 3") ?? portalPlaces[0])?.id;

  // 3. requests from the requester
  const existing = must(await requester.get(`/api/tickets/mine?q=${encodeURIComponent("[Demo]")}&pageSize=100`), "listing demo tickets");
  const have = (Array.isArray(existing) ? existing : existing.data ?? []).filter((t) => t.subject.startsWith(PREFIX)).length;
  const catalog = must(await requester.get("/api/portal/catalog"), "reading the catalog");
  const made = [];
  for (let i = have; i < WANT; i++) {
    const svc = catalog.services[i % catalog.services.length];
    const detail = must(await requester.get(`/api/portal/services/${svc.id}`), "reading a service");
    const body = { subject: PREFIX + SUBJECTS[i % SUBJECTS.length], description: "Demo request.", answers: answersFor(detail.fields, { placeId, adminId: me.id }), locationId: placeId ?? null };
    const t = await requester.post(`/api/portal/services/${svc.id}/requests`, body);
    if (!t.ok) { console.log(`  skipped "${svc.name}": ${t.data?.error?.message ?? t.status}`); continue; }
    made.push(t.data);
  }
  console.log(`  requests: ${made.length} new (${have} already there)`);

  // 4. staff move them along so every status is represented
  let n = 0;
  for (const t of made) {
    const step = n++ % 5; const act = async (action, extra = {}) => { const r = await admin.post(`/api/tickets/${t.id}/transition`, { action, ...extra }); return r.ok; };
    if (t.status === "pendingapproval") continue;
    if (step === 0) continue;                                                      // left waiting in the queue
    if (step >= 1 && !(await act("start"))) continue;
    if (step === 2) await admin.post(`/api/tickets/${t.id}/comments`, { body: "On my way to take a look.", internal: false });
    if (step === 3) await act("resolve", { resolutionCode: "fixed", resolutionNote: "Fixed on site; please confirm it works for you." });
    if (step === 4) await act("hold", { reason: "Waiting for a spare part from the supplier." });
  }
  status = must(await admin.get("/api/setup/status"), "reading the setup status");
  console.log(`Done. Setup ${status.done}/${status.total} required steps. Sign in as ${ADMIN} (staff) or ${REQUESTER} (requester).`);
}

main().catch((e) => { console.error(e.message); process.exit(1); });
