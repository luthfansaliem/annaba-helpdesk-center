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

test.describe("asset register (R2-1)", () => {
  test("a manager defines a type, adds an asset, moves it, retires and restores it, and links a request to it", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const member = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const typeName = `E2E Generator ${uniq()}`; const assetName = `Genset ${uniq()}`; const siteName = `Genset yard ${uniq()}`;
    const site = await page.request.post("/api/locations", { data: { type: "site", code: `GY-${uniq()}`.toUpperCase(), name: siteName }, headers: await hdr(page.request) });
    expect(site.status(), await site.text()).toBe(201);

    // --- an asset type with one question of its own
    await page.goto("/setup/asset-types");
    await page.getByRole("button", { name: "New asset type" }).click();
    const typeDlg = page.getByRole("dialog", { name: "New asset type" });
    await typeDlg.getByLabel("Name").fill(typeName);
    await typeDlg.getByRole("button", { name: "Add question" }).click();
    const q = page.getByRole("dialog", { name: "Add question" });
    await q.getByLabel("Question").fill("Power (kVA)");
    await q.getByLabel("Type").selectOption("number");
    await expect(q.getByLabel("Type").locator("option")).toHaveCount(6);                                      // only the simple kinds are offered
    await q.getByRole("checkbox", { name: "Required" }).check();
    await q.getByRole("button", { name: "Done" }).click();
    await typeDlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("row", { name: new RegExp(typeName) })).toBeVisible();

    // --- a new asset: the type's question is asked, the tag is handed out
    await page.goto("/assets");
    await expect(page.getByRole("heading", { name: "Assets", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "New asset" }).click();
    const dlg = page.getByRole("dialog", { name: "New asset" });
    await dlg.getByLabel("Type").selectOption({ label: typeName });
    await dlg.getByLabel("Name").fill(assetName);
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(dlg.getByText("This is required.")).toBeVisible();                                         // the kVA question
    await dlg.getByLabel("Power (kVA)").fill("250");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page).toHaveURL(/\/assets\/[0-9a-f-]{36}$/);
    const heading = page.getByRole("heading", { level: 1 });
    await expect(heading).toContainText(assetName); await expect(heading).toContainText(/AST-\d{6}/);
    const tag = (await heading.textContent())!.split(" · ")[0].trim();
    await expect(page.getByText("250")).toBeVisible();
    await axe(page, "asset detail");

    // --- move it and put it in repair: both on its history
    await page.getByRole("button", { name: "Edit" }).click();
    const ed = page.getByRole("dialog", { name: "Edit asset" });
    await ed.getByLabel("Status").selectOption("in_repair");
    await ed.getByLabel("Place").selectOption({ label: siteName });
    await ed.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Status changed from in use to in repair.")).toBeVisible();
    await expect(page.getByText(`Moved from no place to ${siteName}.`)).toBeVisible();

    // --- a request about it: staff pick the asset on the ticket, and the asset lists the request
    const admin = page.request;
    const cat = await (await admin.post("/api/catalog/categories", { data: { name: `Assets cat ${uniq()}` }, headers: await hdr(admin) })).json() as { id: string };
    const svc = (await (await admin.post("/api/catalog/services", { data: { name: `Genset repair ${uniq()}`, categoryId: cat.id }, headers: await hdr(admin) })).json() as { id: string }).id;
    expect((await admin.post(`/api/catalog/services/${svc}/publish`, { headers: await hdr(admin) })).ok()).toBeTruthy();
    const made = await member.post(`/api/portal/services/${svc}/requests`, { data: { subject: `Genset will not start ${uniq()}`, answers: {} }, headers: await hdr(member) });
    expect(made.status(), await made.text()).toBe(201);
    const ticket = await made.json() as { id: string; number: string };
    await page.goto(`/tickets/${ticket.id}`);
    await page.getByRole("button", { name: "Edit" }).click();
    const te = page.getByRole("dialog", { name: "Edit ticket" });
    await te.getByLabel("Asset").fill(tag);
    await te.getByRole("button", { name: new RegExp(`^${tag}`) }).click();
    await te.getByRole("button", { name: "Save" }).click();
    await expect(page.getByRole("link", { name: `${tag} · ${assetName}` })).toBeVisible();
    await page.getByRole("link", { name: `${tag} · ${assetName}` }).click();
    await expect(page.getByRole("link", { name: ticket.number })).toBeVisible();

    // --- retire (a reason is required) and restore
    await page.getByRole("button", { name: "Retire" }).click();
    const rd = page.getByRole("dialog", { name: "Retire this asset?" });
    await rd.getByRole("button", { name: "Retire" }).click();
    await expect(rd.getByRole("alert")).toBeVisible();
    await rd.getByLabel("Why?").fill("Replaced by a bigger unit");
    await rd.getByRole("button", { name: "Retire" }).click();
    await expect(page.getByText(/Retired on .*Replaced by a bigger unit/)).toBeVisible();
    await page.getByRole("button", { name: "Restore" }).click();
    await expect(page.getByText("Back in the register (stored).")).toBeVisible();
  });

  test("a CSV file is checked line by line, then imported, and the register can be exported", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const typeName = `E2E Chair ${uniq()}`;
    const res = await page.request.post("/api/asset-types", { data: { name: typeName, fields: [] }, headers: await hdr(page.request) });
    expect(res.status(), await res.text()).toBe(201);

    await page.goto("/assets/import");
    await expect(page.getByRole("heading", { name: "Import assets", level: 1 })).toBeVisible();
    const bad = `name,type,purchase_date\nChair A,${typeName},31-12-2024\nChair B,No such type,\n`;
    await page.getByLabel("CSV file").setInputFiles({ name: "chairs.csv", mimeType: "text/csv", buffer: Buffer.from(bad) });
    await page.getByRole("button", { name: "Check the file" }).click();
    const report = page.getByTestId("import-report");
    await expect(report).toContainText("2 of 2 lines have problems");
    await expect(page.getByRole("table", { name: "Problems by line" })).toContainText("purchase_date");
    await expect(page.getByRole("button", { name: "Import", exact: true })).toBeDisabled();
    await axe(page, "asset import with problems");

    const good = `name,type,serial\nChair A ${uniq()},${typeName},CH-1\n"Chair, B",${typeName},CH-2\n`;
    await page.getByLabel("CSV file").setInputFiles({ name: "chairs.csv", mimeType: "text/csv", buffer: Buffer.from(good) });
    await page.getByRole("button", { name: "Check the file" }).click();
    await expect(report).toContainText("2 would be created and 0 updated. Nothing has been written yet.");
    await page.getByRole("button", { name: "Import", exact: true }).click();
    await expect(report).toContainText("2 assets created and 0 updated.");

    await page.goto("/assets");
    await page.getByLabel("Type").selectOption({ label: typeName });
    await expect(page.getByRole("link", { name: "Chair, B" })).toBeVisible();
    const download = page.waitForEvent("download");
    await page.getByRole("button", { name: "Export CSV" }).click();
    const file = await download;
    expect(file.suggestedFilename()).toMatch(/^assets-.*\.csv$/);
  });

  test("people who only ask for help do not see the register", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/assets");
    await expect(member.getByRole("heading", { name: "Assets", level: 1 })).toHaveCount(0);
    expect((await member.request.get("/api/assets")).status()).toBe(403);
  });
});
