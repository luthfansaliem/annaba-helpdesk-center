/**
 * 01 — the organisation the helpdesk runs in: places, teams, working calendars, SLA targets, routing rules, approval policies, extra catalog entries.
 * Everything later (tickets, bookings, assets…) hangs off what is created here. Run it first, on a fresh demo database.
 *
 * Leaves in `d.ctx` (a plain object shared by the modules of one run): ctx.loc.{site,tower,lobby,lvl3,garuda,merak,pantry,lvl5,warehouse,bandung}, ctx.team.{ga,eng,clean,reception}.
 */
export const name = "setup";

export async function run(d) {
  const { rows, must, soft } = d;
  const hendra = await d.as("hendra");
  const ids = {};
  for (const k of ["putri", "agus", "bayu", "sinta", "hendra", "dimas", "rizky"]) ids[k] = await d.userId(k);
  d.ctx.userIds = { ...(d.ctx.userIds ?? {}), ...ids };

  // 1. quick start: first site, first team and the default SLA targets and routing (only what is missing)
  const status = must(await hendra.get("/api/setup/status"), "setup status");
  if (!status.ready) {
    const r = must(await hendra.post("/api/setup/quick-start", { siteName: "Head Office Jakarta", teamName: "General Affairs", memberIds: [ids.putri, ids.agus, ids.bayu, ids.hendra], leadId: ids.putri }), "quick start");
    d.log(`  quick start: created ${r.created.join(", ") || "nothing"}`);
  }

  // 2. calendars: office hours (the default, with Indonesian public holidays), a 24/7 security calendar, and a Saturday-half-day one for the warehouse
  const cals = rows(must(await hendra.get("/api/calendars?includeArchived=true"), "calendars"));
  const office = cals.find((c) => c.isDefault);
  const weekdays = [1, 2, 3, 4, 5].map((day) => ({ day, startMinute: 8 * 60, endMinute: 17 * 60 }));
  const holidays = [["2026-12-25", "Hari Raya Natal"], ["2026-12-31", "Cuti bersama akhir tahun"], ["2027-01-01", "Tahun Baru Masehi"], ["2027-02-06", "Tahun Baru Imlek"], ["2027-03-09", "Hari Raya Nyepi"], ["2026-08-17", "Hari Kemerdekaan RI"], ["2026-10-20", "Hari libur (contoh)"]].map(([date, name]) => ({ date, name }));
  must(await hendra.put(`/api/calendars/${office.id}`, { name: office.name, timeZone: "Asia/Jakarta", isAlwaysOpen: false, periods: weekdays, holidays, version: office.version }), "office calendar");
  let always = cals.find((c) => c.name === "Security 24/7");
  always ??= must(await hendra.post("/api/calendars", { name: "Security 24/7", timeZone: "Asia/Jakarta", isAlwaysOpen: true, periods: [], holidays: [] }), "24/7 calendar");
  let wh = cals.find((c) => c.name === "Warehouse (Mon–Sat)");
  wh ??= must(await hendra.post("/api/calendars", { name: "Warehouse (Mon–Sat)", timeZone: "Asia/Jakarta", isAlwaysOpen: false, periods: [1, 2, 3, 4, 5].map((day) => ({ day, startMinute: 7 * 60, endMinute: 16 * 60 })).concat([{ day: 6, startMinute: 7 * 60, endMinute: 12 * 60 }]), holidays: holidays.slice(0, 3) }), "warehouse calendar");

  // 3. places: head office with tower, floors and rooms; a warehouse; a branch in Bandung
  const all = rows(must(await hendra.get("/api/locations?includeArchived=true"), "locations"));
  const find = (n) => all.find((l) => l.name === n);
  const site = find("Head Office Jakarta");
  async function loc(type, parent, code, nm, extra = {}) {
    const f = find(nm); if (f) return f;
    const made = must(await hendra.post("/api/locations", { type, parentId: parent?.id ?? null, code, name: nm, address: null, timeZone: null, calendarId: null, managerUserId: null, capacity: null, ...extra }), `location ${nm}`);
    all.push(made); return made;
  }
  must(await hendra.patch(`/api/locations/${site.id}`, { type: "site", parentId: null, code: site.code, name: site.name, address: "Jl. Jend. Sudirman Kav. 52–53, Jakarta Selatan 12190", timeZone: "Asia/Jakarta", calendarId: office.id, managerUserId: ids.hendra, capacity: 300, version: site.version }), "site details");
  const tower = await loc("building", site, "TWR-A", "Tower A", { address: "Gedung utama, pintu masuk Jl. Sudirman" });
  const lobby = await loc("floor", tower, "TWR-A-GF", "Ground Floor", { capacity: 80 });
  const reception = await loc("room", lobby, "TWR-A-GF-REC", "Reception", { capacity: 6 });
  const lvl3 = await loc("floor", tower, "TWR-A-L3", "Level 3", { capacity: 60 });
  const garuda = await loc("room", lvl3, "TWR-A-L3-GRD", "Garuda meeting room", { capacity: 12 });
  const merak = await loc("room", lvl3, "TWR-A-L3-MRK", "Merak meeting room", { capacity: 6 });
  const pantry = await loc("room", lvl3, "TWR-A-L3-PAN", "Pantry Level 3", { capacity: 10 });
  const lvl5 = await loc("floor", tower, "TWR-A-L5", "Level 5", { capacity: 60 });
  const server = await loc("room", lvl5, "TWR-A-L5-SRV", "Server room", { capacity: 4, calendarId: always.id });
  const basement = await loc("floor", tower, "TWR-A-B1", "Basement (parking)", { capacity: 120, calendarId: always.id });
  const warehouse = await loc("building", site, "WH-1", "Gudang Pusat", { address: "Gudang belakang, akses dari Jl. Setiabudi", calendarId: wh.id, managerUserId: ids.bayu });
  const bandung = await loc("site", null, "BDG", "Branch Office Bandung", { address: "Jl. Asia Afrika No. 88, Bandung", timeZone: "Asia/Jakarta", calendarId: office.id, capacity: 40 });
  const bdgFloor = await loc("floor", bandung, "BDG-1", "Lantai 1 Bandung", { capacity: 40 });
  d.ctx.loc = { site, tower, lobby, reception, lvl3, garuda, merak, pantry, lvl5, server, basement, warehouse, bandung, bdgFloor };
  d.ctx.cal = { office, always, wh };

  // 4. teams
  const teamList = rows(must(await hendra.get("/api/teams?includeArchived=true"), "teams"));
  const ga = teamList.find((t) => t.name === "General Affairs");
  async function team(nm, desc, members) {
    let t = teamList.find((x) => x.name === nm);
    if (!t) { t = must(await hendra.post("/api/teams", { name: nm, description: desc }), `team ${nm}`); teamList.push(t); must(await hendra.put(`/api/teams/${t.id}/members`, { members: members.map(([u, lead]) => ({ userId: ids[u], isLead: !!lead })) }), `members of ${nm}`); }
    return t;
  }
  const eng = await team("Facilities Engineering", "AC, listrik, plumbing dan perawatan gedung.", [["agus", true], ["bayu"]]);
  const clean = await team("Cleaning & Pantry", "Kebersihan, pantri dan hama.", [["bayu", true]]);
  const rec = await team("Reception & Mailroom", "Tamu, surat dan paket.", [["sinta", true], ["putri"]]);
  d.ctx.team = { ga, eng, clean, reception: rec };

  // 5. SLA policies on top of the four defaults: the server room is critical, AC faults have their own target, the warehouse runs on its own hours
  const catalog = must(await hendra.get("/api/catalog/services?includeArchived=true"), "services"); const services = rows(catalog);
  const svc = (n) => services.find((s) => s.name === n);
  d.ctx.services = services;
  const policies = rows(must(await hendra.get("/api/sla/policies?includeArchived=true"), "sla policies"));
  const sla = async (nm, body) => { if (!policies.some((p) => p.name === nm)) must(await hendra.post("/api/sla/policies", { name: nm, serviceId: null, categoryId: null, locationId: null, priority: null, calendarId: null, escalate1Percent: 60, escalate2Percent: 90, ...body }), `sla ${nm}`); };
  await sla("Server room: any request", { locationId: server.id, calendarId: always.id, firstResponseMinutes: 10, resolutionMinutes: 120 });
  if (svc("Air conditioning")) await sla("Air conditioning faults", { serviceId: svc("Air conditioning").id, priority: "high", firstResponseMinutes: 30, resolutionMinutes: 360 });
  await sla("Warehouse requests", { locationId: warehouse.id, calendarId: wh.id, firstResponseMinutes: 240, resolutionMinutes: 2880 });

  // 6. routing: by service category / place, before the catch-all "All requests"
  const rules = rows(must(await hendra.get("/api/assignment-rules?includeArchived=true"), "rules"));
  const cats = rows(must(await hendra.get("/api/catalog/categories?includeArchived=true"), "categories"));
  const cat = (n) => cats.find((c) => c.name === n);
  const rule = async (nm, body) => { if (!rules.some((r) => r.name === nm)) must(await hendra.post("/api/assignment-rules", { name: nm, serviceId: null, categoryId: null, locationId: null, priority: null, ...body }), `rule ${nm}`); };
  await rule("Facilities → Engineering", { categoryId: cat("Facilities & Maintenance")?.id ?? null, teamId: eng.id, strategy: "least_loaded" });
  await rule("Cleaning & hygiene → Cleaning & Pantry", { categoryId: cat("Cleaning & Hygiene")?.id ?? null, teamId: clean.id, strategy: "round_robin" });
  await rule("Visitors & mail → Reception", { serviceId: svc("Visitor registration")?.id ?? null, teamId: rec.id, strategy: "none" });
  await rule("Server room → Engineering (urgent first)", { locationId: server.id, teamId: eng.id, strategy: "least_loaded" });
  // new rules are appended after the catch-all: move the catch-all to the end
  const after = rows(must(await hendra.get("/api/assignment-rules"), "rules"));
  const all1 = after.find((r) => r.name === "All requests");
  if (all1) must(await hendra.post("/api/assignment-rules/reorder", { ids: [...after.filter((r) => r.id !== all1.id).map((r) => r.id), all1.id] }), "reorder rules");

  // 7. approvals: pool cars need the requester's manager; events need manager and then the GA manager; moves need the GA manager
  const pol = async (service, steps) => { const s = svc(service); if (!s) return; await soft(`approval for ${service}`, async () => must(await hendra.put(`/api/approvals/policies/${s.id}`, { steps, fallbackUserId: ids.hendra }), `approval ${service}`)); };
  await pol("Pool car booking", [{ name: "Atasan langsung", rule: "any", userIds: [], roleIds: [], requesterManager: true }]);
  await pol("Event set-up", [{ name: "Atasan langsung", rule: "any", userIds: [], roleIds: [], requesterManager: true }, { name: "GA Manager", rule: "any", userIds: [ids.hendra], roleIds: [] }]);
  await pol("Desk or office move", [{ name: "GA Manager", rule: "any", userIds: [ids.hendra], roleIds: [] }]);

  // 8. a staff-only service, a draft (unpublished) one and an archived one so the catalog shows every state
  const gaCat = cat("Facilities & Maintenance");
  if (gaCat && !svc("Vendor payment follow-up")) {
    const staffOnly = must(await hendra.post("/api/catalog/services", { name: "Vendor payment follow-up", description: "Hanya untuk tim GA: menagih atau memeriksa pembayaran vendor.", icon: "wallet", categoryId: gaCat.id, owningTeamId: ga.id, defaultPriority: "medium", audience: "staff" }), "staff-only service");
    await soft("publish staff-only", async () => must(await hendra.post(`/api/catalog/services/${staffOnly.id}/publish`), "publish"));
    const draft = must(await hendra.post("/api/catalog/services", { name: "Solar panel inspection (draft)", description: "Sedang disiapkan, belum terbit.", icon: "sun", categoryId: gaCat.id, owningTeamId: eng.id, defaultPriority: "low", audience: "everyone" }), "draft service");
    const old = must(await hendra.post("/api/catalog/services", { name: "Fax machine repair (retired)", description: "Layanan lama.", icon: "printer", categoryId: gaCat.id, owningTeamId: eng.id, defaultPriority: "low", audience: "everyone" }), "retired service");
    await soft("archive retired", async () => must(await hendra.post(`/api/catalog/services/${old.id}/archive`), "archive"));
  }
  d.ctx.services = rows(must(await hendra.get("/api/catalog/services?includeArchived=true"), "services"));
  const st2 = must(await hendra.get("/api/setup/status"), "status");
  d.log(`  setup: ${all.length} locations, ${teamList.length} teams, ${st2.done}/${st2.total} required steps done`);
}
