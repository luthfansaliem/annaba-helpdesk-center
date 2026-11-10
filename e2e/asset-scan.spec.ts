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

test.describe("asset labels and reporting from an asset (R2-2)", () => {
  test("a label is printed, its QR leads anyone to the asset, and a problem reported there is linked to the asset and its place", async ({ browser }) => {
    const adminPage = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = adminPage.request;
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const placeName = `Scan floor ${uniq()}`;
    const site = await ok<{ id: string }>(admin.post("/api/locations", { data: { type: "site", code: `SF-${uniq()}`.toUpperCase(), name: placeName }, headers: await hdr(admin) }));
    const type = await ok<{ id: string }>(admin.post("/api/asset-types", { data: { name: `Scanner type ${uniq()}`, fields: [] }, headers: await hdr(admin) }));
    const asset = await ok<{ id: string; tag: string; name: string }>(admin.post("/api/assets", { data: { name: `Copier ${uniq()}`, typeId: type.id, status: "in_use", locationId: site.id, fields: {} }, headers: await hdr(admin) }));
    const cat = await ok<{ id: string }>(admin.post("/api/catalog/categories", { data: { name: `Scan cat ${uniq()}` }, headers: await hdr(admin) }));
    const svcName = `Copier repair ${uniq()}`;
    const svc = await ok<{ id: string }>(admin.post("/api/catalog/services", { data: { name: svcName, categoryId: cat.id }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) }));

    // --- the label sheet: one QR label for the asset, from its page
    await adminPage.goto(`/assets/${asset.id}`);
    await adminPage.getByRole("link", { name: "Print label" }).click();
    await expect(adminPage).toHaveURL(new RegExp(`/assets/labels\\?ids=${asset.id}`));
    const label = adminPage.getByTestId("asset-label");
    await expect(label).toHaveCount(1); await expect(label).toContainText(asset.tag); await expect(label).toContainText(placeName);
    await expect(label.getByRole("img", { name: `QR code for ${asset.tag}` })).toHaveAttribute("src", /^data:image\/png;base64,/);
    await axe(adminPage, "asset labels");

    // --- what the QR encodes: the scan page, which a requester can open (not the register)
    await member.goto(`/a/${encodeURIComponent(asset.tag)}`);
    await expect(member.getByRole("heading", { name: asset.name, level: 1 })).toBeVisible();
    await expect(member.getByText(placeName)).toBeVisible();
    await expect(member.getByRole("link", { name: "Open in the asset register" })).toHaveCount(0);
    await axe(member, "asset scan page");
    await member.getByRole("link", { name: "Report a problem with this" }).click();
    await expect(member.getByTestId("about-asset")).toContainText(`${asset.tag} · ${asset.name}`);
    await member.getByLabel("Search services").fill(svcName);
    await member.getByRole("link", { name: new RegExp(svcName) }).click();
    await expect(member.getByTestId("about-asset")).toBeVisible();
    await expect(member.getByLabel("Where is it?")).toHaveValue(site.id);                                   // starts at the asset's place
    await member.getByLabel("Subject").fill("Copier shows error E-05");
    await member.getByRole("button", { name: "Send request" }).click();
    await expect(member).toHaveURL(/\/tickets\/[0-9a-f-]{36}\?created=1/);
    await expect(member.getByText(`${asset.tag} · ${asset.name}`)).toBeVisible();                             // the request says what it is about

    // --- staff see it on the asset
    await adminPage.goto(`/assets/${asset.id}`);
    await expect(adminPage.getByText("Copier shows error E-05")).toBeVisible();
    await expect(adminPage.getByText("1 in the last 12 months.")).toBeVisible();
  });

  test("an unknown tag says so, and a retired asset cannot be reported", async ({ browser }) => {
    const adminPage = await (await browser.newContext({ storageState: authFile("admin") })).newPage(); const admin = adminPage.request;
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/a/NO-SUCH-TAG-123");
    await expect(member.getByRole("alert")).toBeVisible();
    const type = await ok<{ id: string }>(admin.post("/api/asset-types", { data: { name: `Old type ${uniq()}`, fields: [] }, headers: await hdr(admin) }));
    const asset = await ok<{ id: string; tag: string }>(admin.post("/api/assets", { data: { name: "Old copier", typeId: type.id, status: "in_use", fields: {} }, headers: await hdr(admin) }));
    await ok(admin.post(`/api/assets/${asset.id}/retire`, { data: { reason: "Scrapped" }, headers: await hdr(admin) }));
    await member.goto(`/a/${encodeURIComponent(asset.tag)}`);
    await expect(member.getByText(/no longer in service/)).toBeVisible();
    await expect(member.getByRole("link", { name: "Report a problem with this" })).toHaveCount(0);
  });
});
