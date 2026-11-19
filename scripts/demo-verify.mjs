#!/usr/bin/env node
/**
 * Walks every screen of the helpdesk as the person who would open it and reports which ones are empty, broken or denied.
 * It is the "does every screen have data?" check of the demo seed (scripts/demo-seed.mjs).
 *
 *   node scripts/demo-verify.mjs                       # web on http://localhost:3000, screenshots in scripts/demo/out/screens
 *   node scripts/demo-verify.mjs --web http://localhost:3000 --only tickets,/assets --no-shots
 *
 * A screen "fails" when it shows an error box, access denied, an empty state (.empty) where data is expected, a table with no rows, or a console/page error.
 * Pages that are legitimately forms or settings are marked `plain: true` (checked for errors only). Local development helpdesk only.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const opt = (n, f) => { const i = args.indexOf(n); return i >= 0 && args[i + 1] ? args[i + 1] : f; };
const WEB = opt("--web", "http://localhost:3000").replace(/\/$/, "");
const OUT = join(ROOT, "scripts/demo/out"); const SHOTS = join(OUT, "screens");
const ONLY = opt("--only", null)?.split(",");
const NO_SHOTS = args.includes("--no-shots");
const PASSWORD = process.env.DEMO_PASSWORD ?? "Passw0rd!demo";
if (!["localhost", "127.0.0.1"].includes(new URL(WEB).hostname) && !args.includes("--yes")) { console.error("Local development helpdesk only (pass --yes for a deployment that holds the demo data; DEMO_PASSWORD must be its demo password)."); process.exit(2); }

const E = {
  admin: "sarah.wijaya@annabadigital.io", useradmin: "daniel.tan@annabadigital.io", auditor: "maya.putri@annabadigital.io", hendra: "hendra.kusuma@annabadigital.io", putri: "putri.anggraini@annabadigital.io",
  agus: "agus.setiawan@annabadigital.io", bayu: "bayu.nugroho@annabadigital.io", sinta: "sinta.maharani@annabadigital.io", joko: "joko.susilo@sejukabadi.co.id",
  dimas: "dimas.prakoso@annabadigital.io", rizky: "rizky.pratama@annabadigital.io", anita: "anita.rahmawati@annabadigital.io", fajar: "fajar.ramadhan@annabadigital.io", lina: "lina.marlina@annabadigital.io",
};

/** [who, path, options]. `detail: "<selector of the first link to open>"` follows the first matching link and checks that page instead. `plain` = a form/settings page (errors only). */
const S = (who, path, o = {}) => ({ who, path, ...o });
const SCREENS = [
  // everyone
  S("rizky", "/dashboard"), S("rizky", "/requests/new"), S("rizky", "/requests"), S("rizky", "/requests", { detail: "a[href^='/tickets/']", label: "my ticket detail" }), S("rizky", "/kb"), S("rizky", "/kb", { detail: "a[href^='/kb/']:not([href='/kb/new']):not([href='/kb/categories'])", label: "kb article" }),
  S("rizky", "/bookings"), S("rizky", "/visitors"), S("rizky", "/mail"), S("rizky", "/supplies"), S("rizky", "/approvals", { plain: true }), S("rizky", "/notifications"), S("rizky", "/notifications/settings", { plain: true }),
  S("rizky", "/profile", { plain: true }), S("rizky", "/access-requests"), S("rizky", "/sessions"), S("rizky", "/settings", { plain: true }), S("rizky", "/help", { plain: true }),
  S("anita", "/requests"), S("fajar", "/supplies"), S("lina", "/visitors"),
  // approvers
  S("dimas", "/dashboard"), S("dimas", "/approvals"), S("dimas", "/supplies"), S("hendra", "/approvals"),
  // service desk
  S("putri", "/dashboard"), S("putri", "/tickets"), S("putri", "/tickets", { detail: "a[href^='/tickets/']", label: "ticket detail" }), S("agus", "/tickets"), S("agus", "/work-orders"), S("agus", "/work-orders", { detail: "a[href^='/work-orders/']:not([href$='/field'])", label: "work order detail" }),
  S("agus", "/work-orders/field"), S("bayu", "/work-orders"), S("joko", "/work-orders"), S("joko", "/notifications"), S("sinta", "/reception"), S("sinta", "/mail"), S("putri", "/reception"),
  // assets & vendors
  S("putri", "/assets"), S("putri", "/assets", { detail: "a[href^='/assets/']:not([href$='/import']):not([href$='/labels'])", label: "asset detail" }), S("hendra", "/assets/import", { plain: true }), S("hendra", "/assets/labels", { plain: true }),
  S("hendra", "/maintenance"), S("hendra", "/maintenance", { detail: "a[href^='/maintenance/']:not([href$='/compliance'])", label: "maintenance plan" }),
  S("hendra", "/vendors"), S("hendra", "/vendors", { detail: "a[href^='/vendors/']", label: "vendor detail" }), S("hendra", "/inventory"), S("hendra", "/inventory", { detail: "a[href^='/inventory/']:not([href$='/stock-takes'])", label: "inventory item" }), S("hendra", "/inventory/stock-takes"),
  S("putri", "/supplies"),
  // insights
  S("hendra", "/reports"), S("hendra", "/reports/sla"), S("hendra", "/reports/csat"), S("hendra", "/maintenance/compliance"), S("hendra", "/reports/utilisation"), S("hendra", "/costs"), S("hendra", "/costs/budgets"), S("hendra", "/reports/schedules"),
  // setup
  S("hendra", "/setup/start"), S("hendra", "/setup/catalog"), S("hendra", "/setup/catalog", { detail: "a[href^='/setup/catalog/']", label: "service form" }), S("hendra", "/setup/locations"), S("hendra", "/setup/teams"), S("hendra", "/setup/calendars"),
  S("hendra", "/setup/sla"), S("hendra", "/setup/assignment-rules"), S("hendra", "/setup/approvals"), S("hendra", "/setup/asset-types"), S("hendra", "/setup/resources"), S("hendra", "/setup/visitors"), S("hendra", "/setup/checklists"), S("hendra", "/costs/centers"),
  S("hendra", "/kb/categories"), S("hendra", "/kb/new", { plain: true }),
  // people & access / admin
  S("admin", "/access"), S("admin", "/users"), S("admin", "/users/all"), S("admin", "/users/all", { detail: "a[href^='/users/']:not([href$='/all']):not([href$='/invitations'])", label: "user detail" }), S("useradmin", "/users/invitations"), S("admin", "/roles"), S("admin", "/permissions"), S("admin", "/access-matrix"),
  S("admin", "/access-reviews"), S("admin", "/account-closures"), S("auditor", "/audit"), S("auditor", "/security-events"), S("admin", "/admin/settings", { plain: true }), S("admin", "/admin/security-policy", { plain: true }), S("admin", "/admin/retention"),
  S("admin", "/admin/email"), S("admin", "/admin/email-wording", { plain: true }), S("admin", "/admin/inbound-email"), S("admin", "/admin/integrations"), S("admin", "/admin/directory"), S("admin", "/admin/channels"), S("admin", "/admin/tenants"),
  S("admin", "/sessions"), S("admin", "/notifications"),
];

