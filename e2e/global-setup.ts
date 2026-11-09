import { mkdirSync } from "node:fs";
import { request } from "@playwright/test";
import { PASSWORD, USERS, WEB, authFile } from "./helpers";

/** Signs the four seeded accounts in once (the login endpoint is rate-limited) and stores their cookies. */
export default async function globalSetup() {
  mkdirSync("e2e/.auth", { recursive: true });
  for (const [key, email] of Object.entries(USERS)) {
    const ctx = await request.newContext({ baseURL: WEB, extraHTTPHeaders: { Origin: WEB } });
    const r = await ctx.post("/api/auth/login", { data: { email, password: PASSWORD } });
    if (!r.ok()) throw new Error(`E2E sign-in for ${email} failed: ${r.status()} ${await r.text()}`);
    await ctx.storageState({ path: authFile(key as keyof typeof USERS) });
    await ctx.dispose();
  }
}
