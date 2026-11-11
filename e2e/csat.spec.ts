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

test.describe("satisfaction (R2-6)", () => {
  test("the requester rates a resolved request, changes the rating, and staff see it in the report", async ({ browser }) => {
    const admin = (await (await browser.newContext({ storageState: authFile("admin") })).newPage());
    const page = await (await browser.newContext({ storageState: authFile("member") })).newPage(); const member = page.request;
    const cat = await ok<{ id: string }>(admin.request.post("/api/catalog/categories", { data: { name: `CSAT cat ${uniq()}` }, headers: await hdr(admin.request) }));
    const svc = await ok<{ id: string }>(admin.request.post("/api/catalog/services", { data: { name: `Chair repair ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin.request) }));
    await ok(admin.request.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin.request) }));
    const ticket = await ok<{ id: string; number: string }>(member.post(`/api/portal/services/${svc.id}/requests`, { data: { subject: "Chair wobbles", answers: {} }, headers: await hdr(member) }));
    await ok(admin.request.post(`/api/tickets/${ticket.id}/transition`, { data: { action: "start" }, headers: await hdr(admin.request) }));
    await ok(admin.request.post(`/api/tickets/${ticket.id}/transition`, { data: { action: "resolve", resolutionCode: "fixed", resolutionNote: "Tightened the screws" }, headers: await hdr(admin.request) }));

    // --- the requester rates it
    await page.goto(`/tickets/${ticket.id}`);
    const form = page.getByTestId("rate-form");
    await expect(page.getByRole("heading", { name: "How did it go?" })).toBeVisible();
    await axe(page, "rate a request");
    await form.getByRole("button", { name: "Send rating" }).click();
    await expect(form.getByRole("alert")).toContainText("Choose 1 to 5 stars.");
    await form.getByRole("radio", { name: "2 – Poor" }).check({ force: true });
    await form.getByLabel("What went wrong?").fill("It took three days.");
    await form.getByRole("button", { name: "Send rating" }).click();
    const shown = page.getByTestId("rating");
    await expect(shown).toContainText("Poor"); await expect(shown).toContainText("It took three days.");

    // --- and changes it
    await shown.getByRole("button", { name: "Change rating" }).click();
    await page.getByTestId("rate-form").getByRole("radio", { name: "4 – Good" }).check({ force: true });
    await page.getByTestId("rate-form").getByRole("button", { name: "Send rating" }).click();
    await expect(page.getByTestId("rating")).toContainText("Good");
    await expect(page.getByText("Changed the rating from 2 to 4 out of 5.")).toBeVisible();

    // --- staff see it on the ticket and in the report
    await admin.goto(`/tickets/${ticket.id}`);
    await expect(admin.getByRole("heading", { name: "Requester's rating" })).toBeVisible();
    await admin.goto("/reports/csat");
    await expect(admin.getByTestId("cs-kpis")).toContainText("Average rating");
    await expect(admin.getByRole("link", { name: ticket.number })).toBeVisible();
    // the trend: weekly for 30 days, monthly for a year, with the averages as a table
    await expect(admin.getByRole("heading", { name: "Ratings per week" })).toBeVisible();
    await expect(admin.getByTestId("cs-trend")).toContainText("Average");
    await axe(admin, "satisfaction report");
    await admin.getByLabel("Period", { exact: true }).selectOption("365");
    await expect(admin.getByRole("heading", { name: "Ratings per month" })).toBeVisible();
    expect((await member.get("/api/reports/csat")).status()).toBe(403);
  });
});
