/**
 * The ticket engine of the service-desk seed. A ticket is described as a spec (who asks, for what, where, and a list of steps with the minutes between them);
 * the engine plays it through the public API as the right people, remembers a "mark" (real time of the call -> the moment it is supposed to have happened) after every call,
 * and in `finish()` moves every row those calls created (events, notifications, approvals, ratings, work orders, costs, files and stock movements; NOT the audit log, which is append-only)
 * to those moments with SQL, and sets the ticket's SLA columns exactly as the API would have stored them had it all happened then (see 05-servicedesk-time.mjs).
 */
import { CAST, rows as listOf, sleep } from "./lib.mjs";
import { MIN, HOUR, DAY, WIB, Cal, dayStart, atWib, iso, slaRefresh, crossing, workdayBack, q, ts } from "./05-servicedesk-time.mjs";

const PASCAL = { submitted: "Submitted", assigned: "Assigned", inprogress: "InProgress", onhold: "OnHold", resolved: "Resolved", closed: "Closed", cancelled: "Cancelled", reopened: "Reopened", pendingapproval: "PendingApproval", rejected: "Rejected" };
const NAME_TO_KEY = Object.fromEntries(Object.entries(CAST).map(([k, v]) => [v.name, k]));
const uuidList = (ids) => ids.map((i) => `'${i}'`).join(",") || "'00000000-0000-0000-0000-000000000000'";

export class Desk {
  constructor(d, { now = new Date() } = {}) {
    this.d = d; this.now = +now; this.limit = this.now - 2 * MIN; this.tickets = []; this.failed = []; this.clients = new Map();
    this.strict = process.env.SD_STRICT === "1";
  }

  async as(key) { if (!this.clients.has(key)) this.clients.set(key, await this.d.as(key)); return this.clients.get(key); }
  async uid(key) { return this.d.userId(key); }
  /** Call an endpoint with a few retries when the SLA worker touched the row at the same moment (409). */
  async call(who, method, path, body, what) {
    const c = await this.as(who);
    for (let i = 0; ; i++) {
      const r = method === "get" ? await c.get(path) : method === "del" ? await c.del(path, body) : await c[method](path, body);
      if (r.status === 409 && i < 3) { await sleep(300); continue; }
      if (!r.ok) throw new Error(`${what ?? `${method} ${path}`} as ${who} failed (${r.status}): ${(r.text ?? "").slice(0, 300)}`);
      return r.data;
    }
  }

  // ------------------------------------------------------------------------------------------------------------------ the world (what other modules left)
  async init() {
    const d = this.d, hendra = await this.as("hendra");
    this.cals = {};
    for (const c of listOf(d.must(await hendra.get("/api/calendars"), "calendars"))) { const cal = new Cal(c); cal.apiId = c.id; cal.isDefault = c.isDefault; this.cals[c.name] = cal; if (c.isDefault) this.cals.office = cal; }
    this.cals.always ??= Object.values(this.cals).find((c) => c.alwaysOpen);
    this.office = this.cals.office;
    const services = listOf(d.must(await hendra.get("/api/catalog/services"), "services"));
    this.svc = new Map(services.map((s) => [s.name, s]));
    const places = listOf(d.must(await (await this.as("rizky")).get("/api/portal/locations"), "places"));
    this.places = places;
    const L = d.ctx.loc ?? {};
    this.loc = (key) => { const v = L[key]; if (v?.id) return v; const p = places.find((x) => x.name === key || x.name === v?.name); return p ?? places.find((x) => x.name.toLowerCase().includes(String(key).toLowerCase())); };
    this.users = {}; for (const k of Object.keys(CAST)) this.users[k] = await d.userId(k);
    // what the other modules made: assets, vendors, stock, checklists, cost centres (all optional)
    const soft = async (what, f, dflt) => { try { return await f(); } catch (e) { d.log(`    ! ${what}: ${String(e.message).slice(0, 120)}`); return dflt; } };
    this.assets = await soft("assets", async () => listOf(d.must(await hendra.get("/api/assets?pageSize=100"), "assets")), []);
    this.vendors = await soft("vendors", async () => listOf(d.must(await hendra.get("/api/vendors?pageSize=100"), "vendors")), []);
    this.items = await soft("inventory", async () => listOf(d.must(await hendra.get("/api/inventory/items"), "items")), []);
    this.templates = await soft("checklists", async () => listOf(d.must(await hendra.get("/api/checklist-templates"), "templates")), []);
    this.stockLeft = new Map(this.items.map((i) => [i.id, (i.stock ?? []).map((s) => ({ ...s }))]));
  }
  /** An asset of the register that matches and stands in the place the ticket is about (or inside it); none when there is no such asset, rather than a wrong one. */
  pickAsset(re, loc) {
    if (!re) return null;
    const here = (a) => !loc || (a.locationPath ?? "").split(" › ").some((p) => p === loc.name) || (loc.path && (a.locationPath ?? "").startsWith(loc.path));
    const ok = this.assets.filter((a) => a.status !== "retired" && here(a) && re.test(`${a.tag} ${a.name} ${a.typeName ?? ""}`));
    return ok.find((a) => loc && (a.locationPath ?? "").endsWith(loc.name)) ?? ok[0] ?? null;
  }
  pickVendor(re) { return re ? this.vendors.find((v) => re.test(`${v.name} ${v.services ?? ""}`)) : null; }
  pickTemplate(re) { return re ? this.templates.find((t) => re.test(t.name)) : null; }
  pickItem(re, qty = 1) {
    for (const it of this.items) {
      if (!re.test(`${it.sku} ${it.name} ${it.category ?? ""}`)) continue;
      const at = (this.stockLeft.get(it.id) ?? []).find((s) => s.quantity >= qty + 1);
      if (at) { at.quantity -= qty; return { item: it, locationId: at.locationId }; }
    }
    return null;
  }

