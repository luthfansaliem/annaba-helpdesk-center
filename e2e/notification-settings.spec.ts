import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("notification settings (NTF-002)", () => {
  test.use({ storageState: authFile("member") });

  test("a person chooses what reaches them in the app and by e-mail, and can go back to the usual", async ({ page }) => {
    await page.goto("/notifications");
    await page.getByRole("link", { name: "Settings" }).click();
    await expect(page.getByRole("heading", { name: "Notification settings", level: 1 })).toBeVisible();

    const app = page.getByRole("checkbox", { name: "Work starts on your request — In the app" });
    const mail = page.getByRole("checkbox", { name: "Work starts on your request — By e-mail" });
    await expect(app).toBeChecked(); await expect(mail).not.toBeChecked();                               // the usual: in the app only
    const needed = page.getByRole("checkbox", { name: /Your approval is needed — / });
    await expect(needed).toHaveCount(2);
    for (const c of await needed.all()) { await expect(c).toBeChecked(); await expect(c).toBeDisabled(); }  // what work waits for cannot be switched off
    await expect(page.getByRole("button", { name: "Save changes" })).toBeDisabled();                       // nothing to save yet
    await axe(page, "notification settings");

    await app.uncheck(); await mail.check();
    await page.getByRole("button", { name: "Save changes" }).click();
    await expect(page.getByText("Notification settings saved.")).toBeVisible();

    await page.reload();
    await expect(page.getByRole("checkbox", { name: "Work starts on your request — In the app" })).not.toBeChecked();
    await expect(page.getByRole("checkbox", { name: "Work starts on your request — By e-mail" })).toBeChecked();

    await page.getByRole("button", { name: "Back to the usual settings" }).click();
    await expect(page.getByText("Notification settings reset.")).toBeVisible();
    await expect(page.getByRole("checkbox", { name: "Work starts on your request — In the app" })).toBeChecked();
    await expect(page.getByRole("checkbox", { name: "Work starts on your request — By e-mail" })).not.toBeChecked();
  });
});
