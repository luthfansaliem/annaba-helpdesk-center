import { createHmac } from "node:crypto";
import { readFileSync } from "node:fs";
import { expect, type APIRequestContext, type Page } from "@playwright/test";

export const WEB = `http://localhost:${process.env.E2E_WEB_PORT ?? 3100}`;
export const PASSWORD = "Passw0rd!demo";
export const USERS = { admin: "sarah.wijaya@annabadigital.io", userAdmin: "daniel.tan@annabadigital.io", auditor: "maya.putri@annabadigital.io", member: "rizky.pratama@annabadigital.io" } as const;
export const authFile = (k: keyof typeof USERS) => `e2e/.auth/${k}.json`;
const LOG = "e2e/.tmp/api.log";

/** RFC 6238 (SHA-1, 6 digits, 30 s) from a base32 secret. */
export function totp(secret: string, at = Date.now()) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"; let bits = ""; 
  for (const c of secret.replace(/=+$/, "").replace(/\s/g, "").toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  const key = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8); counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / 30)));
  const h = createHmac("sha1", key).update(counter).digest(); const o = h[h.length - 1] & 15;
  return String(((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000)).padStart(6, "0");
}

/** Polls the API log (console e-mail sender) for the newest link sent to `to` containing `path`. */
export async function emailedLink(to: string, path: string, timeout = 15_000): Promise<string> {
  const re = new RegExp(`https?://[^\\s]*${path.replace(/[/.]/g, "\\$&")}[^\\s]*`, "g");
  const end = Date.now() + timeout;
  while (Date.now() < end) {
    const text = readFileSync(LOG, "utf8"); const blocks = text.split(/EMAIL to=/).slice(1).reverse();
    for (const b of blocks) { if (!b.startsWith(to)) continue; const m = b.match(re); if (m) return m[0].replace(/[).,]+$/, ""); }
    await new Promise((r) => setTimeout(r, 300));
  }
  throw new Error(`No e-mail with ${path} for ${to} found in ${LOG}`);
}

export async function csrf(req: APIRequestContext) {
  const r = await req.get("/api/auth/csrf"); expect(r.ok()).toBeTruthy(); return (await r.json()).token as string;
}
/** Authenticated mutation through the same-origin proxy, as the browser would do it. */
export async function apiPost(req: APIRequestContext, path: string, data: unknown = {}) {
  return req.post(path, { data, headers: { "X-CSRF-Token": await csrf(req), Origin: WEB } });
}

export async function uiLogin(page: Page, email: string, password = PASSWORD, returnTo?: string) {
  await page.goto(returnTo ? `/login?returnTo=${encodeURIComponent(returnTo)}` : "/login");
  await page.getByLabel("Email").fill(email);
  await page.getByPlaceholder("Enter your password").fill(password);
  await page.getByRole("button", { name: /^sign in$/i }).click();
}

/** Admin invites `email`; returns after the invitee has accepted and set `password`. */
export async function provisionUser(adminReq: APIRequestContext, page: Page, email: string, name: string, password = "Sup3r-Secret!pass-9", roleKey = "user") {
  const roles = await (await adminReq.get("/api/roles")).json() as { id: string; key: string }[];
  const role = roles.find((r) => r.key === roleKey)!;
  const r = await apiPost(adminReq, "/api/invitations", { email, roleIds: [role.id] }); expect(r.status()).toBe(201);
  const link = await emailedLink(email, "/invite");
  await page.goto(link);
  await page.getByLabel("Full name").fill(name);
  await page.getByLabel("Create password").fill(password);
  await page.getByRole("button", { name: /activate account/i }).click();
  await page.waitForURL(/\/login\?accepted=1/);
  return { email, password };
}

/** If the server demanded a fresh password (critical action), fill the global re-authentication dialog. */
export async function reauthIfAsked(page: Page, password = PASSWORD) {
  const dlg = page.getByRole("dialog", { name: /confirm it.s you/i });
  try { await dlg.waitFor({ state: "visible", timeout: 4000 }); } catch { return false; }
  await dlg.getByLabel(/password/i).first().fill(password);
  await dlg.getByRole("button", { name: /confirm|continue|verify/i }).click();
  await dlg.waitFor({ state: "hidden" });
  return true;
}
