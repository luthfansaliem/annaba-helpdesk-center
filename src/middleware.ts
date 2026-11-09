import { NextResponse, type NextRequest } from "next/server";

/**
 * Script policy for pages (CSP with a per-request nonce).
 * Next.js reads the nonce from the request's CSP header and stamps it on its own inline scripts; the theme script in the root layout reads `x-nonce`.
 * CSP_MODE=enforce blocks violations; anything else (default) only reports them (Content-Security-Policy-Report-Only), so a new policy can be watched on staging before it is enforced.
 */
export function buildCsp(nonce: string, dev: boolean): string {
  return [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${dev ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self' data:",
    "connect-src 'self'",
    "worker-src 'self'",
    "manifest-src 'self'",
    "frame-src 'none'",
    "frame-ancestors 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "object-src 'none'",
    "report-uri /csp-report",
  ].join("; ");
}

function pageResponse(req: NextRequest): NextResponse {
  const nonce = btoa(crypto.randomUUID());
  const csp = buildCsp(nonce, process.env.NODE_ENV !== "production");
  const reportOnly = process.env.CSP_MODE !== "enforce";
  const name = reportOnly ? "content-security-policy-report-only" : "content-security-policy";
  const headers = new Headers(req.headers);
  headers.set("x-nonce", nonce);
  headers.set(name, csp);
  const res = NextResponse.next({ request: { headers } });
  res.headers.set(name, csp);
  return res;
}

/**
 * Same-origin proxy to the .NET API (ADR-013).
 * - Keeps the session cookie first-party (no CORS, no third-party cookie problems).
 * - Adds the shared proxy key so the API can refuse any request that did not come through this app.
 * - Always overwrites X-Internal-Proxy-Key (a client-supplied value is never forwarded).
 * Disabled (404) when API_URL is not configured, so the prototype keeps working without a backend.
 */
export function middleware(req: NextRequest) {
  if (!req.nextUrl.pathname.startsWith("/api/")) return pageResponse(req);

  const api = process.env.API_URL;
  if (!api) return new NextResponse(JSON.stringify({ error: { code: "not_found", message: "API not configured." } }), { status: 404, headers: { "content-type": "application/json" } });

  const headers = new Headers(req.headers);
  headers.set("x-internal-proxy-key", process.env.API_PROXY_KEY ?? "");
  const url = new URL(req.nextUrl.pathname + req.nextUrl.search, api);
  return NextResponse.rewrite(url, { request: { headers } });
}

export const config = {
  // Pages and the API proxy; static assets, images, the service worker and the report endpoint are skipped.
  matcher: [{ source: "/((?!_next/static|_next/image|favicon.ico|sw.js|csp-report|.*\\.(?:png|jpg|jpeg|svg|ico|webp|json|txt|js|css|woff2?)$).*)" }],
};
