import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

const PNG = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.alloc(120, 7)]);
const PDF = Buffer.from("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");

/** A published service whose form has a "Photos" question allowing two files; the starter catalog's own forms are not touched. */
async function service(admin: APIRequestContext, name: string) {
  const h = async () => ({ "X-CSRF-Token": await csrf(admin), Origin: WEB });
  const cat = await (await admin.post("/api/catalog/categories", { data: { name: `${name} cat` }, headers: await h() })).json() as { id: string };
  const svc = await (await admin.post("/api/catalog/services", { data: { name, categoryId: cat.id }, headers: await h() })).json() as { id: string };
  const form = await admin.put(`/api/catalog/services/${svc.id}/form`, { data: { fields: [{ key: "what", type: "text", label: "What is broken?", required: true }, { key: "photos", type: "file", label: "Photos of the damage", max: 2 }] }, headers: await h() });
  expect(form.ok(), await form.text()).toBeTruthy();
  const pub = await admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await h() });
  expect(pub.ok(), await pub.text()).toBeTruthy();
  return svc.id;
}

test.describe("attachments and the file question (TKT-005)", () => {
  test("a photo is attached while requesting, staff add a document, and both are served safely", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const name = `E2E broken fixture ${Date.now()}`;
    await service(admin.request, name);

    // --- the requester attaches a photo to the form; a program renamed to .png is turned away
    await member.goto("/requests/new");
    await member.getByLabel("Search services").fill(name);
    await member.getByRole("link", { name: new RegExp(name) }).click();
    await expect(member.getByRole("heading", { name })).toBeVisible();
    await axe(member, "request form with a file question");

    const picker = member.getByLabel("Photos of the damage");
    await picker.setInputFiles({ name: "setup.exe", mimeType: "application/octet-stream", buffer: Buffer.from("MZ\u0090\u0000") });
    await expect(member.getByRole("alert").filter({ hasText: "setup.exe" })).toContainText("not accepted");
    await picker.setInputFiles({ name: "disguised.png", mimeType: "image/png", buffer: Buffer.from("MZ\u0090\u0000program") });
    await expect(member.getByRole("alert").filter({ hasText: "disguised.png" })).toContainText("does not match");

    await picker.setInputFiles({ name: "crack.png", mimeType: "image/png", buffer: PNG });
    await expect(member.getByRole("listitem").filter({ hasText: "crack.png" })).toBeVisible();
    await picker.setInputFiles({ name: "wall.png", mimeType: "image/png", buffer: PNG });
    await expect(member.getByRole("listitem").filter({ hasText: "wall.png" })).toBeVisible();
    await expect(picker).toBeDisabled();                                                          // the question allows two
    await member.getByRole("button", { name: "Remove wall.png" }).click();
    await expect(member.getByRole("listitem").filter({ hasText: "wall.png" })).toHaveCount(0);
    await expect(picker).toBeEnabled();

    await member.getByRole("textbox", { name: "What is broken?" }).fill("The wall is cracked");
    await member.getByRole("button", { name: "Send request" }).click();
    await expect(member.getByText("Your request has been sent")).toBeVisible();
    const number = (await member.getByRole("heading", { level: 1 }).innerText()).split(" · ")[0];

    // --- the photo is on the ticket, listed by name, and the answer reads as a file name
    await expect(member.getByRole("link", { name: "crack.png" })).toBeVisible();
    await expect(member.getByText("Photos of the damage")).toBeVisible();
    await axe(member, "ticket with attachments");

    // --- staff open it and add a document of their own
    await admin.goto("/tickets");
    await admin.getByLabel("Search", { exact: true }).fill(number);
    await admin.getByRole("link", { name: number }).first().click();
    await expect(admin.getByRole("link", { name: "crack.png" })).toBeVisible();
    await admin.getByLabel("Add files").setInputFiles({ name: "quote.pdf", mimeType: "text/html", buffer: PDF });   // the claimed type is ignored
    await expect(admin.getByRole("link", { name: "quote.pdf" })).toBeVisible();
    await expect(admin.getByText("Attached quote.pdf")).toBeVisible();                           // and the timeline says so

    // --- files are served under our own headers: an image inline, a document as a download, never sniffed or scripted
    const href = async (p: Page, file: string) => (await p.getByRole("link", { name: file }).getAttribute("href"))!;
    const img = await member.request.get(await href(member, "crack.png"));
    expect(img.status()).toBe(200);
    expect(img.headers()["content-type"]).toBe("image/png");
    expect(img.headers()["content-disposition"]).toMatch(/^inline/);
    expect(img.headers()["x-content-type-options"]).toBe("nosniff");
    expect(img.headers()["content-security-policy"]).toContain("sandbox");
    await member.reload();
    const doc = await member.request.get(await href(member, "quote.pdf"));
    expect(doc.headers()["content-type"]).toBe("application/pdf");
    expect(doc.headers()["content-disposition"]).toMatch(/^attachment/);

    // --- the requester cannot remove staff's file; staff can remove the requester's, after confirming
    await expect(member.getByRole("button", { name: "Remove quote.pdf" })).toHaveCount(0);
    await expect(member.getByRole("button", { name: "Remove crack.png" })).toBeVisible();
    await admin.getByRole("button", { name: "Remove quote.pdf" }).click();
    await admin.getByRole("dialog").getByRole("button", { name: "Remove", exact: true }).click();
    await expect(admin.getByText("File removed.")).toBeVisible();
    await expect(admin.getByRole("link", { name: "quote.pdf" })).toHaveCount(0);
    expect((await member.request.get(await href(member, "crack.png"))).status()).toBe(200);
  });

  test("a stranger cannot reach another person's ticket files", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const auditor = await (await browser.newContext({ storageState: authFile("auditor") })).newPage();
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const name = `E2E private fixture ${Date.now()}`;
    const serviceId = await service(admin.request, name);
    const body = { subject: "Private", answers: { what: "x" } };
    const t = await (await member.request.post(`/api/portal/services/${serviceId}/requests`, { data: body, headers: { "X-CSRF-Token": await csrf(member.request), Origin: WEB } })).json() as { id: string };
    const up = await member.request.post(`/api/tickets/${t.id}/attachments`, { multipart: { file: { name: "secret.png", mimeType: "image/png", buffer: PNG } }, headers: { "X-CSRF-Token": await csrf(member.request), Origin: WEB } });
    expect(up.status(), await up.text()).toBe(201);
    const a = await up.json() as { id: string };
    expect((await member.request.get(`/api/tickets/${t.id}/attachments/${a.id}`)).status()).toBe(200);
    expect((await auditor.request.get(`/api/tickets/${t.id}/attachments/${a.id}`)).status()).toBe(404);      // a security auditor is not on the ticket's team
    expect((await auditor.request.post(`/api/tickets/${t.id}/attachments`, { multipart: { file: { name: "x.png", mimeType: "image/png", buffer: PNG } }, headers: { "X-CSRF-Token": await csrf(auditor.request), Origin: WEB } })).status()).toBe(404);
  });

  test("the form builder offers the file question with a file limit", async ({ browser }) => {
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const name = `E2E builder fixture ${Date.now()}`;
    const id = await service(admin.request, name);
    await admin.goto(`/setup/catalog/${id}`);
    await admin.getByRole("button", { name: "Add question" }).click();
    const q = admin.getByRole("dialog", { name: "Add question" });
    await q.getByRole("combobox", { name: "Type" }).selectOption({ label: "Photos / files" });
    await q.getByRole("textbox", { name: "Question", exact: true }).fill("Receipt");
    await q.getByLabel("Most files per request").fill("11");
    await q.getByRole("button", { name: "Done" }).click();
    await expect(q.getByText("Use a whole number from 1 to 10.")).toBeVisible();
    await q.getByLabel("Most files per request").fill("3");
    await q.getByRole("button", { name: "Done" }).click();
    await expect(admin.getByText(/Receipt/).first()).toBeVisible();
    await expect(admin.getByText("People will be able to attach files here.")).toHaveCount(2);          // the preview shows both file questions but never uploads
  });
});
