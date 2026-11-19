/**
 * 05 — the service desk: ~90 tickets over ten weeks across the catalog (every status, priority and SLA state), comments and notes, photos, approvals, work orders with checklists,
 * vendors, parts from stock and costs, ratings, saved views, an approval delegation, the knowledge base and scheduled report exports.
 * Everything is made through the API as the cast; History is then moved back in time with SQL (see 05-servicedesk-engine.mjs). Runs after setup, spaces, assets and stock and uses what they made.
 */
import { CAST, rows } from "./lib.mjs";
import { Desk } from "./05-servicedesk-engine.mjs";
import { tickets, VENDOR_RE } from "./05-servicedesk-data.mjs";
import { seedKb } from "./05-servicedesk-kb.mjs";

export const name = "servicedesk";
const MARKER = "AC ruang Merak berisik dan bergetar";

export async function run(d) {
  const t0 = Date.now();
  const hendra = await d.as("hendra"), putri = await d.as("putri");
  await prerequisites(d, hendra);
  await ensureVendors(d, hendra);

  const desk = new Desk(d); await desk.init();
  const already = rows(d.must(await putri.get(`/api/tickets?q=${encodeURIComponent(MARKER)}&status=&pageSize=5`), "marker")).length > 0;
  if (already) d.log("  tickets: already seeded, skipped");
  else await seedTickets(d, desk);

  await savedViews(d, desk);
  await delegation(d);
  await seedKb(d, desk);
  await schedules(d);
  await report(d, desk);
  d.log(`  servicedesk done in ${((Date.now() - t0) / 1000).toFixed(0)} s`);
}

// ---------------------------------------------------------------------------------------------------------------------- prerequisites
async function prerequisites(d, hendra) {
  // labour rate, so logged time has a cost
  const cs = d.must(await hendra.get("/api/costs/settings"), "cost settings");
  if (cs.labourRatePerHour == null) await d.soft("labour rate", async () => d.must(await hendra.put("/api/costs/settings", { currency: cs.currency || "IDR", labourRatePerHour: 85000 }), "cost settings"));
  // SLA: the specific policies (server room, AC faults, warehouse) must come before the catch-all "by priority" ones, or they would never match (first match wins)
  await d.soft("sla order", async () => {
    const list = rows(d.must(await hendra.get("/api/sla/policies"), "sla policies"));
    const specific = ["Server room: any request", "Air conditioning faults", "Warehouse requests"].map((n) => list.find((p) => p.name === n)).filter(Boolean);
    const order = [...specific, ...list.filter((p) => !specific.includes(p))].map((p) => p.id);
    if (specific.length && order.join() !== list.map((p) => p.id).join()) { d.must(await hendra.post("/api/sla/policies/reorder", { ids: order }), "reorder sla"); d.log("  sla: specific policies moved before the catch-all ones"); }
  });
  // cost centres: only when nobody made any (the stock module does)
  await d.soft("cost centres", async () => {
    const centers = rows(d.must(await hendra.get("/api/costs/centers"), "cost centres"));
    const L = d.ctx.loc ?? {};
    if (centers.length === 0 && L.site) {
      const made = [["CC-100", "Kantor Pusat Jakarta", [L.site?.id]], ["CC-120", "Gudang Pusat", [L.warehouse?.id]], ["CC-200", "Cabang Bandung", [L.bandung?.id]]];
      // a place belongs to one cost centre: the warehouse building is carved out of the head office
      for (const [code, nm, ids] of made) { const r = await hendra.post("/api/costs/centers", { code, name: nm, locationIds: ids.filter(Boolean) }); if (!r.ok) d.log(`    ! cost centre ${code}: ${r.text?.slice(0, 120)}`); }
      d.log("  cost centres: 3 created (none existed)");
    }
  });
}

