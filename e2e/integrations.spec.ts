import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { PASSWORD, WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const uniq = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 6);

test.describe("integrations (R2-10)", () => {
  test("an API key is made once, used by another system on the public API, and revoked; a webhook is made and removed", async ({ browser, playwright }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    expect((await admin.post("/api/auth/reauth", { data: { password: PASSWORD }, headers: await hdr(admin) })).ok()).toBeTruthy();
    const keyName = `Reporting ${uniq()}`;

    await page.goto("/admin/integrations");
    await page.getByRole("button", { name: "New API key" }).click();
    const kd = page.getByRole("dialog", { name: "New API key" });
    await kd.getByLabel("Name").fill(keyName);
    await kd.getByRole("button", { name: "Create key" }).click();
    const shown = page.getByTestId("one-time-secret");
    await expect(shown).toContainText("adk_");
    const token = (await shown.locator("code").textContent())!.trim();
    await expect(page.getByRole("row", { name: new RegExp(keyName) })).toContainText("tickets.read");

    // another system: no session, no Origin — just the key, through the web front
    const other = await playwright.request.newContext({ baseURL: WEB });
    const ok = await other.get("/api/v1/tickets?pageSize=5", { headers: { Authorization: `Bearer ${token}` } });
    expect(ok.status()).toBe(200);
    expect((await other.post("/api/v1/tickets", { headers: { Authorization: `Bearer ${token}` }, data: { subject: "x" } })).status()).toBe(403);   // no write scope

    // webhook: made (secret shown once), then removed before anything is sent
    await page.getByRole("button", { name: "New webhook" }).click();
    const wd = page.getByRole("dialog", { name: "New webhook" });
    await wd.getByLabel("Address").fill("https://hooks.example.com/helpdesk");
    const created = wd.getByRole("checkbox", { name: /A ticket is created/ }); await created.click(); await expect(created).not.toBeChecked();
    const done = wd.getByRole("checkbox", { name: /A work order is completed/ }); await done.click(); await expect(done).toBeChecked();
    await wd.getByRole("button", { name: "Create webhook" }).click();
    await expect(page.getByTestId("one-time-secret").filter({ hasText: "whsec_" })).toBeVisible();
    const hook = page.getByRole("row", { name: /hooks\.example\.com/ });
    await hook.getByRole("button", { name: "Switch off" }).click();
    await expect(hook).toContainText("Off");
    await hook.getByRole("button", { name: /0 waiting/ }).click();
    await expect(page.getByRole("dialog", { name: "Deliveries" })).toContainText("Nothing sent yet.");
    await page.keyboard.press("Escape");
    await axe(page, "integrations");
    await hook.getByRole("button", { name: /Remove/ }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Remove" }).click();
    await expect(page.getByRole("row", { name: /hooks\.example\.com/ })).toHaveCount(0);

    // revoke the key: the other system is shut out at once
    await page.getByRole("row", { name: new RegExp(keyName) }).getByRole("button", { name: "Revoke" }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Revoke" }).click();
    await expect(page.getByRole("row", { name: new RegExp(keyName) })).toContainText("Stopped");
    expect((await other.get("/api/v1/tickets", { headers: { Authorization: `Bearer ${token}` } })).status()).toBe(401);
    await other.dispose();
  });
});
