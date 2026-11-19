/**
 * Shared toolkit for the product-demo seed (scripts/demo-seed.mjs and the modules in scripts/demo/).
 *
 * Everything is created through the public API, signed in as the demo cast (api/src/AnnabaDigital.Api/DemoPersonas.cs), so the data goes
 * through the same validation, audit trail and notifications as real use. It talks to the API directly (not through the Next proxy):
 * it sends the shared proxy key, the allowed Origin and the CSRF token itself.
 */
import { deflateSync } from "node:zlib";
import { spawnSync } from "node:child_process";

export const PASSWORD = process.env.DEMO_PASSWORD ?? "Passw0rd!demo";

/** The demo cast. `key` is what modules use in as("hendra"). Roles are the system roles of the helpdesk. */
export const CAST = {
  admin:   { email: "sarah.wijaya@annabadigital.io",    name: "Sarah Wijaya",    role: "super_admin" },
  useradmin: { email: "daniel.tan@annabadigital.io",    name: "Daniel Tan",      role: "user_admin" },
  auditor: { email: "maya.putri@annabadigital.io",      name: "Maya Putri",      role: "security_auditor" },
  hendra:  { email: "hendra.kusuma@annabadigital.io",   name: "Hendra Kusuma",   role: "ga_manager" },
  putri:   { email: "putri.anggraini@annabadigital.io", name: "Putri Anggraini", role: "ga_supervisor" },
  agus:    { email: "agus.setiawan@annabadigital.io",   name: "Agus Setiawan",   role: "ga_agent" },
  bayu:    { email: "bayu.nugroho@annabadigital.io",    name: "Bayu Nugroho",    role: "ga_agent" },
  sinta:   { email: "sinta.maharani@annabadigital.io",  name: "Sinta Maharani",  role: "receptionist" },
  joko:    { email: "joko.susilo@sejukabadi.co.id",     name: "Joko Susilo",     role: "vendor" },
  dimas:   { email: "dimas.prakoso@annabadigital.io",   name: "Dimas Prakoso",   role: "user" },   // head of Operations, manager of rizky/anita/fajar/lina
  rizky:   { email: "rizky.pratama@annabadigital.io",   name: "Rizky Pratama",   role: "user" },
  anita:   { email: "anita.rahmawati@annabadigital.io", name: "Anita Rahmawati", role: "user" },
  fajar:   { email: "fajar.ramadhan@annabadigital.io",  name: "Fajar Ramadhan",  role: "user" },
  lina:    { email: "lina.marlina@annabadigital.io",    name: "Lina Marlina",    role: "user" },
};

