import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const uniq = () => Date.now().toString(36).toUpperCase() + Math.random().toString(36).slice(2, 5).toUpperCase();

test.describe("supply requests (R3-7)", () => {
  test("an employee asks for supplies and GA issues them from the store", async ({ browser }) => {
    const gaPage = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = gaPage.request;
    const h = { "X-CSRF-Token": await csrf(admin), Origin: WEB };
    const place = `Supply store ${uniq()}`;
    const store = (await (await admin.post("/api/locations", { data: { type: "site", code: `SS-${uniq()}`, name: place }, headers: h })).json()).id as string;
    const sku = `ATK-${uniq()}`; const name = `Sticky notes ${sku}`;
    const item = (await (await admin.post("/api/inventory/items", { data: { sku, name, category: "atk", unit: "pad", reorderLevel: 0, unitCost: 8000 }, headers: h })).json()).id as string;
    await admin.post(`/api/inventory/items/${item}/moves`, { data: { kind: "receipt", locationId: store, quantity: 10 }, headers: h });
    await admin.put("/api/supplies/settings", { data: { approvalAbove: null, defaultLocationId: store }, headers: h });

    // --- the employee asks for 3 pads
    const page = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await page.goto("/supplies");
    await expect(page.getByRole("heading", { name: "Supplies", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "Ask for supplies" }).first().click();
    const dlg = page.getByRole("dialog", { name: "Ask for supplies" });
    await dlg.getByLabel("Collect from").selectOption({ label: place });
    await dlg.getByLabel("Search items").fill(sku);
    await expect(dlg.getByTestId("supply-catalog")).toContainText("10 pad available");
    await dlg.getByLabel(`Quantity ${name}`).fill("3");
    await expect(dlg.getByTestId("sr-estimate")).toContainText("1 items");
    await axe(page, "ask for supplies");
    await dlg.getByRole("button", { name: "Send request" }).click();
    await expect(dlg).toBeHidden();
    const mine = page.getByTestId("supply-mine").getByRole("listitem").filter({ hasText: name });
    await expect(mine).toContainText("To be issued");
    await axe(page, "supplies (employee)");

    // --- GA issues them; the employee sees them issued and the stock went down
    await gaPage.goto("/supplies");
    const row = gaPage.getByTestId("supply-desk").getByRole("listitem").filter({ hasText: name });
    await row.getByRole("button", { name: "Issue" }).click();
    const issue = gaPage.getByRole("dialog", { name: /^Issue SR-/ });
    await expect(issue.getByLabel(`Issue now ${name}`)).toHaveValue("3");
    await axe(gaPage, "issue");
    await issue.getByRole("button", { name: "Issue" }).click();
    await expect(issue).toBeHidden();
    await gaPage.getByLabel("Show").selectOption({ label: "Everything" });
    await expect(gaPage.getByTestId("supply-desk").getByRole("listitem").filter({ hasText: name })).toContainText("Issued");
    await axe(gaPage, "supplies (desk)");
    await page.reload();
    await page.getByLabel("Show").selectOption({ label: "Everything" });
    await expect(page.getByTestId("supply-mine").getByRole("listitem").filter({ hasText: name })).toContainText("Issued");
    const left = await (await admin.get(`/api/inventory/items/${item}`)).json() as { total: number };
    expect(left.total).toBe(7);
  });
});