  // ------------------------------------------------------------------------------------------------------------------ one ticket
  /** Plays a spec. Never throws unless SD_STRICT=1: a ticket that cannot be made is reported at the end. */
  async ticket(spec) {
    try { return await this._ticket(spec); }
    catch (e) { this.failed.push(`${spec.id ?? spec.subject}: ${e.message}`); this.d.log(`    ! ticket "${spec.subject}": ${String(e.message).slice(0, 300)}`); if (this.strict) throw e; return null; }
  }

  /** When the ticket starts (and on which calendar its clock runs); the engine plays tickets oldest first so numbers follow dates. */
  async plan(spec) {
    const s = this.svc.get(spec.svc); if (!s) throw new Error(`service ${spec.svc} not found`);
    const loc = this.loc(spec.loc); if (!loc) throw new Error(`location ${spec.loc} not found`);
    const cal = /server/i.test(loc.name) && this.cals.always ? this.cals.always : /Gudang/i.test(loc.name) ? this.cals["Warehouse (Mon–Sat)"] ?? this.office : this.office;
    const prio = spec.prio ?? s.defaultPriority;
    const stepsMin = (spec.steps ?? []).reduce((a, x) => a + (x.m ?? 0), 0) + 30;
    let startMs, calUsed = cal;
    if (spec.use != null) {
      const pv = await this.call("hendra", "post", "/api/sla/preview", { serviceId: s.id, locationId: loc.id, priority: prio }, "sla preview");
      calUsed = pv.calendarName && this.cals[pv.calendarName] ? this.cals[pv.calendarName] : cal;
      startMs = calUsed.sub(this.now - 3 * MIN, spec.use * pv.resolutionMinutes + (spec.waited ?? 0));
    } else if (spec.endAgo != null) {
      startMs = cal.sub(cal.prevClosing(this.now - spec.endAgo * MIN), stepsMin - 30);
    } else if (spec.ago != null) startMs = this.now - spec.ago * MIN;
    else {
      const [hh, mm] = spec.at ?? [9, 0];
      startMs = workdayBack(this.office, this.now, spec.wd ?? 5) + hh * HOUR + mm * MIN;
      if (startMs + stepsMin * MIN > this.limit) startMs = this.limit - stepsMin * MIN;
    }
    spec._start = startMs; spec._cal = calUsed; spec._svc = s; spec._loc = loc; spec._prio = prio;
    return startMs;
  }

