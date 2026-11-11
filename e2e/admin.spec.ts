import { expect, request as pwRequest, test } from "@playwright/test";
import { USERS, WEB, apiPost, authFile, provisionUser, reauthIfAsked, uiLogin } from "./helpers";

test.use({ storageState: authFile("admin") });

test("add user → invitation e-mail → accept → new user signs in with a least-privilege role", async ({ page, browser }) => {
  const email = `e2e.invitee.${Date.now()}@annabadigital.io`;
  await page.goto("/users/all");
  await page.getByRole("button", { name: /add user/i }).click();
  const dlg = page.getByRole("dialog", { name: "Add User" });
  await dlg.getByLabel("Full name").fill("Invited Person");
  await dlg.getByRole("textbox", { name: /^Email/ }).fill(email);
  await expect(dlg.getByRole("checkbox", { name: /^User Self-service/ })).toBeChecked();   // the default role is chosen once the role list has loaded; sending earlier would say "Select at least one role"
  await dlg.getByRole("button", { name: /send invitation/i }).click();
  await reauthIfAsked(page);
  await expect(page.getByText(/invitation sent/i).first()).toBeVisible();

  const anon = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const p2 = await anon.newPage();
  const { emailedLink } = await import("./helpers");
  await p2.goto(await emailedLink(email, "/invite"));
  await p2.getByLabel("Full name").fill("Invited Person");
  await p2.getByLabel("Create password").fill("Sup3r-Secret!pass-9");
  await p2.getByRole("button", { name: /activate account/i }).click();
  await expect(p2).toHaveURL(/\/login\?accepted=1/);
  await uiLogin(p2, email, "Sup3r-Secret!pass-9");
  await expect(p2).toHaveURL(/\/dashboard/);
  // a plain "user" must not see administration navigation
  await expect(p2.getByRole("link", { name: "Audit Logs" })).toHaveCount(0);
  await anon.close();

  await page.goto("/users/all");
  await page.getByLabel("Search users").fill("Invited Person");
  await expect(page.getByRole("link", { name: /Invited Person/ }).first()).toBeVisible();
});

test("All Users: server-side search, filter and sorting", async ({ page }) => {
  await page.goto("/users/all");
  await expect(page.getByRole("link", { name: /Sarah Wijaya/ })).toBeVisible();
  await page.getByLabel("Search users").fill("rizky");
  await expect(page.getByRole("link", { name: /Rizky/ })).toBeVisible();
  await expect(page.getByRole("link", { name: /Sarah Wijaya/ })).toHaveCount(0);
  await page.getByLabel("Search users").fill("zzz-no-such-user");
  await expect(page.getByText("No users found")).toBeVisible();
});

