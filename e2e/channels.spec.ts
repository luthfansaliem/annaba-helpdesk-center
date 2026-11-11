import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("notification channels (R3-10)", () => {
  test("an administrator offers Teams and an employee connects their own chat", async ({ browser }) => {
    const admin = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await admin.goto("/admin/channels");
    await expect(admin.getByRole("heading", { name: "Notification channels", level: 1 })).toBeVisible();
    // WhatsApp cannot be turned on without its settings
    const wa = admin.getByTestId("channel-whatsapp");
    await wa.getByLabel("Offer this channel to everyone").check();
    await wa.getByRole("button", { name: "Save" }).click();
    await expect(wa.getByRole("alert").first()).toContainText("Fill in what the channel needs");
    // Teams needs nothing
    const teams = admin.getByTestId("channel-teams");
    await teams.getByLabel("Offer this channel to everyone").check();
    await teams.getByRole("button", { name: "Save" }).click();
    await expect(teams.getByText("On", { exact: true })).toBeVisible();
    await axe(admin, "channels (admin)");

    // the employee connects a Teams Workflows URL; a foreign host is refused
    const page = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await page.goto("/notifications/settings");
    const mine = page.getByTestId("my-channels");
    await expect(mine).toContainText("Microsoft Teams");
    await page.getByLabel("Teams Workflows URL").fill("https://example.com/hook");
    await page.getByRole("button", { name: "Connect", exact: true }).click();
    await expect(mine).toContainText("Workflows flow");
    await page.getByLabel("Teams Workflows URL").fill("https://prod-11.westeurope.logic.azure.com/workflows/e2e/triggers/manual/paths/invoke?sig=e2e");
    await page.getByRole("button", { name: "Connect", exact: true }).click();
    await expect(mine).toContainText("prod-11.westeurope.logic.azure.com");
    await expect(mine.getByLabel("Instead of e-mail")).not.toBeChecked();
    await axe(page, "notification settings with channels");

    // disconnect again (keeps the shared test data quiet) and switch Teams off
    await page.getByRole("button", { name: "Disconnect Microsoft Teams" }).click();
    await expect(mine.getByLabel("Teams Workflows URL")).toBeVisible();
    await teams.getByLabel("Offer this channel to everyone").uncheck();
    await teams.getByRole("button", { name: "Save" }).click();
    await expect(teams.getByText("Off", { exact: true })).toBeVisible();
  });
});