  async _ticket(spec) {
    const d = this.d; if (spec._start == null) await this.plan(spec);
    const s = spec._svc, loc = spec._loc, prio = spec._prio, startMs = spec._start;
    const creatorKey = spec.creator ?? spec.by, behalf = creatorKey !== spec.by;
    const tcal = spec._cal;
    const T = { spec, key: spec.id, marks: [], facts: { holds: [], reopens: [], resolved: null, closed: null, cancelled: null, responded: null, assigned: null, released: null, approval: false }, cal: tcal, by: spec.by, log: [] };
    let cursor = startMs;
    const nextTime = (m, off) => { let t = cursor + (m ?? 5) * MIN; if (!off && !tcal.isOpen(t)) t = tcal.nextOpening(t) + Math.floor(Math.random() * 12) * MIN; if (t >= this.limit) t = this.limit - 1 * MIN; if (t <= cursor) t = cursor + MIN; cursor = t; return t; };
    T.created = startMs;
    const act = async (at, fn) => { await sleep(3); const r = await fn(); T.marks.push({ real: Date.now(), target: at }); T.last = at; return r; };
    T.act = act;

    // --- file it
    const fields = d.must(await (await this.as(spec.by)).get(`/api/portal/services/${s.id}`), "service form").fields ?? [];
    const answers = this.answersFor(fields, spec, loc);
    const body = { subject: spec.subject, description: spec.desc ?? null, answers, locationId: loc.id };
    if (behalf) body.onBehalfOfUserId = this.users[spec.by];
    let t = await act(startMs, () => this.call(creatorKey, "post", `/api/portal/services/${s.id}/requests`, body, `filing "${spec.subject}"`));
    T.id = t.id; T.number = t.number; T.status = t.status; T.assignee = NAME_TO_KEY[t.assigneeName] ?? null; T.teamId = t.teamId; T.version = t.version;
    T.facts.assigned = t.assigneeName ? startMs : null;
    if (t.status === "pendingapproval") T.facts.approval = true;
    spec.created = startMs; this.tickets.push(T);

    // --- priority and asset (set by staff, a few minutes after)
    const asset = this.pickAsset(spec.asset, loc);
    if (prio !== t.priority || asset) {
      const at = nextTime(spec.pm ?? 6);
      t = await act(at, () => this.call(spec.prioBy ?? "putri", "patch", `/api/tickets/${T.id}`, { subject: spec.subject, priority: prio, locationId: loc.id, assetId: asset?.id ?? null }, "priority/asset"));
      T.asset = asset?.id ?? null;
    }
    // --- the person who should have it (the routing rule may have chosen someone else)
    if (spec.to !== undefined && spec.to !== T.assignee && !(spec.to === "queue" && !T.assignee) && t.status !== "pendingapproval") await this.assign(T, nextTime(spec.am ?? 4), spec.to, spec.assignBy ?? "putri");

    for (const st of spec.steps ?? []) {
      try { await this.step(T, st, nextTime, act); }
      catch (e) { throw new Error(`step ${st.k} of "${spec.subject}": ${e.message}`); }
    }
    return T;
  }

  answersFor(fields, spec, loc) {
    const a = {};
    for (const f of fields) {
      if (spec.ans && f.key in spec.ans) {
        let v = spec.ans[f.key];
        if (f.type === "location" && typeof v === "string" && !/^[0-9a-f-]{36}$/i.test(v)) v = this.loc(v)?.id ?? loc.id;
        if (f.type === "user" && typeof v === "string" && this.users[v]) v = this.users[v];
        a[f.key] = v; continue;
      }
      if (f.type === "location") { if (f.required) a[f.key] = loc.id; continue; }
      if (!f.required) continue;
      switch (f.type) {
        case "text": a[f.key] = spec.ans?.text ?? "-"; break;
        case "long_text": a[f.key] = spec.desc ?? "-"; break;
        case "number": a[f.key] = f.min ?? 1; break;
        case "date": a[f.key] = new Date(this.now + 3 * DAY).toISOString().slice(0, 10); break;
        case "datetime": a[f.key] = new Date(this.now + 2 * DAY).toISOString().replace(/\.\d+Z$/, "Z"); break;
        case "yes_no": a[f.key] = false; break;
        case "select": a[f.key] = f.options?.[0]?.value; break;
        case "user": a[f.key] = this.users[spec.by]; break;
        default: break;
      }
    }
    return a;
  }

  async assign(T, at, to, by = "putri") {
    const detail = await this.call("putri", "get", `/api/tickets/${T.id}`);
    const body = to === "queue" ? { assigneeId: null, teamId: detail.teamId } : { assigneeId: this.users[to], teamId: detail.teamId };
    await T.act(at, () => this.call(by, "post", `/api/tickets/${T.id}/assign`, body, `assign to ${to}`));
    T.assignee = to === "queue" ? null : to; T.facts.assigned = to === "queue" ? null : at;
  }