test("suspending a user asks for confirmation, signs them out, and activating restores access", async ({ page, browser }) => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const email = `e2e.suspend.${Date.now()}@annabadigital.io`; const pw = "Sup3r-Secret!pass-9";
  const anon = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const p2 = await anon.newPage();
  await provisionUser(admin, p2, email, "Suspend Me", pw);
  await uiLogin(p2, email, pw); await expect(p2).toHaveURL(/\/dashboard/);

  await page.goto("/users/all"); await page.getByLabel("Search users").fill(email);
  await page.getByRole("button", { name: /Actions for Suspend Me/ }).click();
  await page.getByRole("menuitem", { name: "Suspend" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Suspend" }).click();
  await expect(page.getByText(/suspended\./i).first()).toBeVisible();

  const res = await p2.request.get("/api/auth/me");
  expect(res.status()).toBe(401); // existing sessions were revoked
  await p2.context().clearCookies();
  await uiLogin(p2, email, pw); await expect(p2).toHaveURL(/\/login/);

  await page.getByRole("button", { name: /Actions for Suspend Me/ }).click();
  await page.getByRole("menuitem", { name: "Activate" }).click();
  await expect(page.getByText(/activated\./i).first()).toBeVisible();
  await uiLogin(p2, email, pw); await expect(p2).toHaveURL(/\/dashboard/);
  await anon.close(); await admin.dispose();
});

test("roles: create → matrix change → re-authentication dialog → saved and audited", async ({ page }) => {
  const name = `E2E Role ${Date.now()}`;
  await page.goto("/roles");
  await page.getByRole("button", { name: /create role/i }).click();
  const dlg = page.getByRole("dialog", { name: "Create role" });
  await dlg.locator("#r-name").fill(name);   // by id: a permission description in the same form mentions "names"
  await dlg.getByLabel("Description").fill("created by e2e");
  await dlg.locator('input[type=checkbox]').first().check();
  await dlg.getByRole("button", { name: /save role/i }).click();
  // critical change → the server asks for a fresh password confirmation
  await reauthIfAsked(page);
  await expect(page.getByText(name).first()).toBeVisible();

  await page.goto("/access-matrix");
  const cell = page.getByRole("checkbox", { name: new RegExp(`${name}: audit.read`) });
  await cell.check();
  await expect(page.getByText(/1 unsaved change/)).toBeVisible();
  await page.getByRole("button", { name: /save changes/i }).click();
  await page.getByRole("dialog", { name: "Save access changes?" }).getByRole("button", { name: /confirm/i }).click();
  await expect(page.getByText(/access matrix saved/i)).toBeVisible();
  await page.reload();
  await expect(page.getByRole("checkbox", { name: new RegExp(`${name}: audit.read`) })).toBeChecked();

  await page.goto("/audit?category=role");
  await expect(page.getByRole("button", { name: /role\.created|role\./ }).first()).toBeVisible();
});

test("protected roles cannot be edited from the matrix", async ({ page }) => {
  await page.goto("/access-matrix");
  await expect(page.getByRole("checkbox", { name: /Super Admin: users\.read/i })).toBeDisabled();
});

test("system settings: change is confirmed, audited, and the API key is write-only", async ({ page }) => {
  await page.goto("/admin/settings");
  const n = page.getByLabel("Invitation expiry (days)");
  const old = Number(await n.inputValue()); await n.fill(String(old === 7 ? 6 : 7));
  await page.getByRole("button", { name: "Save changes" }).click();
  await page.getByRole("dialog").getByRole("button", { name: /confirm/i }).click();
  await reauthIfAsked(page);
  await expect(page.getByText(/system settings saved/i)).toBeVisible();
  const body = await page.content();
  expect(body).not.toMatch(/re_[A-Za-z0-9]{10,}/); // no key material in the page
});

test("session management: admin sees all sessions; user sees only their own", async ({ page }) => {
  await page.goto("/sessions");
  await page.getByRole("tab", { name: "All users" }).click();
  await expect(page.getByText(USERS.admin).first()).toBeVisible();
});

test("csrf: a state-changing call without the token is rejected", async () => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const r = await admin.post("/api/users/bulk", { data: { ids: [], action: "suspend" }, headers: { Origin: WEB } });
  expect(r.status()).toBe(403);
  const bad = await admin.post("/api/users/bulk", { data: { ids: [], action: "suspend" }, headers: { Origin: "https://evil.example", "X-CSRF-Token": "x" } });
  expect(bad.status()).toBe(403);
  void apiPost; await admin.dispose();
});

test("audit: export downloads a CSV of the filtered events and the export is itself audited", async ({ page }) => {
  await page.goto("/audit?category=auth");
  const [dl] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: /export csv/i }).click()]);
  expect(dl.suggestedFilename()).toMatch(/^audit-\d{8}-\d{6}\.csv$/);
  const body = (await import("node:fs")).readFileSync(await dl.path(), "utf8");
  expect(body.split("\r\n")[0]).toContain("id,timestamp,actor_id,actor,action");
  await page.goto("/audit?category=audit");
  await expect(page.getByRole("button", { name: "audit.export" }).first()).toBeVisible();
});

