// @vitest-environment node
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";
import { beforeEach, describe, expect, it, vi } from "vitest";

/** Loads public/sw.js into a sandbox with in-memory CacheStorage so we can assert the caching policy. */
function loadSw() {
  const stores = new Map<string, Map<string, unknown>>();
  const listeners: Record<string, (e: unknown) => void> = {};
  const makeCache = (name: string) => {
    const m = stores.get(name) ?? new Map<string, unknown>(); stores.set(name, m);
    return { add: async (u: string) => { m.set(new URL(u, "http://app.test").href, "offline-page"); }, match: async (r: string | { url: string }) => m.get(typeof r === "string" ? new URL(r, "http://app.test").href : r.url),
      put: async (r: { url: string }, res: unknown) => { m.set(r.url, res); }, keys: async () => [...m.keys()].map((url) => ({ url })) };
  };
  const network = vi.fn(async (r: { url: string }) => ({ ok: true, url: r.url, clone() { return this; } }));
  const sandbox = {
    self: { location: { origin: "http://app.test" }, addEventListener: (t: string, fn: (e: unknown) => void) => { listeners[t] = fn; }, skipWaiting: async () => {}, clients: { claim: async () => {} } },
    caches: { open: async (n: string) => makeCache(n), keys: async () => [...stores.keys()], delete: async (n: string) => stores.delete(n), match: async (u: string) => { for (const m of stores.values()) { const h = m.get(new URL(u, "http://app.test").href); if (h) return h; } } },
    fetch: network, URL, Response: { error: () => "network-error" },
  };
  runInNewContext(readFileSync("public/sw.js", "utf8"), sandbox);
  const request = async (url: string, opts: { method?: string; mode?: string } = {}) => {
    let responded: Promise<unknown> | undefined;
    listeners.fetch({ request: { url: `http://app.test${url}`, method: opts.method ?? "GET", mode: opts.mode ?? "cors" }, respondWith: (p: Promise<unknown>) => { responded = p; } });
    return { intercepted: !!responded, value: responded ? await responded : undefined };
  };
  const install = async () => { let p: Promise<unknown> = Promise.resolve(); listeners.install({ waitUntil: (x: Promise<unknown>) => { p = x; } }); await p; };
  const cached = () => [...stores.values()].flatMap((m) => [...m.keys()].map((k) => new URL(k).pathname));
  return { request, install, network, cached, stores, listeners, sandbox };
}

describe("service worker caching policy (never cache personalised data)", () => {
  let sw: ReturnType<typeof loadSw>;
  beforeEach(async () => { sw = loadSw(); await sw.install(); });

  it("precaches only the static offline page", () => { expect(sw.cached()).toEqual(["/offline"]); });

  it("caches hashed build assets and serves them from cache next time", async () => {
    await sw.request("/_next/static/chunks/app.abc123.js");
    expect(sw.cached()).toContain("/_next/static/chunks/app.abc123.js");
    sw.network.mockClear();
    await sw.request("/_next/static/chunks/app.abc123.js");
    expect(sw.network).not.toHaveBeenCalled();
  });

  it("never intercepts or stores API calls, RSC payloads, non-GET or cross-origin requests", async () => {
    expect((await sw.request("/api/users")).intercepted).toBe(false);
    expect((await sw.request("/users/all?_rsc=1abc")).intercepted).toBe(false);
    expect((await sw.request("/api/auth/login", { method: "POST" })).intercepted).toBe(false);
    let cross = false; sw.listeners.fetch({ request: { url: "https://evil.test/_next/static/x.js", method: "GET", mode: "cors" }, respondWith: () => { cross = true; } });
    expect(cross).toBe(false);
    expect(sw.cached()).toEqual(["/offline"]);
  });

  it("does not cache an asset URL that carries a query string", async () => {
    expect((await sw.request("/_next/static/x.js?token=secret")).intercepted).toBe(false);
  });

  it("page navigations are network-only and HTML is never stored", async () => {
    const r = await sw.request("/dashboard", { mode: "navigate" });
    expect(r.intercepted).toBe(true);
    expect(sw.cached()).toEqual(["/offline"]);
  });

  it("falls back to the offline page when the network fails", async () => {
    sw.network.mockRejectedValueOnce(new Error("offline"));
    const r = await sw.request("/users/all", { mode: "navigate" });
    expect(r.value).toBe("offline-page");
  });

  it("CLEAR_CACHES wipes everything and old cache versions are deleted on activate", async () => {
    await sw.request("/_next/static/a.js");
    let p: Promise<unknown> = Promise.resolve();
    sw.listeners.message({ data: "CLEAR_CACHES", waitUntil: (x: Promise<unknown>) => { p = x; } }); await p;
    expect(sw.stores.size).toBe(0);
    sw.stores.set("annaba-digital-static-v0", new Map());
    await sw.sandbox.caches.open("annaba-digital-static-v1");
    sw.listeners.activate({ waitUntil: (x: Promise<unknown>) => { p = x; } }); await p;
    expect([...sw.stores.keys()]).toEqual(["annaba-digital-static-v1"]);
  });
});
