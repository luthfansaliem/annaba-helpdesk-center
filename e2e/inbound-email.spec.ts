import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { PASSWORD, USERS, WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const uniq = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
async function ok<T>(r: Promise<import("@playwright/test").APIResponse>): Promise<T> { const x = await r; expect(x.ok(), await x.text()).toBeTruthy(); return await x.json() as T; }

test.describe("e-mail to ticket (R2-9)", () => {
  test("the mailbox is set up with a one-time webhook address, an e-mail becomes a request, and the log shows it", async ({ browser, playwright }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const catName = `Mail cat ${uniq()}`;
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: catName }, headers: await hdr(admin) }));
    const svcName = `General question ${uniq()}`;
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: svcName, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));
    expect((await admin.post("/api/auth/reauth", { data: { password: PASSWORD }, headers: await hdr(admin) })).ok()).toBeTruthy();

    await page.goto("/admin/inbound-email");
    await page.getByRole("button", { name: /Make (the|a new) webhook address/ }).click();
    const shown = page.getByTestId("webhook-url");
    await expect(shown).toContainText("/api/inbound/email/");
    const url = (await shown.locator("code").textContent())!.trim();
    await page.getByLabel("Helpdesk e-mail address").fill("help@annabadigital.io");
    await page.getByLabel("New e-mails become requests for").selectOption({ label: `${svcName} (${catName})` });
    const on = page.getByRole("checkbox", { name: "Receive e-mail" }); await on.click(); await expect(on).toBeChecked();
    await page.getByRole("button", { name: "Save" }).click();
    await expect.poll(async () => (await (await admin.get("/api/settings/inbound-email")).json() as { enabled: boolean }).enabled).toBe(true);
    await expect(page.getByTestId("webhook-url")).toBeVisible();                           // still shown after saving, until the page is left
    await axe(page, "e-mail to ticket");

    // --- a mail service posts a message: no Origin, no session, just the secret address (through the web front, like in production)
    const subject = `Leaking tap ${uniq()}`;
    const raw = `From: Rizky <${USERS.member}>\r\nTo: help@annabadigital.io\r\nSubject: ${subject}\r\nMessage-ID: <${uniq()}@mail.test>\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nThe tap in the 3rd floor pantry keeps dripping.\r\n`;
    const mailService = await playwright.request.newContext();
    const res = await mailService.post(url, { data: raw, headers: { "Content-Type": "message/rfc822" } });
    expect(res.ok(), await res.text()).toBeTruthy();
    const out = await res.json() as { outcome: string; ticketNumber: string };
    expect(out.outcome).toBe("created");

    await page.reload();
    const row = page.getByRole("row").filter({ hasText: subject });
    await expect(row).toContainText("New request"); await expect(row.getByRole("link", { name: out.ticketNumber })).toBeVisible();
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/requests");
    await expect(member.getByRole("link", { name: subject })).toBeVisible();
    await mailService.dispose();
  });
});
