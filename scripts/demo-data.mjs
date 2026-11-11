#!/usr/bin/env node
/**
 * Fills a DEVELOPMENT or STAGING helpdesk with believable demo data (requests, assets, vendors, work orders and costs, maintenance plans, knowledge base, ratings, a budget) through the public API, signed in as the seeded dev accounts
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
  await modules(admin, requester, { siteId: site?.id, placeId, adminId: me.id });
  status = must(await admin.get("/api/setup/status"), "reading the setup status");
  console.log(`Done. Setup ${status.done}/${status.total} required steps. Sign in as ${ADMIN} (staff) or ${REQUESTER} (requester).`);
}

// ------------------------------------------------------------------ the rest of the helpdesk (docs/52): assets, vendors, work orders, costs, maintenance, knowledge base, ratings, budgets
/** A step that may fail on an unusual setup without spoiling the rest: it logs and carries on. */
async function soft(what, fn) { try { return await fn(); } catch (e) { console.log(`  ${what}: skipped (${e.message.slice(0, 160)})`); return null; } }
const rows = (d) => (Array.isArray(d) ? d : d?.data ?? d?.items ?? []);
const day = (offset) => { const d = new Date(Date.now() + offset * 864e5); return d.toISOString().slice(0, 10); };

async function modules(admin, requester, ctx) {
  const { siteId, placeId, adminId } = ctx;

  // costs: Rupiah and a labour rate, so logged time has a cost
  await soft("cost settings", async () => must(await admin.put("/api/costs/settings", { currency: "IDR", labourRatePerHour: 75000 }), "cost settings"));

  // asset types and assets
  const types = await soft("asset types", async () => {
    const have = rows(must(await admin.get("/api/asset-types?includeArchived=true"), "reading asset types"));
    const want = ["Air conditioner", "Generator", "Vehicle", "Furniture", "Printer"];
    const out = {};
    for (const name of want) {
      const found = have.find((x) => x.name === name);
      out[name] = found ?? must(await admin.post("/api/asset-types", { name, description: `${name}s looked after by General Affairs.`, fields: [] }), `creating asset type ${name}`);
    }
    return out;
  });
  const assets = await soft("assets", async () => {
    if (!types) return [];
    const have = rows(must(await admin.get("/api/assets?q=DEMO-&status=all&pageSize=100"), "reading assets"));
    const spec = [
      ["DEMO-AC-01", "Daikin split AC, meeting room A", "Air conditioner", "in_use", 12_500_000, 20], ["DEMO-AC-02", "Daikin split AC, meeting room B", "Air conditioner", "in_repair", 12_500_000, -40],
      ["DEMO-AC-03", "Panasonic AC, pantry", "Air conditioner", "in_use", 9_800_000, 200], ["DEMO-AC-04", "LG cassette AC, lobby", "Air conditioner", "in_use", 21_000_000, 75],
      ["DEMO-GEN-01", "Cummins 100 kVA generator", "Generator", "in_use", 185_000_000, 400], ["DEMO-VEH-01", "Toyota Innova B 1234 XYZ", "Vehicle", "in_use", 420_000_000, 25],
      ["DEMO-VEH-02", "Toyota Avanza B 5678 ABC", "Vehicle", "stored", 260_000_000, -100], ["DEMO-FUR-01", "Meeting table, 12 seats", "Furniture", "in_use", 8_000_000, null],
      ["DEMO-FUR-02", "Ergonomic chairs (set of 10)", "Furniture", "in_use", 15_000_000, 300], ["DEMO-PRN-01", "Canon multifunction printer, level 3", "Printer", "in_use", 24_000_000, 12],
      ["DEMO-PRN-02", "Epson printer, reception", "Printer", "in_repair", 6_500_000, 150],
    ];
    const out = [];
    for (const [tag, name, type, status, cost, warrantyDays] of spec) {
      const found = have.find((a) => a.tag === tag);
      if (found) { out.push(found); continue; }
      out.push(must(await admin.post("/api/assets", { tag, name, typeId: types[type].id, status, locationId: placeId ?? null, purchaseDate: day(-400), purchaseCost: cost, warrantyEnd: warrantyDays === null ? null : day(warrantyDays), fields: {} }), `creating asset ${tag}`));
    }
    return out;
  });
  console.log(`  assets: ${assets?.length ?? 0} demo assets`);

  // vendors with a contract each (one ends soon)
  const vendors = await soft("vendors", async () => {
    const have = rows(must(await admin.get("/api/vendors?status=all&pageSize=100"), "reading vendors"));
    const spec = [["PT Sejuk Abadi", "Air-conditioner service and repair", 90], ["CV Bersih Selalu", "Cleaning and pest control", 40], ["PT Daya Listrik Nusantara", "Electrical works and generators", 300]];
    const out = [];
    for (const [name, services, ends] of spec) {
      let v = have.find((x) => x.name === name);
      if (!v) {
        v = must(await admin.post("/api/vendors", { name, services, phone: "+62 21 555 0100", email: `halo@${name.toLowerCase().replace(/[^a-z]+/g, "")}.co.id`, locationIds: siteId ? [siteId] : [] }), `creating vendor ${name}`);
        await soft(`contract for ${name}`, async () => must(await admin.post(`/api/vendors/${v.id}/contracts`, { title: `Service agreement ${new Date().getFullYear()}`, reference: `SA-${v.id.slice(0, 6).toUpperCase()}`, startsOn: day(-200), endsOn: day(ends), value: 120_000_000 }), "contract"));
      }
      out.push(v);
    }
    return out;
  });
  console.log(`  vendors: ${vendors?.length ?? 0}`);

  // work orders on the demo tickets: some by vendors, with costs and time; some finished
  await soft("work orders", async () => {
    const tickets = rows(must(await admin.get(`/api/tickets?q=${encodeURIComponent("[Demo]")}&status=&pageSize=60`), "listing demo tickets")).filter((t) => t.subject.startsWith(PREFIX));
    const existing = rows(must(await admin.get("/api/work-orders?status=all&pageSize=100"), "listing work orders"));
    if (existing.some((w) => w.title.startsWith("Demo:"))) { console.log("  work orders: already there"); return; }
    let n = 0;
    for (const t of tickets.filter((x) => ["assigned", "inprogress", "onhold", "submitted"].includes(x.status)).slice(0, 12)) {
      const vendor = vendors && n % 3 !== 0 ? vendors[n % vendors.length] : null;
      const due = new Date(Date.now() + (n % 4 === 0 || n % 4 === 3 ? -1 : (n % 4) * 2) * 864e5).toISOString();   // some late, some on time, some still to come
      const wo = await admin.post(`/api/tickets/${t.id}/work-orders`, { title: `Demo: ${t.subject.replace(PREFIX, "")}`, instructions: "Check on site and fix.", assigneeId: adminId, dueAt: due, required: false, vendorId: vendor?.id ?? null, purchaseOrder: vendor ? `PO-${2026}${String(100 + n)}` : null, tasks: [{ text: "Inspect", required: true }, { text: "Fix or replace", required: true }] });
      if (!wo.ok) { console.log(`  work order on ${t.number}: ${wo.data?.error?.message ?? wo.status}`); n++; continue; }
      const id = wo.data.id;
      await admin.post(`/api/work-orders/${id}/costs`, { kind: vendor ? "invoice" : "part", description: vendor ? `Invoice ${vendor.name}` : "Spare parts", quantity: 1, unitCost: [350_000, 1_250_000, 780_000, 2_400_000][n % 4] });
      if (n % 2 === 0) await admin.post(`/api/work-orders/${id}/costs`, { kind: "service", description: "Call-out fee", quantity: 1, unitCost: 150_000 });
      if (n % 4 !== 3) {
        await admin.post(`/api/work-orders/${id}/action`, { action: "start" });
        await admin.post(`/api/work-orders/${id}/time`, { minutes: 45 + 15 * (n % 3), note: "On site" });
      }
      if (n % 4 === 0 || n % 4 === 1) {
        const detail = must(await admin.get(`/api/work-orders/${id}`), "reading a work order");
        for (const task of detail.tasks ?? []) await admin.post(`/api/work-orders/${id}/tasks/${task.id}`, { done: true });
        const r = await admin.post(`/api/work-orders/${id}/action`, { action: "complete", note: "Done and tested." });
        if (!r.ok) console.log(`  completing ${detail.number}: ${r.data?.error?.message ?? r.status}`);
      }
      n++;
    }
    console.log(`  work orders: ${n} on demo tickets`);
  });

  // planned maintenance for the air conditioners and the generator
  await soft("maintenance plans", async () => {
    if (!types) return;
    const have = rows(must(await admin.get("/api/maintenance/plans?pageSize=100"), "reading plans"));
    const catalog = must(await admin.get("/api/portal/catalog"), "reading the catalog");
    const svc = catalog.services.find((s) => /air|ac\b|maintenance|repair/i.test(s.name)) ?? catalog.services[0];
    const spec = [["Demo: AC servicing", "Air conditioner", "month", 3, -70], ["Demo: Generator check", "Generator", "week", 2, -20], ["Demo: Printer cleaning", "Printer", "month", 1, 5]];
    for (const [name, type, unit, every, start] of spec) {
      if (have.some((p) => p.name === name)) continue;
      must(await admin.post("/api/maintenance/plans", { name, instructions: "Follow the checklist and note anything unusual.", serviceId: svc.id, assetTypeId: types[type].id, unit, every, startsOn: day(start), leadDays: 7, assigneeId: adminId, vendorId: vendors?.[type === "Generator" ? 2 : 0]?.id ?? null }), `creating plan ${name}`);
    }
    console.log("  maintenance: 3 plans");
  });

  // knowledge base: categories, published articles, reads, votes and "this solved it"
  await soft("knowledge base", async () => {
    const cats = rows(must(await admin.get("/api/kb/categories"), "reading KB categories"));
    const catId = async (name) => { let c = cats.find((x) => x.name === name); if (!c) { c = must(await admin.post("/api/kb/categories", { name }), `creating category ${name}`); cats.push(c); } return c.id; };
    const list = rows(must(await admin.get("/api/kb/articles?status=all&pageSize=100"), "reading articles"));
    const spec = [
      ["How to book a meeting room", "Rooms", "Pick the room and time under New request → Meeting room booking. Bookings are confirmed by e-mail."],
      ["What to do when the air conditioning is too warm", "Facilities", "Check the remote is on cool at 24 °C and the filter light is off. If it is still warm after 15 minutes, raise an Air conditioning request."],
      ["Visitor registration and badges", "Visitors", "Register visitors a day ahead so reception can prepare a badge. Bring an ID card on arrival."],
      ["Using the pool cars", "Transport", "Pool cars are booked per half day. Return the car with at least a quarter tank and note the kilometres."],
      ["Office supplies (ATK): what you can order", "Facilities", "Pens, paper, folders and small stationery come from the ATK request. Larger items need your manager's approval."],
    ];
    let made = 0;
    for (const [title, cat, body] of spec) {
      let a = list.find((x) => x.title === title);
      if (!a) {
        a = must(await admin.post("/api/kb/articles", { title, summary: body.split(".")[0] + ".", body, categoryId: await catId(cat), visibility: "everyone", reviewBy: day(made === 0 ? -3 : 120) }), `creating article ${title}`);
        must(await admin.post(`/api/kb/articles/${a.id}/publish`), `publishing ${title}`);
        made++;
        for (let i = 0; i < 2 + made; i++) await requester.get(`/api/kb/articles/${a.id}`);
        await requester.post(`/api/kb/articles/${a.id}/feedback`, { helpful: made !== 3 });
        if (made % 2 === 1) await requester.post(`/api/kb/articles/${a.id}/solved`);
      }
    }
    console.log(`  knowledge base: ${made} new articles`);
  });

  // ratings on the resolved demo tickets (by the requester)
  await soft("ratings", async () => {
    const mine = rows(must(await requester.get(`/api/tickets/mine?q=${encodeURIComponent("[Demo]")}&status=&pageSize=100`), "listing my demo tickets")).filter((t) => t.status === "resolved");
    let n = 0;
    for (const t of mine) { const r = await requester.put(`/api/tickets/${t.id}/rating`, { score: [5, 4, 5, 2, 4, 3][n % 6], comment: n % 3 === 1 ? "Quick and friendly, thanks!" : n % 6 === 3 ? "Took two visits to fix." : null }); if (r.ok) n++; }
    console.log(`  ratings: ${n}`);
  });

  // a budget for the site this year
  await soft("budget", async () => {
    if (!siteId) return;
    const year = new Date().getFullYear();
    const list = must(await admin.get(`/api/costs/budgets?year=${year}`), "reading budgets");
    if (!rows(list.items ?? list).some((b) => b.scopeId === siteId)) must(await admin.post("/api/costs/budgets", { scope: "site", scopeId: siteId, year, amount: 25_000_000, note: "Facilities upkeep" }), "creating a budget");
    console.log("  budget: site budget for this year");
  });
}

main().catch((e) => { console.error(e.message); process.exit(1); });
