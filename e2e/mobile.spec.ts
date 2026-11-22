import { expect, test, type Page } from "@playwright/test";
import { authFile } from "./helpers";

/**
 * Phone-size checks (doc 70): the other specs run at desktop size, so a page that grows wider than a phone, or a sign-in button
 * below the first screen, would go unnoticed. 375 × 812 is the narrowest common phone.
 */
test.use({ viewport: { width: 375, height: 812 } });

async function fitsTheScreen(page: Page, path: string) {
  await page.goto(path);
  await expect(page.getByRole("heading", { level: 1 }).first()).toBeVisible();
  await page.waitForLoadState("networkidle");
  const width = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(width, `${path} is ${width}px wide on a 375px screen`).toBeLessThanOrEqual(376);
}

test.describe("on a phone", () => {
  test("the sign-in form fits the first screen", async ({ browser }) => {
    const page = await (await browser.newContext({ viewport: { width: 375, height: 812 } })).newPage();   // signed out
    await page.goto("/login");
    const signIn = page.getByRole("button", { name: /^sign in$/i });
    await expect(signIn).toBeVisible();
    const box = await signIn.boundingBox();
    expect(box!.y + box!.height).toBeLessThanOrEqual(812);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(376);
  });

  test.describe("a requester", () => {
    test.use({ storageState: authFile("member") });

    test("every page they use fits the screen", async ({ page }) => {
      for (const path of ["/dashboard", "/requests/new", "/requests", "/kb", "/bookings", "/visitors", "/mail", "/supplies", "/approvals", "/notifications", "/settings"]) await fitsTheScreen(page, path);
    });

    test("a request form with long choices and a knowledge-base suggestion fits the screen", async ({ page }) => {
      await page.goto("/requests/new");
      await page.getByRole("link", { name: /Air conditioning/ }).first().click();
      await expect(page.getByRole("button", { name: "Send request" })).toBeVisible();
      await page.waitForLoadState("networkidle");
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(376);
      await expect(page.getByLabel("Where is it?")).toHaveCount(0);                                             // the service asks where itself
    });

    test("booking figures meant for whoever runs the rooms stay hidden", async ({ page }) => {
      await page.goto("/bookings");
      await expect(page.getByRole("heading", { name: "Bookings", level: 1 })).toBeVisible();
      await expect(page.getByText("Room and vehicle use")).toHaveCount(0);
      await expect(page.getByText("No-shows")).toHaveCount(0);
    });
  });

  test.describe("staff", () => {
    test.use({ storageState: authFile("admin") });

    test("the service desk pages fit the screen", async ({ page }) => {
      for (const path of ["/dashboard", "/tickets", "/work-orders", "/work-orders/field", "/reception", "/assets", "/reports", "/inventory"]) await fitsTheScreen(page, path);
    });
  });
});
