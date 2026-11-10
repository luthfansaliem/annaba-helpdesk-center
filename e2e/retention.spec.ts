import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { PASSWORD, WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const reauth = async (r: APIRequestContext) => expect((await r.post("/api/auth/reauth", { data: { password: PASSWORD }, headers: await hdr(r) })).ok()).toBeTruthy();

async function publishedService(admin: APIRequestContext, name: string) {
  const cat = await (await admin.post("/api/catalog/categories", { data: { name: `${name} cat` }, headers: await hdr(admin) })).json() as { id: string };
  const svc = await (await admin.post("/api/catalog/services", { data: { name, categoryId: cat.id }, headers: await hdr(admin) })).json() as { id: string };
  expect((await admin.put(`/api/catalog/services/${svc.id}/form`, { data: { fields: [{ key: "what", type: "text", label: "What?", required: true }] }, headers: await hdr(admin) })).ok()).toBeTruthy();
  expect((await admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) })).ok()).toBeTruthy();
  return svc.id;
}

test.describe("data retention (ADM-006)", () => {
  // The policy is only ever saved with ten-year periods here: nothing in the shared test data is that old, so the daily clean-up and "Run now" cannot touch other tests' tickets.
  test("an administrator sets the policy, previews it, runs it, holds a ticket and releases it; people read what is kept", async ({ browser }) => {
    const adminPage = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const admin = adminPage.request;
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const svc = await publishedService(admin, `E2E retention ${Date.now().toString(36)}`);
    const made = await member.request.post(`/api/portal/services/${svc}/requests`, { data: { subject: "Retention hold test", answers: { what: "x" } }, headers: await hdr(member.request) });
    expect(made.status(), await made.text()).toBe(201);
    const ticket = await made.json() as { id: string; number: string };
    await reauth(admin);

    // --- the page starts switched off; nothing to save yet
    await adminPage.goto("/admin/retention");
    await expect(adminPage.getByRole("heading", { name: "Data Retention", level: 1 })).toBeVisible();
    await expect(adminPage.getByRole("button", { name: "Save policy" })).toBeDisabled();
    await expect(adminPage.getByRole("button", { name: "Run now" })).toBeDisabled();

    // --- a period that is too short is refused with a message on the field
    await adminPage.getByLabel("Apply this policy every day").check();
    await adminPage.getByLabel("Finished tickets: act after (days)").fill("5");
    await adminPage.getByRole("button", { name: "Save policy" }).click();
    await expect(adminPage.getByText("Use 30–3650 days, or leave it empty to keep tickets.")).toBeVisible();

    // --- ten-year periods: preview, save, run (nothing is that old)
    await adminPage.getByLabel("Finished tickets: act after (days)").fill("3650");
    await adminPage.getByLabel("Files: remove after (days)").fill("3650");
    await adminPage.getByLabel("Notifications: remove after (days)").fill("3650");
    await adminPage.getByRole("button", { name: "Preview what it would remove" }).click();
    const preview = adminPage.getByTestId("retention-preview");
    await expect(preview).toContainText("tickets would be anonymised");
    await expect(preview).toContainText("files would be removed");
    await axe(adminPage, "data retention");
    await adminPage.getByRole("button", { name: "Save policy" }).click();
    await expect(adminPage.getByText("Retention policy saved.")).toBeVisible();
    await adminPage.getByRole("button", { name: "Run now" }).click();
    await adminPage.getByRole("dialog").getByRole("button", { name: "Run now" }).click();
    await expect(adminPage.getByTestId("retention-done")).toContainText("Done: 0 tickets, 0 files and 0 notifications removed.");
    await expect(adminPage.getByText(/0 tickets, 0 files, 0 notifications/)).toBeVisible();                // "Last run" shows it too

    // --- everyone signed in can read what their organisation keeps
    await member.goto("/profile?tab=privacy");
    const notice = member.getByTestId("retention-notice");
    await expect(notice).toContainText("3650 days after it was finished");
    await expect(notice).toContainText("Notifications are removed after 3650 days.");
    await axe(member, "privacy tab with the retention notice");

    // --- a legal hold from the ticket's page, seen in the list, then released
    await adminPage.goto(`/tickets/${ticket.id}`);
    await adminPage.getByRole("button", { name: "Place legal hold" }).click();
    const dlg = adminPage.getByRole("dialog", { name: "Place a legal hold" });
    await dlg.getByRole("button", { name: "Place hold" }).click();
    await expect(dlg.getByText("Use 3–300 characters.")).toBeVisible();                                    // a reason is required
    await dlg.getByLabel(/Reason/).fill("Court order 2026/17");
    await dlg.getByRole("button", { name: "Place hold" }).click();
    await expect(adminPage.getByText("Under legal hold")).toBeVisible();
    await expect(adminPage.getByText("Court order 2026/17")).toBeVisible();
    await member.goto(`/tickets/${ticket.id}`);                                                           // the requester never sees the hold
    await expect(member.getByRole("heading", { name: new RegExp(ticket.number) })).toBeVisible();
    await expect(member.getByText("Legal hold")).toHaveCount(0);

    await adminPage.goto("/admin/retention");
    const row = adminPage.getByRole("row", { name: new RegExp(ticket.number) });
    await expect(row).toContainText("Court order 2026/17");
    await row.getByRole("button", { name: "Release hold" }).click();
    await adminPage.getByRole("dialog").getByRole("button", { name: "Release hold" }).click();
    await expect(adminPage.getByRole("row", { name: new RegExp(ticket.number) })).toHaveCount(0);

    // --- the stored files are compared with the database (read only here: the shared volume may hold other tests' leftovers)
    await adminPage.getByRole("button", { name: "Check stored files" }).click();
    await expect(adminPage.getByTestId("files-check")).toContainText("files are recorded");
    await axe(adminPage, "data retention with the file check");

    // --- put the shared data back as it was found: policy off
    await reauth(admin);
    const off = await admin.put("/api/retention", { data: { enabled: false, finishedTicketDays: null, ticketAction: "anonymise", attachmentDays: null, notificationDays: null }, headers: await hdr(admin) });
    expect(off.ok(), await off.text()).toBeTruthy();
  });

  test("people without the permission neither see the page nor the hold control", async ({ browser }) => {
    const sup = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await sup.goto("/admin/retention");
    await expect(sup.getByRole("heading", { name: "Data Retention", level: 1 })).toHaveCount(0);
    expect((await sup.request.get("/api/retention")).status()).toBe(403);
  });
});
