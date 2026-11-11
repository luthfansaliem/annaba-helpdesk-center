import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { USERS, authFile, reauthIfAsked } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("directory (R3-9)", () => {
  test("an administrator maps a group to a role and previews and applies the HR file", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await page.goto("/admin/directory");
    await expect(page.getByRole("heading", { name: "Directory", level: 1 })).toBeVisible();
    await expect(page.getByTestId("scim-url")).toContainText("/api/v1/scim/v2");

    // a group that gives the GA Agent role
    const group = `E2E-GA-${Date.now().toString(36)}`;
    await page.locator("#dm-group").fill(group);
    await page.locator("#dm-role").selectOption({ label: "GA Agent" });
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await reauthIfAsked(page);
    const row = page.getByTestId("dir-mappings").getByRole("listitem").filter({ hasText: group });
    await expect(row).toContainText("GA Agent");
    await axe(page, "directory");

    // the HR file: a preview first, then apply; an unknown person is reported, nobody is created
    const emp = `E-${Date.now().toString(36).toUpperCase()}`;
    await page.locator("#dir-csv").fill(`email,employee_id,status\n${USERS.member},${emp},active\nnobody-${emp.toLowerCase()}@example.com,X,active`);
    await page.getByRole("button", { name: "Preview" }).click();
    const result = page.getByTestId("dir-result");
    await expect(result).toContainText("1 updated");
    await expect(result).toContainText("1 not in the app");
    await page.getByRole("button", { name: "Apply" }).click();
    await reauthIfAsked(page);
    await expect(result).toContainText("Applied");
    await axe(page, "directory result");

    // remove the mapping again
    await row.getByRole("button", { name: `Remove ${group} → GA Agent` }).click();
    await reauthIfAsked(page);
    await expect(page.getByTestId("dir-mappings").getByRole("listitem").filter({ hasText: group })).toHaveCount(0);
  });
});
