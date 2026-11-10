import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { WEB, authFile, csrf } from "./helpers";

test.use({ storageState: authFile("admin") });

const SERVICE = "Mail & Courier › Courier & package dispatch";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

/** Moves a row to the top one step at a time, waiting for the order number to change after each step (the list reloads in between). */
async function moveToTop(page: Page, name: string) {
  const row = page.getByRole("row", { name: new RegExp(name) });
  const order = async () => Number((await row.getByRole("cell").first().innerText()).trim());
  for (let n = await order(); n > 1; n--) {
    await row.getByRole("button", { name: `Move up ${name}` }).click();
    await expect(row.getByRole("cell").first()).toHaveText(String(n - 1));
  }
}

test.describe("SLA policies and assignment rules (TKT-003, SLA-002/006)", () => {
  test("policies are listed in order, can be created and reordered, and a test ticket shows which one applies", async ({ page }) => {
    await page.goto("/setup/sla");
    await expect(page.getByRole("heading", { name: "SLA Policies" })).toBeVisible();
    await expect(page.getByRole("row", { name: /Urgent requests/ })).toBeVisible();      // every organisation starts with policies per priority
    await axe(page, "SLA policies");

    const name = `E2E policy ${Date.now()}`;
    await page.getByRole("button", { name: "New policy" }).click();
    const dlg = page.getByRole("dialog", { name: "New SLA policy" });
    await dlg.getByLabel("Name").fill(name);
    await dlg.getByLabel("Service", { exact: true }).selectOption({ label: SERVICE });
    await dlg.getByLabel("Priority", { exact: true }).selectOption("high");
    await dlg.getByLabel("First response (minutes)").fill("20");
    await dlg.getByLabel("Resolution (minutes)").fill("120");
    await expect(dlg.getByText("20 min to respond and 2 h to resolve")).toBeVisible();
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Policy created.")).toBeVisible();

    // a new policy goes last, so the default for high priority still wins until it is moved up
    const test = async () => {
      await page.getByLabel("Service", { exact: true }).selectOption({ label: SERVICE });
      await page.getByLabel("Priority", { exact: true }).selectOption("high");
      await page.getByRole("button", { name: "Check" }).click();
    };
    await test();
    await expect(page.getByRole("status").getByText("High priority requests")).toBeVisible();
    await moveToTop(page, name);
    await test();
    await expect(page.getByRole("status").getByText(name)).toBeVisible();
    await expect(page.getByRole("status").getByText(/respond in 20 min, resolve in 2 h/)).toBeVisible();

    await page.getByRole("button", { name: `Archive ${name}` }).click();
    await expect(page.getByText("Policy archived.")).toBeVisible();
  });

  test("a rule routes new requests to a team and a person, and the ticket shows its service level to staff only", async ({ browser, page }) => {
    // a team with the administrator in it (the administrator may work tickets)
    const team = `E2E team ${Date.now()}`;
    const created = await page.request.post("/api/teams", { data: { name: team }, headers: { "X-CSRF-Token": await csrf(page.request), Origin: WEB } });
    expect(created.ok()).toBeTruthy();
    const teamId = (await created.json()).id as string;
    const me = await (await page.request.get("/api/auth/me")).json();
    const members = await page.request.put(`/api/teams/${teamId}/members`, { data: { members: [{ userId: me.id, isLead: true }] }, headers: { "X-CSRF-Token": await csrf(page.request), Origin: WEB } });
    expect(members.ok()).toBeTruthy();

    await page.goto("/setup/assignment-rules");
    await expect(page.getByRole("heading", { name: "Assignment Rules" })).toBeVisible();
    const rule = `E2E rule ${Date.now()}`;
    // The database is shared by every spec: a live rule for this service would change how the other specs' tickets are routed, so it is archived at the end, pass or fail.
    const cleanup = async () => {
      const list = await (await page.request.get("/api/assignment-rules")).json() as { id: string; name: string }[];
      for (const r of list.filter((x) => x.name === rule)) await page.request.post(`/api/assignment-rules/${r.id}/archive`, { headers: { "X-CSRF-Token": await csrf(page.request), Origin: WEB } });
    };
    try {
    await page.getByRole("button", { name: "New rule" }).click();
    const dlg = page.getByRole("dialog", { name: "New assignment rule" });
    await dlg.getByLabel("Name").fill(rule);
    await dlg.getByLabel("Service", { exact: true }).selectOption({ label: SERVICE });
    await dlg.getByLabel("Send to team").selectOption({ label: team });
    await dlg.getByLabel("Then").selectOption("roundrobin");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(page.getByText("Rule created.")).toBeVisible();
    await moveToTop(page, rule);                                       // earlier rules from other runs can't shadow it
    await axe(page, "assignment rules");

    await page.getByLabel("Service", { exact: true }).selectOption({ label: SERVICE });
    await page.getByRole("button", { name: "Check" }).click();
    await expect(page.getByRole("status").getByText(rule)).toBeVisible();
    await expect(page.getByRole("status").getByText("Sarah Wijaya").first()).toBeVisible();     // the only person in the team who works tickets

    // --- a requester raises a ticket: it is routed and assigned by the rule
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const subject = `E2E routed ${Date.now()}`;
    await member.goto("/requests/new");
    await member.getByLabel("Search services").fill("courier");
    await member.getByRole("link", { name: /Courier & package dispatch/ }).click();
    await member.getByLabel("Subject").fill(subject);
    await member.getByRole("textbox", { name: "Delivery address" }).fill("Jl. Thamrin 2, Jakarta");
    await member.getByRole("textbox", { name: "Recipient" }).fill("Ani");
    await member.getByRole("combobox", { name: "How fast?" }).selectOption("regular");
    await member.getByRole("textbox", { name: "What are you sending?" }).fill("Invoices");
    await member.getByRole("button", { name: "Send request" }).click();
    await expect(member.getByText("Your request has been sent")).toBeVisible();
    await expect(member.getByText("Assigned", { exact: true }).first()).toBeVisible();          // already assigned when it arrived
    await expect(member.getByText("Expected by")).toBeVisible();                                 // the requester gets a date, nothing more
    await expect(member.getByText("Service level")).toHaveCount(0);
    const number = (await member.getByRole("heading", { level: 1 }).innerText()).split(" · ")[0];

    // --- staff see the clock, can filter and sort by it
    await page.goto("/tickets");
    await page.getByLabel("Search", { exact: true }).fill(number);
    await expect(page.getByRole("row", { name: new RegExp(number) }).getByText("On track")).toBeVisible();
    await page.getByLabel("SLA", { exact: true }).selectOption("ontrack");
    await page.getByLabel("Sort by").selectOption("due");
    await expect(page.getByRole("row", { name: new RegExp(number) })).toBeVisible();
    await page.getByRole("link", { name: number }).first().click();
    await expect(page.getByRole("heading", { name: "Service level" })).toBeVisible();
    await expect(page.getByText(/Respons|First response/).first()).toBeVisible();
    await axe(page, "ticket with service level");
    } finally { await cleanup(); }
  });
});
