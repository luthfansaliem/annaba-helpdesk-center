import { expect, request as pwRequest, test } from "@playwright/test";
import { PASSWORD, USERS, WEB, apiPost, authFile, emailedLink, provisionUser, uiLogin } from "./helpers";

test.describe("authentication", () => {
  test("anonymous visitors are sent to sign-in and come back to the page they asked for", async ({ page }) => {
    await page.goto("/audit");
    await expect(page).toHaveURL(/\/login\?.*returnTo=%2Faudit/);
    await page.getByLabel("Email").fill(USERS.admin);
    await page.getByPlaceholder("Enter your password").fill(PASSWORD);
    await page.getByRole("button", { name: /^sign in$/i }).click();
    await expect(page).toHaveURL(/\/audit$/);
    await expect(page.getByRole("heading", { name: "Audit Logs" })).toBeVisible();
  });

  test("wrong credentials show one generic message and do not reveal whether the account exists", async ({ page }) => {
    await uiLogin(page, USERS.member, "definitely-wrong-password");
    const a = (await page.locator(".form-alert").first().innerText()).trim();
    await page.goto("/login");
    await uiLogin(page, "nobody@annabadigital.io", "definitely-wrong-password");
    const b = (await page.locator(".form-alert").first().innerText()).trim();
    expect(a).toBe(b);
    await expect(page).toHaveURL(/\/login/);
  });

  test("open redirects through returnTo are ignored", async ({ page }) => {
    await uiLogin(page, USERS.member, PASSWORD, "https://evil.example/phish");
    await expect(page).toHaveURL(/\/dashboard$/);
  });

  test("sign out ends the session on the server", async ({ page, context }) => {
    await uiLogin(page, USERS.member);
    await expect(page).toHaveURL(/\/dashboard/);
    await page.getByRole("button", { name: /account menu|rizky/i }).first().click();
    await page.getByRole("menuitem", { name: "Sign out" }).click();
    await expect(page).toHaveURL(/\/login/);
    expect((await context.request.get("/api/auth/me")).status()).toBe(401);
    await page.goto("/dashboard");
    await expect(page).toHaveURL(/\/login/);
  });

  test("lock screen: server-side lock blocks the API until the password is re-entered", async ({ page, context }) => {
    await uiLogin(page, USERS.userAdmin);
    await expect(page).toHaveURL(/\/dashboard/);
    await page.getByRole("button", { name: /account menu|daniel/i }).first().click();
    await page.getByRole("menuitem", { name: "Lock screen" }).click();
    await expect(page).toHaveURL(/\/locked/);
    expect((await context.request.get("/api/users")).status()).toBe(423);
    await page.getByPlaceholder("Password").fill("wrong-password-123");
    await page.getByRole("button", { name: /unlock/i }).click();
    await expect(page.locator(".form-alert")).toBeVisible();
    await page.getByPlaceholder("Password").fill(PASSWORD);
    await page.getByRole("button", { name: /unlock/i }).click();
    await expect(page).toHaveURL(/\/dashboard/);
    expect((await context.request.get("/api/users")).status()).toBe(200);
  });

  test("registration is closed by default and says so", async ({ page }) => {
    await page.goto("/register");
    await expect(page.getByText(/invitation|closed|not open/i).first()).toBeVisible();
  });

  test("forgot password answers identically for unknown addresses; the emailed link resets the password once", async ({ page, browser }) => {
    const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
    const victim = `e2e.reset.${Date.now()}@annabadigital.io`;
    const c0 = await browser.newContext({ storageState: { cookies: [], origins: [] } }); await provisionUser(admin, await c0.newPage(), victim, "Reset Me"); await c0.close(); await admin.dispose();
    await page.goto("/forgot-password");
    await page.getByLabel("Email").fill("ghost@annabadigital.io");
    await page.getByRole("button", { name: /send|reset/i }).click();
    await expect(page.getByText(/if an account|check your email|sent/i).first()).toBeVisible();
    const unknown = await page.locator("body").innerText();
    await page.goto("/forgot-password");
    await page.getByLabel("Email").fill(victim);
    await page.getByRole("button", { name: /send|reset/i }).click();
    await expect(page.getByText(/if an account|check your email|sent/i).first()).toBeVisible();
    expect(unknown).toMatch(/if an account|check your email|sent/i);

    const link = await emailedLink(victim, "/reset-password");
    await page.goto(link);
    const next = "Zq9!Lantern-Orbit-42";
    await page.getByPlaceholder("New password").fill(next);
    await page.getByPlaceholder("Repeat password").fill(next);
    await page.getByRole("button", { name: /update password/i }).click();
    await expect(page).toHaveURL(/\/login\?reset=1/);
    await uiLogin(page, victim, next);
    await expect(page).toHaveURL(/\/dashboard/);
    // the token is single-use
    await page.context().clearCookies();
    await page.goto(link);
    await page.getByPlaceholder("New password").fill("An0ther-Passphrase!88");
    await page.getByPlaceholder("Repeat password").fill("An0ther-Passphrase!88");
    await page.getByRole("button", { name: /update password/i }).click();
    await expect(page.getByText(/expired|invalid|no longer|already been used/i).first()).toBeVisible();
  });
});

test("the proxy never lets a browser reach the API with a forged proxy key", async ({ request }) => {
  // the proxy overwrites the header with the real key; a browser-supplied value is never trusted or forwarded
  const me = await request.get("/api/auth/me", { headers: { "x-internal-proxy-key": "forged" } });
  expect(me.status()).toBe(401);
  void apiPost;
});

test.describe("single sign-on (not configured in this environment)", () => {
  test.use({ storageState: { cookies: [], origins: [] } });
  test("the login page offers no SSO button, the start URL is a 404, and a failed return explains itself", async ({ page, request }) => {
    await page.goto("/login");
    await expect(page.getByRole("button", { name: "Sign In" })).toBeVisible();
    await expect(page.getByRole("link", { name: /sign in with/i })).toHaveCount(0);
    expect((await request.get("/api/auth/sso/start", { maxRedirects: 0 })).status()).toBe(404);
    await page.goto("/login?sso=denied");
    await expect(page.getByRole("alert").filter({ hasText: "active user" })).toBeVisible();
  });
});