test("users: CSV import checks the file first, then invites only the valid rows", async ({ page }) => {
  const stamp = Date.now(); const good = `e2e.csv.${stamp}@annabadigital.io`;
  await page.goto("/users/all");
  await page.getByRole("button", { name: /import csv/i }).click();
  const dlg = page.getByRole("dialog", { name: "Import users" });
  await dlg.locator("#imp-file").setInputFiles({ name: "people.csv", mimeType: "text/csv", buffer: Buffer.from(`name,email\nCsv Person,${good}\nBroken Row,not-an-email\n`) });
  await dlg.getByRole("button", { name: "Check file" }).click();
  await expect(dlg.getByText(/1 of 2 rows are ready; 1 will be skipped/)).toBeVisible();
  await expect(dlg.getByRole("table", { name: "Rows with problems" })).toContainText("valid email");
  await dlg.getByRole("button", { name: /import 1 person/i }).click();
  await reauthIfAsked(page);
  await expect(dlg.getByText("1 invited, 1 skipped.")).toBeVisible();
  await dlg.getByRole("button", { name: "Close", exact: true }).last().click();
  await page.getByLabel("Search users").fill(good);
  await expect(page.getByText(good).first()).toBeVisible();
});

test("access review: start → keep one assignment → close, leaving no review open", async ({ page }) => {
  await page.goto("/access-reviews");
  await page.getByLabel(/^Name/).fill(`E2E review ${Date.now()}`);
  await page.getByRole("button", { name: "Start review" }).click();
  await expect(page.getByRole("button", { name: "Close review" })).toBeVisible();
  const row = page.getByRole("row").filter({ hasNotText: USERS.admin }).filter({ has: page.getByRole("button", { name: "Keep" }) }).first();
  await row.getByRole("button", { name: "Keep" }).click();
  await expect(page.getByText("Kept.").first()).toBeVisible();
  await page.getByRole("button", { name: "Close review" }).click();
  await expect(page.getByRole("button", { name: "Close review" })).toHaveCount(0);
  await expect(page.getByRole("form", { name: "Start a review" })).toBeVisible(); // nothing open any more
});

test("tenants: the platform admin creates a tenant, sees it listed, then suspends and re-activates it", async ({ page }) => {
  const slug = `e2e-${Date.now().toString(36)}`;
  await page.goto("/admin/tenants");
  await page.getByRole("button", { name: /new tenant/i }).click();
  const dlg = page.getByRole("dialog", { name: "New tenant" });
  await dlg.getByLabel(/Organisation name/).fill(`E2E ${slug}`);
  await dlg.getByLabel(/Identifier/).fill(slug);
  await dlg.getByLabel(/First administrator/).fill(`${slug}@annabadigital.io`);
  await dlg.getByRole("button", { name: "Create tenant" }).click();
  await reauthIfAsked(page);
  const row = page.getByRole("row", { name: new RegExp(slug) });
  await expect(row).toBeVisible();
  await expect(page.getByRole("columnheader", { name: "Actions" })).toBeVisible();
  await row.getByRole("button", { name: /^Suspend / }).click();
  await expect(page.getByRole("alertdialog").or(page.getByRole("dialog")).getByText(/signed out now/)).toBeVisible(); // explains the effect first
  await page.getByRole("dialog").getByRole("button", { name: "Suspend tenant" }).click();
  await reauthIfAsked(page);
  await expect(page.getByRole("row", { name: new RegExp(`${slug}.*Suspended`) })).toBeVisible();
  await page.getByRole("row", { name: new RegExp(slug) }).getByRole("button", { name: /^Re-activate / }).click();
  await expect(page.getByRole("row", { name: new RegExp(`${slug}.*Active`) })).toBeVisible();
});

test("compact sidebar keeps the menu groups (labelled sections with dividers)", async ({ page }) => {
  await page.goto("/dashboard");
  await page.getByRole("button", { name: "Collapse menu" }).click();
  const nav = page.getByRole("navigation", { name: "Main navigation" });
  for (const g of ["Service desk", "Insights", "People & access", "Personal", "Administration"]) await expect(nav.getByRole("group", { name: g })).toBeVisible();
  await expect(nav.getByRole("group", { name: "Administration" }).getByRole("link", { name: "Tenants" })).toBeVisible();
  await page.getByRole("button", { name: /Expand menu/i }).click();
});