  async step(T, st, nextTime, act) {
    const spec = T.spec, tk = () => nextTime(st.m, st.off);
    const worker = () => st.by ?? T.assignee ?? "putri";
    switch (st.k) {
      case "assign": await this.assign(T, tk(), st.to, st.by ?? "putri"); break;
      case "start": { const at = tk(); await act(at, () => this.call(worker(), "post", `/api/tickets/${T.id}/transition`, { action: "start" }, "start")); if (!T.assignee) { T.assignee = worker(); T.facts.assigned = at; } T.facts.responded ??= at; T.status = "inprogress"; break; }
      case "note": { const at = tk(); await act(at, () => this.call(st.by ?? T.assignee ?? "putri", "post", `/api/tickets/${T.id}/comments`, { body: st.text, internal: true }, "note")); break; }
      case "reply": { const at = tk(); await act(at, () => this.call(st.by ?? T.assignee ?? "putri", "post", `/api/tickets/${T.id}/comments`, { body: st.text, internal: false }, "reply")); T.facts.responded ??= at; break; }
      case "ask": { const at = tk(); await act(at, () => this.call(st.by ?? spec.by, "post", `/api/tickets/${T.id}/comments`, { body: st.text, internal: false }, "requester comment")); break; }
      case "hold": { const at = tk(); await act(at, () => this.call(worker(), "post", `/api/tickets/${T.id}/transition`, { action: "hold", reason: st.reason }, "hold")); T.facts.holds.push({ from: at, to: null }); T.status = "onhold"; break; }
      case "resume": { const at = tk(); await act(at, () => this.call(worker(), "post", `/api/tickets/${T.id}/transition`, { action: "resume" }, "resume")); const h = T.facts.holds.at(-1); if (h && !h.to) h.to = at; T.status = "inprogress"; break; }
      case "resolve": { const at = tk(); await act(at, () => this.call(worker(), "post", `/api/tickets/${T.id}/transition`, { action: "resolve", resolutionCode: st.code ?? "fixed", resolutionNote: st.note }, "resolve")); T.facts.resolved = at; T.facts.responded ??= at; T.status = "resolved"; break; }
      case "confirm": {
        const at = tk(); await act(at, () => this.call(st.by ?? spec.by, "post", `/api/tickets/${T.id}/transition`, { action: "confirm" }, "confirm")); T.facts.closed = at; T.status = "closed";
        if (st.rate) await this.rate(T, nextTime(st.rate[2] ?? 6, true), st.rate[0], st.rate[1]);
        break;
      }
      case "rate": await this.rate(T, tk(), st.score, st.comment); break;
      case "reopen": { const at = tk(); await act(at, () => this.call(st.by ?? spec.by, "post", `/api/tickets/${T.id}/transition`, { action: "reopen", reason: st.reason }, "reopen")); T.facts.reopens.push(at); T.facts.resolved = null; T.facts.closed = null; T.facts.holds = []; T.facts.approval = false; T.status = "reopened"; break; }
      case "cancel": { const at = tk(); await act(at, () => this.call(st.by ?? spec.by, "post", `/api/tickets/${T.id}/transition`, { action: "cancel", reason: st.reason }, "cancel")); T.facts.cancelled = at; T.status = "cancelled"; break; }
      case "photo": {
        const at = tk(); const { png } = this.d;
        await act(at, async () => { const r = await (await this.as(st.by ?? spec.by)).upload(`/api/tickets/${T.id}/attachments`, [{ name: st.name ?? "foto-kondisi.png", type: "image/png", data: png(...(st.size ?? [320, 200]), st.rgb ?? [92, 130, 170]) }]); if (!r.ok) throw new Error(`photo: ${r.status} ${r.text?.slice(0, 160)}`); return r.data; });
        break;
      }
      case "approve": case "reject": {
        const at = tk(); const mine = await this.call(st.by, "get", "/api/approvals/mine");
        const a = (Array.isArray(mine) ? mine : listOf(mine)).find((x) => x.ticketId === T.id && x.status === "pending");
        if (!a) throw new Error(`no pending approval of ${st.by}`);
        await act(at, () => this.call(st.by, "post", `/api/approvals/${a.id}/decide`, { approve: st.k === "approve", comment: st.comment ?? null }, "decide"));
        const det = await this.call("putri", "get", `/api/tickets/${T.id}`);
        T.status = det.status; T.assignee = NAME_TO_KEY[det.assigneeName] ?? null; T.facts.assigned = det.assigneeName ? at : null;
        if (det.status !== "pendingapproval") { T.facts.released = at; T.facts.approvalDone = true; }
        break;
      }
      case "legalhold": { const at = tk(); await this.call("admin", "post", "/api/auth/reauth", { password: this.d.PASSWORD }, "re-authentication"); await act(at, () => this.call("admin", "put", `/api/tickets/${T.id}/legal-hold`, { reason: st.reason }, "legal hold")); break; }
      case "setprio": { const at = tk(); const detail = await this.call("putri", "get", `/api/tickets/${T.id}`); await act(at, () => this.call(st.by ?? "putri", "patch", `/api/tickets/${T.id}`, { subject: detail.subject, priority: st.prio, locationId: detail.locationId, assetId: T.asset ?? null }, "priority")); break; }
      case "wo": await this.workOrder(T, st, tk, act); break;
      case "wstart": case "wtick": case "wtime": case "wcost": case "wpart": case "wphoto": case "whold": case "wresume": case "wdone": case "wcancel": case "wedit":
        await this.woStep(T, st, nextTime, act); break;
      default: throw new Error(`unknown step ${st.k}`);
    }
  }

