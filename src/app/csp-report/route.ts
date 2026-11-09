/** Receives CSP violation reports and writes one line each to the server log (visible in Coolify). Never stores or echoes the body. */
export async function POST(req: Request) {
  try {
    const text = (await req.text()).slice(0, 2000);
    console.warn("[csp-violation]", text.replace(/\s+/g, " "));
  } catch { /* reports are best-effort */ }
  return new Response(null, { status: 204 });
}
