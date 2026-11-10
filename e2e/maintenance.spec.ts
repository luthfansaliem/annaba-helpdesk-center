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
const day = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10);

test.describe("preventive maintenance (R2-5)", () => {
  test("a plan for every asset of a type in a place makes its visit at once, can be paused and resumed, and shows in the compliance report", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const planName = `AC monthly ${uniq()}`; const siteName = `PM site ${uniq()}`; const typeName = `Split AC ${uniq()}`; const listName = `AC check ${uniq()}`; const svcName = `Planned upkeep ${uniq()}`;

    const site = await ok<{ id: string }>(admin.post("/api/locations", { data: { type: "site", code: `PM-${uniq()}`.toUpperCase(), name: siteName }, headers: await hdr(admin) }));
    const type = await ok<{ id: string }>(admin.post("/api/asset-types", { data: { name: typeName }, headers: await hdr(admin) }));
    const asset = await ok<{ id: string; tag: string }>(admin.post("/api/assets", { data: { name: "Lobby split AC", typeId: type.id, status: "in_use", locationId: site.id }, headers: await hdr(admin) }));
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `PM cat ${uniq()}` }, headers: await hdr(admin) }));
    await ok(admin.post("/api/catalog/services", { data: { name: svcName, categoryId: cat.id }, headers: await hdr(admin) }));        // unpublished: staff-only
    await ok(admin.post("/api/checklist-templates", { data: { name: listName, items: [{ text: "Clean the filters", required: true }] }, headers: await hdr(admin) }));

    // --- the plan
    await page.goto("/maintenance");
    await axe(page, "maintenance plans");
    await page.getByRole("button", { name: "New plan" }).click();
    const dlg = page.getByRole("dialog", { name: "New maintenance plan" });
    await dlg.getByLabel("Name").fill(planName);
    await dlg.getByLabel("Filed under service").selectOption({ label: svcName });
    await expect(dlg.getByRole("radio", { name: "Every asset of a type" })).toBeChecked();
    await dlg.getByLabel("Asset type").selectOption({ label: typeName });
    await dlg.getByLabel("Only in this place").selectOption({ label: siteName });
    await dlg.locator("#pm-every").fill("1");
    await dlg.getByLabel("Unit").selectOption("month");
    await dlg.getByLabel("First date").fill(day(2));
    await dlg.getByLabel("Make the work this many days before").fill("7");
    await dlg.getByLabel("Checklist").selectOption({ label: listName });
    await dlg.getByRole("button", { name: "Save" }).click();

    // --- its page: the first visit is already made
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(planName);
    await expect(page.getByText("Every Split AC", { exact: false }).first()).toBeVisible();
    const visits = page.getByRole("table", { name: "Visits" });
    await expect(visits.getByRole("row")).toHaveCount(2);                  // header + one visit
    await expect(visits).toContainText(asset.tag); await expect(visits).toContainText("Planned"); await expect(visits).toContainText(day(2));
    await expect(page.getByTestId("pm-upcoming").getByRole("listitem")).toHaveCount(5);
    await axe(page, "maintenance plan");
    const planUrl = page.url();

    // --- the work order carries the checklist
    await visits.getByRole("link", { name: /-W1$/ }).click();
    await expect(page.getByRole("heading", { level: 1 })).toContainText(planName);
    await expect(page.getByRole("checkbox", { name: /Clean the filters/ })).toBeVisible();

    // --- pause and resume
    await page.goto(planUrl);
    await page.getByRole("button", { name: "Pause" }).click();
    await expect(page.getByText("Paused: no work is made until it is resumed.")).toBeVisible();
    await page.getByRole("button", { name: "Resume" }).click();
    await expect(page.getByTestId("pm-upcoming")).toBeVisible();
    await expect(page.getByRole("button", { name: "Remove" })).toHaveCount(0);     // it has history

    // --- the compliance report lists it (not due yet: no percentage)
    await page.goto("/maintenance/compliance");
    await page.locator("#pc-to").fill(day(30));
    const row = page.getByRole("row").filter({ hasText: planName });
    await expect(row).toBeVisible(); await expect(row).toContainText("—");
    await page.getByLabel("Group by").selectOption("site");
    await expect(page.getByRole("row").filter({ hasText: siteName })).toBeVisible();
    await axe(page, "maintenance compliance");

    // --- requesters see no plans
    expect((await member.get("/api/maintenance/plans")).status()).toBe(403);
  });
});