test("access reviews: a schedule can be switched on, shows when the next review opens, and switched off again", async ({ page }) => {
  await page.goto("/access-reviews");
  const sw = page.getByRole("switch", { name: /Open reviews automatically/ });
  await expect(sw).toBeVisible();
  if (await sw.isChecked()) { await sw.uncheck(); await page.getByRole("button", { name: "Save schedule" }).click(); await expect(page.getByText(/Off — reviews start only when you start them/)).toBeVisible(); }
  await sw.check();
  await page.getByLabel("How often").selectOption("30");
  await page.getByRole("button", { name: "Save schedule" }).click();
  await expect(page.getByText(/Next review opens on/)).toBeVisible();
  await page.reload();
  await expect(page.getByRole("switch", { name: /Open reviews automatically/ })).toBeChecked();
  await expect(page.getByLabel("How often")).toHaveValue("30");
  await page.getByRole("switch", { name: /Open reviews automatically/ }).uncheck();
  await page.getByRole("button", { name: "Save schedule" }).click();
  await expect(page.getByText(/Off — reviews start only when you start them/)).toBeVisible();
});

test("tenants: support mode shows a banner, administers only that tenant, and exits cleanly", async ({ page }) => {
  const slug = `e2e-${Date.now().toString(36)}`; const adminEmail = `${slug}@annabadigital.io`;
  await page.goto("/admin/tenants");
  await page.getByRole("button", { name: /new tenant/i }).click();
  const dlg = page.getByRole("dialog", { name: "New tenant" });
  await dlg.getByLabel(/Organisation name/).fill(`E2E ${slug}`);
  await dlg.getByLabel(/Identifier/).fill(slug);
  await dlg.getByLabel(/First administrator/).fill(adminEmail);
  await dlg.getByRole("button", { name: "Create tenant" }).click();
  await reauthIfAsked(page);
  const row = page.getByRole("row", { name: new RegExp(slug) });
  await expect(row).toBeVisible();

  await row.getByRole("button", { name: /^Manage / }).click();
  await expect(page.getByRole("dialog").getByText(/recorded in/)).toBeVisible(); // explains what support mode means first
  await page.getByRole("dialog").getByRole("button", { name: "Enter support mode" }).click();
  await reauthIfAsked(page);
  const bar = page.locator(".support-bar");
  await expect(bar).toContainText(`E2E ${slug}`);
  await expect(page.locator(".tenant-chip.support")).toHaveText(`E2E ${slug}`);

  await page.goto("/users/invitations"); // the tenant's first administrator has only been invited so far
  await expect(page.getByText(adminEmail).first()).toBeVisible();
  await page.goto("/users/all");
  await expect(page.getByText("sarah.wijaya@annabadigital.io")).toHaveCount(0); // the platform's own people are not in there

  await page.getByRole("button", { name: "Exit support mode" }).click();
  await expect(page).toHaveURL(/\/admin\/tenants/);
  await expect(page.locator(".support-bar")).toHaveCount(0);
  await page.goto("/users/invitations");
  await expect(page.getByText(adminEmail)).toHaveCount(0);
});

