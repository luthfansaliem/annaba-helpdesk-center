import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { WEB, authFile, csrf, provisionUser, uiLogin } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const uniq = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
async function ok<T>(r: Promise<import("@playwright/test").APIResponse>): Promise<T> { const x = await r; expect(x.ok(), await x.text()).toBeTruthy(); return await x.json() as T; }

test.describe("vendor portal (R2-11)", () => {
  test("a vendor's person is invited, linked to the vendor, signs in to only their company's work orders and starts one", async ({ browser }) => {
    test.setTimeout(90_000);                                                    // an invitation e-mail, its acceptance and a second sign-in
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const email = `tech.${uniq()}@coolco.test`; const vendorName = `CoolCo ${uniq()}`;
    // its own client address: invitation acceptance is limited per address, and a full run uses it up from one (see setup.spec.ts)
    const ip = () => `10.30.${Math.floor(Math.random() * 200) + 20}.${Math.floor(Math.random() * 200) + 20}`;
    const freshCtx = await browser.newContext({ storageState: { cookies: [], origins: [] }, extraHTTPHeaders: { "X-Forwarded-For": ip() } });
    const fresh = await freshCtx.newPage();
    const { password } = await provisionUser(admin, fresh, email, "Tono Technician", undefined, "vendor");

    // --- the vendor, its portal person, and two work orders: one for CoolCo, one in-house
    const vendor = await ok<{ id: string }>(admin.post("/api/vendors", { data: { name: vendorName }, headers: await hdr(admin) }));
    await page.goto(`/vendors/${vendor.id}`);
    await page.getByLabel("Person's e-mail").fill(email);
    await page.getByRole("button", { name: "Give access" }).click();
    await expect(page.getByRole("list", { name: "Vendor portal" })).toContainText("Tono Technician");
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `VP cat ${uniq()}` }, headers: await hdr(admin) }));
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: `Cooling ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));
    const ticket = await ok<{ id: string; number: string }>(member.post(`/api/portal/services/${svc.id}/requests`, { data: { subject: "Server room too warm", answers: {} }, headers: await hdr(member) }));
    await ok(admin.post(`/api/tickets/${ticket.id}/work-orders`, { data: { title: "Service the CRAC unit", vendorId: vendor.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/tickets/${ticket.id}/work-orders`, { data: { title: "Move the rack" }, headers: await hdr(admin) }));

    // --- the vendor's person: lands on their work orders, sees only the vendor menu and only CoolCo's work
    const vpCtx = await browser.newContext({ storageState: { cookies: [], origins: [] }, extraHTTPHeaders: { "X-Forwarded-For": ip() } });
    const vp = await vpCtx.newPage();
    await uiLogin(vp, email, password);
    await expect(vp).toHaveURL(/\/work-orders$/);
    const sidebar = vp.getByRole("navigation").first();
    await expect(sidebar.getByRole("link", { name: "My work orders" })).toBeVisible();
    await expect(vp.getByRole("link", { name: "New request" })).toHaveCount(0);
    const row = vp.getByRole("row", { name: /Service the CRAC unit/ });
    await expect(row).toBeVisible();
    await expect(row.getByRole("link")).toHaveCount(1);                                            // the work order, not the ticket
    await expect(vp.getByText("Move the rack")).toHaveCount(0);
    await axe(vp, "vendor's work orders");
    await row.getByRole("link").click();
    await expect(vp.getByRole("link", { name: "My work orders" }).first()).toBeVisible();
    await expect(vp.getByRole("link", { name: new RegExp(`${ticket.number} ·`) })).toHaveCount(0);       // no way into the ticket
    await vp.getByRole("button", { name: "Start" }).click();
    await vp.getByRole("dialog").getByRole("button", { name: "Start" }).click();
    await expect(vp.getByText("In progress").first()).toBeVisible();
    expect((await vp.request.get("/api/portal/locations")).status()).toBe(403);
    await freshCtx.close(); await vpCtx.close();
  });
});
