import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";
import { TOPICS } from "../src/content/help";
import { authFile } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("in-app help", () => {
  test.describe("a requester", () => {
    test.use({ storageState: authFile("member") });

    test("starts on what people who ask need, can switch persona, and search spans everything", async ({ page }) => {
      await page.goto("/help");
      await expect(page.getByRole("heading", { name: "Help", level: 1 })).toBeVisible();
      await expect(page.getByRole("button", { name: "I need something" })).toHaveAttribute("aria-pressed", "true");
      await expect(page.getByRole("heading", { name: "Make a request", level: 2 })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Work a ticket", level: 2 })).toHaveCount(0);
      await axe(page, "help");

      await page.getByRole("button", { name: "I work the tickets" }).click();
      await expect(page.getByRole("heading", { name: "Work a ticket", level: 2 })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Make a request", level: 2 })).toHaveCount(0);

      await page.getByLabel("Search help").fill("approval");                                           // searching looks in every topic and says which kind of person each is for
      await expect(page.getByRole("status")).toContainText("topics match");
      await expect(page.getByRole("heading", { name: "When your request needs approval", level: 2 })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Approval policies", level: 2 })).toBeVisible();
      await expect(page.getByRole("heading", { name: "Reports", level: 2 })).toHaveCount(0);                  // a topic that never mentions approvals
      await page.getByLabel("Search help").fill("zzzz-nothing");
      await expect(page.getByRole("status")).toContainText("Nothing in the help matches");
    });

    test("a link to a topic opens its tab; the sidebar leads to the help; the language follows the person's choice", async ({ page }) => {
      await page.goto("/help?topic=work-a-ticket");
      await expect(page.getByRole("button", { name: "I work the tickets" })).toHaveAttribute("aria-pressed", "true");
      await expect(page.getByRole("region", { name: "Work a ticket" })).toBeVisible();
      await page.goto("/dashboard");
      const nav = page.getByRole("navigation", { name: "Main navigation" });
      await nav.getByRole("button", { name: "Personal" }).click();
      await nav.getByRole("link", { name: "Help" }).click();
      await expect(page).toHaveURL(/\/help$/);

      // the language is the person's own choice (Settings → Language, saved on the account); the help follows it
      try {
        await page.goto("/settings");
        await page.getByLabel("Language").selectOption("id");
        await page.getByRole("button", { name: "Save settings" }).click();
        await expect(page.getByRole("navigation", { name: "Navigasi utama" })).toBeVisible();
        await page.goto("/help");
        await expect(page.getByRole("heading", { name: "Bantuan", level: 1 })).toBeVisible();
        await expect(page.getByRole("heading", { name: "Membuat permintaan", level: 2 })).toBeVisible();
        await expect(page.getByRole("button", { name: "Saya butuh bantuan" })).toHaveAttribute("aria-pressed", "true");
        await axe(page, "help in Indonesian");
      } finally {
        await page.goto("/settings");
        await page.getByLabel("Bahasa").selectOption("en");
        await page.getByRole("button", { name: "Simpan pengaturan" }).click();
        await expect(page.getByRole("heading", { name: "Settings" })).toBeVisible();
        await page.evaluate(() => localStorage.removeItem("annaba-digital-lang"));
      }
    });
  });

  test.describe("an administrator", () => {
    test.use({ storageState: authFile("admin") });

    test("starts on setting up, gets a Help button on the main screens, and every screen a topic points to exists", async ({ page }) => {
      await page.goto("/help");
      await expect(page.getByRole("button", { name: "I set up the helpdesk" })).toHaveAttribute("aria-pressed", "true");
      await expect(page.getByRole("heading", { name: "Going live: the checklist", level: 2 })).toBeVisible();

      await page.goto("/tickets");
      await page.getByRole("link", { name: /^Help: / }).click();
      await expect(page).toHaveURL(/\/help\?topic=work-the-queue/);
      await expect(page.getByRole("region", { name: "The ticket queue" })).toBeVisible();

      // no dead links: every screen named in the help loads (for someone who holds every permission)
      for (const href of new Set(TOPICS.flatMap((x) => (x.go ? [x.go.href] : [])))) {
        const res = await page.goto(href);
        expect(res?.status(), href).toBeLessThan(400);
        await expect(page.getByRole("heading", { level: 1 }).first(), href).toBeVisible();
        await expect(page.getByText("Page not found"), href).toHaveCount(0);
      }
    });
  });
});
