/** @type {import('next').NextConfig} */
const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
  { key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" },
  // Static part of the policy (framing, plugins, base, forms). The script policy with a nonce is added per request in src/middleware.ts.
  { key: "Content-Security-Policy", value: "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'" },
];

const nextConfig = {
  poweredByHeader: false,
  // Container image only (Dockerfile sets NEXT_OUTPUT); local dev, tests and Vercel keep the default output.
  ...(process.env.NEXT_OUTPUT === "standalone" ? { output: "standalone" } : {}),
  async headers() {
    return [
      { source: "/:path*", headers: securityHeaders },
      // The service worker must never be served from a stale HTTP cache, or users can get stuck on an old policy.
      { source: "/sw.js", headers: [
        { key: "Cache-Control", value: "no-cache, no-store, must-revalidate" },
        { key: "Service-Worker-Allowed", value: "/" },
        { key: "Content-Type", value: "application/javascript; charset=utf-8" },
      ] },
    ];
  },
};
export default nextConfig;
