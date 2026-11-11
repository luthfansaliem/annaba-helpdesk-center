import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { USERS, authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const uniq = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 6);

test.describe("visitors (R3-4)", () => {
  test("a member registers a visitor, reception checks them in and out, and the host is told", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const mp = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const guest = `Rina ${uniq()}`;

    // --- the member registers their visitor
    await mp.goto("/visitors");
    await expect(mp.getByRole("heading", { name: "My visitors", level: 1 })).toBeVisible();
    await mp.getByRole("button", { name: "Register a visitor" }).click();
    const dlg = mp.getByRole("dialog", { name: "Register a visitor" });
    await dlg.getByLabel("Visitor's name").fill(guest);
    await dlg.getByLabel("Company").fill("PT Contoh");
    await dlg.getByLabel("Purpose").fill("Contract signing");
    await axe(mp, "register visitor");
    await dlg.getByRole("button", { name: "Save" }).click();
    await expect(dlg).toBeHidden();
    await expect(mp.getByTestId("my-visits").getByRole("listitem").filter({ hasText: guest })).toContainText("Expected");

    // --- reception checks them in with a badge, then out
    await page.goto("/reception");
    await expect(page.getByRole("heading", { name: "Reception", level: 1 })).toBeVisible();
    const row = page.getByTestId("desk-visits").getByRole("listitem").filter({ hasText: guest });
    await row.getByRole("button", { name: "Check in" }).click();
    const ci = page.getByRole("dialog", { name: `Check in ${guest}` });
    await ci.getByLabel("Badge number").fill(`V-${uniq().slice(-4)}`);
    await axe(page, "check-in");
    await ci.getByRole("button", { name: "Check in" }).click();
    await expect(row).toContainText("On site");
    await axe(page, "reception");
    await row.getByRole("button", { name: "Check out" }).click();
    await expect(row).toContainText("Left");

    // --- the host was told
    const notes = await (await mp.request.get("/api/notifications?pageSize=20")).json() as { data?: { title: string }[]; items?: { title: string }[] };
    expect(JSON.stringify(notes)).toContain(`${guest} (PT Contoh) has arrived`);
    await mp.reload();
    await expect(mp.getByTestId("past-visits").getByRole("listitem").filter({ hasText: guest })).toContainText("Left");

    // --- visitor rules and the block list
    await page.goto("/setup/visitors");
    await expect(page.getByRole("heading", { name: "Visitor rules", level: 1 })).toBeVisible();
    const blocked = `Blocked ${uniq()}`;
    await page.locator("#vb-name").fill(blocked); await page.locator("#vb-reason").fill("Test entry");
    await page.getByRole("button", { name: "Add to block list" }).click();
    await expect(page.getByTestId("blocks")).toContainText(blocked);
    await axe(page, "visitor rules");
    await page.getByTestId("blocks").getByRole("button", { name: `Remove ${blocked}` }).click();
    await expect(page.getByTestId("blocks").getByText(blocked)).toHaveCount(0);
  });

  test("reception logs a parcel, the member sees it waiting, and it is collected (R3-5)", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    const mp = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    const sender = `Toko ${uniq()}`;
    await page.goto("/mail");
    await expect(page.getByRole("heading", { name: "Mailroom", level: 1 })).toBeVisible();
    await page.getByRole("button", { name: "Incoming" }).click();
    const dlg = page.getByRole("dialog", { name: "Log incoming mail" });
    await dlg.getByLabel("For (e-mail of the employee)").fill(USERS.member);
    await dlg.getByLabel("Sender").fill(sender);
    await dlg.getByLabel("Courier").fill("JNE"); await dlg.getByLabel("Waybill").fill(`JNE${uniq()}`);
    await axe(page, "log incoming mail");
    await dlg.getByRole("button", { name: "Save" }).click();
    const row = page.getByTestId("mail-list").getByRole("listitem").filter({ hasText: sender });
    await expect(row).toContainText("Waiting at reception");
    await axe(page, "mailroom");

    await mp.goto("/mail");
    await expect(mp.getByRole("heading", { name: "My mail", level: 1 })).toBeVisible();
    await expect(mp.getByTestId("mail-list").getByRole("listitem").filter({ hasText: sender })).toContainText("Waiting at reception");
    await expect(mp.getByRole("button", { name: "Incoming" })).toHaveCount(0);

    await row.getByRole("button", { name: "Collected" }).click();
    await page.getByRole("dialog", { name: "Who collected it?" }).getByRole("button", { name: "Confirm" }).click();
    await expect(row).toHaveCount(0);   // no longer at reception
    await mp.getByLabel("Show").selectOption("");
    await expect(mp.getByTestId("mail-list").getByRole("listitem").filter({ hasText: sender })).toContainText("Collected");
  });
});
