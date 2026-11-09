import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, api, get, messageOf, post, resetCsrf, setApiHandlers } from "@/lib/api/client";

const json = (status: number, body: unknown, headers: Record<string, string> = {}) => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json", ...headers } });
const err = (status: number, code: string, message = "m") => json(status, { error: { code, message, requestId: "r1" } });
let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => { fetchMock = vi.fn(); vi.stubGlobal("fetch", fetchMock); resetCsrf(); setApiHandlers({}); });
afterEach(() => vi.unstubAllGlobals());

describe("api client", () => {
  it("does not fetch a CSRF token for GET and sends same-origin credentials", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { ok: 1 }));
    await expect(get("/api/x", { a: 1, b: undefined, c: "" })).resolves.toEqual({ ok: 1 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/x?a=1");
    expect(fetchMock.mock.calls[0][1].credentials).toBe("same-origin");
  });

  it("attaches the CSRF token to unsafe requests and caches it", async () => {
    fetchMock.mockImplementation(async (u: string) => (u === "/api/auth/csrf" ? json(200, { token: "tok" }) : json(200, {})));
    await post("/api/a"); await post("/api/b");
    expect(fetchMock.mock.calls.filter((c) => c[0] === "/api/auth/csrf")).toHaveLength(1);
    expect(fetchMock.mock.calls[1][1].headers["X-CSRF-Token"]).toBe("tok");
    expect(fetchMock.mock.calls[2][1].headers["X-CSRF-Token"]).toBe("tok");
  });

  it("refreshes a rejected CSRF token once and retries", async () => {
    fetchMock
      .mockResolvedValueOnce(json(200, { token: "old" }))
      .mockResolvedValueOnce(err(403, "denied", "CSRF token missing or invalid."))
      .mockResolvedValueOnce(json(200, { token: "new" }))
      .mockResolvedValueOnce(json(200, { done: true }));
    await expect(post("/api/a")).resolves.toEqual({ done: true });
    expect(fetchMock.mock.calls[3][1].headers["X-CSRF-Token"]).toBe("new");
  });

  it("turns the error envelope into ApiError", async () => {
    fetchMock.mockResolvedValueOnce(json(422, { error: { code: "validation", message: "Bad.", fields: { email: "Invalid" }, requestId: "abc" } }));
    const e = await get("/api/x").catch((x) => x);
    expect(e).toBeInstanceOf(ApiError);
    expect(e).toMatchObject({ status: 422, code: "validation", fields: { email: "Invalid" }, requestId: "abc" });
  });

  it("copes with a non-JSON error body", async () => {
    fetchMock.mockResolvedValueOnce(new Response("<html>bad gateway</html>", { status: 502 }));
    const e = await get("/api/x").catch((x) => x);
    expect(e).toMatchObject({ status: 502, code: "error" });
  });

  it("routes 401 / 423 / MFA errors to handlers unless noRoute", async () => {
    const h = { unauthenticated: vi.fn(), locked: vi.fn(), mfaRequired: vi.fn(), enrollmentRequired: vi.fn() };
    setApiHandlers(h);
    fetchMock.mockResolvedValueOnce(err(401, "unauthenticated")).mockResolvedValueOnce(err(423, "locked")).mockResolvedValueOnce(err(403, "mfa_required")).mockResolvedValueOnce(err(403, "mfa_enrollment_required")).mockResolvedValueOnce(err(401, "unauthenticated"));
    for (let i = 0; i < 4; i++) await get("/api/x").catch(() => {});
    await get("/api/x", undefined, { noRoute: true }).catch(() => {});
    expect(h.unauthenticated).toHaveBeenCalledTimes(1); expect(h.locked).toHaveBeenCalledTimes(1);
    expect(h.mfaRequired).toHaveBeenCalledTimes(1); expect(h.enrollmentRequired).toHaveBeenCalledTimes(1);
  });

  it("asks for re-authentication and replays the request exactly once", async () => {
    const reauth = vi.fn().mockResolvedValue(true); setApiHandlers({ reauth });
    fetchMock.mockResolvedValueOnce(json(200, { token: "t" })).mockResolvedValueOnce(err(403, "reauth_required")).mockResolvedValueOnce(json(200, { saved: 1 }));
    await expect(post("/api/critical")).resolves.toEqual({ saved: 1 });
    expect(reauth).toHaveBeenCalledTimes(1);
  });

  it("does not replay when the re-authentication dialog is cancelled, nor loop on a second reauth_required", async () => {
    const reauth = vi.fn().mockResolvedValueOnce(false); setApiHandlers({ reauth });
    fetchMock.mockImplementation(async (u: string) => (u === "/api/auth/csrf" ? json(200, { token: "t" }) : err(403, "reauth_required")));
    await expect(api("/api/a", { method: "DELETE" })).rejects.toMatchObject({ code: "reauth_required" });
    reauth.mockResolvedValue(true);
    await expect(api("/api/b", { method: "DELETE" })).rejects.toMatchObject({ code: "reauth_required" });
    expect(reauth).toHaveBeenCalledTimes(2);
  });

  it("sends raw bodies with their content type (avatar upload)", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { token: "t" })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    const blob = new Blob(["x"], { type: "image/png" });
    await expect(api("/api/me/avatar", { method: "PUT", rawBody: blob, contentType: "image/png" })).resolves.toBeUndefined();
    expect(fetchMock.mock.calls[1][1].headers["Content-Type"]).toBe("image/png");
    expect(fetchMock.mock.calls[1][1].body).toBe(blob);
  });

  it("messageOf: friendly rate-limit text, plain errors, unknown values", () => {
    expect(messageOf(new ApiError(429, "rate_limited", "x", undefined, undefined, 120))).toMatch(/2 min/);
    expect(messageOf(new Error("boom"))).toBe("boom");
    expect(messageOf(42)).toBe("Something went wrong.");
  });
});
