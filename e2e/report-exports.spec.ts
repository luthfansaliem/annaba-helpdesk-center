import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { USERS, authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("report exports (R3-8)", () => {
  test("an administrator downloads a report and schedules it by e-mail", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await page.goto("/reports/schedules");
    await expect(page.getByRole("heading", { name: "Report exports", level: 1 })).toBeVisible();

    // download now: the link carries the choice and returns a workbook
    await page.getByLabel("Report", { exact: true }).selectOption({ label: "Stock on hand" });
    await page.getByLabel("File", { exact: true }).selectOption({ label: "CSV" });
    const href = await page.getByTestId("rx-download").getAttribute("href");
    expect(href).toContain("report=inventory"); expect(href).toContain("format=csv");
    const file = await page.request.get(href!);
    expect(file.status()).toBe(200);
    expect(file.headers()["content-type"]).toContain("text/csv");
    expect(await file.text()).toContain("SKU,Item,Category,Place,Quantity");
    await axe(page, "report exports");

    // a weekly schedule to themselves, sent now
    const name = `Weekly stock ${Date.now().toString(36)}`;
    await page.getByRole("button", { name: "New schedule" }).click();
    const dlg = page.getByRole("dialog", { name: "New scheduled report" });
    await dlg.getByLabel("Name").fill(name);
    await dlg.getByLabel("Report").selectOption({ label: "Stock on hand" });
    await dlg.getByLabel("How often").selectOption({ label: "Every week" });
    await dlg.getByLabel("On", { exact: true }).selectOption({ label: "Friday" });
    await dlg.getByLabel("Send to (e-mail addresses)").fill(USERS.admin);
    await axe(page, "new schedule");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(dlg).toBeHidden();
    const row = page.getByTestId("rs-list").getByRole("listitem").filter({ hasText: name });
    await expect(row).toContainText("Every Friday at 07:00");
    await row.getByRole("button", { name: "Send now" }).click();
    await expect(row.getByTestId("rs-last")).toContainText("Sent to 1 person");
    await row.getByRole("button", { name: `Remove ${name}` }).click();
    await expect(page.getByTestId("rs-list").getByRole("listitem").filter({ hasText: name })).toHaveCount(0);
  });
});
