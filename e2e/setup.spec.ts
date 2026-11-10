import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { PASSWORD, apiPost, authFile, emailedLink, uiLogin } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("go-live checklist and quick start (ADM-001)", () => {
  test("the checklist shows progress and where each missing piece is configured", async ({ browser }) => {
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await admin.goto("/setup/start");
    await expect(admin.getByRole("heading", { name: "Get started", level: 1 })).toBeVisible();
    const bar = admin.getByRole("progressbar", { name: "Setup progress" });
    await expect(bar).toBeVisible();
    await expect(bar).toHaveAttribute("aria-valuemax", "6");
    for (const title of ["Add your first site", "Working hours", "A team with people in it", "Publish at least one service", "Service-level targets", "Route new requests to a team"])
      await expect(admin.getByText(title, { exact: true })).toBeVisible();
    await expect(admin.getByRole("link", { name: "Open catalog" })).toHaveAttribute("href", "/setup/catalog");
    await axe(admin, "get started");
  });

  test("a new organisation's administrator sets up a working helpdesk in one step", async ({ browser }) => {
    const platform = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const slug = `qs-${Date.now().toString(36)}`; const email = `${slug}@annabadigital.io`; const pw = "Sup3r-Secret!pass-9";
    await platform.goto("/dashboard");
    expect((await apiPost(platform.request, "/api/auth/reauth", { password: PASSWORD })).ok()).toBeTruthy();
    const created = await apiPost(platform.request, "/api/tenants", { name: `Quick ${slug}`, slug, adminEmail: email });
    expect(created.status(), await created.text()).toBe(201);

    // The API limits invitation acceptance to 10 per 15 minutes per client address, and a fast full run of the suite uses them all from one address. The proxy passes X-Forwarded-For
    // through, so this organisation's administrator "comes from" an address of their own.
    const anon = await browser.newContext({ storageState: { cookies: [], origins: [] }, extraHTTPHeaders: { "X-Forwarded-For": `10.20.${Math.floor(Math.random() * 200) + 20}.${Math.floor(Math.random() * 200) + 20}` } }); const p = await anon.newPage();
    await p.goto(await emailedLink(email, "/invite"));
    await p.getByLabel("Full name").fill("Quick Admin");
    await p.getByLabel("Create password").fill(pw);
    await p.getByRole("button", { name: /activate account/i }).click();
    await expect(p).toHaveURL(/\/login\?accepted=1/);
    await uiLogin(p, email, pw);
    await expect(p).toHaveURL(/\/dashboard/);

    await p.goto("/setup/start");
    await expect(p.getByRole("heading", { name: /3 steps left/ })).toBeVisible();                       // calendar, catalog and service levels come with the organisation
    await expect(p.getByRole("progressbar", { name: "Setup progress" })).toHaveAttribute("aria-valuenow", "3");
    await axe(p, "get started (new organisation)");

    // the form says what is missing, in the right place
    await p.getByRole("button", { name: "Set up for me" }).click();
    await expect(p.getByText("Name your first site (2–120 characters).")).toBeVisible();
    await expect(p.getByText("Choose at least one person who will work the tickets.")).toBeVisible();

    await p.getByLabel("Name of your first site").fill("Head Office");
    await p.getByLabel("Name of your first team").fill("General Affairs");
    await p.getByRole("group", { name: /Who works the tickets/ }).getByRole("checkbox", { name: /Quick Admin/ }).check();
    await p.getByRole("button", { name: "Set up for me" }).click();

    await expect(p.getByRole("heading", { name: "Your helpdesk is ready to take requests" })).toBeVisible();
    await expect(p.getByRole("progressbar", { name: "Setup progress" })).toHaveAttribute("aria-valuenow", "6");
    await expect(p.getByRole("form", { name: "Quick start" })).toHaveCount(0);                            // nothing left to fill in
    await axe(p, "get started (ready)");

    // what it made is real and editable on the ordinary screens
    await p.goto("/setup/locations"); await expect(p.getByText("Head Office").first()).toBeVisible();
    await p.goto("/setup/teams"); await expect(p.getByText("General Affairs").first()).toBeVisible();
    await p.goto("/setup/assignment-rules"); await expect(p.getByText("All requests").first()).toBeVisible();
    await anon.close();
  });

  test("people who only work tickets are turned away from the checklist", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/setup/start");
    await expect(member.getByRole("heading", { name: "Get started", level: 1 })).toHaveCount(0);
    await expect(member.getByText(/permission|not allowed|access/i).first()).toBeVisible();
  });
});
