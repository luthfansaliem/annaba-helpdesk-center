import AxeBuilder from "@axe-core/playwright";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { PASSWORD, WEB, authFile, csrf } from "./helpers";

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}
const hdr = async (r: APIRequestContext) => ({ "X-CSRF-Token": await csrf(r), Origin: WEB });
const reauth = async (r: APIRequestContext) => expect((await r.post("/api/auth/reauth", { data: { password: PASSWORD }, headers: await hdr(r) })).ok()).toBeTruthy();
const standard = { senderName: null, subjectPrefix: null, footerEn: null, footerId: null };

test.describe("e-mail wording (NTF-002)", () => {
  test("an administrator writes the organisation's wording, previews it in both languages, saves it and goes back to the standard", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    await reauth(page.request);
    try {
      await page.goto("/admin/email-wording");
      await expect(page.getByRole("heading", { name: "Email Wording", level: 1 })).toBeVisible();
      await expect(page.getByRole("button", { name: "Save wording" })).toBeDisabled();                    // nothing to save yet
      await expect(page.getByRole("button", { name: "Use the standard wording" })).toBeDisabled();

      await page.getByLabel("Name in the sign-off").fill("Acme General Affairs");
      await page.getByLabel("Start of every subject").fill("[Acme GA]");
      await page.getByLabel("Closing text (English)").fill("Urgent? Call ext. 1234.");
      await page.getByLabel("Closing text (Bahasa Indonesia)").fill("Mendesak? Hubungi ext. 1234.");
      await page.getByRole("button", { name: "Preview a message" }).click();
      const preview = page.getByTestId("wording-preview");
      await expect(preview.getByRole("region", { name: "English" })).toContainText("[Acme GA] Your request GA-2026-000123 was resolved");
      await expect(preview.getByRole("region", { name: "English" })).toContainText("Urgent? Call ext. 1234.");
      await expect(preview.getByRole("region", { name: "English" })).toContainText("— Acme General Affairs");
      await expect(preview.getByRole("region", { name: "Bahasa Indonesia" })).toContainText("[Acme GA] Permintaan GA-2026-000123");
      await expect(preview.getByRole("region", { name: "Bahasa Indonesia" })).toContainText("Mendesak? Hubungi ext. 1234.");
      await axe(page, "e-mail wording with a preview");

      await page.getByRole("button", { name: "Save wording" }).click();
      await expect(page.getByText("E-mail wording saved.")).toBeVisible();
      await page.reload();
      await expect(page.getByLabel("Name in the sign-off")).toHaveValue("Acme General Affairs");
      await expect(page.getByLabel("Start of every subject")).toHaveValue("[Acme GA]");
      await expect(page.getByText(/Last changed/)).toBeVisible();

      await page.getByRole("button", { name: "Use the standard wording" }).click();
      await expect(page.getByText("Back to the standard wording.")).toBeVisible();
      await expect(page.getByLabel("Name in the sign-off")).toHaveValue("");
    } finally {
      // other specs read real mail of this organisation: never leave a custom wording behind
      await reauth(page.request);
      await page.request.put("/api/settings/email-wording", { data: standard, headers: await hdr(page.request) });
    }
  });

  test("people who cannot manage settings do not see the page", async ({ browser }) => {
    const member = await (await browser.newContext({ storageState: authFile("member") })).newPage();
    await member.goto("/admin/email-wording");
    await expect(member.getByRole("heading", { name: "Email Wording", level: 1 })).toHaveCount(0);
    expect((await member.request.get("/api/settings/email-wording")).status()).toBe(403);
  });
});
