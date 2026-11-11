import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

/** docs/52: home pages by role, the helpdesk menu, and the analytics above each module list. */
test.describe("helpdesk homes and module analytics", () => {
  test("a requester's home asks what they need, finds services as they type and lists their requests, with only their own menu", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await page.goto("/dashboard");
    await expect(page.getByRole("heading", { name: "What do you need?" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Popular services" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "My open requests" })).toBeVisible();
    const nav = page.getByRole("navigation", { name: "Main navigation" });
    await expect(nav.getByRole("link", { name: "Home" })).toHaveAttribute("aria-current", "page");
    for (const hidden of ["Service desk", "Insights", "People & access", "Administration"]) await expect(nav.getByRole("button", { name: new RegExp(hidden) })).toHaveCount(0);
    await page.getByRole("textbox", { name: "What do you need?" }).fill("courier");
    await expect(page.locator(".ask-results").getByRole("link", { name: /Courier & package dispatch/ })).toBeVisible();
    await axe(page, "requester home");
    // the catalog is tiles grouped by category
    await page.goto("/requests/new");
    await expect(page.locator(".tile").first()).toBeVisible();
    await axe(page, "new request catalog");
  });

  test("staff get the service-desk home, the queue with quick views, and a summary above every module", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await page.goto("/dashboard");
    await expect(page.getByRole("heading", { name: /^Hello, / })).toBeVisible();
    const kpis = page.getByRole("region", { name: "Service desk now" });
    for (const k of ["Open tickets", "Unassigned", "At risk", "Overdue"]) await expect(kpis.getByText(k, { exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "My tickets" })).toBeVisible();
    await axe(page, "staff home");

    await page.goto("/tickets");
    const quick = page.getByRole("group", { name: "Quick views" });
    await expect(quick.getByRole("button", { name: /All open/ })).toHaveAttribute("aria-pressed", "true");
    await quick.getByRole("button", { name: /^On hold/ }).click();
    await expect(page.getByLabel("Status", { exact: true })).toHaveValue("onhold");
    await page.getByRole("button", { name: "More filters" }).click();
    await expect(page.getByLabel("Created from")).toBeVisible();
    await axe(page, "ticket queue");

    for (const [path, region, label] of [
      ["/work-orders", "Work orders at a glance", "Not finished"],
      ["/assets", "Assets at a glance", "In service"],
      ["/vendors", "Vendors at a glance", "Active vendors"],
      ["/maintenance", "Maintenance at a glance", "Active plans"],
      ["/kb", "Knowledge base at a glance", "Published"],
    ] as const) {
      await page.goto(path);
      const strip = page.getByRole("region", { name: region });
      // an organisation without any assets/vendors/plans sees the "start here" card instead of the numbers
      await expect(strip.getByText(label, { exact: true }).or(page.locator(".m-empty h3"))).toBeVisible();
      await axe(page, path);
    }

    // the period of a module summary: the finished-work figures follow the chosen period
    await page.goto("/work-orders");
    const glance = page.getByRole("region", { name: "Work orders at a glance" });
    await expect(glance.getByText(/from creation · last 30 days/)).toBeVisible();
    await page.getByLabel("Period", { exact: true }).selectOption("365");
    await expect(glance.getByText(/from creation · last 12 months/)).toBeVisible();

    // the old account overview lives under People & access now
    await page.goto("/access");
    await expect(page.getByRole("heading", { name: "Access overview" })).toBeVisible();
    const nav = page.getByRole("navigation", { name: "Main navigation" });
    await expect(nav.getByRole("link", { name: "Access overview" })).toHaveAttribute("aria-current", "page");
  });
});
