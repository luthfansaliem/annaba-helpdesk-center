import { expect, request as pwRequest, test } from "@playwright/test";
import { reauthIfAsked, WEB, authFile, provisionUser, totp, uiLogin } from "./helpers";

test("MFA: enrol with a real TOTP, recovery codes shown once, sign-in needs the second step", async ({ browser }) => {
  const admin = await pwRequest.newContext({ baseURL: WEB, storageState: authFile("admin") });
  const email = `e2e.mfa.${Date.now()}@annabadigital.io`; const pw = "Sup3r-Secret!pass-9";
  const ctx = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const p = await ctx.newPage();
  await provisionUser(admin, p, email, "Mfa Person", pw);
  await uiLogin(p, email, pw); await expect(p).toHaveURL(/\/dashboard/);

  await p.goto("/profile?tab=security");
  await p.getByRole("button", { name: /set up authenticator/i }).click();
  await reauthIfAsked(p, pw);
  const dlg = p.getByRole("dialog", { name: "Set up authenticator" });
  await expect(dlg.getByRole("img", { name: /QR code/i })).toBeVisible();
  const secret = (await dlg.locator("code").first().innerText()).trim();
  await dlg.getByRole("group", { name: /verification code/i }).locator("input").first().click();
  await p.keyboard.type(totp(secret), { delay: 80 });
  await dlg.getByRole("button", { name: /verify/i }).click();
  await expect(p.getByText(/save these recovery codes/i)).toBeVisible();
  expect(await p.locator(".codes span").count()).toBeGreaterThanOrEqual(8);
  await p.getByRole("button", { name: /i.ve saved them/i }).click();
  await p.reload(); await p.goto("/profile?tab=security");
  await expect(p.getByText(/recovery code\(s\) left/)).toBeVisible();
  await expect(p.locator(".codes")).toHaveCount(0); // never shown again

  // fresh sign-in must pass the second step
  await ctx.clearCookies();
  await uiLogin(p, email, pw);
  await expect(p).toHaveURL(/\/mfa/);
  await p.getByRole("group", { name: /verification code/i }).locator("input").first().click();
  await p.keyboard.type("000000", { delay: 80 });
  await expect(p.locator(".form-alert").first()).toBeVisible(); // auto-submits on the 6th digit
  await p.getByRole("group", { name: /verification code/i }).locator("input").first().click();
  await p.keyboard.press("Control+A"); await p.keyboard.type(totp(secret, Date.now() + 30_000), { delay: 80 }); // next window avoids replay of the code used at enrolment
  await expect(p).toHaveURL(/\/dashboard/);
  await ctx.close(); await admin.dispose();
});
