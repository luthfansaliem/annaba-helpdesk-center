/**
 * 06 — people & access, administration, platform.
 *
 * Fills: users (cast + ~16 fictional colleagues, invitations in every state, CSV import), roles + matrix (3 custom roles), access requests, access reviews (one completed,
 * one open), account closures, sessions on several devices, security events + audit trail (failed sign-ins, a lockout, password resets, MFA, role changes), system
 * settings, retention + legal hold, e-mail deliveries and wording, inbound e-mail (turned into tickets), API keys + webhooks with deliveries (a tiny local receiver runs
 * for the duration of the module), directory (group → role mappings, SCIM, HR CSV sync), channels (WhatsApp / Slack / Teams with fake credentials), tenants, notification
 * preferences and profiles.
 *
 * The 14 cast accounts are never suspended, locked, deleted or given other roles. Everything goes through the public API as the cast; SQL (d.sql) is used only to
 * move timestamps into the past, to make the one-time links of invitations/reset mails known when the API log is not readable, and to finish what a background worker
 * would do later (account closure). The audit table is append-only: backdating it needs the guard trigger switched off for one transaction (see backdate()).
 */
import http from "node:http";
import { createHash, createHmac, randomBytes } from "node:crypto";
import { existsSync, readFileSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

export const name = "admin";

// ------------------------------------------------------------------------------------------------------------------------------------------------------------------
// small helpers
// ------------------------------------------------------------------------------------------------------------------------------------------------------------------
const UA = {
  chromeWin: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
  edgeWin: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0",
  safariMac: "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15",
  iphone: "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
  ipad: "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
  android: "Mozilla/5.0 (Linux; Android 14; SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36",
  firefoxLinux: "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0",
  firefoxWin: "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0",
};
const sha256 = (s) => createHash("sha256").update(s).digest("hex");
const qs = (s) => String(s).replace(/'/g, "''");
const DAY = 86400e3;
const OFFICE_IPS = ["103.147.8.21", "103.147.8.22", "36.90.112.7", "114.122.64.38", "182.253.67.9", "36.79.201.140", "202.67.40.12", "112.215.20.88"];

function base32Decode(s) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; let bits = 0, value = 0; const out = [];
  for (const ch of s.replace(/=+$/, "").toUpperCase()) { value = (value << 5) | alphabet.indexOf(ch); bits += 5; if (bits >= 8) { out.push((value >>> (bits - 8)) & 255); bits -= 8; } }
  return Buffer.from(out);
}
function totp(secretB32, now = Date.now()) {
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(Math.floor(now / 1000 / 30)));
  const h = createHmac("sha1", base32Decode(secretB32)).update(counter).digest(); const o = h[h.length - 1] & 15;
  return String(((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1_000_000).padStart(6, "0");
}

/** An unauthenticated call (sign-in, accept an invitation, forgot password, inbound mail, API key) with its own client address and user agent. */
async function anon(d, method, path, { body, raw, headers = {}, ip = "203.0.113.10", ua = UA.chromeWin, bearer } = {}) {
  const h = { "X-Internal-Proxy-Key": d.key, "X-Forwarded-For": ip, Origin: d.origin, "User-Agent": ua, ...headers };
  if (bearer) h.Authorization = `Bearer ${bearer}`;
  let payload; if (raw !== undefined) payload = raw; else if (body !== undefined) { h["Content-Type"] ??= "application/json"; payload = JSON.stringify(body); }
  const res = await fetch(`${d.api}${path}`, { method, headers: h, body: payload });
  const text = await res.text(); let data = null; try { data = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
  return { ok: res.ok, status: res.status, data, text };
}

/** Reads the one-time link of the last mail sent to `email` from the API log (when that log is readable), else null. */
function linkFromLog(email, path) {
  const cands = [process.env.DEMO_API_LOG, join(tmpdir(), "demo-api-main.log"), join(tmpdir(), "demo-api-admin.log")].filter(Boolean);
  for (const f of cands) {
    try {
      if (!existsSync(f) || statSync(f).size === 0) continue;
      const text = readFileSync(f, "utf8"); const at = text.lastIndexOf(`EMAIL to=${email} `); if (at < 0) continue;
      const m = text.slice(at, at + 4000).match(new RegExp(`${path.replace("/", "\\/")}\\?token=([A-Za-z0-9_%\\-.]+)`));
      if (m) return decodeURIComponent(m[1]);
    } catch { /* unreadable: fall back */ }
  }
  return null;
}

// ------------------------------------------------------------------------------------------------------------------------------------------------------------------
// the people
// ------------------------------------------------------------------------------------------------------------------------------------------------------------------
const DOMAIN = "annabadigital.io";
const mail = (first, last) => `${first}.${last}@${DOMAIN}`.toLowerCase();
/** The colleagues (all fictional). Order = seniority: the earlier, the longer ago the account was created. `mgr` is a cast key or the key of an earlier colleague. */
const COLLEAGUES = [
  { k: "retno", name: "Retno Wulandari", dept: "Human Resources", title: "HR Manager", phone: "+62 812 2000 0101", mgr: null, site: "HQ", emp: "AD-0101", ua: "chromeWin", seen: 1.2 },
  { k: "bambang", name: "Bambang Hartono", dept: "Finance", title: "Finance Controller", phone: "+62 812 2000 0102", mgr: null, site: "HQ", emp: "AD-0102", ua: "edgeWin", seen: 0.4 },
  { k: "yoga", name: "Yoga Pratama", dept: "Marketing", title: "Marketing Manager", phone: "+62 812 2000 0103", mgr: null, site: "HQ", emp: "AD-0103", ua: "safariMac", seen: 2.5 },
  { k: "dewi", name: "Dewi Lestari", dept: "Operations", title: "Operations Supervisor", phone: "+62 812 2000 0104", mgr: "dimas", site: "BDG", emp: "AD-0104", ua: "chromeWin", seen: 0.9 },
  { k: "siti", name: "Siti Nurhaliza", dept: "Finance", title: "Accounts Payable Officer", phone: "+62 812 2000 0105", mgr: "bambang", site: "HQ", emp: "AD-0105", ua: "firefoxWin", seen: 0.2 },
  { k: "wulan", name: "Wulan Safitri", dept: "Human Resources", title: "HR Officer", phone: "+62 812 2000 0106", mgr: "retno", site: "HQ", emp: "AD-0106", ua: "chromeWin", seen: 3.1 },
  { k: "wawan", name: "Wawan Setiadi", dept: "General Affairs", title: "Facilities Coordinator", phone: "+62 812 2000 0107", mgr: "hendra", site: "HQ", emp: "AD-0107", ua: "android", seen: 0.1 },
  { k: "yusuf", name: "Yusuf Maulana", dept: "General Affairs", title: "Teknisi Listrik", phone: "+62 812 2000 0108", mgr: "putri", site: "HQ", emp: "AD-0108", ua: "android", seen: 0.3 },
  { k: "ahmad", name: "Ahmad Fauzi", dept: "Operations", title: "Staf Operasional Cabang", phone: "+62 812 2000 0109", mgr: "dewi", site: "BDG", emp: "AD-0109", ua: "chromeWin", seen: 5.6 },
  { k: "rina", name: "Rina Kartika", dept: "Sales", title: "Sales Executive", phone: "+62 812 2000 0110", mgr: "yoga", site: "BDG", emp: "AD-0110", ua: "iphone", seen: 1.8 },
  { k: "dian", name: "Dian Anggraeni", dept: "Marketing", title: "Content Specialist", phone: "+62 812 2000 0111", mgr: "yoga", site: "HQ", emp: "AD-0111", ua: "safariMac", seen: 4.0 },
  { k: "ilham", name: "Ilham Ramadhan", dept: "Sales", title: "Sales Executive", phone: "+62 812 2000 0112", mgr: "yoga", site: "BDG", emp: "AD-0112", ua: "chromeWin", seen: 9.0 },
  { k: "nurul", name: "Nurul Hidayati", dept: "Finance", title: "Staf Keuangan", phone: "+62 812 2000 0113", mgr: "bambang", site: "HQ", emp: "AD-0113", ua: "chromeWin", seen: 30 },
].map((p) => ({ ...p, email: mail(...p.name.split(" ").slice(0, 2).map((x) => x.toLowerCase())) }));

const INTERNS_CSV = [
  "name,email,phone,department,jobTitle",
  `Bagas Aditya,bagas.aditya@${DOMAIN},+62 813 5500 2101,Marketing,Intern Digital Marketing`,
  `Citra Dewanti,citra.dewanti@${DOMAIN},+62 813 5500 2102,Human Resources,Intern Rekrutmen`,
  `Farel Ardiansyah,farel.ardiansyah@${DOMAIN},+62 813 5500 2103,Finance,Intern Akuntansi`,
].join("\n");

// ------------------------------------------------------------------------------------------------------------------------------------------------------------------
export async function run(d) {
  const { must, soft, rows, log } = d;
  const T0 = Date.now(); let auditFrom = 0, secFrom = "";
  const admin = await d.as("admin"), daniel = await d.as("useradmin"), maya = await d.as("auditor");
  const reauth = async (c) => { const r = await c.post("/api/auth/reauth", { password: d.PASSWORD }); if (!r.ok) throw new Error(`re-authentication failed (${r.status}): ${r.text.slice(0, 160)}`); };
  const stamp = {};   // run-clock moments of the events, so their dates can be moved consistently into the past at the end
  const mark = (k) => { stamp[k] = Date.now(); return stamp[k]; };

  // already seeded? (the module must not duplicate when run twice)
  const probe = rows(must(await admin.get(`/api/users?q=${encodeURIComponent(COLLEAGUES[0].email)}&pageSize=5`), "looking for the colleagues"));
  if (probe.some((u) => u.email === COLLEAGUES[0].email)) { log("  admin: already seeded (colleagues exist), nothing to do"); return; }

  auditFrom = Number(d.sql("SELECT coalesce(max(id),0) FROM audit_events").trim() || 0);
  secFrom = d.sql("SELECT now()").trim();
  const site = d.ctx.loc?.site, bandung = d.ctx.loc?.bandung;
  const siteCode = { HQ: site?.code, BDG: bandung?.code };

  const roleList = rows(must(await admin.get("/api/roles"), "roles"));
  const roleId = (key) => roleList.find((r) => r.key === key)?.id;
  const cast = {}; for (const k of Object.keys(d.CAST)) cast[k] = await d.userId(k);
  const people = {};    // key → { id, email, client }
  const noteBlock = { slice: {} };

  // the breach check on new passwords asks an online service: switch it off while the people choose their passwords, on again at the end
  await reauth(admin);
  await soft("breach check off", async () => must(await admin.patch("/api/settings", { requireBreachCheck: false }), "settings"));

  // ================================================================ 1. profiles of the cast (department, phone, language)
  const castProfiles = {
    admin: { phone: "+62 812 1000 0020", department: "IT & Information Security", jobTitle: "System Administrator" },
    useradmin: { phone: "+62 812 1000 0021", department: "IT & Information Security", jobTitle: "IT Service Desk Lead" },
    auditor: { phone: "+62 812 1000 0022", department: "Internal Audit", jobTitle: "Information Security Auditor" },
    rizky: { phone: "+62 812 1000 0014", department: "Operations", jobTitle: "Operations Officer" },
  };
  for (const k of Object.keys(d.CAST)) {
    const c = await d.as(k);
    if (castProfiles[k]) await soft(`profile ${k}`, async () => must(await c.patch("/api/me/profile", castProfiles[k]), `profile ${k}`));
    await soft(`preferences ${k}`, async () => must(await c.patch("/api/me/preferences", { language: "id", timezone: "Asia/Jakarta" }), `preferences ${k}`));
  }
  log("  profiles: 14 cast profiles with department, phone, language id and Asia/Jakarta");

  // ================================================================ 2. e-mail wording (every later mail carries it)
  await reauth(admin);
  await soft("e-mail wording", async () => must(await admin.put("/api/settings/email-wording", {
    senderName: "GA Helpdesk Annaba Digital", subjectPrefix: "[GA Helpdesk]",
    footerEn: "Thank you for using the GA Helpdesk. For urgent matters call the General Affairs desk on ext. 200.\nThis is an automatic message; replying by e-mail is also read by our team.",
    footerId: "Terima kasih telah menggunakan GA Helpdesk. Untuk hal mendesak, hubungi meja General Affairs di ext. 200.\nPesan ini dikirim otomatis; balasan lewat e-mail tetap kami baca.",
  }), "email wording"));
  log("  email wording: sender name, subject prefix and closing lines (EN + ID)");

  // ================================================================ 3. custom roles
  await reauth(admin);
  const perms = rows(must(await admin.get("/api/permissions"), "permissions")).map((p) => p.key);
  const keep = (list) => list.filter((p) => perms.includes(p));
  const roleSpecs = [
    { name: "Finance Viewer", description: "Melihat biaya, anggaran, aset dan vendor untuk keperluan keuangan. Tidak dapat mengubah apa pun.", permissions: keep(["cost.read", "budgets.read", "assets.read", "vendors.read", "workorders.read", "locations.read", "kb.read"]) },
    { name: "Facilities Coordinator", description: "Mengoordinasikan pekerjaan gedung: melihat dan menugaskan tiket tim, mengelola work order, vendor dan jadwal perawatan.", permissions: keep(["tickets.read_team", "tickets.assign", "tickets.transition", "workorders.read", "workorders.manage", "workorders.complete", "vendors.read", "pm.read", "pm.manage", "assets.read", "locations.read", "inventory.read", "kb.read"]) },
    { name: "HR Onboarding", description: "Mengundang karyawan baru dan melihat daftar pengguna untuk proses onboarding.", permissions: keep(["users.read", "invitations.read", "invitations.manage", "roles.read"]) },
  ];
  const custom = {};
  for (const spec of roleSpecs) {
    const have = roleList.find((r) => r.name === spec.name);
    custom[spec.name] = have ?? must(await admin.post("/api/roles", spec), `role ${spec.name}`);
  }
  // the access matrix: Facilities Coordinator also sees all bookings (one saved matrix change)
  await soft("matrix change", async () => {
    await reauth(admin);
    const m = must(await admin.get("/api/access-matrix"), "matrix");
    if (perms.includes("bookings.read_all")) must(await admin.put("/api/access-matrix", { expectedVersion: m.version, changes: [{ roleId: custom["Facilities Coordinator"].id, add: ["bookings.read_all"], remove: [] }] }), "matrix save");
  });
  const roleOf = (n) => custom[n].id;
  log(`  roles: 3 custom roles (${Object.keys(custom).join(", ")}), one matrix change`);

  // ================================================================ 4. colleagues: invitation → acceptance → profile, manager, roles
  mark("people.start");
  // 4a. invitations that stay in other states first: revoked, expired
  const invite = async (c, email, roles, message) => must(await c.post("/api/invitations", { email, roleIds: roles, message }), `invite ${email}`);
  const baseRole = roleId("user");
  const revoked = await invite(daniel, "hasan.basri@annabadigital.io", [baseRole], "Selamat bergabung di tim Sales Bandung.");
  must(await daniel.post(`/api/invitations/${revoked.id}/revoke`), "revoke invitation");
  const expired = await invite(daniel, "novi.andriani@annabadigital.io", [baseRole], "Mohon aktifkan akun Anda sebelum tanggal 1.");

  // 4b. the colleagues
  let ipN = 20;
  const acceptWith = async (email, nameText) => {
    let token = linkFromLog(email, "/invite");
    if (!token) { token = randomBytes(32).toString("base64url"); d.sql(`UPDATE invitations SET token_hash='${sha256(token)}' WHERE email='${qs(email)}' AND status ILIKE 'pending'`); }
    const r = await anon(d, "POST", "/api/invitations/accept", { body: { token, name: nameText, password: d.PASSWORD }, ip: `36.${60 + (ipN % 30)}.${100 + ipN}.${ipN++}`, ua: UA.chromeWin });
    if (!r.ok) throw new Error(`accept ${email} failed (${r.status}): ${r.text.slice(0, 200)}`);
  };
  const colleagueInvites = {};
  for (const p of COLLEAGUES) {
    const inviter = ["wawan", "yusuf", "siti"].includes(p.k) ? admin : daniel;
    colleagueInvites[p.k] = await invite(inviter, p.email, [baseRole], `Halo ${p.name.split(" ")[0]}, selamat bergabung. Silakan aktifkan akun GA Helpdesk Anda lewat tautan ini.`);
  }
  await d.sleep(400);
  for (const p of COLLEAGUES) {
    await acceptWith(p.email, p.name); mark(`accept.${p.k}`);
    const detail = rows(must(await admin.get(`/api/users?q=${encodeURIComponent(p.email)}&pageSize=5`), "find user")).find((u) => u.email === p.email);
    p.id = detail.id; people[p.k] = p;
    must(await daniel.patch(`/api/users/${p.id}`, { phone: p.phone, department: p.dept, jobTitle: p.title }), `profile of ${p.k}`);
    const mgrId = p.mgr ? (cast[p.mgr] ?? people[p.mgr]?.id) : null;
    if (mgrId) must(await daniel.put(`/api/users/${p.id}/manager`, { managerId: mgrId }), `manager of ${p.k}`);
  }
  // each person signs in (their own device), so there are sessions and a last sign-in
  for (const p of COLLEAGUES) { p.client = await d.asDevice(p.email, UA[p.ua]); p.loginAt = mark(`login.${p.k}`); }
  log(`  people: ${COLLEAGUES.length} colleagues invited, accepted, with department/manager/phone and a first sign-in`);

  // 4c. CSV import (three interns, pending invitations): preview first, then for real
  await reauth(daniel);
  const dry = must(await daniel.post("/api/users/import", { csv: INTERNS_CSV + `\nLukman Hakim,lukman-hakim-at-annabadigital,+62 813 5500 2104,Operations,Intern`, roleIds: [baseRole], dryRun: true }), "import preview");
  const imp = must(await daniel.post("/api/users/import", { csv: INTERNS_CSV, roleIds: [baseRole], dryRun: false }), "import");
  mark("import");
  log(`  import: preview ${dry.total} rows (${dry.failed} with an error), then ${imp.created} interns created (pending)`);

  // 4d. invitations that are still pending: one is for the presenter to accept live
  const live = await invite(daniel, "farhan.alamsyah@annabadigital.io", [baseRole], "Selamat bergabung di tim Customer Success, Farhan. Aktifkan akun Anda lewat tautan ini.");
  await invite(admin, "gita.permata@annabadigital.io", [baseRole], "Halo Gita, ini undangan untuk akun GA Helpdesk Anda di tim Marketing.");
  let liveToken = linkFromLog("farhan.alamsyah@annabadigital.io", "/invite");
  if (!liveToken) { liveToken = randomBytes(32).toString("base64url"); d.sql(`UPDATE invitations SET token_hash='${sha256(liveToken)}' WHERE id='${live.id}'`); }
  // the expired one: created 12 days ago, valid until 5 days ago
  d.sql(`UPDATE invitations SET created_at = now() - interval '12 days', expires_at = now() - interval '5 days' WHERE id='${expired.id}'`);

  // ================================================================ 5. roles for the colleagues (the cast keeps theirs)
  const setRoles = async (k, keys) => { await reauth(admin); const ids = keys.map((x) => (x.startsWith("custom:") ? roleOf(x.slice(7)) : roleId(x))); must(await admin.put(`/api/users/${people[k].id}/roles`, { roleIds: ids }), `roles of ${k}`); };
  await setRoles("wawan", ["user", "custom:Facilities Coordinator"]);
  await setRoles("yusuf", ["user", "ga_agent"]);
  await setRoles("bambang", ["user", "custom:Finance Viewer"]);
  await setRoles("wulan", ["user", "custom:HR Onboarding"]);
  await setRoles("ahmad", ["user", "receptionist"]);       // covers the front desk in Bandung for now (revoked in the first access review)
  mark("roles");
  log("  roles: Facilities Coordinator, Finance Viewer, HR Onboarding, GA Agent, Receptionist given to colleagues (the cast is untouched)");

  // ================================================================ 6. access requests: approved, rejected, cancelled, pending
  const req = async (c, roleName, reason) => {
    const mine = must(await c.get("/api/access-requests/roles"), "requestable roles");
    const role = mine.find((r) => r.name === roleName); if (!role) throw new Error(`role ${roleName} is not requestable for ${c.email}`);
    return must(await c.post("/api/access-requests", { roleId: role.id, reason }), `request ${roleName}`);
  };
  const rSiti = await req(people.siti.client, "Finance Viewer", "Saya membutuhkan akses laporan biaya dan anggaran untuk rekonsiliasi tagihan vendor setiap akhir bulan.");
  const rYusuf = await req(people.yusuf.client, "Facilities Coordinator", "Saya menggantikan Wawan sebagai koordinator shift hari Sabtu, perlu menugaskan tiket dan melihat work order tim.");
  const rDewi = await req(people.dewi.client, "GA Supervisor", "Saya ingin ikut menugaskan tiket GA untuk kantor cabang Bandung agar lebih cepat ditangani.");
  const rWulan = await req(people.wulan.client, "Security Auditor", "Untuk keperluan investigasi internal HR saya ingin melihat log audit sign-in karyawan.");
  const rRina = await req(people.rina.client, "Receptionist", "Saya sering menerima tamu klien di kantor Bandung, mohon akses untuk mendaftarkan tamu.");
  must(await people.rina.client.post(`/api/access-requests/${rRina.id}/cancel`), "cancel request");
  // pending: for the presenter to decide
  const rizky = await d.as("rizky"), fajar = await d.as("fajar"), anita = await d.as("anita");
  const pendRizky = await req(rizky, "Facilities Coordinator", "Saya mengoordinasikan proyek relokasi lantai 3 dan perlu melihat jadwal work order serta menugaskan tiket tim.");
  await req(fajar, "Finance Viewer", "Saya perlu membaca laporan biaya dan anggaran perawatan gedung untuk rekonsiliasi bulanan Finance.");
  await req(anita, "HR Onboarding", "Saya membantu proses onboarding karyawan baru setiap bulan dan perlu mengirim undangan akun.");
  await req(people.yoga.client, "Finance Viewer", "Saya perlu memantau realisasi biaya acara dan kampanye Marketing terhadap anggaran.");
  await reauth(admin);
  must(await admin.post(`/api/access-requests/${rSiti.id}/approve`, { note: "Disetujui: sesuai tugas rekonsiliasi Finance." }), "approve siti");
  await reauth(admin);
  must(await admin.post(`/api/access-requests/${rYusuf.id}/approve`, { note: "Disetujui sementara untuk shift Sabtu." }), "approve yusuf");
  must(await admin.post(`/api/access-requests/${rDewi.id}/reject`, { note: "Belum sesuai peran. Silakan ajukan lewat Head of Operations." }), "reject dewi");
  must(await daniel.post(`/api/access-requests/${rWulan.id}/reject`, { note: "Akses log audit tidak diperlukan untuk peran HR Officer." }), "reject wulan");
  mark("requests");
  log("  access requests: 2 approved, 2 rejected, 1 cancelled, 4 pending (rizky, fajar, anita, yoga)");

  // ================================================================ 7. directory: group → role mappings, SCIM groups, HR CSV sync
  await reauth(admin);
  const mappings = [["SG-GA-Teknisi", roleId("ga_agent")], ["SG-Finance-Viewer", roleOf("Finance Viewer")], ["SG-Facilities-Coordinator", roleOf("Facilities Coordinator")], ["SG-HR-Onboarding", roleOf("HR Onboarding")]];
  const haveMap = rows(must(await admin.get("/api/directory/mappings"), "mappings"));
  for (const [group, rid] of mappings) if (!haveMap.some((m) => m.group === group)) must(await admin.post("/api/directory/mappings", { group, roleId: rid }), `mapping ${group}`);
  await reauth(admin);
  const scimKey = must(await admin.post("/api/integrations/keys", { name: "Provisioning Azure AD (SCIM)", scopes: ["directory.write"] }), "SCIM key");
  mark("scimkey");
  const scim = async (method, path, body) => { const r = await anon(d, method, `/api/v1/scim/v2${path}`, { bearer: scimKey.token, body, headers: { "Content-Type": "application/scim+json" }, ip: "10.20.0.5" }); if (!r.ok) throw new Error(`SCIM ${method} ${path} failed (${r.status}): ${r.text.slice(0, 200)}`); return r.data; };
  // the identity provider keeps the colleagues' own ids (externalId) and the groups
  for (const p of COLLEAGUES) await soft(`scim ${p.k}`, async () => scim("PATCH", `/Users/${p.id}`, { schemas: ["urn:ietf:params:scim:api:messages:2.0:PatchOp"], Operations: [{ op: "replace", value: { externalId: `aad-${sha256(p.email).slice(0, 12)}` } }] }));
  const groups = [["SG-GA-Teknisi", ["yusuf"]], ["SG-Finance-Viewer", ["bambang", "siti"]], ["SG-Facilities-Coordinator", ["wawan", "yusuf"]], ["SG-HR-Onboarding", ["wulan", "retno"]]];
  for (const [g, members] of groups) await soft(`scim group ${g}`, async () => scim("POST", "/Groups", { schemas: ["urn:ietf:params:scim:schemas:core:2.0:Group"], displayName: g, members: members.map((m) => ({ value: people[m].id })) }));
  // the HR file: employee ID and place for everyone (cast included; they keep manager, roles and status), managers for the colleagues, one person left the company
  const hrLine = (email, emp, sc, mgr = "", dept = "", title = "", status = "") => `${email},${emp},${dept},${title},${mgr},${sc},${status}`;
  const castEmp = Object.entries(d.CAST).filter(([k]) => k !== "joko").map(([k, c], i) => hrLine(c.email, `AD-00${String(10 + i).padStart(2, "0")}`, siteCode.HQ ?? ""));
  const hrCsv = ["email,employee_id,department,job_title,manager_email,site_code,status", ...castEmp,
    ...COLLEAGUES.filter((p) => p.k !== "ilham" && p.k !== "nurul").map((p) => hrLine(p.email, p.emp, siteCode[p.site] ?? "", p.mgr ? (d.CAST[p.mgr]?.email ?? people[p.mgr].email) : "", p.dept, p.title)),
    hrLine("pegawai.baru@annabadigital.io", "AD-0199", siteCode.HQ ?? "", "", "Finance", "Staf Baru")].join("\n");
  await reauth(admin);
  const prev = must(await admin.post("/api/directory/sync", { csv: hrCsv, dryRun: true, suspendMissing: false }), "directory preview");
  const applied = must(await admin.post("/api/directory/sync", { csv: hrCsv, dryRun: false, suspendMissing: false }), "directory sync");
  mark("sync");
  noteBlock.hrCsv = hrCsv; noteBlock.scimKey = null;
  log(`  directory: 4 group mappings, ${groups.length} SCIM groups, HR sync previewed (${prev.updated} updates, ${prev.notFound} not found) and applied (${applied.updated} updated)`);

  // ================================================================ 8. status changes: suspended, deactivated (closure), locked, password reset required
  await reauth(daniel);
  must(await daniel.post(`/api/users/${people.ilham.id}/suspend`, { reason: "Dalam pemeriksaan kepatuhan oleh Internal Audit; akses ditangguhkan sementara." }), "suspend ilham");
  mark("suspend");

  // ================================================================ 9. closure requests: pending, approved (waiting period), completed
  await reauth(people.rina.client);
  const cRina = must(await people.rina.client.post("/api/me/closure", { reason: "Saya akan pindah ke perusahaan lain mulai bulan depan, mohon akun saya ditutup." }), "closure rina");
  await reauth(people.ahmad.client);
  const cAhmad = must(await people.ahmad.client.post("/api/me/closure", { reason: "Kontrak saya di cabang Bandung berakhir, mohon akun dihapus sesuai kebijakan privasi." }), "closure ahmad");
  await reauth(people.nurul.client);
  const cNurul = must(await people.nurul.client.post("/api/me/closure", { reason: "Saya sudah resign dari perusahaan. Mohon tutup akun dan hapus data pribadi saya." }), "closure nurul");
  mark("closures");
  await reauth(admin);
  must(await admin.post(`/api/account-closures/${cAhmad.id}/approve`, { note: "Disetujui. Akun ditutup setelah masa tunggu 14 hari." }), "approve closure ahmad");
  must(await admin.post(`/api/account-closures/${cNurul.id}/approve`, { note: "Disetujui, karyawan sudah resign (konfirmasi HR)." }), "approve closure nurul");
  // Nurul's waiting period is over: the background worker closes the account at its next run, so let it (or finish what it does) at the end of the module
  d.sql(`UPDATE account_closure_requests SET execute_after = now() - interval '1 hour' WHERE id='${cNurul.id}'`);
  log("  closures: 1 pending (Rina), 1 approved and waiting (Ahmad), 1 ready to be closed (Nurul)");

  // ================================================================ 10. access reviews: one completed (decided, closed), one open with items left
  const review = async (title, dueInDays) => must(await daniel.post("/api/access-reviews", { name: title, dueInDays }), "start review");
  const items = async (rid, decision = "all") => { const out = []; for (let page = 1; ; page++) { const r = must(await daniel.get(`/api/access-reviews/${rid}/items?pageSize=100&page=${page}&decision=${decision}`), "review items"); out.push(...r.data); if (out.length >= r.total) break; } return out; };
  const decide = async (reviewer, rid, it, decision, note) => { if (decision === "revoke") await reauth(reviewer); must(await reviewer.post(`/api/access-reviews/${rid}/items/${it.id}/decision`, { decision, note }), `decide ${it.userEmail}`); };
  // Nurul, Ilham and Ahmad's situation changed before the review starts: only active people with privileged roles are in it
  const first = await review("Tinjauan akses triwulan III 2026", 14); mark("review1.start");
  const firstItems = await items(first.id);
  for (const it of firstItems) {
    const reviewer = it.userEmail === d.CAST.useradmin.email ? admin : daniel;
    if (it.userEmail === people.ahmad.email) { await decide(admin, first.id, it, "revoke", "Penugasan cover resepsionis Bandung selesai; peran dicabut."); continue; }
    await decide(reviewer, first.id, it, "keep", it.role.key === "super_admin" ? "Administrator platform; tetap dibutuhkan." : "Sesuai tugas pokok jabatan.");
  }
  must(await admin.post(`/api/access-reviews/${first.id}/close`), "close review"); mark("review1.close");
  await soft("review schedule", async () => must(await admin.put("/api/access-reviews/schedule", { enabled: true, everyDays: 90, dueInDays: 14 }), "schedule"));
  const second = await review("Tinjauan akses triwulan IV 2026", 30); mark("review2.start");
  const secondItems = await items(second.id);
  // decide about half: the cast first (kept), colleagues left open so there is something to decide on stage
  let decided = 0;
  for (const it of secondItems) {
    if (decided >= Math.floor(secondItems.length / 2)) break;
    const isCastRole = !Object.values(people).some((p) => p.email === it.userEmail);
    if (!isCastRole) continue;
    const reviewer = it.userEmail === d.CAST.useradmin.email ? admin : daniel;
    if (it.userEmail === d.CAST.admin.email && reviewer === admin) continue;
    await decide(reviewer, second.id, it, "keep", "Masih sesuai peran saat ini."); decided++;
  }
  log(`  access reviews: 1 completed (${firstItems.length} items, 1 revoked), 1 open (${secondItems.length} items, ${decided} decided), quarterly schedule on`);

  // ================================================================ 11. integrations: API keys, webhooks with deliveries (a small local receiver), tickets that raise the events
  const port0 = Number(process.env.DEMO_HOOK_PORT ?? 9191);
  const received = [];
  const receiver = http.createServer((rq, rs) => {
    const chunks = []; rq.on("data", (c) => chunks.push(c)); rq.on("end", () => {
      received.push({ path: rq.url, event: rq.headers["x-annaba-event"] });
      if (rq.url.startsWith("/hooks/erp")) { rs.writeHead(500, { "Content-Type": "application/json" }); rs.end(JSON.stringify({ error: "ERP sedang maintenance" })); }
      else { rs.writeHead(200, { "Content-Type": "application/json" }); rs.end(JSON.stringify({ ok: true })); }
    });
  });
  const hookPort = await new Promise((resolve) => { receiver.once("error", () => receiver.listen(0, "127.0.0.1", () => resolve(receiver.address().port))); receiver.listen(port0, "127.0.0.1", () => resolve(receiver.address().port)); });
  let keyErp, ticketIds = [];
  const putChannel = async (kind, body) => must(await admin.put(`/api/channels/settings/${kind}`, body), `channel ${kind}`);
  let channelsTouched = false;
  try {
    await reauth(admin);
    must(await admin.post("/api/integrations/keys", { name: "Dasbor Monitoring Gedung (BMS)", scopes: ["tickets.read", "catalog.read"] }), "key BMS");
    await reauth(admin);
    keyErp = must(await admin.post("/api/integrations/keys", { name: "Integrasi ERP Keuangan", scopes: ["tickets.read", "tickets.write", "catalog.read"], expiresOn: new Date(Date.now() + 300 * DAY).toISOString().slice(0, 10) }), "key ERP");
    await reauth(admin);
    const old = must(await admin.post("/api/integrations/keys", { name: "Prototipe lama (tidak dipakai)", scopes: ["tickets.read"] }), "key old");
    must(await admin.post(`/api/integrations/keys/${old.key.id}/revoke`), "revoke old key");
    const hookUrl = (path) => `http://127.0.0.1:${hookPort}${path}`;
    const hooks = [];
    for (const [url, description, events] of [
      [hookUrl("/hooks/chatops"), "Notifikasi ke kanal obrolan GA", ["ticket.created", "ticket.assigned"]],
      [hookUrl("/hooks/erp"), "Sinkronisasi status ke ERP Keuangan", ["ticket.status_changed", "ticket.commented", "workorder.completed"]],
      [hookUrl("/hooks/bms"), "Dasbor Monitoring Gedung", ["ticket.created", "ticket.status_changed"]],
    ]) { await reauth(admin); hooks.push(must(await admin.post("/api/integrations/webhooks", { url, description, events, enabled: true }), `webhook ${description}`)); }
    log(`  integrations: 3 API keys (1 revoked), 3 webhooks to a local receiver on port ${hookPort}`);

    // a service for requests that fit nowhere else (e-mail requests land here): made by the GA manager when the catalog has none
    const svcList = rows(must(await admin.get("/api/catalog/services?includeArchived=true"), "services"));
    let general = svcList.find((s) => s.name === "General request" && !s.archivedAt);
    if (!general) general = await soft("general service", async () => {
      const hendra = await d.as("hendra");
      const cats = rows(must(await hendra.get("/api/catalog/categories?includeArchived=true"), "categories")); const cat = cats.find((c) => c.name === "Facilities & Maintenance") ?? cats[0];
      const made = must(await hendra.post("/api/catalog/services", { name: "General request", description: "Permintaan umum yang tidak masuk kategori lain, termasuk yang dikirim lewat e-mail ke helpdesk.", icon: "inbox", categoryId: cat.id, owningTeamId: d.ctx.team.ga.id, defaultPriority: "medium", audience: "everyone" }), "general service");
      must(await hendra.post(`/api/catalog/services/${made.id}/publish`), "publish general service"); return made;
    });

    // tickets from the public API (ERP key) raise ticket.created
    const v1 = async (body) => anon(d, "POST", "/api/v1/tickets", { bearer: keyErp.token, body, ip: "10.20.0.9" });
    if (general) {
      const subjects = [
        ["rizky", "Dispenser air di pantry Lantai 3 bocor, lantai basah"],
        ["anita", "Tolong ganti lampu koridor depan ruang HR yang mati"],
        ["fajar", "Kursi kerja di meja Finance patah sandarannya"],
        ["lina", "Papan nama ruang rapat Merak terlepas dari dinding"],
      ];
      for (const [who, subject] of subjects) {
        const r = await v1({ serviceId: general.id, subject, description: `${subject}. Mohon segera ditindaklanjuti. (dikirim otomatis oleh sistem ERP)`, requesterEmail: d.CAST[who].email });
        if (r.ok) ticketIds.push(r.data.id); else log(`    ! v1 ticket for ${who}: ${r.status} ${r.text.slice(0, 120)}`);
      }
    }

    // the technicians' Teams channel is switched on only for the next few calls: the assignment queues a message, then the channel is switched off again so that
    // the delivery worker fails it ("the channel is switched off"; the e-mail is sent instead) and nothing is ever sent to Microsoft
    const putri = await d.as("putri"), agus = await d.as("agus"), bayu = await d.as("bayu");
    await soft("channels", async () => {
      await putChannel("whatsapp", { enabled: false, settings: { phoneNumberId: "104500000000001", notifyTemplate: "helpdesk_notifikasi", verifyTemplate: "helpdesk_verifikasi", language: "id" }, secrets: { accessToken: "EAAdemo-fake-token-for-demonstration-only" } });
      await putChannel("slack", { enabled: false, secrets: { botToken: "xoxb-0000000000-demo-fake-token" } });
      await putChannel("teams", { enabled: true }); channelsTouched = true;
      const teamsUrl = (n) => `https://prod-${10 + n}.southeastasia.logic.azure.com:443/workflows/0000demo0000fake0000/triggers/manual/paths/invoke?api-version=2016-06-01&sig=demo-fake-${n}`;
      const link = async (c, n, replaceEmail) => { must(await c.post("/api/me/channels/teams", { address: teamsUrl(n) }), "connect teams"); if (replaceEmail) must(await c.put("/api/me/channels/teams", { replaceEmail: true }), "replace e-mail"); };
      await link(agus, 1, true); await link(putri, 2, false); await link(await d.as("hendra"), 3, false);
      const hendraC = await d.as("hendra");
      if (ticketIds[0]) must(await putri.post(`/api/tickets/${ticketIds[0]}/assign`, { assigneeId: cast.agus }), "assign");
      if (ticketIds[1]) must(await putri.post(`/api/tickets/${ticketIds[1]}/assign`, { assigneeId: cast.agus }), "assign 2");
      for (const c of [agus, putri, hendraC]) await soft("teams test", async () => must(await c.post("/api/me/channels/teams/test"), "teams test"));
      await putChannel("teams", { enabled: false });
    });
    // staff move the tickets along: assigned, started, commented, resolved (events for the other endpoints)
    if (ticketIds[0]) {
      await soft("start", async () => must(await agus.post(`/api/tickets/${ticketIds[0]}/transition`, { action: "start" }), "start"));
      await soft("comment", async () => must(await agus.post(`/api/tickets/${ticketIds[0]}/comments`, { body: "Sudah dicek di lokasi: selang dispenser longgar. Saya ganti klem dan selang hari ini.", internal: false }), "comment"));
      await soft("resolve", async () => must(await agus.post(`/api/tickets/${ticketIds[0]}/transition`, { action: "resolve", resolutionCode: "fixed", resolutionNote: "Selang dan klem dispenser sudah diganti, tidak ada kebocoran lagi." }), "resolve"));
    }
    if (ticketIds[1]) await soft("start 2", async () => must(await agus.post(`/api/tickets/${ticketIds[1]}/transition`, { action: "start" }), "start 2"));
    if (ticketIds[2]) await soft("assign 3", async () => must(await putri.post(`/api/tickets/${ticketIds[2]}/assign`, { assigneeId: cast.bayu }), "assign 3"));
    if (ticketIds[2]) await soft("comment 3", async () => must(await bayu.post(`/api/tickets/${ticketIds[2]}/comments`, { body: "Kursi pengganti sedang diambil dari gudang, siang ini saya antar ke meja Finance.", internal: false }), "comment 3"));
    mark("tickets");

    // ================================================================ 12. inbound e-mail
    await soft("inbound mailbox", async () => {
      const mbxService = general?.id;
      must(await admin.put("/api/settings/inbound-email", { address: "helpdesk@annabadigital.io", defaultServiceId: mbxService, enabled: false }), "mailbox");
      await reauth(admin);
      const tok = must(await admin.post("/api/settings/inbound-email/token"), "mailbox token");
      must(await admin.put("/api/settings/inbound-email", { address: "helpdesk@annabadigital.io", defaultServiceId: mbxService, enabled: true }), "mailbox on");
      noteBlock.inboundPath = tok.path;
      const send = (from, fromName, subject, text, id, extra = []) => anon(d, "POST", tok.path, { raw: Buffer.from([`From: "${fromName}" <${from}>`, "To: helpdesk@annabadigital.io", `Subject: ${subject}`, `Message-ID: <${id}@mail.annabadigital.io>`, `Date: ${new Date().toUTCString()}`, "MIME-Version: 1.0", "Content-Type: text/plain; charset=UTF-8", "Content-Transfer-Encoding: 8bit", ...extra, "", text].join("\r\n"), "utf8"), headers: { "Content-Type": "message/rfc822" }, ip: "10.20.0.30" });
      const created = [];
      const mails = [
        [d.CAST.lina.email, "Lina Marlina", "Mohon bantu pindahkan 2 meja ke area event lantai 5", "Selamat siang tim GA,\n\nUntuk acara peluncuran produk Jumat depan kami butuh 2 meja lipat dan 20 kursi dipindahkan ke area event lantai 5.\nTerima kasih.\n\nLina - Marketing", "m1"],
        [people.retno.email, "Retno Wulandari", "Kehilangan laptop kantor di ruang rapat Garuda", "Dengan hormat,\n\nSaya melaporkan laptop inventaris HR (tag AD-LT-0231) tertinggal dan hilang di ruang rapat Garuda setelah sesi wawancara kemarin sore. Mohon dibantu cek CCTV dan penelusuran oleh tim GA.\n\nTerima kasih,\nRetno", "m2"],
        [people.siti.email, "Siti Nurhaliza", "Permintaan kunci laci arsip Finance cadangan", "Halo GA,\n\nKunci laci arsip Finance (lemari 3) hilang. Mohon dibuatkan kunci cadangan dan dua duplikat.\n\nSiti", "m3"],
        [d.CAST.fajar.email, "Fajar Ramadhan", "AC ruang Finance bocor menetes ke meja", "Pagi,\n\nAC di ruang Finance menetes ke meja kerja sejak kemarin. Mohon dicek teknisi.\n\nFajar", "m4"],
      ];
      for (const [from, nm, subject, text, id] of mails) { const r = await send(from, nm, subject, text, id); if (r.ok && r.data?.ticketNumber) created.push(r.data); else log(`    ! inbound mail ${id}: ${r.status} ${r.text.slice(0, 160)}`); }
      // a reply to the first ticket, a duplicate, an auto-reply and a sender nobody knows
      if (created[0]?.ticketNumber) await send(d.CAST.lina.email, "Lina Marlina", `Re: [${created[0].ticketNumber}] Mohon bantu pindahkan 2 meja ke area event lantai 5`, "Tambahan: acaranya mulai pukul 09.00, jadi mohon sudah siap pukul 08.00.\n\nOn Mon, GA Helpdesk wrote:\n> Permintaan Anda sudah kami terima", "m5");
      await send(d.CAST.lina.email, "Lina Marlina", "Mohon bantu pindahkan 2 meja ke area event lantai 5", "Selamat siang tim GA,\n\nUntuk acara peluncuran produk Jumat depan...", "m1");
      await send(d.CAST.anita.email, "Anita Rahmawati", "Out of office: Anita Rahmawati", "Saya sedang cuti sampai 24 November.", "m6", ["Auto-Submitted: auto-replied"]);
      await send("tawaran.promosi@gmail.com", "Promo Kantor", "Penawaran spesial ATK untuk perusahaan Anda", "Dapatkan diskon 30% untuk alat tulis kantor...", "m7");
      noteBlock.inboundTickets = created.map((c) => c.ticketNumber);
    });

    // wait for the deliveries of the working endpoints
    for (let i = 0; i < 40; i++) {
      const list = rows(must(await admin.get("/api/integrations/webhooks"), "webhooks")); const ok = list.filter((w) => !w.url.includes("/hooks/erp"));
      const untried = Number(d.sql("SELECT count(*) FROM webhook_deliveries WHERE attempts = 0 AND delivered_at IS NULL AND failed_at IS NULL").trim());
      if (ok.every((w) => w.pending === 0) && untried === 0) break; await d.sleep(1500);
    }
    // an abandoned delivery: the ERP endpoint refused it again and again
    d.sql(`UPDATE webhook_deliveries SET attempts = 8, failed_at = now() - interval '2 days', last_status = 500, last_error = 'HTTP 500' WHERE id = (SELECT id FROM webhook_deliveries WHERE endpoint_id = '${hooks[1].webhook.id}' AND delivered_at IS NULL ORDER BY created_at LIMIT 1)`);
    const finalHooks = rows(must(await admin.get("/api/integrations/webhooks"), "webhooks"));
    log(`  webhooks: ${received.length} deliveries answered by the local receiver; the ERP endpoint keeps answering 500 (${finalHooks.find((w) => w.url.includes("/hooks/erp"))?.pending ?? "?"} retrying, 1 abandoned)`);
    // the receiver is closed when this module ends: switch the endpoints off so that the tickets of the later modules (and of the demo itself) do not pile up failing deliveries.
    // The deliveries above stay in the log; the presenter switches an endpoint on again after starting a receiver (see the notes).
    for (const w of finalHooks) await soft("switch webhook off", async () => must(await admin.put(`/api/integrations/webhooks/${w.id}`, { url: w.url, description: w.description, events: w.events, enabled: false, version: w.version }), "webhook off"));
  } finally { receiver.closeAllConnections?.(); await new Promise((r) => receiver.close(() => r())); }

  // ================================================================ 13. retention, legal hold, settings
  await reauth(admin);
  await soft("system settings", async () => must(await admin.patch("/api/settings", { emailFrom: "GA Helpdesk Annaba Digital <no-reply@annabadigital.io>", auditRetentionDays: 730, invitationExpiryDays: 7 }), "settings"));
  const policy = { enabled: true, finishedTicketDays: 730, ticketAction: "anonymise", attachmentDays: 365, notificationDays: 180 };
  await soft("retention", async () => {
    await reauth(admin);
    must(await admin.put("/api/retention", policy), "retention policy");
    must(await admin.post("/api/retention/preview", { ...policy, finishedTicketDays: 90, attachmentDays: 60, notificationDays: 30 }), "retention preview");
    await reauth(admin);
    must(await admin.post("/api/retention/run"), "retention run");
  });
  await soft("legal hold", async () => {
    const lost = rows(must(await admin.get(`/api/tickets?q=${encodeURIComponent("Kehilangan laptop")}&pageSize=5`), "find ticket"))[0];
    if (!lost) throw new Error("ticket not found");
    await reauth(admin);
    must(await admin.put(`/api/tickets/${lost.id}/legal-hold`, { reason: "Dugaan kehilangan aset; sedang diperiksa Internal Audit dan pihak berwenang. Jangan dihapus." }), "legal hold");
  });
  log("  retention: policy on (730/365/180 days), preview and a manual run, one legal hold; system settings tuned (breach check back on below)");

  // ================================================================ 14. channels, part 2: the worker has failed the queued messages; switch the channels on for the demo, one person pauses theirs
  if (channelsTouched) await soft("channels flush", async () => {
    for (let i = 0; i < 30; i++) { const pend = d.sql("SELECT count(*) FROM channel_deliveries WHERE delivered_at IS NULL AND failed_at IS NULL").trim(); if (pend === "0") break; await d.sleep(5000); }
    const stuck = Number(d.sql("SELECT count(*) FROM channel_deliveries WHERE delivered_at IS NULL AND failed_at IS NULL").trim());
    // WhatsApp and Slack are on (nobody is linked yet, so nothing is queued). Teams stays OFF on purpose: the three people who linked it would otherwise get every later
    // notification of the demo sent to a fake address on Microsoft's servers. The presenter can switch it on (the message then fails and the e-mail is sent instead).
    await putChannel("whatsapp", { enabled: true }); await putChannel("slack", { enabled: true });
    const dl = rows(must(await admin.get("/api/channels/deliveries"), "deliveries"));
    log(`  channels: WhatsApp and Slack on, Teams set up but off (linked by agus instead of e-mail, putri, hendra); ${dl.length} deliveries logged (${dl.filter((x) => x.failedAt).length} failed, ${dl.filter((x) => x.fellBack).length} fell back to e-mail${stuck ? `, ${stuck} still waiting for the worker` : ""})`);
  });

  // ================================================================ 15. tenants
  await soft("tenants", async () => {
    await reauth(admin);
    const have = rows(must(await admin.get("/api/tenants"), "tenants"));
    const mk = async (nm, slug, adminEmail) => have.find((t) => t.slug === slug) ?? must(await admin.post("/api/tenants", { name: nm, slug, adminEmail }), `tenant ${slug}`);
    await mk("PT Annaba Logistik Nusantara", "annaba-logistik", "admin@annabalogistik.co.id");
    const t2 = await mk("CV Mitra Properti Sejahtera", "mitra-properti", "owner@mitraproperti.co.id");
    if (t2.status !== "suspended") { await reauth(admin); must(await admin.post(`/api/tenants/${t2.id}/suspend`), "suspend tenant"); }
    // the active organisation tightens its own security policy: needs the platform administrator in support mode (their own, separate session)
    const t1 = rows(must(await admin.get("/api/tenants"), "tenants")).find((t) => t.slug === "annaba-logistik");
    const sup = await d.asDevice("admin", UA.edgeWin);
    try {
      await reauth(sup); must(await sup.post(`/api/tenants/${t1.id}/support`), "support mode");
      await reauth(sup); must(await sup.patch("/api/settings/tenant", { values: { passwordMinLength: 14, sessionIdleMinutes: 20, lockoutAttempts: 4, mfaPolicy: "required", invitationExpiryDays: 5 } }), "tenant policy");
    } finally { await soft("leave support mode", async () => must(await sup.post("/api/tenants/support/exit"), "support exit")); }
    log("  tenants: 2 organisations created (PT Annaba Logistik Nusantara active with its own stricter security policy, CV Mitra Properti Sejahtera suspended)");
  });

  // ================================================================ 16. notification preferences of a few people
  const prefsFor = {
    hendra: { "sla.at_risk": [true, true], "ticket.started": [true, false], "ticket.reply": [true, true] },
    putri: { "ticket.note": [false, false], "ticket.hold": [true, true], "sla.at_risk": [true, true] },
    sinta: { "ticket.closed": [false, false], "ticket.cancelled": [false, false] },
    rizky: { "ticket.started": [true, true], "ticket.closed": [true, true] },
    anita: { "ticket.reply": [true, false], "ticket.resolved": [true, true] },
    auditor: { "ticket.assigned": [false, false] },
  };
  for (const [who, map] of Object.entries(prefsFor)) await soft(`notification preferences ${who}`, async () => {
    const c = await d.as(who); const cur = must(await c.get("/api/notifications/preferences"), "prefs");
    const itemsOut = Object.entries(map).filter(([kind]) => cur.items.some((i) => i.kind === kind && !i.locked)).map(([kind, [inApp, email]]) => ({ kind, inApp, email }));
    if (itemsOut.length) must(await c.put("/api/notifications/preferences", { items: itemsOut }), "save prefs");
  });
  await soft("email preferences", async () => { must(await (await d.as("anita")).patch("/api/me/preferences", { notifyProduct: false }), "prefs"); must(await (await d.as("auditor")).patch("/api/me/preferences", { notifySecurity: true, timezone: "Asia/Jakarta" }), "prefs"); });
  log(`  notification settings: customised for ${Object.keys(prefsFor).length} people`);

  // ================================================================ 17. sessions on several devices for the people who open /sessions, MFA
  const devices = [
    ["admin", UA.safariMac], ["admin", UA.iphone], ["admin", UA.firefoxLinux], ["useradmin", UA.edgeWin], ["useradmin", UA.android], ["auditor", UA.firefoxWin], ["auditor", UA.safariMac],
    ["hendra", UA.android], ["putri", UA.ipad], ["rizky", UA.iphone], ["sinta", UA.edgeWin], ["dimas", UA.chromeWin],
  ];
  const deviceClients = [];
  for (const [who, ua] of devices) { const c = await d.asDevice(who, ua); const cur = rows(must(await c.get("/api/sessions/me"), "my sessions")).find((x) => x.current || x.isCurrent); deviceClients.push([who, c, cur?.id]); }
  // MFA: Bambang keeps it, Yoga loses her phone and an administrator resets it
  const enrol = async (p) => { const c = p.client; await reauth(c); const e = must(await c.post("/api/me/mfa/enroll"), "mfa enroll"); must(await c.post("/api/me/mfa/confirm", { code: totp(e.secret) }), "mfa confirm"); };
  await soft("mfa bambang", async () => enrol(people.bambang));
  await soft("mfa yoga", async () => { await enrol(people.yoga); await reauth(admin); must(await admin.post(`/api/users/${people.yoga.id}/reset-mfa`, { reason: "Ponsel hilang dan kode pemulihan tidak tersimpan; identitas dikonfirmasi lewat HR." }), "reset mfa"); });
  log(`  sessions: ${deviceClients.length} extra devices for the cast; MFA on for Bambang, reset for Yoga`);

  // ================================================================ 18. security activity: failed sign-ins, a lockout, password resets, revoked sessions
  const fail = async (email, ip, ua, n = 1) => { for (let i = 0; i < n; i++) await anon(d, "POST", "/api/auth/login", { body: { email, password: `Salah-${randomBytes(3).toString("hex")}!` }, ip, ua }); };
  await fail("admin@annabadigital.io", "45.155.205.233", UA.firefoxLinux, 3);
  await fail("finance@annabadigital.io", "185.220.101.34", UA.chromeWin, 2);
  await fail("hendra.kusuma@annabadigital.id", "103.28.12.45", UA.chromeWin, 2);
  await fail("sarah@gmail.com", "36.72.214.8", UA.android, 1);
  await fail("ceo@annabadigital.io", "114.125.34.201", UA.edgeWin, 2);
  await fail(people.siti.email, "182.253.67.9", UA.firefoxWin, 2);
  await fail(people.wawan.email, "36.79.201.140", UA.android, 1);
  await fail(people.dian.email, "112.215.20.88", UA.safariMac, 5);          // the fifth failure locks the account
  d.sql(`UPDATE users SET locked_until = now() + interval '3 days' WHERE id = '${people.dian.id}'`);   // a lockout of 15 minutes would be over before anyone looks
  mark("lockout");
  // forgot password → reset (Wulan); forgot password for an address nobody has
  await anon(d, "POST", "/api/auth/forgot-password", { body: { email: "tidak.ada@annabadigital.io" }, ip: "36.72.214.8" });
  await anon(d, "POST", "/api/auth/forgot-password", { body: { email: people.wulan.email }, ip: "103.147.8.22", ua: UA.chromeWin });
  await d.sleep(300);
  let rt = linkFromLog(people.wulan.email, "/reset-password");
  if (!rt) { rt = randomBytes(32).toString("base64url"); d.sql(`UPDATE password_reset_tokens SET token_hash='${sha256(rt)}' WHERE user_id='${people.wulan.id}' AND consumed_at IS NULL`); }
  await soft("reset password", async () => { const r = await anon(d, "POST", "/api/auth/reset-password", { body: { token: rt, password: "Matahari-Pagi-Jakarta-2026!" }, ip: "103.147.8.22" }); if (!r.ok) throw new Error(`${r.status} ${r.text.slice(0, 160)}`); });
  // a person must choose a new password (administrator action)
  await soft("require reset", async () => { await reauth(admin); must(await admin.post(`/api/users/${people.siti.id}/require-password-reset`, { reason: "Akun terindikasi dipakai di perangkat tidak dikenal; wajib ganti kata sandi." }), "require reset"); });
  // an administrator ends a session of a colleague that looks wrong
  await soft("revoke session", async () => { await reauth(admin); const ses = rows(must(await admin.get(`/api/users/${people.ahmad.id}/sessions`), "sessions")); if (ses[0]) must(await admin.del(`/api/users/${people.ahmad.id}/sessions?sessionId=${ses[0].id}`), "revoke"); });
  // one more role change that is later undone is part of normal history: Wawan loses and regains a role
  log("  security: failed sign-ins (known and made-up addresses), one lockout (Dian), password reset (Wulan), reset required (Siti), a revoked session");

  // ================================================================ 19. the completed closure (Nurul) and the live invitation
  let closed = false;
  for (let i = 0; i < 3 && !closed; i++) { await d.sleep(2000); closed = d.sql(`SELECT status FROM account_closure_requests WHERE id='${cNurul.id}'`).trim().toLowerCase() === "completed"; }
  if (!closed) {
    // the background worker has not run yet (it starts a couple of minutes after the API and then runs hourly): do what its closing step does
    const uid = people.nurul.id; const closedMail = `closed-${uid.replace(/-/g, "")}@closed.invalid`;
    d.sql(`BEGIN;
      UPDATE account_closure_requests SET status = 'Completed', completed_at = now() WHERE id = '${cNurul.id}' AND status ILIKE 'approved';
      DELETE FROM sessions WHERE user_id = '${uid}'; DELETE FROM mfa_recovery_codes WHERE user_id = '${uid}'; DELETE FROM mfa_factors WHERE user_id = '${uid}';
      DELETE FROM user_identities WHERE user_id = '${uid}'; DELETE FROM email_verification_tokens WHERE user_id = '${uid}'; DELETE FROM password_reset_tokens WHERE user_id = '${uid}';
      DELETE FROM avatars WHERE user_id = '${uid}'; DELETE FROM user_credentials WHERE user_id = '${uid}';
      UPDATE invitations SET email = '${closedMail}', message = NULL WHERE accepted_user_id = '${uid}' OR email = '${qs(people.nurul.email)}';
      DELETE FROM user_roles WHERE user_id = '${uid}';
      UPDATE users SET status = 'Deactivated', name = 'Closed account', email = '${closedMail}', deactivated_at = now(), updated_at = now(), version = version + 1 WHERE id = '${uid}';
      UPDATE user_profiles SET phone = NULL, department = NULL, job_title = NULL, avatar_id = NULL WHERE user_id = '${uid}';
      COMMIT;`);
    log("  closure: Nurul's account closed (finished by SQL because the background worker had not run yet)");
  }

  // ================================================================ 20. back to the intended policy, security events acknowledged, audit export
  await reauth(admin);
  await soft("breach check on", async () => must(await admin.patch("/api/settings", { requireBreachCheck: true }), "settings"));
  const ev = rows(must(await maya.get("/api/security-events?pageSize=100"), "security events"));
  const sorted = [...ev].sort((a, b) => new Date(a.ts) - new Date(b.ts));
  const lowMedium = sorted.filter((e) => e.severity !== "high");
  const toAck = lowMedium.slice(0, Math.floor(lowMedium.length * 0.6));      // the oldest low/medium ones; every high one is left for the presenter
  for (const e of toAck) await maya.post(`/api/security-events/${e.id}/ack`);
  await soft("audit export", async () => { const r = await maya.get("/api/audit/export?format=csv"); if (!r.ok) throw new Error(`${r.status}`); });
  log(`  security events: ${ev.length} raised, ${toAck.length} acknowledged by the auditor, the rest left for the presenter`);

  // ================================================================ 21. dates, sessions that stay valid, realistic addresses
  if (!process.env.DEMO_NO_BACKDATE) backdate();

  // ---------------------------------------------------------------- summary
  const st = must(await admin.get("/api/users/stats"), "stats");
  log(`  users: ${st.total} people (${st.active} active, ${st.pending} pending, ${st.suspended} suspended, ${st.deactivated} deactivated, ${st.locked} locked)`);
  log(`  >> LIVE INVITATION for the presenter: ${d.origin.replace(/\/$/, "")}/invite?token=${liveToken}   (farhan.alamsyah@annabadigital.io)`);
  if (noteBlock.inboundPath) log(`  >> inbound e-mail webhook path: ${noteBlock.inboundPath}`);
  d.ctx.admin = { liveInvitation: `${d.origin.replace(/\/$/, "")}/invite?token=${liveToken}`, hrCsv: noteBlock.hrCsv, interns: INTERNS_CSV, people: Object.fromEntries(Object.entries(people).map(([k, p]) => [k, { id: p.id, email: p.email }])) };

  // ---------------------------------------------------------------- local functions that need the run state
  function backdate() {
    const t1 = Date.now(), now = t1; const D = 85 * DAY, span = Math.max(1, t1 - T0);
    const F = (ms) => new Date(now - D * Math.pow(Math.max(0, 1 - (ms - T0) / span), 1.25));   // run clock → a date in the last 85 days (later events stay close to today)
    const iso = (dt) => dt.toISOString();
    const parts = [];
    // audit trail + security events of this module: the same mapping, in SQL (the audit table is append-only: its guard trigger is off for this one transaction only)
    const f = `to_timestamp(${now / 1000} - ${D / 1000} * power(greatest(0, 1 - (extract(epoch from COL) - ${T0 / 1000}) / ${span / 1000}), 1.25))`;
    parts.push(`ALTER TABLE audit_events DISABLE TRIGGER audit_events_no_update;`);
    parts.push(`UPDATE audit_events SET ts = ${f.replace(/COL/g, "ts")} WHERE id > ${auditFrom} AND ts >= to_timestamp(${T0 / 1000});`);
    parts.push(`UPDATE audit_events SET ts = now() - interval '30 hours' + (id - ${auditFrom}) * interval '20 seconds' WHERE id > ${auditFrom} AND action IN ('directory.scim','directory.sync','directory.mapping_add');`);
    parts.push(`ALTER TABLE audit_events ENABLE TRIGGER audit_events_no_update;`);
    parts.push(`UPDATE security_events SET acknowledged_at = LEAST(now(), ${f.replace(/COL/g, "ts")} + interval '5 hours' * (1 + (extract(epoch from ts)::bigint % 7))) WHERE acknowledged_at IS NOT NULL AND ts >= '${secFrom}'::timestamptz;`);
    parts.push(`UPDATE security_events SET ts = ${f.replace(/COL/g, "ts")} WHERE ts >= '${secFrom}'::timestamptz;`);
    // people: created when they accepted; invitation dates to match; last sign-in and session times
    for (const p of COLLEAGUES) {
      const joined = F(stamp[`accept.${p.k}`]);
      parts.push(`UPDATE users SET created_at = '${iso(joined)}' WHERE id = '${p.id}';`);
      parts.push(`UPDATE invitations SET created_at = '${iso(new Date(joined.getTime() - 2 * DAY))}', accepted_at = '${iso(joined)}', expires_at = '${iso(new Date(joined.getTime() + 5 * DAY))}' WHERE accepted_user_id = '${p.id}' OR email = '${qs(p.email)}';`);
      const seenMs = now - p.seen * DAY;
      parts.push(`UPDATE users SET last_login_at = '${iso(new Date(seenMs))}' WHERE id = '${p.id}' AND status = 'Active';`);
      parts.push(`UPDATE sessions SET created_at = '${iso(new Date(seenMs - 40 * 60e3))}', last_active_at = '${iso(new Date(seenMs))}', ip = '${OFFICE_IPS[Math.abs(sha256(p.email).charCodeAt(2)) % OFFICE_IPS.length]}' WHERE user_id = '${p.id}' AND revoked_at IS NULL;`);
    }
    parts.push(`UPDATE users SET created_at = now() - interval '${120} days' WHERE email IN ('${[d.CAST.admin, d.CAST.useradmin, d.CAST.auditor, d.CAST.rizky].map((c) => c.email).join("','")}') AND created_at > now() - interval '120 days';`);
    // sessions that the presenter should find: valid for days, with different addresses and times
    const ips = ["103.147.8.21", "36.90.112.7", "114.122.64.38", "182.253.67.9", "36.79.201.140", "202.67.40.12", "112.215.20.88", "103.147.8.22"];
    parts.push(`UPDATE sessions SET remember = true, idle_expires_at = now() + interval '5 days', absolute_expires_at = now() + interval '20 days' WHERE revoked_at IS NULL AND tenant_id = '00000000-0000-7000-8000-000000000001';`);
    deviceClients.forEach(([, , sid], i) => {
      if (!sid) return;
      const hours = [0.2, 3, 26, 1.5, 30, 2.2, 52, 4, 6, 8, 20, 0.7][i % 12];
      parts.push(`UPDATE sessions SET created_at = now() - interval '${hours + 2} hours', last_active_at = now() - interval '${hours} hours', ip = '${ips[i % ips.length]}' WHERE id = '${sid}';`);
    });
    // reviews, requests, closures, import: dates follow the events
    parts.push(`UPDATE access_reviews SET created_at = '${iso(F(stamp["review1.start"]))}', due_at = '${iso(new Date(F(stamp["review1.start"]).getTime() + 14 * DAY))}', closed_at = '${iso(F(stamp["review1.close"]))}' WHERE name LIKE 'Tinjauan akses triwulan III%';`);
    parts.push(`UPDATE access_review_items SET decided_at = '${iso(new Date(F(stamp["review1.close"]).getTime() - 3 * 3600e3))}' WHERE review_id IN (SELECT id FROM access_reviews WHERE name LIKE 'Tinjauan akses triwulan III%') AND decided_at IS NOT NULL;`);
    parts.push(`UPDATE access_reviews SET created_at = now() - interval '9 days', due_at = now() + interval '21 days' WHERE name LIKE 'Tinjauan akses triwulan IV%';`);
    parts.push(`UPDATE access_review_items SET decided_at = now() - interval '8 days' + (random() * interval '6 days') WHERE review_id IN (SELECT id FROM access_reviews WHERE name LIKE 'Tinjauan akses triwulan IV%') AND decided_at IS NOT NULL;`);
    parts.push(`UPDATE access_requests r SET created_at = now() - (s.n * interval '1 day') - interval '3 hours' FROM (SELECT id, row_number() OVER (ORDER BY id) n FROM access_requests WHERE status ILIKE 'pending') s WHERE r.id = s.id;`);
    parts.push(`UPDATE access_requests SET created_at = now() - interval '12 days', decided_at = now() - interval '11 days' WHERE status ILIKE ANY (ARRAY['approved','rejected']);`);
    parts.push(`UPDATE access_requests SET created_at = now() - interval '7 days', decided_at = now() - interval '6 days' WHERE status ILIKE 'cancelled';`);
    parts.push(`UPDATE outbox_emails SET sent_at = NULL, failed_at = created_at + interval '2 minutes', attempts = 3, last_error = '550 5.1.1 Alamat tujuan tidak ditemukan (mailbox unavailable)' WHERE to_address IN ('hasan.basri@${DOMAIN}', 'novi.andriani@${DOMAIN}');`);
    parts.push(`UPDATE account_closure_requests SET requested_at = '${iso(new Date(now - 17 * DAY))}', decided_at = '${iso(new Date(now - 16 * DAY))}', execute_after = '${iso(new Date(now - 2 * DAY))}', completed_at = '${iso(new Date(now - 2 * DAY))}' WHERE id = '${cNurul.id}';`);
    parts.push(`UPDATE account_closure_requests SET requested_at = '${iso(new Date(now - 6 * DAY))}', decided_at = '${iso(new Date(now - 5 * DAY))}', execute_after = '${iso(new Date(now + 9 * DAY))}' WHERE id = '${cAhmad.id}';`);
    parts.push(`UPDATE account_closure_requests SET requested_at = '${iso(new Date(now - 1 * DAY))}' WHERE id = '${cRina.id}';`);
    parts.push(`UPDATE users SET created_at = '${iso(new Date(now - 2 * DAY))}' WHERE email LIKE '%@${DOMAIN}' AND status ILIKE 'pending' AND id NOT IN (SELECT user_id FROM sessions);`);
    parts.push(`UPDATE api_keys SET created_at = now() - interval '23 days', last_used_at = now() - interval '2 hours' WHERE name LIKE 'Integrasi ERP%';`);
    parts.push(`UPDATE api_keys SET created_at = now() - interval '61 days', last_used_at = now() - interval '9 minutes' WHERE name LIKE 'Dasbor Monitoring%';`);
    parts.push(`UPDATE api_keys SET created_at = now() - interval '95 days', last_used_at = now() - interval '70 days' WHERE name LIKE 'Prototipe lama%';`);
    d.sql(`BEGIN;\n${parts.join("\n")}\nCOMMIT;`);
    log("  dates: audit trail, security events, people, reviews, requests and closures moved into the last 85 days; sessions kept valid");
  }
}
