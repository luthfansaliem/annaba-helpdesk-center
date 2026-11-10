import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { USERS, WEB, authFile, csrf, emailedLink } from "./helpers";

// One service of its own, so the approval policy never changes how other specs' tickets are routed (the database is shared).
test.describe.configure({ mode: "serial" });

const SERVICE = `E2E approval service ${Date.now()}`;
const CATEGORY = `E2E approval category ${Date.now()}`;
let serviceId = ""; let categoryId = "";

async function send(req: APIRequestContext, method: "post" | "put", path: string, data?: unknown) {
  const res = await req[method](path, { data, headers: { "X-CSRF-Token": await csrf(req), Origin: WEB } });
  expect(res.ok(), `${method.toUpperCase()} ${path}: ${await res.text()}`).toBeTruthy();
  return res.status() === 204 ? null : await res.json();
}

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

async function request(member: Page, subject: string) {
  await member.goto("/requests/new");
  await member.getByLabel("Search services").fill(SERVICE);
  await member.getByRole("link", { name: new RegExp(SERVICE) }).click();
  await member.getByLabel("Subject").fill(subject);
  await member.getByRole("textbox", { name: "What?" }).fill("Two laptops for the new team");
  await member.getByRole("button", { name: "Send request" }).click();
  await expect(member.getByText("Your request has been sent")).toBeVisible();
  return (await member.getByRole("heading", { level: 1 }).innerText()).split(" · ")[0];
}

test.beforeAll(async ({ browser }) => {
  const ctx = await browser.newContext({ storageState: authFile("admin") });
  const req = ctx.request;
  categoryId = (await send(req, "post", "/api/catalog/categories", { name: CATEGORY })).id;
  serviceId = (await send(req, "post", "/api/catalog/services", { name: SERVICE, categoryId })).id;
  await send(req, "put", `/api/catalog/services/${serviceId}/form`, { fields: [{ key: "what", type: "text", label: "What?", required: true }] });
  await send(req, "post", `/api/catalog/services/${serviceId}/publish`);
  await ctx.close();
});

test.afterAll(async ({ browser }) => {
  const ctx = await browser.newContext({ storageState: authFile("admin") });
  const req = ctx.request;
  await req.post(`/api/approvals/policies/${serviceId}/remove`, { headers: { "X-CSRF-Token": await csrf(req), Origin: WEB } });
  await req.post(`/api/catalog/services/${serviceId}/archive`, { headers: { "X-CSRF-Token": await csrf(req), Origin: WEB } });
  await req.post(`/api/catalog/categories/${categoryId}/archive`, { headers: { "X-CSRF-Token": await csrf(req), Origin: WEB } });
  await ctx.close();
});

test.describe("approvals and notifications (APR-001/003/006, NTF-001)", () => {
  test("an administrator makes a service need approval from the settings screen", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await page.goto("/setup/approvals");
    await expect(page.getByRole("heading", { name: "Approval Policies" })).toBeVisible();
    await axe(page, "approval policies");

    await page.getByRole("button", { name: `Set up ${SERVICE}` }).click();
    const dlg = page.getByRole("dialog", { name: new RegExp(`Approval for ${SERVICE}`) });
    await dlg.getByLabel("Name").fill("Line manager");
    await dlg.getByRole("textbox", { name: "Search people" }).first().fill("daniel");
    const pick = dlg.getByLabel("Add a person");
    const option = pick.locator("option", { hasText: "Daniel Tan" });
    await expect(option).toHaveCount(1);
    await pick.selectOption((await option.getAttribute("value"))!);
    await expect(dlg.getByRole("button", { name: "Remove Daniel Tan" })).toBeVisible();
    await axe(page, "approval policy drawer");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Approval policy saved.")).toBeVisible();
    await expect(page.getByRole("row", { name: new RegExp(SERVICE) }).getByText(/Line manager \(any of 1\)/)).toBeVisible();
  });

  test("a request waits for approval, the approver decides in the app, and everyone is told", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const approver = await (await browser.newContext({ storageState: authFile("userAdmin") })).newPage();
    const number = await request(member, `E2E laptops ${Date.now()}`);

    await expect(member.getByText("Pending approval", { exact: true }).first()).toBeVisible();
    await expect(member.getByText("Nothing starts until every step is approved.")).toBeVisible();
    await expect(member.getByText("Waiting for decision")).toBeVisible();
    await axe(member, "ticket waiting for approval");

    await approver.goto("/approvals");
    await expect(approver.getByRole("heading", { name: "Approvals" })).toBeVisible();
    await expect(approver.getByRole("link", { name: new RegExp(number) })).toBeVisible();
    await axe(approver, "approvals inbox");
    await approver.getByRole("button", { name: `Approve ${number}` }).click();
    const dlg = approver.getByRole("dialog", { name: "Approve this request" });
    await dlg.getByLabel("Comment (optional)").fill("Budget confirmed");
    await dlg.getByRole("button", { name: "Approve", exact: true }).click();
    await expect(approver.getByText("Approved.", { exact: true })).toBeVisible();
    await expect(approver.getByRole("table", { name: "Decided" }).getByText(number)).toBeVisible();

    await member.reload();
    await expect(member.getByText("Submitted", { exact: true }).first()).toBeVisible();                  // released to the team
    await expect(member.getByText(/Budget confirmed/).first()).toBeVisible();                                       // the trail shows the comment

    await member.goto("/notifications");
    await expect(member.getByRole("heading", { name: "Notifications" })).toBeVisible();
    await expect(member.getByText(`${number} was approved`)).toBeVisible();
    await axe(member, "notifications");
    await member.getByRole("button", { name: "Mark all as read" }).click();
    await expect(member.getByText("All marked as read.")).toBeVisible();
    await expect(member.getByRole("link", { name: "0 unread notifications" })).toBeVisible();
  });

  test("the e-mail link decides without signing in, needs a reason to reject, and works only once", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const number = await request(member, `E2E monitors ${Date.now()}`);
    const link = await emailedLink(USERS.userAdmin, "/approve");

    const anon = await (await browser.newContext()).newPage();       // nobody signed in
    await anon.goto(link);
    await expect(anon.getByRole("heading", { name: "Approval needed" })).toBeVisible();
    await expect(anon.getByText(number)).toBeVisible();
    await axe(anon, "approval link page");
    await anon.getByRole("button", { name: "Reject" }).click();
    await expect(anon.getByText(/A rejection needs a comment/)).toBeVisible();
    await anon.getByLabel("Comment (required to reject)").fill("Use the monitors we already have");
    await anon.getByRole("button", { name: "Reject" }).click();
    await expect(anon.getByRole("heading", { name: "Rejected" })).toBeVisible();

    await anon.goto(link);
    await expect(anon.getByRole("heading", { name: "This link is no longer valid" })).toBeVisible();     // single use

    await member.reload();
    await expect(member.getByText("Rejected", { exact: true }).first()).toBeVisible();
    await expect(member.getByText(/Use the monitors we already have/).first()).toBeVisible();
    await expect(member.getByText(/by e-mail link/)).toBeVisible();                                         // the trail says how it was decided
  });
});
