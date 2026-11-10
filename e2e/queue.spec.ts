import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });

/** A published service of its own, so the queue can be narrowed to exactly this test's tickets. */
async function service(admin: APIRequestContext, name: string) {
  const cat = await (await admin.post("/api/catalog/categories", { data: { name: `${name} cat` }, headers: await hdr(admin) })).json() as { id: string };
  const svc = await (await admin.post("/api/catalog/services", { data: { name, categoryId: cat.id, defaultPriority: "low" }, headers: await hdr(admin) })).json() as { id: string };
  const form = await admin.put(`/api/catalog/services/${svc.id}/form`, { data: { fields: [{ key: "what", type: "text", label: "What?", required: true }] }, headers: await hdr(admin) });
  expect(form.ok(), await form.text()).toBeTruthy();
  expect((await admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) })).ok()).toBeTruthy();
  return svc.id;
}

test.describe("queue: bulk actions and saved views (TKT-011)", () => {
  test("tickets are ticked, changed together, and the ones that cannot take it are listed", async ({ browser }) => {
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const member = await (await browser.newContext({ storageState: authFile("member") })).request;
    const tag = `bulk${Date.now().toString(36)}`;
    const svc = await service(admin.request, `E2E ${tag}`);
    const made: { id: string; number: string }[] = [];
    for (const n of [1, 2, 3]) {
      const r = await member.post(`/api/portal/services/${svc}/requests`, { data: { subject: `${tag} ticket ${n}`, answers: { what: "x" } }, headers: await hdr(member) });
      expect(r.status(), await r.text()).toBe(201); made.push(await r.json());
    }
    // one of them is withdrawn by its requester, so it can no longer be worked
    expect((await member.post(`/api/tickets/${made[2].id}/transition`, { data: { action: "cancel", reason: "Not needed" }, headers: await hdr(member) })).ok()).toBeTruthy();

    await admin.goto("/tickets");
    await admin.getByLabel("Status", { exact: true }).selectOption({ label: "Any status" });
    await admin.getByLabel("Search", { exact: true }).fill(tag);
    await expect(admin.getByRole("link", { name: `${tag} ticket 1` })).toBeVisible();
    await expect(admin.getByRole("link", { name: `${tag} ticket 3` })).toBeVisible();

    // nothing is offered until something is ticked
    await expect(admin.getByRole("toolbar", { name: "Actions for the selected tickets" })).toHaveCount(0);
    await admin.getByRole("checkbox", { name: "Select all rows" }).check();
    const bar = admin.getByRole("toolbar", { name: "Actions for the selected tickets" });
    await expect(bar.getByText("3 selected")).toBeVisible();
    await axe(admin, "queue with a selection");

    // priority for all three: the cancelled one cannot be edited, the other two can
    await bar.getByRole("button", { name: "Set priority…" }).click();
    const dlg = admin.getByRole("dialog", { name: "Set priority" });
    await dlg.getByLabel("Priority").selectOption({ label: "Urgent" });
    await dlg.getByRole("button", { name: "Set priority", exact: true }).click();
    await expect(admin.getByText("2 of 3 tickets updated.")).toBeVisible();
    await expect(admin.getByRole("status").getByText(new RegExp(`${made[2].number}.*cancelled`, "i"))).toBeVisible();
    await expect(admin.getByRole("toolbar", { name: "Actions for the selected tickets" })).toHaveCount(0);   // the selection is cleared after the change
    await expect(admin.getByRole("table").getByText("Urgent", { exact: true })).toHaveCount(2);
    await admin.getByRole("button", { name: "Dismiss" }).click();

    // assigning, then starting work: the two live tickets go through, the cancelled one is listed with its number each time
    await admin.getByRole("checkbox", { name: "Select all rows" }).check();
    await admin.getByRole("button", { name: "Assign…" }).click();
    const assign = admin.getByRole("dialog", { name: "Assign tickets" });
    await assign.getByLabel("Person").selectOption({ label: "Sarah Wijaya" });
    await assign.getByRole("button", { name: "Assign tickets" }).click();
    await expect(admin.getByText("2 of 3 tickets updated.")).toBeVisible();
    await expect(admin.getByRole("status").getByText(new RegExp(`${made[2].number}.*cancelled`, "i"))).toBeVisible();   // which one failed, and why
    await admin.getByRole("button", { name: "Dismiss" }).click();

    await admin.getByRole("checkbox", { name: "Select all rows" }).check();
    await admin.getByRole("button", { name: "Start work" }).click();
    await expect(admin.getByText("2 of 3 tickets updated.")).toBeVisible();
    await expect(admin.getByRole("table").getByText("In progress", { exact: true })).toHaveCount(2);
  });

  test("a filter set can be saved as a view, found again after a reload, and removed", async ({ browser }) => {
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const name = `Urgent ${Date.now().toString(36)}`;
    await admin.goto("/tickets");
    await admin.getByLabel("Priority", { exact: true }).selectOption({ label: "Urgent" });
    await admin.getByLabel("Search", { exact: true }).fill("leak");
    await admin.getByRole("checkbox", { name: "Assigned to me" }).check();
    await admin.getByRole("button", { name: "Save as view" }).click();
    const dlg = admin.getByRole("dialog", { name: "Save as view" });
    await dlg.getByRole("button", { name: "Save", exact: true }).click();
    await expect(dlg.getByText("Use 2–60 characters.")).toBeVisible();                                         // the server's message, on the field
    await dlg.getByLabel("Name").fill(name);
    await expect(dlg.getByRole("checkbox", { name: /Share with everyone/ })).toBeVisible();                     // an administrator may share
    await dlg.getByRole("button", { name: "Save", exact: true }).click();
    await expect(admin.getByText("View saved.")).toBeVisible();
    await expect(admin.getByLabel("Saved views")).toHaveValue(/.+/);

    // a clean page, then the view puts every filter back
    await admin.reload();
    await expect(admin.getByLabel("Search", { exact: true })).toHaveValue("");
    await admin.getByLabel("Saved views").selectOption({ label: name });
    await expect(admin.getByLabel("Search", { exact: true })).toHaveValue("leak");
    await expect(admin.getByLabel("Priority", { exact: true })).toHaveValue("urgent");
    await expect(admin.getByRole("checkbox", { name: "Assigned to me" })).toBeChecked();
    await axe(admin, "queue with views");

    await admin.getByRole("button", { name: `Remove view ${name}` }).click();
    await admin.getByRole("dialog").getByRole("button", { name: "Remove", exact: true }).click();
    await expect(admin.getByText("View removed.")).toBeVisible();
    await expect(admin.getByLabel("Saved views").locator("option", { hasText: name })).toHaveCount(0);
  });

  test("a requester has no queue, so no bulk actions or views either", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).request;
    expect((await member.get("/api/ticket-views")).status()).toBe(403);
    expect((await member.post("/api/tickets/bulk", { data: { ids: [], action: "start" }, headers: await hdr(member) })).status()).toBe(403);
  });
});
