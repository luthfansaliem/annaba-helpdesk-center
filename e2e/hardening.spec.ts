import { deflateSync } from "node:zlib";
import { expect, request as pwRequest, test, type Browser } from "@playwright/test";
import { authFile, provisionUser, reauthIfAsked, totp, uiLogin, WEB } from "./helpers";

test.use({ storageState: authFile("admin") });

const PW = "Sup3r-Secret!pass-9";
/** Anonymous browser with its own client IP (the proxy-authenticated API trusts the first X-Forwarded-For entry), so the per-IP invite/login throttles of one e2e run never starve another test. */
const anonContext = (browser: Browser) => browser.newContext({
  storageState: { cookies: [], origins: [] },
  extraHTTPHeaders: { "X-Forwarded-For": `10.${Math.floor(Math.random() * 250)}.${Math.floor(Math.random() * 250)}.${1 + Math.floor(Math.random() * 250)}` },
});

test("recovery code signs in once and is then burned", async ({ browser }) => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const email = `e2e.recovery.${Date.now()}@annabadigital.io`;
  const ctx = await anonContext(browser); const p = await ctx.newPage();
  await provisionUser(admin, p, email, "Recovery Person", PW);
  await uiLogin(p, email, PW); await expect(p).toHaveURL(/\/dashboard/);

  await p.goto("/profile?tab=security");
  await p.getByRole("button", { name: /set up authenticator/i }).click();
  await reauthIfAsked(p, PW);
  const dlg = p.getByRole("dialog", { name: "Set up authenticator" });
  const secret = (await dlg.locator("code").first().innerText()).trim();
  await dlg.getByRole("group", { name: /verification code/i }).locator("input").first().click();
  await p.keyboard.type(totp(secret), { delay: 80 });
  await dlg.getByRole("button", { name: /verify/i }).click();
  await expect(p.getByText(/save these recovery codes/i)).toBeVisible();
  const code = (await p.locator(".codes span").first().innerText()).trim();
  await p.getByRole("button", { name: /i.ve saved them/i }).click();

  for (const attempt of ["first use", "replay"] as const) {
    await ctx.clearCookies();
    await uiLogin(p, email, PW); await expect(p).toHaveURL(/\/mfa/);
    await p.getByRole("button", { name: "Use a recovery code" }).click();
    await p.getByLabel("Recovery code").fill(code);
    await p.getByRole("button", { name: /verify|continue|sign in/i }).click();
    if (attempt === "first use") await expect(p).toHaveURL(/\/dashboard/);
    else await expect(p.locator(".form-alert").first()).toContainText(/invalid or was already used/i);
  }
  await ctx.close(); await admin.dispose();
});

test("bulk suspend asks for confirmation and revokes every selected user", async ({ page, browser }) => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const tag = `e2e.bulk.${Date.now()}`;
  const people = [1, 2].map((n) => ({ email: `${tag}.${n}@annabadigital.io`, name: `Bulk Person ${n}` }));
  const contexts = [];
  for (const u of people) {
    const c = await anonContext(browser); contexts.push(c);
    const p = await c.newPage(); await provisionUser(admin, p, u.email, u.name, PW);
    await uiLogin(p, u.email, PW); await expect(p).toHaveURL(/\/dashboard/);
  }
  await page.goto("/users/all"); await page.getByLabel("Search users").fill(tag);
  await expect(page.getByRole("table", { name: "Users" }).locator("tbody tr")).toHaveCount(2); // the debounced filter has applied: never select-all on the unfiltered list
  await page.getByRole("checkbox", { name: "Select all rows" }).check();
  const bar = page.getByRole("region", { name: "Bulk actions" });
  await expect(bar).toContainText("2 selected");
  await bar.getByRole("button", { name: "Suspend" }).click();
  const dlg = page.getByRole("dialog", { name: /Suspend 2 users/ });
  await dlg.getByRole("button", { name: "Suspend" }).click();
  await expect(page.getByText(/Suspended 2 user/)).toBeVisible();
  for (const c of contexts) expect((await c.request.get("/api/auth/me")).status()).toBe(401);
  for (const c of contexts) await c.close();
  await admin.dispose();
});