/** PT Sejuk Abadi (with Joko linked as its portal user) and a pest and an electrical vendor, unless the assets module already made vendors like them. */
async function ensureVendors(d, hendra) {
  const list = rows(d.must(await hendra.get("/api/vendors?pageSize=100"), "vendors"));
  const has = (re) => list.find((v) => re.test(`${v.name} ${v.services ?? ""}`));
  const L = d.ctx.loc ?? {};
  const want = [
    [VENDOR_RE.ac, { name: "PT Sejuk Abadi", services: "Servis AC, chiller dan pendingin presisi (server room)", email: "layanan@sejukabadi.co.id", phone: "+62 21 5790 2211", website: "https://sejukabadi.co.id", address: "Jl. Raya Pasar Minggu No. 45, Jakarta Selatan 12780", taxId: "01.234.567.8-012.000", notes: "Vendor AC utama. Teknisi lapangan: Joko Susilo.",
      contacts: [{ name: "Joko Susilo", role: "Teknisi lapangan", email: CAST.joko.email, phone: "+62 812 3456 7788" }, { name: "Ratna Dewi", role: "Admin dan penagihan", email: "ratna@sejukabadi.co.id", phone: "+62 21 5790 2211" }] }],
    [VENDOR_RE.pest, { name: "CV Bersih Sehat Pest Control", services: "Pest control, fogging dan penanganan tikus", email: "order@bersihsehat.co.id", phone: "+62 21 8660 4410", address: "Jl. Pondok Gede Raya No. 12, Bekasi", taxId: "02.345.678.9-407.000", notes: "Kontrak tahunan, kunjungan rutin tiap bulan.", contacts: [{ name: "Hari Wibowo", role: "Supervisor lapangan", phone: "+62 813 2200 4411" }] }],
    [VENDOR_RE.elec, { name: "PT Daya Listrik Mandiri", services: "Kontraktor listrik, panel dan UPS", email: "ops@dayalistrik.co.id", phone: "+62 21 2930 7788", address: "Jl. Tebet Barat Dalam No. 8, Jakarta Selatan", taxId: "03.456.789.0-015.000", notes: "Panggilan darurat 24 jam.", contacts: [{ name: "Budi Hartono", role: "Koordinator proyek", phone: "+62 815 7788 9900" }] }],
  ];
  let created = 0;
  for (const [re, body] of want) if (!has(re)) { d.must(await hendra.post("/api/vendors", { ...body, locationIds: [L.site?.id, L.bandung?.id].filter(Boolean) }), `vendor ${body.name}`); created++; }
  // Joko works for PT Sejuk Abadi
  const sejuk = rows(d.must(await hendra.get("/api/vendors?pageSize=100"), "vendors")).find((v) => VENDOR_RE.ac.test(`${v.name} ${v.services ?? ""}`));
  if (sejuk) {
    const det = d.must(await hendra.get(`/api/vendors/${sejuk.id}`), "vendor");
    if (!(det.portalUsers ?? []).some((u) => u.email === CAST.joko.email)) await d.soft("link joko", async () => d.must(await hendra.post(`/api/vendors/${sejuk.id}/portal-users`, { email: CAST.joko.email }), "link portal user"));
  }
  if (created) d.log(`  vendors: ${created} created (none matched)`);
}

// ---------------------------------------------------------------------------------------------------------------------- tickets
async function seedTickets(d, desk) {
  const specs = tickets();
  for (const s of specs) await desk.plan(s);
  specs.sort((a, b) => a._start - b._start);          // oldest first: ticket numbers follow dates
  let n = 0;
  for (const s of specs) { await desk.ticket(s); if (++n % 20 === 0) d.log(`    ... ${n}/${specs.length} tickets`); }
  const made = desk.tickets;
  d.ctx.tickets = Object.fromEntries(made.map((T) => [T.spec.id, { id: T.id, number: T.number }]));   // for modules that run later
  // anything that happens "in the future" would be a bug of a spec: say so
  const late = made.flatMap((T) => T.marks.filter((m) => m.target > desk.now).map(() => T.spec.id));
  if (late.length) d.log(`    ! steps dated in the future: ${[...new Set(late)].join(", ")}`);
  const r = await desk.finish();
  d.log(`  tickets: ${made.length}/${specs.length} made (${desk.failed.length} failed), history moved back in time (${r.statements} rows); SLA now: ` +
    Object.entries(r.sla).filter(([, v]) => v).map(([k, v]) => `${k} ${v}`).join(", "));
  for (const f of desk.failed) d.log(`    ! ${f}`);
  const wos = d.sql("select status, count(*) from work_orders group by 1 order by 1").trim().replace(/\n/g, ", ");
  d.log(`  work orders: ${wos}`);
}

// ---------------------------------------------------------------------------------------------------------------------- saved views
async function savedViews(d, desk) {
  const L = d.ctx.loc ?? {};
  const plan = [
    ["putri", "Belum ditugaskan", { status: "open", unassigned: "true", sort: "due" }, true],
    ["putri", "Terlambat (SLA lewat)", { status: "open", sla: "breached", sort: "due" }, true],
    ["putri", "Server room", { locationId: L.server?.id, status: "open" }, false],
    ["hendra", "Menunggu persetujuan", { status: "pendingapproval" }, true],
    ["hendra", "Cabang Bandung", { locationId: L.bandung?.id }, false],
    ["hendra", "Mendesak dan tinggi", { priority: "urgent", status: "open" }, false],
    ["agus", "Tugas saya yang mendesak", { assignedToMe: "true", status: "open", sort: "due" }, false],
    ["bayu", "Tiket saya", { assignedToMe: "true", status: "open" }, false],
  ];
  let made = 0;
  for (const [who, name, filter, shared] of plan) {
    const c = await d.as(who); const have = rows(d.must(await c.get("/api/ticket-views"), "views")).some((v) => v.name === name && v.mine);
    if (have) continue;
    const f = Object.fromEntries(Object.entries(filter).filter(([, v]) => v)); // locations may be missing
    const r = await c.post("/api/ticket-views", { name, filter: f, shared }); if (r.ok) made++; else d.log(`    ! view ${name}: ${r.text?.slice(0, 140)}`);
  }
  d.log(`  saved views: ${made} new`);
}