  async rate(T, at, score, comment) {
    await T.act(at, () => this.call(T.spec.by, "put", `/api/tickets/${T.id}/rating`, { score, comment: comment ?? null }, "rating"));
    T.rated = score;
  }

  // ------------------------------------------------------------------------------------------------------------------ work orders
  async workOrder(T, st, tk, act) {
    const vendor = st.vendor ? (this.pickVendor(st.vendor) ?? null) : null;
    const tpl = st.tpl ? this.pickTemplate(st.tpl) : null;
    const to = st.to === undefined ? null : st.to;
    const at = tk();
    const due = st.dueNow != null ? new Date(this.now + st.dueNow * MIN).toISOString() : st.due != null ? new Date(at + st.due * MIN).toISOString() : null;
    const body = {
      title: st.title, instructions: st.instr ?? null, assigneeId: to ? this.users[to] : null, teamId: st.team ?? null, dueAt: due, required: st.required ?? true, requireAfterPhoto: !!st.after,
      tasks: tpl ? [] : (st.tasks ?? []).map((x) => (typeof x === "string" ? { text: x, required: true } : { text: x[0], required: x[1] !== false })), templateId: tpl?.id ?? null,
      resolveTicket: st.resolve ?? null, vendorId: vendor?.id ?? null, purchaseOrder: vendor ? st.po ?? null : null,
    };
    const w = await act(at, () => this.call(st.by ?? "putri", "post", `/api/tickets/${T.id}/work-orders`, body, `work order "${st.title}"`));
    (T.wos ??= {})[st.ref ?? "w"] = { id: w.id, number: w.number, vendor, to, due: due ? +new Date(due) : null }; T.lastWo = st.ref ?? "w";
    return w;
  }