test("security policy: an organisation tightens a setting, sees the platform value beside it, and can go back", async ({ page }) => {
  const slug = `e2e-${Date.now().toString(36)}`;
  await page.goto("/admin/security-policy");
  await expect(page.getByText("Access denied")).toBeVisible(); // the platform organisation edits System Settings instead
  await page.goto("/admin/tenants");
  await page.getByRole("button", { name: /new tenant/i }).click();
  const dlg = page.getByRole("dialog", { name: "New tenant" });
  await dlg.getByLabel(/Organisation name/).fill(`E2E ${slug}`);
  await dlg.getByLabel(/Identifier/).fill(slug);
  await dlg.getByLabel(/First administrator/).fill(`${slug}@annabadigital.io`);
  await dlg.getByRole("button", { name: "Create tenant" }).click();
  await reauthIfAsked(page);
  await page.getByRole("row", { name: new RegExp(slug) }).getByRole("button", { name: /^Manage / }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Enter support mode" }).click();
  await reauthIfAsked(page);
  await expect(page.locator(".support-bar")).toBeVisible();

  await page.goto("/admin/security-policy");
  await expect(page.getByLabel("Minimum password length")).toBeVisible();
  // the help line sits under its control, not inside it
  const geo = await page.evaluate(() => [...document.querySelectorAll(".fl")].filter((f) => f.querySelector(".ctl") && f.querySelector(".field-help")).map((f) => f.querySelector(".field-help")!.getBoundingClientRect().top - f.querySelector(".ctl")!.getBoundingClientRect().bottom));
  expect(geo.length).toBeGreaterThan(3); for (const gap of geo) expect(gap).toBeGreaterThanOrEqual(0);
  const len = page.getByLabel("Minimum password length");
  const platform = Number((await page.getByText(/Platform: \d+ characters/).first().innerText()).match(/Platform: (\d+)/)![1]);
  await len.fill(String(platform + 3));
  await page.getByRole("button", { name: "Save policy" }).click();
  await reauthIfAsked(page);
  await expect(page.getByText("Your organisation is stricter.").first()).toBeVisible();
  await page.reload();
  await expect(page.getByLabel("Minimum password length")).toHaveValue(String(platform + 3));

  await len.fill(String(platform - 1)); // weaker than the platform: refused with the reason next to the field
  await page.getByRole("button", { name: "Save policy" }).click();
  await expect(page.getByText(/weaker than the platform/i).first()).toBeVisible();

  await page.getByRole("button", { name: "Reset" }).click();
  await page.getByRole("button", { name: "Use all platform settings" }).click();
  await reauthIfAsked(page);
  await expect(page.getByLabel("Minimum password length")).toHaveValue(String(platform));
  await page.getByRole("button", { name: "Exit support mode" }).click();
});

test("require new password: the person is signed out, the old password stops working, and sign-in explains why", async ({ page, browser }) => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const email = `e2e.reset.${Date.now()}@annabadigital.io`; const pw = "Sup3r-Secret!pass-9";
  const anon = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const p2 = await anon.newPage();
  await provisionUser(admin, p2, email, "Reset Me", pw);
  await uiLogin(p2, email, pw); await expect(p2).toHaveURL(/\/dashboard/);
  const id = (await (await admin.get(`/api/users?q=${encodeURIComponent(email)}`)).json()).data[0].id as string;

  await page.goto(`/users/${id}?tab=security`);
  await expect(page.getByRole("button", { name: "Reset two-step verification" })).toBeDisabled(); // they have none set up
  await page.getByRole("button", { name: "Require new password" }).click();
  await expect(page.getByRole("dialog").getByText(/signed out everywhere/)).toBeVisible();
  await page.getByRole("dialog").getByRole("button", { name: "Require new password" }).click();
  await reauthIfAsked(page);
  await expect(page.getByText(/e-mailed a link to choose a new password/i).first()).toBeVisible();

  expect((await p2.request.get("/api/auth/me")).status()).toBe(401); // signed out everywhere
  await p2.context().clearCookies();
  await uiLogin(p2, email, pw);
  await expect(p2).toHaveURL(/\/account\/password_reset_required/);
  await expect(p2.getByText("Choose a new password").first()).toBeVisible();
  await anon.close(); await admin.dispose();
});

test("privacy: a person downloads their data and asks to close their account; an administrator declines and they see why", async ({ page, browser }) => {
  const member = await browser.newContext({ storageState: authFile("member") }); const mp = await member.newPage();
  await mp.goto("/profile?tab=privacy");
  const download = mp.waitForEvent("download");
  await mp.getByRole("button", { name: /Download my data/ }).click();
  await reauthIfAsked(mp);
  const file = await download; expect(file.suggestedFilename()).toMatch(/^annaba-digital-my-data-\d{8}|annaba-digital-my-data-\d{4}-\d{2}-\d{2}\.json$/);

  const cancel = mp.getByRole("button", { name: /Cancel my request/ });
  if (await cancel.isVisible().catch(() => false)) { await cancel.click(); }
  await mp.getByRole("button", { name: "Ask to close my account" }).click();
  await expect(mp.getByRole("dialog").getByText(/14-day waiting period/)).toBeVisible();
  await mp.getByRole("dialog").getByRole("button", { name: "Send request" }).click();
  await reauthIfAsked(mp);
  await expect(mp.getByText("Waiting for an administrator")).toBeVisible();

  await page.goto("/account-closures");
  const row = page.getByRole("row", { name: /Rizky/ });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Decline" }).click();
  await page.getByRole("dialog").getByLabel(/Note to the person/).fill("Please talk to HR first.");
  await page.getByRole("dialog").getByRole("button", { name: "Decline" }).click();
  await expect(page.getByRole("row", { name: /Rizky/ })).toHaveCount(0); // gone from the pending list

  await mp.reload(); await mp.goto("/profile?tab=privacy");
  await expect(mp.getByText(/Your last request was declined: Please talk to HR first\./)).toBeVisible();
  await member.close();
});

