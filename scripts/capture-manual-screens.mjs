#!/usr/bin/env node
/**
 * Takes the screenshots for the user manual (docs/68-helpdesk-user-manual.md) from a running DEVELOPMENT helpdesk with demo data, in Indonesian.
 *
 *   npm run dev & npm run api:run           # API on :5080, web on :3000, dev accounts seeded (Development only)
 *   node scripts/demo-data.mjs              # believable demo data
 *   node scripts/capture-manual-screens.mjs [--web http://localhost:3000] [--text <folder>]
 *
 * Signs in as the seeded dev accounts, switches them to Indonesian for the run (and back afterwards), and writes docs/manual/NN-*.png.
 * With --text, also writes each page's visible text, which is how the manual is checked against the exact words on screen.
 * Refuses anything but localhost: it changes the accounts' language while it runs.
 */
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const opt = (name, fallback) => { const i = args.indexOf(name); return i >= 0 && args[i + 1] ? args[i + 1] : fallback; };
const WEB = opt("--web", "http://localhost:3000").replace(/\/$/, "");
const TEXT = opt("--text", null);
const OUT = join(ROOT, "docs/manual");
const PASSWORD = process.env.DEMO_PASSWORD ?? "Passw0rd!demo";
const STAFF = "sarah.wijaya@annabadigital.io", EMPLOYEE = "rizky.pratama@annabadigital.io";
if (!["localhost", "127.0.0.1"].includes(new URL(WEB).hostname)) { console.error("Screenshots are taken from a local development helpdesk only."); process.exit(2); }

/** What to capture: file name, who, which page, and optionally what to do before the picture (open a dialog, pick a row). */
const SHOTS = [
  { file: "01-masuk", who: null, path: "/login" },
  { file: "02-beranda-karyawan", who: EMPLOYEE, path: "/dashboard" },
  { file: "03-permintaan-baru", who: EMPLOYEE, path: "/requests/new" },
  { file: "04-permintaan-saya", who: EMPLOYEE, path: "/requests" },
  { file: "05-pemesanan", who: EMPLOYEE, path: "/bookings" },
  { file: "09-minta-perlengkapan", who: EMPLOYEE, path: "/supplies", then: async (p) => { await p.getByRole("button", { name: "Minta perlengkapan" }).first().click(); await p.getByRole("dialog").waitFor(); } },
  { file: "10-pengaturan-notifikasi", who: EMPLOYEE, path: "/notifications/settings" },
  { file: "12-persetujuan", who: STAFF, path: "/approvals" },
  { file: "13-antrean-tiket", who: STAFF, path: "/tickets" },
  { file: "14-detail-tiket", who: STAFF, path: "/tickets", then: async (p) => { await p.locator("a[href^='/tickets/']", { hasText: "Air conditioning too warm" }).first().click(); await p.waitForURL(/\/tickets\/[0-9a-f-]{36}/); } },
  { file: "16-detail-perintah-kerja", who: STAFF, path: "/work-orders", then: async (p) => { await p.locator("a[href^='/work-orders/']:not([href$='/field'])").first().click(); await p.waitForURL(/\/work-orders\/[0-9a-f-]{36}/); } },
  { file: "17-mode-lapangan", who: STAFF, path: "/work-orders/field" },
  { file: "18-aset", who: STAFF, path: "/assets" },
  { file: "20-resepsionis", who: STAFF, path: "/reception" },
  { file: "22-inventaris", who: STAFF, path: "/inventory" },
  { file: "23-meja-perlengkapan", who: STAFF, path: "/supplies" },
  { file: "24-operasional", who: STAFF, path: "/reports" },
  { file: "25-ekspor-laporan", who: STAFF, path: "/reports/schedules" },
  { file: "26-mulai", who: STAFF, path: "/setup/start" },
  { file: "29-direktori", who: STAFF, path: "/admin/directory" },
];

async function signIn(ctx, email) {
  const r = ctx.request;
  const res = await r.post(`${WEB}/api/auth/login`, { data: { email, password: PASSWORD }, headers: { Origin: WEB } });
  if (!res.ok()) throw new Error(`Could not sign in as ${email} (${res.status()}).`);
  const csrf = (await (await r.get(`${WEB}/api/auth/csrf`)).json()).token;
  const before = (await (await r.get(`${WEB}/api/me/preferences`)).json()).language;
  await r.patch(`${WEB}/api/me/preferences`, { data: { language: "id" }, headers: { "X-CSRF-Token": csrf, Origin: WEB } });
  return async () => { await r.patch(`${WEB}/api/me/preferences`, { data: { language: before }, headers: { "X-CSRF-Token": csrf, Origin: WEB } }); };
}

mkdirSync(OUT, { recursive: true });
if (TEXT) mkdirSync(TEXT, { recursive: true });
const browser = await chromium.launch();
const contexts = new Map(); const restore = [];
try {
  for (const shot of SHOTS) {
    let ctx = contexts.get(shot.who ?? "anon");
    if (!ctx) {
      ctx = await browser.newContext({ viewport: { width: 1360, height: 860 }, deviceScaleFactor: 1.5, locale: "id-ID", timezoneId: "Asia/Jakarta" });
      await ctx.addInitScript(() => { try { localStorage.setItem("annaba-digital-lang", "id"); } catch { /* not ready */ } });
      if (shot.who) restore.push(await signIn(ctx, shot.who));
      contexts.set(shot.who ?? "anon", ctx);
    }
    const page = await ctx.newPage();
    await page.goto(`${WEB}${shot.path}`, { waitUntil: "networkidle" });
    await page.addStyleTag({ content: "nextjs-portal{display:none!important}" });   // the development-mode indicator
    if (shot.then) await shot.then(page);
    await page.waitForLoadState("networkidle");
    await page.waitForTimeout(700);                          // charts and toasts settle
    await page.mouse.move(0, 0);
    await page.screenshot({ path: join(OUT, `${shot.file}.png`) });
    if (TEXT) writeFileSync(join(TEXT, `${shot.file}.txt`), `${page.url()}\n\n${await page.locator("body").innerText()}`);
    console.log(`  ${shot.file}.png  ${page.url().replace(WEB, "")}`);
    await page.close();
  }
} finally {
  for (const r of restore) await r().catch(() => {});
  await browser.close();
}