// ---------------------------------------------------------------------------------------------------------------------- delegation
async function delegation(d) {
  const hendra = await d.as("hendra");
  const mine = d.must(await hendra.get("/api/approvals/delegations"), "delegations");
  if ((Array.isArray(mine) ? mine : rows(mine)).some((x) => x.state === "active" || x.state === "upcoming")) { d.log("  delegation: already there"); return; }
  const putri = await d.userId("putri"); const now = Date.now();
  const r = await hendra.post("/api/approvals/delegations", { delegateId: putri, startsAt: new Date(now - 5 * 60_000).toISOString(), endsAt: new Date(now + 5 * 86_400_000).toISOString(), note: "Dinas ke kantor Bandung. Permintaan rutin boleh disetujui Putri; set-up acara dan pindah lantai tunggu saya." });
  d.must(r, "delegation");
  d.log(`  delegation: Hendra → Putri for 5 days (${r.data.handedOver} approvals handed over, ${r.data.kept} kept: they were filed by Putri herself)`);
}

// ---------------------------------------------------------------------------------------------------------------------- scheduled report exports
async function schedules(d) {
  const mk = async (who, body) => {
    const c = await d.as(who); const have = rows(d.must(await c.get("/api/report-schedules"), "schedules")).find((s) => s.name === body.name);
    if (have) return have;
    return d.must(await c.post("/api/report-schedules", body), `schedule ${body.name}`);
  };
  const e = (k) => CAST[k].email;
  const a = await mk("hendra", { name: "Ringkasan SLA mingguan", report: "sla", format: "csv", frequency: "weekly", weekday: 1, hour: 7, days: 7, recipients: [e("hendra"), e("putri"), e("admin")], active: true });
  await mk("putri", { name: "Daftar tiket GA harian", report: "tickets", format: "csv", frequency: "daily", hour: 6, days: 1, recipients: [e("putri"), e("hendra")], active: true });
  await mk("hendra", { name: "Laporan biaya fasilitas bulanan", report: "costs", format: "xlsx", frequency: "monthly", monthDay: 1, hour: 8, days: 30, recipients: [e("hendra"), e("putri"), e("admin")], active: true });
  if (!a.lastRunAt) await d.soft("send now", async () => { const c = await d.as("hendra"); d.must(await c.post(`/api/report-schedules/${a.id}/send`), "send now"); });
  d.log("  report schedules: 3 (one sent now)");
}

// ---------------------------------------------------------------------------------------------------------------------- what the screens show
async function report(d, desk) {
  const out = [];
  const putri = await d.as("putri");
  const ops = d.must(await putri.get("/api/reports/operations?days=30"), "operations"), k = ops.kpis;
  out.push(`ops(30d): open ${k.open}, unassigned ${k.unassigned}, at risk ${k.atRisk}, overdue ${k.overdue}, pending approval ${k.pendingApproval}, created ${k.created}, resolved ${k.resolved}, SLA ${k.slaAttainmentPercent}%`);
  const csat = d.must(await putri.get("/api/reports/csat?days=90"), "csat");
  out.push(`csat(90d): ${csat.count} ratings, avg ${csat.average}, dist ${csat.distribution.join("/")}`);
  const dimas = d.must(await (await d.as("dimas")).get("/api/approvals/mine"), "approvals");
  out.push(`dimas approvals: ${dimas.filter((a) => a.status === "pending").length} waiting, ${dimas.filter((a) => a.status !== "pending").length} decided`);
  const hend = d.must(await (await d.as("hendra")).get("/api/approvals/mine"), "approvals");
  out.push(`hendra approvals: ${hend.filter((a) => a.status === "pending").length} waiting, ${hend.filter((a) => a.status !== "pending").length} decided`);
  const off = d.must(await (await d.as("agus")).get("/api/work-orders/offline"), "offline");
  out.push(`agus offline work orders: ${off.items.length}`);
  const joko = rows(d.must(await (await d.as("joko")).get("/api/work-orders?status=all&pageSize=100"), "joko wo"));
  out.push(`joko work orders: ${joko.length} (${joko.filter((w) => w.status !== "done" && w.status !== "cancelled").length} open)`);
  const cost = d.must(await (await d.as("hendra")).get("/api/costs/report?by=month"), "costs");
  out.push(`costs by month: ${cost.rows.map((r) => `${r.key} ${Math.round(r.total).toLocaleString("en-US")}`).join(" · ")}`);
  for (const l of out) d.log(`  verify: ${l}`);
}
