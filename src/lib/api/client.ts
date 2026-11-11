/**
 * Browser API client for the .NET backend (same-origin `/api/*`, proxied by src/middleware.ts).
 * - attaches the per-session CSRF token to every state-changing request (fetched lazily, refreshed once on rejection)
 * - turns the error envelope into ApiError
 * - routes session-level failures (401 / locked / MFA pending / MFA enrolment required) to registered handlers
 * - when the server says `reauth_required`, asks the UI for a password (+ code) and transparently retries once
 */
export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string, public fields?: Record<string, string>, public requestId?: string, public retryAfter?: number) { super(message); }
  get denied() { return this.status === 403 && this.code === "denied"; }
}

export type Handlers = {
  unauthenticated?: () => void; locked?: () => void; mfaRequired?: () => void; enrollmentRequired?: () => void;
  reauth?: () => Promise<boolean>;
};
let handlers: Handlers = {};
export const setApiHandlers = (h: Handlers) => { handlers = h; };

let csrf: string | null = null;
export const resetCsrf = () => { csrf = null; };
async function csrfToken(force = false): Promise<string> {
  if (!csrf || force) {
    const r = await fetch("/api/auth/csrf", { credentials: "same-origin", cache: "no-store" });
    if (!r.ok) throw await toError(r);
    csrf = (await r.json()).token as string;
  }
  return csrf;
}

async function toError(r: Response): Promise<ApiError> {
  let body: { error?: { code?: string; message?: string; fields?: Record<string, string>; requestId?: string } } = {};
  try { body = await r.json(); } catch { /* non-JSON (proxy/network error page) */ }
  const e = body.error;
  const retry = Number(r.headers.get("Retry-After")) || undefined;
  return new ApiError(r.status, e?.code ?? (r.status === 404 ? "not_found" : "error"), e?.message ?? `Request failed (${r.status}).`, e?.fields ?? undefined, e?.requestId, retry);
}

export type Opts = { method?: string; body?: unknown; rawBody?: BodyInit; contentType?: string; query?: Record<string, string | number | boolean | undefined | null>; signal?: AbortSignal; noRoute?: boolean; noReauth?: boolean; headers?: Record<string, string> };

function url(path: string, query?: Opts["query"]) {
  if (!query) return path;
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== null && v !== "") q.set(k, String(v));
  const s = q.toString();
  return s ? `${path}?${s}` : path;
}

export async function api<T = void>(path: string, opts: Opts = {}, retried = false): Promise<T> {
  const method = (opts.method ?? "GET").toUpperCase();
  const unsafe = method !== "GET" && method !== "HEAD";
  const headers: Record<string, string> = { ...(opts.headers ?? {}) };
  let body: BodyInit | undefined;
  if (opts.rawBody !== undefined) { body = opts.rawBody; if (opts.contentType) headers["Content-Type"] = opts.contentType; }
  else if (opts.body !== undefined) { body = JSON.stringify(opts.body); headers["Content-Type"] = "application/json"; }
  if (unsafe && path !== "/api/auth/login") headers["X-CSRF-Token"] = await csrfToken().catch(() => "");

  const res = await fetch(url(path, opts.query), { method, headers, body, credentials: "same-origin", cache: "no-store", signal: opts.signal });
  if (res.ok) {
    if (res.status === 204 || res.status === 202 && !res.headers.get("content-type")?.includes("json")) return undefined as T;
    return (await res.json()) as T;
  }
  const err = await toError(res);

  // a stale CSRF token (session rotated in another tab) is refreshed once
  if (err.status === 403 && /csrf/i.test(err.message) && !retried && unsafe) { await csrfToken(true).catch(() => {}); return api<T>(path, opts, true); }
  // recent-reauthentication gate: ask the user, then replay the original request exactly once
  if (err.code === "reauth_required" && !retried && !opts.noReauth && handlers.reauth) {
    const ok = await handlers.reauth();
    if (ok) return api<T>(path, opts, true);
  }
  if (!opts.noRoute) {
    if (err.status === 401 && err.code === "unauthenticated") handlers.unauthenticated?.();
    else if (err.status === 423 && err.code === "locked") handlers.locked?.();
    else if (err.code === "mfa_required") handlers.mfaRequired?.();
    else if (err.code === "mfa_enrollment_required") handlers.enrollmentRequired?.();
  }
  throw err;
}

export const get = <T,>(path: string, query?: Opts["query"], o?: Opts) => api<T>(path, { ...o, query });
export const post = <T = void,>(path: string, body?: unknown, o?: Opts) => api<T>(path, { ...o, method: "POST", body: body ?? {} });
export const patch = <T = void,>(path: string, body?: unknown, o?: Opts) => api<T>(path, { ...o, method: "PATCH", body });
export const put = <T = void,>(path: string, body?: unknown, o?: Opts) => api<T>(path, { ...o, method: "PUT", body });
export const del = <T = void,>(path: string, query?: Opts["query"], o?: Opts) => api<T>(path, { ...o, method: "DELETE", query });

/** Human-readable text for any thrown value (used by toasts and inline form errors). */
export function messageOf(e: unknown): string {
  if (e instanceof ApiError) return e.code === "rate_limited" ? `Too many attempts. Try again${e.retryAfter ? ` in ${Math.ceil(e.retryAfter / 60)} min` : " later"}.` : e.message;
  return e instanceof Error ? e.message : "Something went wrong.";
}
