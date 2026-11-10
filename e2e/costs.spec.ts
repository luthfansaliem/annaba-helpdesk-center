import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const uniq = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
async function ok<T>(r: Promise<import("@playwright/test").APIResponse>): Promise<T> { const x = await r; expect(x.ok(), await x.text()).toBeTruthy(); return await x.json() as T; }

test.describe("cost and budget (R2-8)", () => {
  test("a cost centre and a site budget are set up, work costs fill the budget, and the cost report groups and exports them", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const siteName = `Cost site ${uniq()}`; const code = `E2E-${uniq()}`.toUpperCase().slice(0, 20);
    const site = await ok<{ id: string }>(admin.post("/api/locations", { data: { type: "site", code: `CS-${uniq()}`.toUpperCase(), name: siteName }, headers: await hdr(admin) }));

    // --- cost centre for the site
    await page.goto("/costs/centers");
    await page.getByRole("button", { name: "New cost centre" }).click();
    const cc = page.getByRole("dialog", { name: "New cost centre" });
    await cc.getByLabel("Code").fill(code); await cc.getByLabel("Name", { exact: false }).first().fill("Facilities upkeep");
    await cc.getByLabel("Find a place").fill(siteName);
    const box = cc.getByRole("checkbox", { name: new RegExp(siteName) }); await box.click(); await expect(box).toBeChecked();
    await cc.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("row", { name: new RegExp(code) })).toContainText(siteName);

    // --- a budget for the site
    await page.goto("/costs/budgets");
    await page.getByRole("button", { name: "New budget" }).click();
    const bd = page.getByRole("dialog", { name: "New budget" });
    await bd.getByLabel("For").selectOption("site");
    await bd.getByLabel("Which").selectOption({ label: siteName });
    await bd.getByLabel("Amount").fill("1000");
    await bd.getByRole("button", { name: "Save" }).click();
    const row = page.getByTestId("budget-row").filter({ hasText: siteName });
    await expect(row).toContainText("On track"); await expect(row).toContainText("(0%)");

    // --- work at the site costs 900: the budget is near its limit
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `Cost cat ${uniq()}` }, headers: await hdr(admin) }));
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: `Repairs ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));
    const ticket = await ok<{ id: string }>(member.post(`/api/portal/services/${svc.id}/requests`, { data: { subject: "Door lock broken", answers: {}, locationId: site.id }, headers: await hdr(member) }));
    const wo = await ok<{ id: string }>(admin.post(`/api/tickets/${ticket.id}/work-orders`, { data: { title: "Replace the lock" }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/work-orders/${wo.id}/costs`, { data: { kind: "invoice", description: "Locksmith invoice 77", quantity: 1, unitCost: 900 }, headers: await hdr(admin) }));
    await page.reload();
    await expect(row).toContainText("Near the limit"); await expect(row).toContainText("(90%)");
    await axe(page, "budgets");

    // --- the cost report by cost centre, and its export
    await page.goto("/costs");
    await page.getByLabel("Group by").selectOption("cost_center");
    const line = page.getByRole("row", { name: new RegExp(code) });
    await expect(line).toContainText("900.00");
    await expect(page.getByRole("link", { name: "Export CSV" })).toHaveAttribute("href", /report\.csv\?.*by=cost_center/);
    const csv = await (await admin.get("/api/costs/report.csv?by=cost_center")).text();
    expect(csv).toContain(`${code} · Facilities upkeep,`);
    await axe(page, "cost report");
    expect((await member.get("/api/costs/report")).status()).toBe(403);
  });
});
