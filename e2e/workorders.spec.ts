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

test.describe("work orders (R2-3)", () => {
  test("a checklist is saved, a work order is made from it on a ticket, done with photo, time and costs, and the ticket resolves itself", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const listName = `AC service ${uniq()}`;

    // --- a saved checklist
    await page.goto("/setup/checklists");
    await page.getByRole("button", { name: "New checklist" }).click();
    const dlg = page.getByRole("dialog", { name: "New checklist" });
    await dlg.getByLabel("Name").fill(listName);
    for (const line of ["Clean the filters", "Check the refrigerant"]) { await dlg.getByLabel("New checklist line").fill(line); await dlg.getByRole("button", { name: "Add line" }).click(); }
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("row", { name: new RegExp(listName) })).toBeVisible();

    // --- a request, being worked
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `WO cat ${uniq()}` }, headers: await hdr(admin) }));
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: `AC repair ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));
    const ticket = await ok<{ id: string; number: string }>(member.post(`/api/portal/services/${svc.id}/requests`, { data: { subject: "AC is blowing warm air", answers: {} }, headers: await hdr(member) }));
    await ok(admin.post(`/api/tickets/${ticket.id}/transition`, { data: { action: "start" }, headers: await hdr(admin) }));

    // --- a work order from the saved checklist, needing an after photo, resolving the ticket when done
    await page.goto(`/tickets/${ticket.id}`);
    await page.getByRole("button", { name: "New work order" }).click();
    const wo = page.getByRole("dialog", { name: "New work order" });
    await wo.getByLabel("What needs doing").fill("Service the split unit");
    await wo.getByLabel("Start from a saved checklist").selectOption({ label: `${listName} (2)` });
    await wo.getByRole("checkbox", { name: "Needs a photo of the finished work" }).check();
    await wo.getByRole("checkbox", { name: "Resolve the ticket when its required work orders are done" }).check();
    await wo.getByRole("button", { name: "Save" }).click();
    const link = page.getByRole("link", { name: new RegExp(`${ticket.number}-W1`) });
    await expect(link).toBeVisible();
    await link.click();
    await expect(page.getByRole("heading", { level: 1 })).toContainText(`${ticket.number}-W1 · Service the split unit`);

    // --- start it (nobody had it: it becomes mine), then what completing needs
    await page.getByRole("button", { name: "Start" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Start" }).click();
    const missing = page.getByTestId("wo-missing");
    await expect(missing).toContainText("required checklist lines are not done"); await expect(missing).toContainText("after");
    for (const line of [/Clean the filters/, /Check the refrigerant/]) {                                       // ticked once the server has it
      const box = page.getByRole("checkbox", { name: line }); await box.click(); await expect(box).toBeChecked();
    }
    await expect(missing).not.toContainText("checklist");
    await page.locator("label", { hasText: "Add an after photo" }).locator("input[type=file]").setInputFiles({ name: "after.png", mimeType: "image/png", buffer: PNG });
    await expect(page.getByRole("img", { name: "After: after.png" })).toBeVisible();
    await expect(missing).toHaveCount(0);

    // --- time and parts
    await page.getByLabel("Minutes").fill("45"); await page.getByRole("button", { name: "Log time" }).click();
    await expect(page.getByText("45 min in total").or(page.getByText(/45m in total/))).toBeVisible();
    await page.getByLabel("Description").fill("Refrigerant R-32, 1 kg"); await page.getByLabel("Qty").fill("2"); await page.getByLabel("Unit cost").fill("50000");
    await page.getByRole("button", { name: "Add", exact: true }).click();
    await expect(page.getByText(/Total: 100[.,]000/)).toBeVisible();
    await axe(page, "work order");

    // --- complete: the ticket resolves itself
    await page.getByRole("button", { name: "Complete" }).click();
    const done = page.getByRole("dialog", { name: "Complete" });
    await done.getByLabel("What was done?").fill("Filters cleaned and gas topped up");
    await done.getByRole("button", { name: "Complete" }).click();
    await expect(page.getByText(/Done .*Filters cleaned and gas topped up/)).toBeVisible();
    await page.getByRole("link", { name: new RegExp(ticket.number) }).first().click();
    await expect(page.getByText("Resolved").first()).toBeVisible();
    await expect(page.getByText(`All required work orders are done (${ticket.number}-W1).`).first()).toBeVisible();   // in the resolution and on the timeline

    // --- my work orders list
    await page.goto("/work-orders");
    await page.getByLabel("Status").selectOption("done");
    await expect(page.getByRole("link", { name: `${ticket.number}-W1` })).toBeVisible();
  });

  test("people who only ask for help see no work orders", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/work-orders");
    await expect(member.getByRole("heading", { name: "Work Orders", level: 1 })).toHaveCount(0);
    expect((await member.request.get("/api/work-orders")).status()).toBe(403);
  });
});
