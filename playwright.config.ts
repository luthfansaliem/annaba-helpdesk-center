import { defineConfig, devices } from "@playwright/test";

const API = Number(process.env.E2E_API_PORT ?? 5180);
const WEB = Number(process.env.E2E_WEB_PORT ?? 3100);
const KEY = process.env.E2E_PROXY_KEY ?? "e2e-proxy-key-0123456789-abcdefghijklmnop";

export default defineConfig({
  testDir: "./e2e",
  testMatch: /.*\.spec\.ts/,
  fullyParallel: false, // one shared database; specs that mutate shared state run serially
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 45_000,
  expect: { timeout: 8_000 },
  reporter: [["list"], ["html", { open: "never" }]],
  globalSetup: "./e2e/global-setup.ts",
  use: { baseURL: `http://localhost:${WEB}`, trace: "retain-on-failure", screenshot: "only-on-failure", locale: "en-GB", timezoneId: "Asia/Jakarta", ...devices["Desktop Chrome"], channel: process.env.E2E_CHANNEL || undefined },
  webServer: [
    { command: "bash e2e/scripts/start-api.sh", url: `http://localhost:${API}/health`, timeout: 180_000, reuseExistingServer: false, env: { E2E_API_PORT: String(API), E2E_WEB_PORT: String(WEB), E2E_PROXY_KEY: KEY } },
    { command: `npx next start -p ${WEB}`, url: `http://localhost:${WEB}/login`, timeout: 60_000, reuseExistingServer: false, env: { API_URL: `http://localhost:${API}`, API_PROXY_KEY: KEY, PORT: String(WEB), CSP_MODE: "enforce" } },
  ],
});
