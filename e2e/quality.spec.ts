import AxeBuilder from "@axe-core/playwright";
import { expect, test } from "@playwright/test";
import { authFile } from "./helpers";

const publicPages = ["/login", "/forgot-password", "/register"];
const appPages = ["/dashboard", "/users", "/users/all", "/users/invitations", "/roles", "/permissions", "/access-matrix", "/sessions", "/access-requests", "/access-reviews", "/audit", "/security-events", "/profile", "/profile?tab=security", "/settings", "/admin/settings", "/admin/email", "/admin/tenants"];
const widths = [1440, 1366, 1024, 768, 390, 375];

async function axe(page: import("@playwright/test").Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("accessibility (axe, WCAG 2.1 A/AA — serious & critical)", () => {
  for (const p of publicPages) test(`public ${p}`, async ({ page }) => { await page.goto(p); await page.waitForLoadState("networkidle"); await axe(page, p); });
  test.describe("signed in", () => {
    test.use({ storageState: authFile("admin") });
    for (const p of appPages) test(p, async ({ page }) => { await page.goto(p); await page.waitForLoadState("networkidle"); await expect(page.getByRole("heading").first()).toBeVisible(); await axe(page, p); });
  });
});

test.describe("accessibility in dark mode (contrast holds on both palettes)", () => {
  test.use({ storageState: authFile("admin") });
  test.beforeEach(async ({ page }) => { await page.addInitScript(() => localStorage.setItem("annaba-digital-theme", "dark")); });
  for (const p of ["/login", ...appPages.filter((x) => !x.startsWith("/design"))]) test(`dark ${p}`, async ({ page }) => {
    await page.goto(p); await page.waitForLoadState("networkidle");
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await axe(page, `dark ${p}`);
  });
});

test.describe("responsive: no horizontal page overflow", () => {
  test.use({ storageState: authFile("admin") });
  for (const w of widths) test(`${w}px`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: 900 });
    for (const p of ["/dashboard", "/users/all", "/roles", "/access-matrix", "/audit", "/profile", "/admin/settings", "/admin/email"]) {
      await page.goto(p); await page.waitForLoadState("networkidle");
      const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      const culprit = over > 1 ? await page.evaluate(() => [...document.querySelectorAll("body *")].filter((e) => e.getBoundingClientRect().right > document.documentElement.clientWidth + 1).slice(0, 4).map((e) => `${e.tagName}.${String((e as HTMLElement).className).slice(0, 40)}`).join(" | ")) : "";
      expect(over, `${p} at ${w}px overflows by ${over}px: ${culprit}`).toBeLessThanOrEqual(1);
    }
  });
  test("login at 375px", async ({ browser }) => {
    const c = await browser.newContext({ storageState: { cookies: [], origins: [] }, viewport: { width: 375, height: 800 } }); const p = await c.newPage();
    await p.goto("/login"); expect(await p.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1); await c.close();
  });
});