test("role: duplicate then archive moves it out of the active list", async ({ page }) => {
  const name = `E2E Dup ${Date.now()}`;
  await page.goto("/roles");
  await page.getByRole("button", { name: /create role/i }).click();
  const dlg = page.getByRole("dialog", { name: "Create role" });
  await dlg.getByLabel("Name").fill(name); await dlg.getByLabel("Description").fill("to duplicate");
  await dlg.locator("input[type=checkbox]").first().check();
  await dlg.getByRole("button", { name: /save role/i }).click();
  await reauthIfAsked(page);
  await expect(page.getByText(name).first()).toBeVisible();

  await page.getByRole("button", { name: `Actions for ${name}` }).click();
  await page.getByRole("menuitem", { name: "Duplicate" }).click();
  await reauthIfAsked(page);
  await expect(page.getByText(`${name} duplicated.`)).toBeVisible();

  await page.getByRole("button", { name: `Actions for ${name}` }).first().click();
  await page.getByRole("menuitem", { name: "Archive" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Archive" }).click();
  await reauthIfAsked(page);
  await expect(page.getByText(`${name} archived.`)).toBeVisible();
  await expect(page.getByText(/Archived:.*E2E Dup/)).toBeVisible();
});

test("an expired or revoked session sends the user to sign-in with their place remembered", async ({ browser }) => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const email = `e2e.expire.${Date.now()}@annabadigital.io`;
  const c = await anonContext(browser); const p = await c.newPage();
  await provisionUser(admin, p, email, "Expire Me", PW);
  await uiLogin(p, email, PW); await expect(p).toHaveURL(/\/dashboard/);
  await p.goto("/profile");
  await expect(p.locator("#avatar-file")).toBeAttached(); // the page has loaded its data, so the next request really is the one after sign-out
  // the server forgets the session (as on expiry/revocation): wipe the cookie's session via sign-out from another tab
  const csrf = (await (await p.request.get("/api/auth/csrf")).json()).token as string;
  expect((await p.request.post("/api/auth/logout", { headers: { "X-CSRF-Token": csrf, Origin: WEB } })).ok()).toBeTruthy();
  await p.getByRole("navigation", { name: "Main navigation" }).getByRole("link", { name: "Home", exact: true }).click().catch(() => p.goto("/dashboard"));
  await expect(p).toHaveURL(/\/login\?.*returnTo=/);
  await c.close(); await admin.dispose();
});

/** A valid solid-colour PNG (the API accepts 32..2048 px per side). */
function solidPng(side: number, rgb: [number, number, number]): Buffer {
  const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b: Buffer) => { let c = 0xffffffff; for (const x of b) c = crcTable[(c ^ x) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type: string, data: Buffer) => { const t = Buffer.concat([Buffer.from(type), data]); const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const c = Buffer.alloc(4); c.writeUInt32BE(crc(t)); return Buffer.concat([len, t, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(side, 0); ihdr.writeUInt32BE(side, 4); ihdr[8] = 8; ihdr[9] = 2;
  const row = Buffer.concat([Buffer.from([0]), Buffer.from(Array.from({ length: side }, () => rgb).flat())]);
  const raw = Buffer.concat(Array.from({ length: side }, () => row));
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

test("an uploaded photo replaces the initials in the header and is gone again after removal", async ({ page }) => {
  await page.goto("/profile");
  const header = page.locator(".user-menu .avatar");
  await expect(header).toBeVisible();
  await page.setInputFiles("#avatar-file", { name: "me.png", mimeType: "image/png", buffer: solidPng(48, [37, 99, 235]) });
  await expect(header).toHaveCSS("background-image", /\/api\/avatars\//);
  await page.getByRole("button", { name: "Remove" }).click();
  await expect(header).not.toHaveCSS("background-image", /\/api\/avatars\//);
});

test("admin sees the e-mail delivery log with the provider status", async ({ page }) => {
  await page.goto("/admin/email");
  await expect(page.getByRole("heading", { name: "Email Delivery" })).toBeVisible();
  await expect(page.getByText(/sent in 24 h/)).toBeVisible();
  await expect(page.getByRole("link", { name: "Email Delivery" }).first()).toBeVisible(); // in the sidebar
});

test("when e-mail cannot be delivered, the UI stops offering password reset by e-mail", async ({ browser }) => {
  const c = await anonContext(browser); const p = await c.newPage();
  await p.route("**/api/settings/public", async (route) => { const res = await route.fetch(); await route.fulfill({ response: res, json: { ...(await res.json()), emailEnabled: false } }); });
  await p.goto("/login");
  await expect(p.getByRole("button", { name: /^sign in$/i })).toBeVisible();
  await expect(p.getByRole("link", { name: /forgot password/i })).toHaveCount(0);
  await p.goto("/forgot-password");
  await expect(p.getByRole("heading", { name: /reset by e-mail is unavailable/i })).toBeVisible();
  await c.close();
});

test("the web app itself is served with anti-framing and sniffing protections and without a framework banner", async ({ request }) => {
  for (const path of ["/login", "/dashboard"]) {
    const res = await request.get(path, { maxRedirects: 0, failOnStatusCode: false });
    const h = res.headers();
    expect(h["x-frame-options"], path).toBe("DENY");
    expect(h["x-content-type-options"], path).toBe("nosniff");
    expect(h["content-security-policy"], path).toContain("frame-ancestors 'none'");
    expect(h["referrer-policy"], path).toBeTruthy();
    expect(h["x-powered-by"], path).toBeUndefined();
  }
});

test("pages carry a nonce-based script policy and run without a single CSP violation", async ({ page, request }) => {
  const res = await request.get("/login", { maxRedirects: 0, failOnStatusCode: false });
  const csp = res.headers()["content-security-policy"] ?? "";
  expect(csp).toMatch(/script-src 'self' 'nonce-[^']+' 'strict-dynamic'/);
  expect(csp).not.toContain("script-src 'unsafe-inline'");
  const other = (await request.get("/login", { maxRedirects: 0, failOnStatusCode: false })).headers()["content-security-policy"];
  expect(other, "the nonce changes on every request").not.toBe(csp);

  await page.addInitScript(() => {
    (window as unknown as { __csp: string[] }).__csp = [];
    document.addEventListener("securitypolicyviolation", (e) => (window as unknown as { __csp: string[] }).__csp.push(`${e.violatedDirective} ${e.blockedURI}`));
  });
  for (const path of ["/dashboard", "/users", "/audit"]) {
    await page.goto(path);
    await page.waitForLoadState("networkidle");
    expect(await page.evaluate(() => (window as unknown as { __csp: string[] }).__csp), path).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.dataset.theme), `theme script ran on ${path}`).toMatch(/^(light|dark)$/);
  }
});