  async woStep(T, st, nextTime, act) {
    const ref = T.wos?.[st.ref ?? T.lastWo]; if (!ref) throw new Error(`no work order for ${st.k}`);
    const worker = st.by ?? (ref.vendor && /sejuk/i.test(ref.vendor.name) ? "joko" : ref.to ?? "putri");
    const tk = (m = st.m, off = st.off) => nextTime(m, off);
    const id = ref.id, p = `/api/work-orders/${id}`;
    switch (st.k) {
      case "wstart": { const at = tk(); await act(at, () => this.call(worker, "post", `${p}/action`, { action: "start" }, "wo start")); break; }
      case "wtick": {
        const det = await this.call(worker, "get", p);
        const todo = det.tasks.filter((x) => !x.doneAt); const n = st.n === "all" || st.n == null ? todo.length : Math.min(st.n, todo.length);
        for (let i = 0; i < n; i++) { const at = tk(i === 0 ? st.m : st.gap ?? 6); await act(at, () => this.call(worker, "post", `${p}/tasks/${todo[i].id}`, { done: true, note: st.notes?.[i] ?? null }, "tick")); }
        break;
      }
      case "wtime": { const at = tk(); const date = new Date(Math.min(at, this.now) + WIB).toISOString().slice(0, 10); const dd = new Date(this.now).toISOString().slice(0, 10); await act(at, () => this.call(st.by ?? worker, "post", `${p}/time`, { minutes: st.min, date: date > dd ? dd : date, note: st.note ?? null }, "wo time")); break; }
      case "wcost": { const at = tk(); await act(at, () => this.call(st.by ?? "putri", "post", `${p}/costs`, { kind: st.kind, description: st.desc, quantity: st.qty ?? 1, unitCost: st.unit }, "wo cost")); break; }
      case "wpart": {
        const at = tk(); const pick = st.item ? this.pickItem(st.item, st.qty ?? 1) : null;
        const manual = () => act(at, () => this.call(st.by ?? "putri", "post", `${p}/costs`, { kind: "part", description: st.fallback[0], quantity: st.fallback[2] ?? st.qty ?? 1, unitCost: st.fallback[1] }, "wo part (manual)"));
        if (pick) {
          try { await act(at, () => this.call(st.by ?? "putri", "post", `${p}/parts`, { itemId: pick.item.id, locationId: pick.locationId, quantity: st.qty ?? 1 }, "wo part")); }
          catch (err) { if (!st.fallback) throw err; this.d.log(`    ! part from stock refused (${String(err.message).slice(0, 90)}), manual line instead`); await manual(); }
        } else if (st.fallback) await manual();
        break;
      }
      case "wphoto": { const at = tk(); await act(at, async () => { const r = await (await this.as(worker)).upload(`${p}/files?stage=${st.stage}`, [{ name: st.name ?? `foto-${st.stage}.png`, type: "image/png", data: this.d.png(320, 200, st.rgb ?? (st.stage === "after" ? [88, 150, 110] : [150, 110, 88])) }]); if (!r.ok) throw new Error(`wo photo ${r.status} ${r.text?.slice(0, 160)}`); return r.data; }); break; }
      case "whold": { const at = tk(); await act(at, () => this.call(worker, "post", `${p}/action`, { action: "hold", reason: st.reason }, "wo hold")); break; }
      case "wresume": { const at = tk(); await act(at, () => this.call(worker, "post", `${p}/action`, { action: "resume" }, "wo resume")); break; }
      case "wdone": {
        const at = tk(); await act(at, () => this.call(worker, "post", `${p}/action`, { action: "complete", note: st.note }, "wo complete"));
        const det = await this.call("putri", "get", `/api/tickets/${T.id}`);
        if (["resolved", "closed"].includes(det.status) && !T.facts.resolved) { T.facts.resolved = at; T.facts.responded ??= at; T.status = det.status; }
        break;
      }
      case "wcancel": { const at = tk(); await act(at, () => this.call(st.by ?? "putri", "post", `${p}/action`, { action: "cancel", reason: st.reason }, "wo cancel")); break; }
      default: throw new Error(`unknown work order step ${st.k}`);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------ the backdating pass
  /** Moves everything the calls created to the moments the specs gave them, and writes the SLA columns. */
  async finish() {
    const d = this.d, all = this.tickets.filter((t) => t.marks.length), now = this.now;
    if (!all.length) return { sla: {} };
    const ids = all.map((t) => t.id); const byId = new Map(all.map((t) => [t.id, t]));
    const sqlIn = uuidList(ids);
    // what the API left: final status and assignment of each ticket, and the policy numbers it copied
    const cur = new Map();
    for (const line of d.sql(`select id, status, sla_policy_id is not null, sla_first_response_minutes, sla_resolution_minutes, sla_escalate1percent, sla_escalate2percent, sla_calendar_id, assignee_id, team_id, asset_id, location_id from tickets where id in (${sqlIn})`).split("\n").filter(Boolean)) {
      const [id, status, hasPol, fr, res, e1, e2, cal, assignee, team, asset, loc] = line.split("|"); cur.set(id, { status, hasPol: hasPol === "t", fr: +fr, res: +res, e1: +e1, e2: +e2, cal, assignee, team, asset, loc });
    }
    const epoch = (c) => `(extract(epoch from ${c})*1000)::bigint`;
    const rowsOut = d.sql(`
      select 'ev', e.id, e.ticket_id, ${epoch("e.created_at")}, '' from ticket_events e where e.ticket_id in (${sqlIn}) and e.kind <> 'Escalation'
      union all select 'nt', n.id, n.ticket_id, ${epoch("n.created_at")}, '' from notifications n where n.ticket_id in (${sqlIn})
      union all select 'ap', a.id, a.ticket_id, ${epoch("a.created_at")}, 'created_at' from ticket_approvals a where a.ticket_id in (${sqlIn})
      union all select 'ap', a.id, a.ticket_id, ${epoch("a.decided_at")}, 'decided_at' from ticket_approvals a where a.ticket_id in (${sqlIn}) and a.decided_at is not null
      union all select 'rt', r.id, r.ticket_id, ${epoch("r.created_at")}, 'created_at' from ticket_ratings r where r.ticket_id in (${sqlIn})
      union all select 'rt', r.id, r.ticket_id, ${epoch("r.updated_at")}, 'updated_at' from ticket_ratings r where r.ticket_id in (${sqlIn})
      union all select 'at', a.id, a.ticket_id, ${epoch("a.created_at")}, '' from attachments a where a.ticket_id in (${sqlIn})
      union all select 'wo', w.id, w.ticket_id, ${epoch("w.created_at")}, 'created_at' from work_orders w where w.ticket_id in (${sqlIn})
      union all select 'wo', w.id, w.ticket_id, ${epoch("w.updated_at")}, 'updated_at' from work_orders w where w.ticket_id in (${sqlIn})
      union all select 'wo', w.id, w.ticket_id, ${epoch("w.started_at")}, 'started_at' from work_orders w where w.ticket_id in (${sqlIn}) and w.started_at is not null
      union all select 'wo', w.id, w.ticket_id, ${epoch("w.completed_at")}, 'completed_at' from work_orders w where w.ticket_id in (${sqlIn}) and w.completed_at is not null
      union all select 'wk', k.id, w.ticket_id, ${epoch("k.done_at")}, '' from work_order_tasks k join work_orders w on w.id = k.work_order_id where w.ticket_id in (${sqlIn}) and k.done_at is not null
      union all select 'wt', k.id, w.ticket_id, ${epoch("k.created_at")}, '' from work_order_times k join work_orders w on w.id = k.work_order_id where w.ticket_id in (${sqlIn})
      union all select 'wc', k.id, w.ticket_id, ${epoch("k.created_at")}, '' from work_order_costs k join work_orders w on w.id = k.work_order_id where w.ticket_id in (${sqlIn})
      union all select 'sm', m.id, w.ticket_id, ${epoch("m.at")}, '' from stock_movements m join work_orders w on w.id = m.group_id where w.ticket_id in (${sqlIn})
      `);
    const items = new Map(); // ticket id -> [{tbl,id,col,real}]
    const add = (tid, o) => { if (!byId.has(tid)) return; (items.get(tid) ?? items.set(tid, []).get(tid)).push(o); };
    for (const line of rowsOut.split("\n").filter(Boolean)) {
      const [tbl, id, tid, ms, col] = line.split("|"); add(tid, { tbl, id, col, real: +ms });
    }

    const out = []; // sql statements
    const setCol = { ev: ["ticket_events", "created_at"], nt: ["notifications", "created_at"], at: ["attachments", "created_at"], wk: ["work_order_tasks", "done_at"], wt: ["work_order_times", "created_at"], wc: ["work_order_costs", "created_at"], sm: ["stock_movements", "at"] };
    const tableOf = { ap: "ticket_approvals", rt: "ticket_ratings", wo: "work_orders" };
    const lit = (v) => (/^\d+$/.test(v) ? v : `'${v}'`);
    for (const [tid, list] of items) {
      const T = byId.get(tid), marks = T.marks.slice().sort((a, b) => a.real - b.real);
      const slot = marks.map(() => []);
      for (const it of list) { let k = marks.findIndex((m) => m.real >= it.real); if (k < 0) k = marks.length - 1; slot[k].push(it); }
      slot.forEach((its, k) => {
        its.sort((a, b) => a.real - b.real);
        const gap = k + 1 < marks.length ? marks[k + 1].target - marks[k].target : 20 * MIN;
        const step = Math.max(200, Math.min(2500, Math.floor(gap / (its.length + 2))));
        its.forEach((it, i) => {
          const t = marks[k].target + i * step;
          if (setCol[it.tbl]) { const [tb, col] = setCol[it.tbl]; out.push(`update ${tb} set ${col} = ${ts(t)} where id = ${lit(it.id)};`); }
          else out.push(`update ${tableOf[it.tbl]} set ${it.col} = ${ts(t)} where id = '${it.id}';`);
        });
      });
    }

    // --- the ticket rows: timestamps and the SLA, as the API would have stored them
    const sla = { OnTrack: 0, AtRisk: 0, Breached: 0, Paused: 0, Met: 0, None: 0 }; const escal = [];
    for (const T of all) {
      const c = cur.get(T.id); if (!c) continue;
      const cal = (c.cal && Object.values(this.cals).find((x) => x.apiId === c.cal)) || T.cal || this.office;
      const f = T.facts, start = f.reopens.length ? f.reopens.at(-1) : T.created;
      let paused = 0, pausedAt = null;
      if (f.approval && !f.reopens.length) { if (f.released) paused += Math.round(cal.between(T.created, f.released)); else if (c.status === "PendingApproval") pausedAt = T.created; }
      for (const h of f.holds) { if (h.to) paused += Math.round(cal.between(h.from, h.to)); else pausedAt = h.from; }
      const status = c.status;
      const r = slaRefresh({ status, hasPol: c.hasPol, start, frMin: c.fr, resMin: c.res, e1: c.e1, e2: c.e2, pausedMinutes: paused, pausedAt, responded: f.responded, resolved: ["Resolved", "Closed"].includes(status) ? f.resolved : null }, cal, now);
      sla[r.state]++; T.sla = { ...r, paused, pausedAt, start, status, calName: cal.name, c };
      const lastMark = Math.max(...T.marks.map((m) => m.target));
      out.push(`update tickets set created_at = ${ts(T.created)}, updated_at = ${ts(lastMark)}, assigned_at = ${ts(c.assignee ? f.assigned ?? T.created : null)}, first_responded_at = ${ts(f.responded)}, ` +
        `resolved_at = ${ts(["Resolved", "Closed"].includes(status) ? f.resolved : null)}, closed_at = ${ts(status === "Closed" ? f.closed : null)}, cancelled_at = ${ts(["Cancelled", "Rejected"].includes(status) ? f.cancelled ?? lastMark : null)}, ` +
        `sla_start_at = ${ts(c.hasPol ? start : null)}, sla_paused_at = ${ts(pausedAt)}, sla_paused_minutes = ${paused}, first_response_due_at = ${ts(r.frDue)}, resolution_due_at = ${ts(r.resDue)}, ` +
        `sla_state = ${q(r.state)}, sla_escalation_level = ${r.level}, sla_breached_at = ${ts(r.breachedAt)} where id = '${T.id}';`);
      // escalation lines on the internal timeline: when a threshold was crossed while the ticket was still running
      if (c.hasPol && r.level >= 1) {
        const end = ["Resolved", "Closed"].includes(status) && f.resolved ? f.resolved : pausedAt ?? now, cap = r.breachedAt ?? Infinity;
        for (const [lvl, pct, text] of [[1, c.e1, `SLA warning: ${c.e1}% of the time allowed has been used.`], [2, c.e2, `SLA at risk: ${c.e2}% of the time allowed has been used.`]]) {
          if (r.level < lvl) continue; const at = crossing(cal, start, c.res, pct, paused); if (at < end && at < cap) escal.push({ T, lvl, text, at });
        }
        if (r.level >= 3 && r.breachedAt) escal.push({ T, lvl: 3, at: r.breachedAt, text: r.frDue != null && r.breachedAt === r.frDue && !(f.responded && f.responded <= r.frDue) ? "SLA breached: nobody has responded within the first-response target." : "SLA breached: the resolution target has been missed." });
      }
    }
    this.escalations = escal;
    // statements in one transaction: shifted rows, escalation lines, read/unread state of notifications, the SLA notifications of what is still open
    const readCut = now - 16 * HOUR;
    const script = `begin;\n${out.join("\n")}\n${this.escalationSql(escal)}\n` +
      `update notifications n set read_at = n.created_at + interval '2 hours' where n.ticket_id in (${sqlIn}) and n.read_at is null and n.created_at < ${ts(readCut)} and not (n.kind = 'approval.needed' and exists (select 1 from ticket_approvals a where a.ticket_id = n.ticket_id and a.approver_id = n.user_id and a.status = 'Pending')) and not (n.kind = 'ticket.resolved' and exists (select 1 from tickets t where t.id = n.ticket_id and t.status = 'Resolved'));\n` +
      `${this.slaNotificationsSql()}\ncommit;\n`;
    d.sql(script);
    return { sla, statements: out.length };
  }

  escalationSql(escal) {
    const lines = [];
    for (const e of escal) {
      lines.push(`insert into ticket_events (id, tenant_id, ticket_id, kind, actor_id, actor_label, internal, body, from_status, to_status, created_at) select gen_random_uuid(), tenant_id, id, 'Escalation', null, 'System', true, ${q(e.text)}, null, null, ${ts(e.at)} from tickets where id = '${e.T.id}';`);
    }
    return lines.join("\n");
  }

  /** Notifications of the ticket's people about SLA trouble on tickets that are still open (unread, at the moment the threshold was crossed). */
  slaNotificationsSql() {
    const out = [];
    for (const e of this.escalations ?? []) {
      if (e.lvl < 2) continue; const T = e.T; if (e.lvl !== T.sla?.level) continue; if (!T.sla || ["Resolved", "Closed", "Cancelled", "Rejected"].includes(T.sla.status)) continue;
      const kind = e.lvl >= 3 ? "sla.breached" : "sla.at_risk", title = `${e.lvl >= 3 ? "SLA breached" : "SLA at risk"}: ${T.number}`;
      const people = new Set([this.users.putri, this.users.hendra]); const asg = T.sla.c.assignee; if (asg) people.add(asg);
      for (const u of people) out.push(`insert into notifications (id, tenant_id, user_id, kind, title, body, link, ticket_id, created_at, read_at) select gen_random_uuid(), tenant_id, '${u}', ${q(kind)}, ${q(title)}, ${q(T.spec.subject)}, '/tickets/' || id, id, ${ts(e.at)}, null from tickets where id = '${T.id}';`);
    }
    return out.join("\n");
  }
}
