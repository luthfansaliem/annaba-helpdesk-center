import { expect, test } from "@playwright/test";
import { authFile } from "./helpers";

test.use({ storageState: authFile("admin") });

test("sidebar groups open only for the active page and the trail follows navigation", async ({ page }) => {
  await page.goto("/roles");
  const nav = page.getByRole("navigation", { name: "Main navigation" });
  await expect(nav.getByRole("link", { name: "Roles", exact: true })).toHaveAttribute("aria-current", "page");
  await expect(nav.getByRole("link", { name: "All Users" })).toBeHidden();
  await nav.getByRole("button", { name: /Users/ }).first().click();
  await nav.getByRole("link", { name: "All Users" }).click();
  await expect(page).toHaveURL(/\/users\/all/);
});

test("collapse state persists across reloads", async ({ page }) => {
  await page.goto("/dashboard");
  await page.getByRole("button", { name: "Collapse menu" }).click();
  await expect(page.getByRole("button", { name: "Expand menu" })).toBeVisible();
  await page.reload();
  await expect(page.getByRole("button", { name: "Expand menu" })).toBeVisible();
  await page.getByRole("button", { name: "Expand menu" }).click();
});

test("command palette (Ctrl/Cmd+K) navigates to pages", async ({ page }) => {
  await page.goto("/dashboard");
  await page.waitForLoadState("networkidle");
  await page.keyboard.press("ControlOrMeta+k");
  const box = page.getByLabel("Search pages and people");
  await expect(box).toBeFocused();
  await box.fill("audit");
  await page.keyboard.press("Enter");
  await expect(page).toHaveURL(/\/audit/);
});

test("mobile drawer opens, traps Escape, and closes after navigating", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 });
  await page.goto("/dashboard");
  await page.getByRole("button", { name: "Open menu" }).click();
  const side = page.getByRole("complementary", { name: "Sidebar" });
  await expect(side).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Open menu" })).toHaveAttribute("aria-expanded", "false");
  await page.getByRole("button", { name: "Open menu" }).click();
  await side.getByRole("button", { name: /Personal/ }).click();
  await side.getByRole("link", { name: "Sessions & Devices" }).click();
  await expect(page).toHaveURL(/\/sessions/);
  await expect(page.getByRole("button", { name: "Open menu" })).toHaveAttribute("aria-expanded", "false");
});

test("unknown routes show a helpful not-found page inside the app", async ({ page }) => {
  const r = await page.goto("/definitely/not/here");
  expect(r?.status()).toBe(404);
});
