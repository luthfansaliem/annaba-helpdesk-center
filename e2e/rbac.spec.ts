import { expect, test } from "@playwright/test";
import { USERS, authFile } from "./helpers";

test.describe("plain user", () => {
  test.use({ storageState: authFile("member") });
  test("sees only personal navigation and a self-service dashboard", async ({ page }) => {
    await page.goto("/dashboard");
    await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
    const nav = page.getByRole("navigation", { name: "Main navigation" });
    for (const hidden of ["All Users", "Roles", "Access Matrix", "Audit Logs", "System Settings"]) await expect(nav.getByRole("link", { name: hidden, exact: true })).toHaveCount(0);
    await nav.getByRole("button", { name: /Personal/ }).click();
    await expect(nav.getByRole("link", { name: "My Profile" })).toBeVisible();
  });
  test("direct URL access to admin pages is denied by the server, not just hidden", async ({ page, request }) => {
    await page.goto("/users/all");
    await expect(page.getByText("Access denied")).toBeVisible();
    for (const p of ["/api/users", "/api/roles", "/api/audit", "/api/settings", "/api/access-matrix"]) expect((await request.get(p)).status(), p).toBe(403);
  });
  test("cannot escalate: assigning a role through the API is refused", async ({ request }) => {
    const r = await request.put(`/api/users/00000000-0000-0000-0000-000000000001/roles`, { data: { roleIds: [] }, headers: { Origin: "http://localhost:3100" } });
    expect([401, 403]).toContain(r.status());
  });
});

test.describe("security auditor", () => {
  test.use({ storageState: authFile("auditor") });
  test("can read audit & security events but not manage users", async ({ page }) => {
    await page.goto("/audit"); await expect(page.getByRole("heading", { name: "Audit Logs" })).toBeVisible();
    await page.goto("/security-events"); await expect(page.getByRole("heading", { name: "Security Events" })).toBeVisible();
    await page.goto("/admin/settings"); await expect(page.getByText("Access denied")).toBeVisible();
  });
});

test.describe("user admin (hierarchy rule)", () => {
  test.use({ storageState: authFile("userAdmin") });
  test("cannot see or grant the Super Admin role", async ({ page, request }) => {
    await page.goto("/users/all");
    await page.getByRole("button", { name: /add user/i }).click();
    const sa = page.getByRole("dialog").getByRole("checkbox", { name: /Super Admin/i });
    if (await sa.count()) await expect(sa).toBeDisabled();
    const r = await request.get("/api/users");
    expect(r.ok()).toBeTruthy();
    void USERS;
  });
  test("the user list offers no Suspend or Edit on a Super Admin's row", async ({ page }) => {
    await page.goto("/users/all");
    await page.getByLabel("Search users").fill(USERS.admin);
    await page.getByRole("button", { name: /Actions for Sarah/ }).click();
    const menu = page.getByRole("menu");
    await expect(menu.getByRole("menuitem", { name: "View details" })).toBeVisible();
    await expect(menu.getByRole("menuitem", { name: /View only/ })).toBeDisabled();
    for (const gone of ["Suspend", "Edit", "Activate", "Unlock"]) await expect(menu.getByRole("menuitem", { name: gone, exact: true })).toHaveCount(0);
  });
  test("a Super Admin's page is view-only with an explanation", async ({ page, request }) => {
    const list = await (await request.get("/api/users?search=sarah")).json();
    const sarah = list.data.find((u: { email: string }) => u.email === USERS.admin);
    await page.goto(`/users/${sarah.id}?tab=roles`);
    await expect(page.getByText(/View only\./)).toBeVisible();
    await expect(page.getByRole("button", { name: "Save roles" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "User actions" })).toHaveCount(0);
    // the API refusal itself is covered by the dotnet hierarchy test
  });
});

test.describe("access requests", () => {
  test.use({ storageState: authFile("member") });
  test("a plain user requests a role, sees it pending, cancels it, and gets no review queue", async ({ page }) => {
    await page.goto("/access-requests");
    await expect(page.getByRole("heading", { name: "Review requests" })).toHaveCount(0); // deciding needs users.assign_role
    await page.getByLabel(/^Role/).selectOption({ label: "Security Auditor" });
    await page.getByLabel(/Why do you need it/).fill("I need to read the audit trail for a review.");
    await page.getByRole("button", { name: "Send request" }).click();
    const row = page.getByRole("row", { name: /Security Auditor.*Pending/ });
    await expect(row).toBeVisible();
    await row.getByRole("button", { name: "Cancel" }).click();
    await expect(page.getByRole("row", { name: /Security Auditor.*Cancelled/ })).toBeVisible();
  });
});

test.describe("approval badges", () => {
  test.use({ storageState: authFile("admin") });
  test("a pending access request shows a count on the admin's sidebar until it is decided", async ({ page, browser }) => {
    const member = await browser.newContext({ storageState: authFile("member") });
    const mp = await member.newPage();
    await mp.goto("/access-requests");
    await mp.getByLabel(/^Role/).selectOption({ label: "Security Auditor" });
    await mp.getByLabel(/Why do you need it/).fill("I need to read the audit trail for a review.");
    await mp.getByRole("button", { name: "Send request" }).click();
    await expect(mp.getByRole("row", { name: /Security Auditor.*Pending/ })).toBeVisible();

    await page.goto("/dashboard");
    const nav = page.getByRole("navigation", { name: "Main navigation" });
    await expect(nav.locator(".badge").first()).toBeVisible(); // on the collapsed group
    await page.goto("/access-requests");
    await expect(nav.getByRole("link", { name: /Access Requests/ }).locator(".badge")).toBeVisible();

    await mp.getByRole("row", { name: /Security Auditor.*Pending/ }).getByRole("button", { name: "Cancel" }).click();
    await expect(mp.getByRole("row", { name: /Security Auditor.*Cancelled/ })).toBeVisible();
    await member.close();
  });
});
