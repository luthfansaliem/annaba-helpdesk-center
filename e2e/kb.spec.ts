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

test.describe("knowledge base (R2-7)", () => {
  test("an article is written, previewed, published, found by a requester, rated helpful, and suggested while asking — which can save the request", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = page.request;
    const memberPage = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const word = `Projector${uniq()}`; const title = `Connect the ${word} to a laptop`; const svcName = `AV help ${uniq()}`;
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `KB cat ${uniq()}` }, headers: await hdr(admin) }));
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: svcName, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));

    // --- write a draft with a preview
    await page.goto("/kb");
    await axe(page, "knowledge base");
    await page.getByRole("link", { name: "New article" }).click();
    await page.getByLabel("Title").fill(title);
    await page.getByLabel("Summary").fill("Use the HDMI cable on the table.");
    await page.getByRole("textbox", { name: "Text" }).fill("## Steps\n\n1. Plug in the **HDMI** cable\n2. Press *Source*\n\n[Bad link](javascript:alert(1)) and [the AV guide](https://example.com/av)");
    await page.getByRole("button", { name: "Preview" }).click();
    const preview = page.getByTestId("kb-preview");
    await expect(preview.getByRole("heading", { name: "Steps" })).toBeVisible();
    await expect(preview.locator("strong")).toHaveText("HDMI");
    await expect(preview.getByRole("link", { name: "Bad link" })).toHaveCount(0);           // unsafe links stay text
    await expect(preview.getByRole("link", { name: "the AV guide" })).toHaveAttribute("href", "https://example.com/av");
    await page.getByLabel("Find a service").fill(svcName);
    const box = page.getByRole("checkbox", { name: new RegExp(svcName) }); await box.click(); await expect(box).toBeChecked();
    await page.getByRole("button", { name: "Save draft" }).click();

    // --- its page: a draft, then published
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(title);
    await expect(page.getByText("Draft", { exact: true }).first()).toBeVisible();
    await expect(page.getByText("You are looking at version 1, which readers do not see yet.")).toBeVisible();
    await page.getByRole("button", { name: "Publish version 1" }).click();
    await expect(page.getByText("Published", { exact: true }).first()).toBeVisible();
    const articleUrl = page.url().replace(/\?.*$/, "");
    await axe(page, "knowledge base article");

    // --- a requester finds and rates it
    await memberPage.goto("/kb");
    await memberPage.getByLabel("Search the knowledge base").fill(word);
    await memberPage.getByRole("link", { name: title }).click();
    await expect(memberPage.getByRole("heading", { level: 1 })).toHaveText(title);
    await expect(memberPage.getByRole("link", { name: "Edit" })).toHaveCount(0);
    await memberPage.getByTestId("kb-helpful").getByRole("button", { name: "Yes" }).click();
    await expect(memberPage.getByTestId("kb-helpful")).toContainText("Thanks for telling us.");
    await expect(memberPage.getByRole("link", { name: `Ask for ${svcName}` })).toBeVisible();

    // --- asking for the service suggests it; "this solved it" sends no request
    await memberPage.goto(`/requests/new/${svc.id}`);
    const sugg = memberPage.getByTestId("kb-suggestions");
    await sugg.getByRole("button", { name: title }).click();
    const dlg = memberPage.getByRole("dialog", { name: title });
    await expect(dlg).toContainText("Plug in the");
    await dlg.getByRole("button", { name: "Yes, this solved it" }).click();
    await expect(memberPage).toHaveURL(/\/requests$/);

    // --- the writer sees how it is used
    await page.goto(articleUrl);
    const stats = page.getByTestId("kb-stats");
    await expect(stats).toContainText("Solved without a request"); await expect(stats).toContainText("1 / 0");
    expect((await memberPage.request.post("/api/kb/articles", { data: { title: "Mine", body: "x" }, headers: await hdr(memberPage.request) })).status()).toBe(403);
  });
});
