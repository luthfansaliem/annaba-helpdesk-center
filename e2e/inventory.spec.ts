import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const uniq = () => Date.now().toString(36).toUpperCase() + Math.random().toString(36).slice(2, 5).toUpperCase();

test.describe("inventory (R3-6)", () => {
  test("GA adds an item, receives and hands out stock, sees it low and to reorder, and counts the shelf", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const place = `Store ${uniq()}`;
    await admin.post("/api/locations", { data: { type: "site", code: `ST-${uniq()}`, name: place }, headers: { "X-CSRF-Token": await csrf(admin), Origin: WEB } });
    const sku = `ATK-${uniq()}`; const name = `Pen black ${sku}`;

    // --- a new item
    await page.goto("/inventory");
    await expect(page.getByRole("heading", { name: "Inventory", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "New item" }).click();
    const dlg = page.getByRole("dialog", { name: "New stock item" });
    await dlg.locator("#it-sku").fill(sku);
    await dlg.locator("#it-name").fill(name);
    await dlg.locator("#it-unit").fill("pcs");
    await dlg.locator("#it-level").fill("10");
    await dlg.locator("#it-rq").fill("100");
    await axe(page, "new item");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("heading", { name, level: 1 })).toBeVisible();

    // --- receive 20, hand out 15: low, and on the reorder list
    const move = async (what: string, qty: string, ref: string) => {
      await page.getByRole("button", { name: "Move stock" }).click();
      const mv = page.getByRole("dialog", { name: "Move stock" });
      await mv.locator("#mv-kind").selectOption({ label: what });
      await mv.locator("#mv-loc").selectOption({ label: place });
      await mv.locator("#mv-qty").fill(qty);
      await mv.locator("#mv-ref").fill(ref);
      await mv.getByRole("button", { name: "Save" }).click();
      await expect(mv).toBeHidden();
    };
    await move("Received a delivery", "20", "PO-1 Toko Sinar");
    await expect(page.getByTestId("stock-places")).toContainText("20 pcs");
    await move("Handed out", "15", "Finance team");
    await expect(page.getByTestId("stock-places")).toContainText("5 pcs");
    await expect(page.getByTestId("stock-places")).toContainText("Low");
    await axe(page, "item");
    await page.goto("/inventory");
    await expect(page.getByTestId("reorder").getByRole("listitem").filter({ hasText: name })).toContainText("Order 100 pcs");
    await axe(page, "inventory");

    // --- a stock-take finds 4 instead of 5 and posts the difference
    await page.goto("/inventory/stock-takes");
    await page.locator("#st-place").selectOption({ label: place });
    await page.getByRole("button", { name: "Start count" }).click();
    await page.getByLabel(`Counted ${name}`).fill("4");
    await axe(page, "stock-take");
    await page.getByRole("button", { name: "Close and post differences" }).click();
    await expect(page.getByText("1 of 1 counted, 1 differences")).toBeVisible();
    const items = await (await admin.get(`/api/inventory/items?q=${sku}`)).json() as { total: number }[];
    expect(items[0].total).toBe(4);
  });
});
