import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { USERS, WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const ymd = (daysFromNow: number) => { const d = new Date(Date.now() + daysFromNow * 864e5); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };

async function userId(admin: APIRequestContext, email: string) {
  const body = await (await admin.get(`/api/users?q=${encodeURIComponent(email)}`)).json() as { data: { id: string; email: string }[] };
  return body.data.find((u) => u.email === email)!.id;
}

/** A published service of its own whose approval policy is given by the caller. */
async function service(admin: APIRequestContext, name: string, step: object) {
  const cat = await (await admin.post("/api/catalog/categories", { data: { name: `${name} cat` }, headers: await hdr(admin) })).json() as { id: string };
  const svc = await (await admin.post("/api/catalog/services", { data: { name, categoryId: cat.id }, headers: await hdr(admin) })).json() as { id: string };
  expect((await admin.put(`/api/catalog/services/${svc.id}/form`, { data: { fields: [{ key: "what", type: "text", label: "What?", required: true }] }, headers: await hdr(admin) })).ok()).toBeTruthy();
  expect((await admin.post(`/api/catalog/services/${svc.id}/publish`, { headers: await hdr(admin) })).ok()).toBeTruthy();
  const pol = await admin.put(`/api/approvals/policies/${svc.id}`, { data: { steps: [step] }, headers: await hdr(admin) });
  expect(pol.ok(), await pol.text()).toBeTruthy();
  return svc.id;
}
const raise = async (r: APIRequestContext, svc: string, subject: string) => {
  const res = await r.post(`/api/portal/services/${svc}/requests`, { data: { subject, answers: { what: "x" } }, headers: await hdr(r) });
  expect(res.status(), await res.text()).toBe(201); return await res.json() as { id: string; number: string };
};

test.describe("approvals while away, and managers (APR-002/004)", () => {
  test("a person goes out of office, their cover approves in their place, and ending it sends approvals back", async ({ browser }) => {
    const admin = (await (await browser.newContext({ storageState: authFile("admin") })).newPage()).request;
    const maya = await (await browser.newContext({ storageState: authFile("auditor") })).newPage();      // the approver who will be away
    const daniel = await (await browser.newContext({ storageState: authFile("userAdmin") })).newPage();  // her cover
    const rizky = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const mayaId = await userId(admin, USERS.auditor);
    const svc = await service(admin, `E2E away ${Date.now().toString(36)}`, { name: "Budget owner", rule: "any", userIds: [mayaId], roleIds: [] });

    // --- Maya names Daniel as her cover until the day after tomorrow
    await maya.goto("/approvals");
    await expect(maya.getByRole("heading", { name: "Out of office" })).toBeVisible();
    await maya.getByLabel("Search people").fill("Daniel");
    await maya.getByLabel("Who covers for you").selectOption({ index: 1 });
    await maya.getByLabel("Until (including)").fill(ymd(2));
    await maya.getByLabel("Note (optional)").fill("Annual leave");
    await axe(maya, "approvals with the out-of-office form");
    await maya.getByRole("button", { name: "Start delegation" }).click();
    await expect(maya.getByRole("row", { name: /Daniel Tan.*Active/ })).toBeVisible();

    // --- a request made while she is away goes to Daniel, marked as being for her; Maya is not asked
    const t = await raise(rizky, svc, `Away test ${Date.now().toString(36)}`);
    await maya.reload();
    await expect(maya.getByText(t.number)).toHaveCount(0);
    await daniel.goto("/approvals");
    const waiting = daniel.getByRole("row", { name: new RegExp(t.number) });
    await expect(waiting).toBeVisible();
    await expect(waiting.getByText(/for Maya Putri/)).toBeVisible();
    await waiting.getByRole("button", { name: `Approve ${t.number}` }).click();
    await daniel.getByRole("dialog").getByRole("button", { name: /^Approve/ }).click();
    await expect(daniel.getByRole("row", { name: new RegExp(t.number) }).getByRole("button", { name: /Approve/ })).toHaveCount(0);   // decided: no longer waiting
    await expect.poll(async () => (await (await rizky.get(`/api/tickets/${t.id}`)).json()).status).toBe("submitted");                  // released to the team

    // --- the trail on the ticket names both people
    await daniel.goto(`/tickets/${t.id}`);
    await expect(daniel.getByText("Daniel Tan").first()).toBeVisible();
    await expect(daniel.getByText("(for Maya Putri)")).toBeVisible();

    // --- Maya ends the delegation; the next request comes back to her
    await maya.reload();
    await maya.getByRole("button", { name: "End delegation to Daniel Tan" }).click();
    await expect(maya.getByRole("row", { name: /Daniel Tan.*Ended early/ })).toBeVisible();
    const t2 = await raise(rizky, svc, `Back test ${Date.now().toString(36)}`);
    await maya.reload();
    await expect(maya.getByRole("row", { name: new RegExp(t2.number) })).toBeVisible();
  });

  test("a waiting approval can be handed to someone else", async ({ browser }) => {
    const admin = (await (await browser.newContext({ storageState: authFile("admin") })).newPage()).request;
    const maya = await (await browser.newContext({ storageState: authFile("auditor") })).newPage();
    const daniel = await (await browser.newContext({ storageState: authFile("userAdmin") })).newPage();
    const rizky = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const mayaId = await userId(admin, USERS.auditor);
    const svc = await service(admin, `E2E handover ${Date.now().toString(36)}`, { name: "Owner", rule: "any", userIds: [mayaId], roleIds: [] });
    const t = await raise(rizky, svc, `Hand over ${Date.now().toString(36)}`);

    await maya.goto("/approvals");
    await maya.getByRole("button", { name: `Hand over ${t.number}` }).click();
    const dlg = maya.getByRole("dialog", { name: "Hand over this approval" });
    await dlg.getByLabel("Search people").fill("Daniel");
    await dlg.getByLabel("Who should decide instead?").selectOption({ index: 1 });
    await dlg.getByRole("button", { name: "Hand over", exact: true }).click();
    await expect(maya.getByText("Approval handed over.")).toBeVisible();
    await expect(maya.getByRole("row", { name: new RegExp(t.number) }).getByRole("button", { name: /Approve/ })).toHaveCount(0);

    await daniel.goto("/approvals");
    await expect(daniel.getByRole("row", { name: new RegExp(t.number) }).getByText(/for Maya Putri/)).toBeVisible();
  });

  test("a user's manager is set on their profile and an approval step can ask for it", async ({ browser }) => {
    const admin = (await (await browser.newContext({ storageState: authFile("admin") })).newPage()).request;
    const daniel = await (await browser.newContext({ storageState: authFile("userAdmin") })).newPage();   // may update users
    const rizky = (await (await browser.newContext({ storageState: authFile("member") })).newPage()).request;
    const rizkyId = await userId(admin, USERS.member);
    const svc = await service(admin, `E2E manager ${Date.now().toString(36)}`, { name: "Line manager", rule: "any", userIds: [], roleIds: [], requesterManager: true });

    await daniel.goto(`/users/${rizkyId}`);
    await daniel.getByRole("tab", { name: "Profile" }).click();
    await expect(daniel.getByText("Nobody set")).toBeVisible();
    await daniel.getByLabel("Search people").fill("Sarah");
    await daniel.getByLabel("Choose a manager").selectOption({ index: 1 });
    await daniel.getByRole("button", { name: "Set manager" }).click();
    await expect(daniel.getByText("Manager set.")).toBeVisible();
    await expect(daniel.getByText("Reports to: Sarah Wijaya")).toBeVisible();

    // the step asks for the requester's manager, who is now Sarah
    const t = await raise(rizky, svc, `Manager test ${Date.now().toString(36)}`);
    const mine = await (await admin.get("/api/approvals/mine")).json() as { ticketId: string; status: string }[];
    expect(mine.some((a) => a.ticketId === t.id && a.status === "pending")).toBeTruthy();

    // the policy screen shows the step's choice
    const sarah = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await sarah.goto("/setup/approvals");
    await expect(sarah.getByRole("row", { name: /E2E manager/ }).getByText(/The requester's manager/)).toBeVisible();

    await daniel.goto(`/users/${rizkyId}`);
    await daniel.getByRole("tab", { name: "Profile" }).click();
    await daniel.getByRole("button", { name: "Clear manager" }).click();
    await expect(daniel.getByText("Manager cleared.")).toBeVisible();
    await expect(daniel.getByText("Nobody set")).toBeVisible();
  });
});