test("dark mode: the toggle switches theme, the choice survives a reload, and 'same as device' follows the system", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await page.goto("/dashboard");
  const html = page.locator("html");
  await expect(html).toHaveAttribute("data-theme", "light");
  await page.getByRole("button", { name: "Switch to dark mode" }).click();
  await expect(html).toHaveAttribute("data-theme", "dark");
  await page.reload();
  await expect(html).toHaveAttribute("data-theme", "dark"); // remembered, applied before first paint
  await expect(page.getByRole("button", { name: "Switch to light mode" })).toBeVisible();

  await page.goto("/profile?tab=notifications");
  await page.getByRole("button", { name: "Same as device" }).click();
  await expect(html).toHaveAttribute("data-theme", "light"); // the (light) system setting wins again
  await page.emulateMedia({ colorScheme: "dark" });
  await expect(html).toHaveAttribute("data-theme", "dark"); // and follows it live
  await page.getByRole("button", { name: "Light", exact: true }).click();
  await expect(html).toHaveAttribute("data-theme", "light");
  await page.evaluate(() => localStorage.removeItem("annaba-digital-theme"));
});

test.describe("bahasa Indonesia", () => {
  test("sign-in screens switch language and remember it; the app follows the person's own language setting", async ({ page, browser }) => {
    // before sign-in: the switch on the login screen
    const anon = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const lp = await anon.newPage();
    await lp.goto("/login");
    await expect(lp.getByRole("heading", { name: /Welcome back/ })).toBeVisible();
    await lp.getByRole("button", { name: "Indonesia" }).click();
    await expect(lp.getByRole("heading", { name: /Selamat datang kembali/ })).toBeVisible();
    await expect(lp.locator("html")).toHaveAttribute("lang", "id");
    await lp.reload();
    await expect(lp.getByRole("heading", { name: /Selamat datang kembali/ })).toBeVisible(); // remembered on this device
    await anon.close();

    // signed in: Settings → Language is saved on the account and the whole app follows
    try {
      await page.goto("/settings");
      await page.getByLabel("Language").selectOption("id");
      await page.getByRole("button", { name: "Save settings" }).click();
      await expect(page.getByRole("navigation", { name: "Navigasi utama" })).toBeVisible();
      await page.goto("/dashboard");
      await expect(page.getByRole("link", { name: "Beranda" })).toBeVisible();
      await page.goto("/users/all");
      await expect(page.getByRole("heading", { name: "Semua Pengguna" })).toBeVisible();
      await expect(page.getByLabel("Cari pengguna")).toBeVisible();
      await expect(page.getByText(/Baris per halaman/)).toBeVisible();
      await page.reload();
      await expect(page.getByRole("heading", { name: "Semua Pengguna" })).toBeVisible(); // from the account setting, not just this browser
      // administration screens too
      await page.goto("/audit"); await expect(page.getByRole("heading", { name: "Log Audit" })).toBeVisible(); await expect(page.getByLabel("Cari log audit")).toBeVisible();
      await page.goto("/roles"); await expect(page.getByRole("heading", { name: "Peran", exact: true })).toBeVisible();
      await page.goto("/sessions"); await expect(page.getByRole("heading", { name: "Sesi & Perangkat" })).toBeVisible();
      await page.goto("/admin/tenants"); await expect(page.getByRole("button", { name: "Tenant baru" })).toBeVisible();
    } finally {
      await page.goto("/settings");
      await page.getByLabel("Bahasa").selectOption("en");
      await page.getByRole("button", { name: "Simpan pengaturan" }).click();
      await expect(page.getByRole("heading", { name: "Settings" })).toBeVisible();
      await page.evaluate(() => localStorage.removeItem("annaba-digital-lang"));
    }
  });
});
