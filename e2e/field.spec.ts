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

test.describe("field mode (R3-11)", () => {
  test("a technician keeps working offline and the changes sync once back online", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("admin") });
    const page = await context.newPage(); const admin = page.request;
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const me = await ok<{ id: string }>(admin.get("/api/auth/me"));
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `Field ${uniq()}` }, headers: await hdr(admin) }));
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: `Field repairs ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));
    const ticket = await ok<{ id: string }>(member.post(`/api/portal/services/${svc.id}/requests`, { data: { subject: "Pump room leak", answers: {} }, headers: await hdr(member) }));
    const title = `Fix the pump ${uniq()}`;
    const wo = await ok<{ id: string; number: string }>(admin.post(`/api/tickets/${ticket.id}/work-orders`, { data: { title, assigneeId: me.id, tasks: [{ text: "Close the valve", required: true }] }, headers: await hdr(admin) }));

    // online: the work order is kept on the device; start it
    await page.goto("/work-orders/field");
    await expect(page.getByRole("heading", { name: "Field mode", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: new RegExp(title) }).click();
    const detail = page.getByTestId("field-detail");
    await detail.getByRole("button", { name: "Start" }).click();
    await expect(detail).toContainText("In progress");
    await expect(page.getByTestId("field-status")).toContainText("No changes waiting");
    await axe(page, "field mode");

    // offline: tick the checklist and log time — kept on the device, shown at once
    await context.setOffline(true);
    await page.evaluate(() => window.dispatchEvent(new Event("offline")));
    const valve = detail.getByRole("checkbox", { name: "Close the valve" });
    await valve.click();
    await expect(valve).toBeChecked();                                                // saved on the device first, then shown
    await detail.getByLabel("Minutes").fill("25");
    await detail.getByRole("button", { name: "Log time" }).click();
    await expect(page.getByTestId("field-status")).toContainText("2 changes waiting");
    await expect(page.getByTestId("field-status")).toContainText("Offline");
    await expect(detail).toContainText("25 min logged");

    // back online: the queue is replayed and the server has both changes
    await context.setOffline(false);
    await page.evaluate(() => window.dispatchEvent(new Event("online")));
    await expect(page.getByTestId("field-status")).toContainText("No changes waiting");
    const server = await ok<{ totalMinutes: number; tasks: { doneAt: string | null }[] }>(admin.get(`/api/work-orders/${wo.id}`));
    expect(server.totalMinutes).toBe(25);
    expect(server.tasks[0].doneAt).not.toBeNull();

    // the data leaves the device on request
    await page.getByRole("button", { name: "Forget this device's data" }).click();
    await expect(page.getByTestId("field-msg")).toContainText("removed from this device");
  });
});