export const rows = (d) => (Array.isArray(d) ? d : d?.data ?? d?.items ?? d?.rows ?? []);
/** yyyy-mm-dd, `offset` days from today (local). */
export const day = (offset = 0) => { const d = new Date(); d.setDate(d.getDate() + offset); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };
/** A Date `offsetDays` from today at local hh:mm. Use .toISOString() for the API. */
export const at = (offsetDays, hh, mm = 0) => { const d = new Date(); d.setDate(d.getDate() + offsetDays); d.setHours(hh, mm, 0, 0); return d; };
/** The next `n`-th working day (Mon–Fri) as an offset in days from today: workday(0) is today if it is a weekday, else the next Monday. */
export const workday = (n = 0) => { let off = 0, seen = -1; for (;;) { const w = at(off, 9).getDay(); if (w !== 0 && w !== 6) { seen++; if (seen === n) return off; } off++; } };
/** The `n`-th previous working day (n=1 is the last one before today). */
export const prevWorkday = (n = 1) => { let off = 0, seen = 0; while (seen < n) { off--; const w = at(off, 9).getDay(); if (w !== 0 && w !== 6) seen++; } return off; };
export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** A small valid PNG (solid colour with a darker band), for photos and attachments. */
export function png(width = 160, height = 100, [r, g, b] = [90, 140, 200]) {
  const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (buf) => { let c = 0xffffffff; for (const x of buf) c = crcTable[(c ^ x) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4); ihdr[8] = 8; ihdr[9] = 2;
  const raw = Buffer.alloc((width * 3 + 1) * height);
  for (let y = 0; y < height; y++) { const o = y * (width * 3 + 1); raw[o] = 0; const band = y > height * 0.4 && y < height * 0.6; for (let x = 0; x < width; x++) { raw[o + 1 + x * 3] = band ? r >> 1 : r; raw[o + 2 + x * 3] = band ? g >> 1 : g; raw[o + 3 + x * 3] = band ? b >> 1 : b; } }
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

/** A minimal one-page PDF (for contracts, documents). */
export function pdf(title = "Demo document") {
  const text = `BT /F1 18 Tf 72 720 Td (${title.replace(/[()\\]/g, "")}) Tj ET`;
  const objs = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>", `<< /Length ${text.length} >>\nstream\n${text}\nendstream`, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"];
  let out = "%PDF-1.4\n"; const offs = [];
  objs.forEach((o, i) => { offs.push(out.length); out += `${i + 1} 0 obj\n${o}\nendobj\n`; });
  const xref = out.length; out += `xref\n0 ${objs.length + 1}\n0000000000 65535 f \n${offs.map((o) => String(o).padStart(10, "0") + " 00000 n ").join("\n")}\ntrailer\n<< /Size ${objs.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(out, "latin1");
}

/**
 * Connects to the API and returns the toolkit handed to every module.
 *   const d = connect({ api: "http://localhost:5080", key: "<proxy key>" });
 *   const hendra = await d.as("hendra");           // a signed-in client (cached)
 *   const r = await hendra.post("/api/...", {...}); // { ok, status, data, text }
 *   d.must(r, "what");                              // r.data, or throws with the server's message
 */
export function connect({ api = process.env.DEMO_API ?? "http://localhost:5080", key = process.env.DEMO_PROXY_KEY ?? "dev-proxy-key-0123456789abcdef0123456789abcdef", origin = process.env.DEMO_ORIGIN ?? "http://localhost:3000", quiet = false } = {}) {
  const clients = new Map();
  let ipSeq = 10;

  async function make(email, ip, ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/126.0 Safari/537.36") {
    const jar = new Map();
    const absorb = (res) => { for (const c of res.headers.getSetCookie?.() ?? []) { const [pair] = c.split(";"); const i = pair.indexOf("="); jar.set(pair.slice(0, i), pair.slice(i + 1)); } };
    const cookie = () => [...jar].map(([k, v]) => `${k}=${v}`).join("; ");
    const base = () => ({ "X-Internal-Proxy-Key": key, "X-Forwarded-For": ip, Origin: origin, Cookie: cookie(), "User-Agent": ua });
    let csrf = null;
    async function raw(method, path, { body, form, headers = {} } = {}) {
      const h = { ...base(), ...headers };
      if (method !== "GET" && method !== "HEAD") { csrf ??= (await (await fetch(`${api}/api/auth/csrf`, { headers: base() })).json()).token; h["X-CSRF-Token"] = csrf; }
      let payload;
      if (form) payload = form; else if (body !== undefined) { h["Content-Type"] = "application/json"; payload = JSON.stringify(body); }
      const res = await fetch(`${api}${path}`, { method, headers: h, body: payload });
      absorb(res);
      const buf = Buffer.from(await res.arrayBuffer()); const text = buf.toString("utf8"); let data = null; try { data = text ? JSON.parse(text) : null; } catch { /* not JSON */ }
      return { ok: res.ok, status: res.status, data, text, buf, headers: res.headers };
    }
    const login = await fetch(`${api}/api/auth/login`, { method: "POST", headers: { "Content-Type": "application/json", "X-Internal-Proxy-Key": key, "X-Forwarded-For": ip, Origin: origin, "User-Agent": ua }, body: JSON.stringify({ email, password: PASSWORD }) });
    absorb(login);
    if (!login.ok) throw new Error(`Could not sign in as ${email} (${login.status}): ${(await login.text()).slice(0, 200)}`);
    const me = await login.json().catch(() => null);
    return {
      email, ip, me: me?.user ?? me,
      get: (p) => raw("GET", p), post: (p, b) => raw("POST", p, { body: b ?? {} }), put: (p, b) => raw("PUT", p, { body: b }), patch: (p, b) => raw("PATCH", p, { body: b }), del: (p, b) => raw("DELETE", p, b === undefined ? {} : { body: b }),
      /** multipart upload: files = [{ name, type, data: Buffer, field? }], fields = { k: v } */
      upload: (p, files, fields = {}, method = "POST") => { const f = new FormData(); for (const [k, v] of Object.entries(fields)) f.append(k, String(v)); for (const x of files) f.append(x.field ?? "file", new Blob([x.data], { type: x.type }), x.name); return raw(method, p, { form: f }); },
      raw,
    };
  }

  const d = {
    api, key, origin,
    /** A signed-in client for a CAST key ("hendra") or any e-mail address. Each person gets their own client address so sign-in rate limits never mix. */
    async as(who) {
      const email = CAST[who]?.email ?? who;
      if (!clients.has(email)) clients.set(email, make(email, `10.77.${Math.floor(ipSeq / 250)}.${(ipSeq++ % 250) + 2}`));
      return clients.get(email);
    },
    /** A fresh sign-in (a "second device"), with its own user agent. */
    async asDevice(who, userAgent) {
      const email = CAST[who]?.email ?? who; const c = await make(email, `10.78.${Math.floor(ipSeq / 250)}.${(ipSeq++ % 250) + 2}`, userAgent);
      return c;
    },
    must(r, what) { if (!r.ok) throw new Error(`${what} failed (${r.status}): ${(r.text ?? "").slice(0, 400)}`); return r.data; },
    /** A step that may fail on an unusual setup without spoiling the rest: it logs and carries on. */
    async soft(what, fn) { try { return await fn(); } catch (e) { console.log(`    ! ${what}: skipped (${String(e.message).slice(0, 220)})`); return null; } },
    log: (...a) => { if (!quiet) console.log(...a); },
    /** People of the cast by key → their user id (cached lookup through the user list). */
    async userId(who) {
      const email = CAST[who]?.email ?? who;
      d._ids ??= new Map();
      if (!d._ids.has(email)) { const admin = await d.as("admin"); const list = rows(d.must(await admin.get(`/api/users?q=${encodeURIComponent(email)}&pageSize=20`), "finding a user")); const u = list.find((x) => x.email === email); if (!u) throw new Error(`user ${email} not found`); d._ids.set(email, u.id); }
      return d._ids.get(email);
    },
    /**
     * Runs SQL against the demo database (docker exec psql, tuples only). For BACKDATING rows the API stamped "now" so reports and trends have history, e.g.
     *   d.sql(`UPDATE tickets SET created_at = created_at - interval '12 days' WHERE id = '${id}'`).
     * Use it narrowly: shift timestamps of rows your module created. Create rows through the API, never by INSERT, unless the API cannot make them.
     * Returns stdout; throws on SQL errors. The database is DEMO_DB (default annaba_demo; the orchestrator sets it from --db).
     */
    sql(text) {
      // PGHOST set (PGUSER/PGPASSWORD/PGDATABASE too): talk to that PostgreSQL with the psql client (the seed runner container of a deployment); otherwise the local dev container
      const r = process.env.PGHOST
        ? spawnSync("psql", ["-v", "ON_ERROR_STOP=1", "-At", "-F", "|"], { input: text, encoding: "utf8" })
        : spawnSync("docker", ["exec", "-i", "annaba-digital-dev-db", "psql", "-U", "annaba_digital", "-d", process.env.DEMO_DB ?? "annaba_demo", "-v", "ON_ERROR_STOP=1", "-At", "-F", "|"], { input: text, encoding: "utf8" });
      if (r.status) throw new Error(`SQL failed: ${(r.stderr || "").slice(0, 400)}`);
      return r.stdout;
    },
    rows, day, at, workday, prevWorkday, sleep, png, pdf, CAST, PASSWORD,
  };
  return d;
}