const slug = (s) => s.replace(/^\//, "").replace(/[^a-z0-9]+/gi, "-").replace(/-+$/, "") || "home";
async function signIn(ctx, email) {
  const r = ctx.request;
  const res = await r.post(`${WEB}/api/auth/login`, { data: { email, password: PASSWORD }, headers: { Origin: WEB } });
  if (!res.ok()) throw new Error(`Could not sign in as ${email} (${res.status()}).`);
}

if (!NO_SHOTS) mkdirSync(SHOTS, { recursive: true }); else mkdirSync(OUT, { recursive: true });
const browser = await chromium.launch();
const contexts = new Map(); const results = [];
try {
  for (const s of SCREENS) {
    const label = s.label ?? s.path;
    if (ONLY && !ONLY.some((o) => s.who === o || s.path.includes(o) || label.includes(o))) continue;
    let ctx = contexts.get(s.who);
    if (!ctx) {
      ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: "id-ID", timezoneId: "Asia/Jakarta" });
      await ctx.addInitScript(() => { try { localStorage.setItem("annaba-digital-lang", "id"); } catch { /* not ready */ } });
      try { await signIn(ctx, E[s.who]); } catch (e) { results.push({ ...s, label, problems: [e.message] }); continue; }
      contexts.set(s.who, ctx);
    }
    const page = await ctx.newPage(); const problems = []; const errors = [];
    page.on("pageerror", (e) => errors.push(String(e.message).slice(0, 140)));
    page.on("console", (m) => { if (m.type() === "error" && !/favicon|Failed to load resource: the server responded with a status of (401|403|404)/.test(m.text())) errors.push(m.text().slice(0, 140)); });
    let info = { rows: 0, empty: 0, errorBoxes: 0, denied: false, chars: 0 }; let finalPath = s.path;
    try {
      await page.goto(`${WEB}${s.path}`, { waitUntil: "networkidle", timeout: 45000 });
      if (s.detail) {
        const link = page.locator(s.detail).first();
        if (await link.count() === 0) problems.push("no item to open (list is empty?)");
        else { await link.click(); await page.waitForLoadState("networkidle"); await page.waitForTimeout(500); finalPath = new URL(page.url()).pathname; }
      }
      await page.addStyleTag({ content: "nextjs-portal{display:none!important}" });
      await page.waitForTimeout(600);
      info = await page.evaluate(() => ({
        rows: document.querySelectorAll("tbody tr, [role=row]:not([aria-rowindex='1']), .list-row, li.row").length,
        empty: document.querySelectorAll(".empty, .tbl-empty, .m-empty, .tab-placeholder").length,
        emptyText: [...document.querySelectorAll(".empty h3, .tbl-empty, .m-empty h3, .tab-placeholder")].map((e) => e.textContent.trim().slice(0, 60)),
        errorBoxes: document.querySelectorAll(".state-box[role=alert]").length,
        denied: /Akses ditolak|Access denied/i.test(document.body.innerText),
        chars: document.body.innerText.length,
        title: (document.querySelector("h1")?.textContent ?? "").trim(),
      }));
      if (info.denied) problems.push("access denied");
      if (info.errorBoxes) problems.push("error box");
      if (!s.plain && info.empty) problems.push(`empty: ${info.emptyText.join(" / ")}`);
      if (!s.plain && !s.detail && info.rows === 0 && info.empty === 0 && info.chars < 400) problems.push("looks blank");
      if (errors.length) problems.push(`console: ${[...new Set(errors)].slice(0, 2).join(" | ")}`);
      if (!NO_SHOTS) await page.screenshot({ path: join(SHOTS, `${s.who}__${slug(s.detail ? `${s.path}-${label}` : s.path)}.png`), fullPage: false });
    } catch (e) { problems.push(`load: ${String(e.message).split("\n")[0].slice(0, 120)}`); }
    results.push({ ...s, label, finalPath, info, problems });
    console.log(`${problems.length ? "✗" : "✓"} ${s.who.padEnd(9)} ${label.padEnd(30)} rows:${String(info.rows).padStart(3)} ${problems.join("; ")}`);
    await page.close();
  }
} finally { await browser.close(); }

const bad = results.filter((r) => r.problems.length);
writeFileSync(join(OUT, "verify.json"), JSON.stringify(results, null, 2));
writeFileSync(join(OUT, "verify.md"), `# Screen sweep\n\n${results.length - bad.length}/${results.length} screens OK\n\n| | who | screen | rows | problems |\n|---|---|---|---|---|\n${results.map((r) => `| ${r.problems.length ? "✗" : "✓"} | ${r.who} | ${r.label} | ${r.info?.rows ?? ""} | ${r.problems.join("; ")} |`).join("\n")}\n`);
console.log(`\n${results.length - bad.length}/${results.length} screens OK${bad.length ? `, ${bad.length} to fix (scripts/demo/out/verify.md)` : ""}`);
process.exit(bad.length ? 1 : 0);
