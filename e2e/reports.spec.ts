import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { authFile } from "./helpers";

test.use({ storageState: authFile("admin") });

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("operations and SLA reports (RPT-001/002)", () => {
  test("the operations dashboard shows the numbers, the charts and what needs attention, and the period can change", async ({ page }) => {
    await page.goto("/reports");
    await expect(page.getByRole("heading", { name: "Operations" })).toBeVisible();
    const metrics = page.getByRole("region", { name: "Key metrics" });
    for (const label of ["Open", "Overdue", "At risk", "Unassigned", "Waiting for approval", "SLA attainment"]) await expect(metrics.getByText(label, { exact: true })).toBeVisible();
    await expect(page.getByRole("region", { name: "Flow and speed" }).getByText("Avg first response")).toBeVisible();
    await expect(page.getByRole("img", { name: "Created and resolved tickets per day" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Backlog by team" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Needs attention now" })).toBeVisible();
    await axe(page, "operations dashboard");

    await page.getByLabel("Period").selectOption("7");
    await expect(page.getByRole("img", { name: "Created and resolved tickets per day" })).toBeVisible();
    await page.getByLabel("Period").selectOption("90");
    await expect(page.getByRole("heading", { name: "Operations" })).toBeVisible();
  });

  test("the SLA report lists attainment by priority, team and service, and the breaches", async ({ page }) => {
    await page.goto("/reports/sla");
    await expect(page.getByRole("heading", { name: "SLA Report" })).toBeVisible();
    await expect(page.getByRole("region", { name: "Key metrics" }).getByText("SLA attainment")).toBeVisible();
    await expect(page.getByRole("table", { name: "SLA by priority" })).toBeVisible();
    await expect(page.getByRole("table", { name: "SLA by team" })).toBeVisible();
    await expect(page.getByRole("table", { name: "SLA by service" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Breaches" })).toBeVisible();
    await axe(page, "SLA report");
    await page.getByLabel("Period").selectOption("7");
    await expect(page.getByRole("heading", { name: "SLA Report" })).toBeVisible();
  });

  test("a person without a ticket read permission is turned away", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/reports");
    await expect(member.getByText(/permission|access/i).first()).toBeVisible();
  });
});
