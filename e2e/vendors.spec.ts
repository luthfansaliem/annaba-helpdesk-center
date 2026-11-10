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
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==", "base64");

test.describe("vendors (R2-4)", () => {
  test("a vendor is added with a contact, place, contract and document, gets a work order with a PO, and is blocked", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const vendorName = `CoolAir ${uniq()}`; const siteName = `Vendor site ${uniq()}`;
    await ok(admin.post("/api/locations", { data: { type: "site", code: `VS-${uniq()}`.toUpperCase(), name: siteName }, headers: await hdr(admin) }));

    // --- add the vendor
    await page.goto("/vendors");
    await axe(page, "vendors");
    await page.getByRole("button", { name: "New vendor" }).click();
    const dlg = page.getByRole("dialog", { name: "New vendor" });
    await dlg.locator("#vd-name").fill(vendorName);
    await dlg.getByLabel("What they do").fill("AC and refrigeration");
    await dlg.locator("#vd-phone").fill("+62 21 555 0101");
    await dlg.getByRole("button", { name: "Add contact" }).click();
    await dlg.locator("#vc-0-n").fill("Budi"); await dlg.locator("#vc-0-r").fill("Account manager");
    await dlg.getByLabel("Find a place").fill(siteName);
    const place = dlg.getByRole("checkbox", { name: new RegExp(siteName) }); await place.click(); await expect(place).toBeChecked();
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(vendorName);
    await expect(page.getByText("Account manager")).toBeVisible();
    await expect(page.getByText(siteName)).toBeVisible();

    // --- a contract and a document
    await page.getByRole("button", { name: "New contract" }).click();
    const ct = page.getByRole("dialog", { name: "New contract" });
    await ct.getByLabel("Title").fill("Yearly AC service"); await ct.getByLabel("Contract number").fill("SPK-001");
    await ct.getByLabel("Starts").fill("2026-01-01"); await ct.getByLabel("Ends").fill("2099-12-31"); await ct.getByLabel("Value").fill("12000000");
    await ct.getByRole("button", { name: "Save" }).click();
    const row = page.getByRole("listitem").filter({ hasText: "Yearly AC service" });
    await expect(row).toContainText("Active"); await expect(row).toContainText("SPK-001");
    await page.locator("label", { hasText: "Add a document" }).locator("input[type=file]").setInputFiles({ name: "contract.png", mimeType: "image/png", buffer: PNG });
    await expect(page.getByRole("link", { name: "contract.png" })).toBeVisible();
    await axe(page, "vendor detail");
    const vendorUrl = page.url();

    // --- the list finds it by what it does and where it works
    await page.goto("/vendors");
    await page.getByRole("textbox", { name: "Search" }).fill("refrigeration");
    await expect(page.getByRole("link", { name: vendorName })).toBeVisible();

    // --- a work order on a ticket given to the vendor, with a PO
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `Vendor cat ${uniq()}` }, headers: await hdr(admin) }));
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: `AC fix ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));
    const ticket = await ok<{ id: string; number: string }>(member.post(`/api/portal/services/${svc.id}/requests`, { data: { subject: "AC leaking water", answers: {} }, headers: await hdr(member) }));
    await ok(admin.post(`/api/tickets/${ticket.id}/transition`, { data: { action: "start" }, headers: await hdr(admin) }));
    await page.goto(`/tickets/${ticket.id}`);
    await page.getByRole("button", { name: "New work order" }).click();
    const wo = page.getByRole("dialog", { name: "New work order" });
    await wo.getByLabel("What needs doing").fill("Fix the leaking unit");
    await wo.getByLabel("Vendor").selectOption({ label: vendorName });
    await wo.getByLabel("Purchase order").fill("PO-7781");
    await wo.getByRole("button", { name: "Save" }).click();
    const link = page.getByRole("link", { name: new RegExp(`${ticket.number}-W1`) });
    await expect(link).toBeVisible();
    await expect(page.getByText(new RegExp(vendorName)).first()).toBeVisible();
    await link.click();
    await expect(page.getByTestId("wo-vendor")).toContainText(vendorName);
    await expect(page.getByTestId("wo-vendor")).toContainText("PO-7781");

    // --- the vendor page shows it; block the vendor and it can no longer be picked
    await page.goto(vendorUrl);
    await expect(page.getByRole("link", { name: new RegExp(`${ticket.number}-W1`) })).toBeVisible();
    await expect(page.getByTestId("vendor-score")).toContainText("1");
    await page.getByRole("button", { name: "Block" }).click();
    const bl = page.getByRole("dialog", { name: "Block this vendor?" });
    await bl.getByRole("button", { name: "Block" }).click();
    await expect(bl.getByRole("alert")).toBeVisible();                                   // a reason is needed
    await bl.getByLabel("Why?").fill("Repeatedly late on site visits");
    await bl.getByRole("button", { name: "Block" }).click();
    await expect(page.getByText(/Repeatedly late on site visits/)).toBeVisible();
    const pick = await ok<{ name: string }[]>(admin.get("/api/vendors/pick"));
    expect(pick.map((v) => v.name)).not.toContain(vendorName);
    await page.getByRole("button", { name: "Unblock" }).click();
    await expect(page.getByRole("button", { name: "Block" })).toBeVisible();

    // --- a requester cannot see vendors
    expect((await member.get("/api/vendors")).status()).toBe(403);
  });
});
