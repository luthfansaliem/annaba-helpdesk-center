/**
 * Creates the demo cast on an API that does NOT run in Development (a real deployment): no dev flags, no dev password.
 * Each person is created the way the product does it for real: an invitation, accepted with a password. Invitations are inserted with a known token hash
 * (like Bootstrap does for the first administrator) so nothing is e-mailed and nobody needs a mailbox; the password is hashed by the API with the deployment's own pepper.
 * People that already exist (by e-mail) are left alone, so real accounts are never touched. Needs SQL access (d.sql) and DEMO_PASSWORD.
 */
import { createHash, randomBytes } from "node:crypto";
import { CAST } from "./lib.mjs";

const sha256 = (s) => createHash("sha256").update(s).digest("hex");
const ZERO = "00000000-0000-0000-0000-000000000000";

/** Same cast and profiles as api/src/AnnabaDigital.Api/DemoPersonas.cs (+ the four dev accounts). */
const PROFILE = {
  admin: { dept: "Management", title: "Platform Administrator", phone: "+62 812 1000 0100" },
  useradmin: { dept: "IT & Security", title: "User Administrator", phone: "+62 812 1000 0101" },
  auditor: { dept: "Internal Audit", title: "Security Auditor", phone: "+62 812 1000 0102" },
  hendra: { dept: "General Affairs", title: "GA Manager", phone: "+62 812 1000 0001", manager: "dimas" },
  putri: { dept: "General Affairs", title: "GA Supervisor", phone: "+62 812 1000 0002", manager: "hendra" },
  agus: { dept: "General Affairs", title: "Teknisi AC & listrik", phone: "+62 812 1000 0003", manager: "putri" },
  bayu: { dept: "General Affairs", title: "Teknisi umum & kebersihan", phone: "+62 812 1000 0004", manager: "putri" },
  sinta: { dept: "General Affairs", title: "Resepsionis", phone: "+62 812 1000 0005", manager: "putri" },
  joko: { dept: "PT Sejuk Abadi", title: "Teknisi lapangan", phone: "+62 812 1000 0006" },
  dimas: { dept: "Operations", title: "Head of Operations", phone: "+62 812 1000 0010" },
  rizky: { dept: "Operations", title: "Operations Officer", phone: "+62 812 1000 0014", manager: "dimas" },
  anita: { dept: "Human Resources", title: "HR Specialist", phone: "+62 812 1000 0011", manager: "dimas" },
  fajar: { dept: "Finance", title: "Finance Analyst", phone: "+62 812 1000 0012", manager: "dimas" },
  lina: { dept: "Marketing", title: "Marketing Executive", phone: "+62 812 1000 0013", manager: "dimas" },
};

async function accept(d, token, name, ip) {
  const res = await fetch(`${d.api}/api/invitations/accept`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Internal-Proxy-Key": d.key, "X-Forwarded-For": ip, Origin: d.origin, "User-Agent": "Mozilla/5.0 Chrome/126.0" },
    body: JSON.stringify({ token, name, password: d.PASSWORD }),
  });
  if (!res.ok) throw new Error(`accepting the invitation of ${name} failed (${res.status}): ${(await res.text()).slice(0, 300)}`);
}

export async function bootstrapCast(d) {
  if (!process.env.DEMO_PASSWORD) throw new Error("Set DEMO_PASSWORD to the password the demo accounts should get (not the well-known development one).");
  const have = new Set(d.sql("SELECT email FROM users").split("\n").map((s) => s.trim()).filter(Boolean));
  const roles = Object.fromEntries(d.sql("SELECT key, id FROM roles WHERE tenant_id = '00000000-0000-7000-8000-000000000001'").split("\n").filter(Boolean).map((l) => l.split("|")));
  let n = 0, ipN = 1;
  // super admin first: everything else (profiles, managers) is done as Sarah
  const order = ["admin", ...Object.keys(CAST).filter((k) => k !== "admin")];
  for (const key of order) {
    const p = CAST[key];
    if (have.has(p.email)) continue;
    const role = roles[p.role]; if (!role) throw new Error(`role ${p.role} not found`);
    const token = randomBytes(32).toString("base64url");
    d.sql(`INSERT INTO invitations (id, email, token_hash, invited_by, message, status, expires_at, created_at) VALUES (gen_random_uuid(), '${p.email}', '${sha256(token)}', '${ZERO}', 'Demo account', 'Pending', now() + interval '1 day', now());
           INSERT INTO invitation_roles (invitation_id, role_id) SELECT id, '${role}' FROM invitations WHERE token_hash = '${sha256(token)}';`);
    await accept(d, token, p.name, `10.99.0.${ipN++}`);
    n++;
  }
  d.log(`  cast: ${n} accounts created through invitations (${order.length - n} already existed)`);
  if (!n) return;

  // profiles, language, managers
  const admin = await d.as("admin");
  const ids = {};
  for (const key of order) ids[key] = await d.userId(key);
  for (const key of order) {
    const pr = PROFILE[key]; if (!pr) continue;
    const r = await admin.patch(`/api/users/${ids[key]}`, { department: pr.dept, jobTitle: pr.title, phone: pr.phone });
    if (!r.ok) d.log(`    ! profile of ${key}: ${r.status} ${r.text.slice(0, 160)}`);
  }
  for (const key of order) {
    const pr = PROFILE[key]; if (!pr?.manager) continue;
    let r = await admin.put(`/api/users/${ids[key]}/manager`, { managerId: ids[pr.manager] });
    if (r.status === 403 || r.status === 401) { await admin.post("/api/auth/reauth", { password: d.PASSWORD }); r = await admin.put(`/api/users/${ids[key]}/manager`, { managerId: ids[pr.manager] }); }
    if (!r.ok) d.log(`    ! manager of ${key}: ${r.status} ${r.text.slice(0, 160)}`);
  }
  for (const key of order) { const c = await d.as(key); const r = await c.patch("/api/me/preferences", { language: "id" }); if (!r.ok) d.log(`    ! language of ${key}: ${r.status}`); }
  d.log("  cast: profiles, departments, managers and Indonesian language set");
}
