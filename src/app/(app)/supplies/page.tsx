"use client";

import { useState } from "react";
import { CheckCircle2, Clock, PackageCheck, PackageOpen, Plus, Settings2, ShoppingBasket, Wallet } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Bars, ChartCard, PALETTE } from "@/components/insights/charts";
import { get, post, put } from "@/lib/api/client";
import type { PagedSupplies, PortalLocation, SupplyCatalogItem, SupplyRequest, SupplySettings, SupplyStatus, SupplySummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime, fmtMoney } from "@/lib/format";
import { t } from "@/i18n";

const statusLabel = (s: SupplyStatus) => ({ pending: t("Waiting for approval"), open: t("To be issued"), partly_fulfilled: t("Partly issued"), fulfilled: t("Issued"), rejected: t("Not approved"), cancelled: t("Cancelled"), closed: t("Closed") })[s];
const tone: Record<SupplyStatus, BadgeTone> = { pending: "orange", open: "blue", partly_fulfilled: "orange", fulfilled: "green", rejected: "red", cancelled: "gray", closed: "gray" };
const places = () => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]);

/** The cart: pick items from the catalogue with what is available at the chosen place, then send. */
function NewRequest({ open, settings, onClose, onDone }: { open: boolean; settings: SupplySettings | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const locs = useQuery(() => (open ? places() : Promise.resolve([] as PortalLocation[])), [open]);
  const [loc, setLoc] = useState(""); const [q, setQ] = useState(""); const [qty, setQty] = useState<Record<string, string>>({}); const [note, setNote] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [seed, setSeed] = useState(false);
  if (open !== seed) { setSeed(open); setLoc(settings?.defaultLocationId ?? ""); setQ(""); setQty({}); setNote(""); setErrs({}); setErr(""); }
  const cat = useQuery(() => (open ? get<SupplyCatalogItem[]>("/api/supplies/catalog", { locationId: loc || undefined, q: q || undefined }) : Promise.resolve([] as SupplyCatalogItem[])), [open, loc, q]);
  const items = cat.data ?? [];
  const chosen = Object.entries(qty).filter(([, v]) => Number(v) > 0);
  const known = new Map(items.map((i) => [i.id, i]));
  const estimate = chosen.reduce((s, [id, v]) => s + Number(v) * (known.get(id)?.unitCost ?? 0), 0);
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const r = await run(() => post<SupplyRequest>("/api/supplies/requests", { locationId: loc || null, note: note || null, lines: chosen.map(([itemId, v]) => ({ itemId, quantity: Number(v) })) }), t("Request sent."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Ask for supplies")} description={t("Choose the items and how many. You are told when they are ready to collect.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="sr-form" disabled={chosen.length === 0}>{t("Send request")}</Button></>}>
      <form id="sr-form" className="stack" noValidate onSubmit={save}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Collect from")} htmlFor="sr-place" required error={errs.locationId}>
          <SelectInput id="sr-place" value={loc} onChange={(e) => setLoc(e.target.value)} options={[{ value: "", label: t("Choose a place") }, ...(locs.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} />
        </Field>
        <Field label={t("Search items")} htmlFor="sr-q"><TextInput id="sr-q" data-autofocus value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("Name or code")} /></Field>
        {errs.lines && <div className="form-alert err" role="alert">{errs.lines}</div>}
        {items.length === 0 ? <p className="tab-placeholder">{t("No items found.")}</p> : (
          <ul className="req-list" data-testid="supply-catalog">
            {items.map((i) => (
              <li key={i.id}>
                <div><b>{i.name}</b><br /><small className="muted">{i.sku} · {loc ? t("{n} {unit} available", { n: i.available, unit: i.unit }) : i.unit}{i.unitCost !== null ? ` · ${fmtMoney(i.unitCost)}` : ""}</small></div>
                <TextInput aria-label={t("Quantity {name}", { name: i.name })} type="number" min={0} max={10000} inputMode="numeric" style={{ width: 90 }} value={qty[i.id] ?? ""} onChange={(e) => setQty((x) => ({ ...x, [i.id]: e.target.value }))} />
              </li>
            ))}
          </ul>
        )}
        <Field label={t("Note")} htmlFor="sr-note" error={errs.note}><TextInput id="sr-note" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t("What it is for")} /></Field>
        <p className="muted" data-testid="sr-estimate">
          {t("{n} items · about {cost}", { n: chosen.length, cost: fmtMoney(estimate) })}
          {settings?.approvalAbove != null && estimate >= settings.approvalAbove ? ` · ${t("needs your manager's approval")}` : ""}
        </p>
      </form>
    </Overlay>
  );
}

/** Issuing: per line, how many to hand over now (prefilled with what is outstanding and in stock). */
function Fulfil({ req, onClose, onDone }: { req: SupplyRequest | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [qty, setQty] = useState<Record<string, string>>({}); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [seed, setSeed] = useState<string | null>(null);
  const key = req ? `${req.id}:${req.version}` : null;
  if (key !== seed) { setSeed(key); setQty(Object.fromEntries((req?.lines ?? []).map((l) => [l.itemId, String(Math.max(0, Math.min(l.backOrder, l.available)))]))); setErrs({}); setErr(""); }
  async function go() {
    if (!req) return;
    const lines = Object.fromEntries(Object.entries(qty).filter(([, v]) => Number(v) > 0).map(([k, v]) => [k, Number(v)]));
    const r = await run(() => post<SupplyRequest>(`/api/supplies/requests/${req.id}/fulfil`, { lines }), t("Issued. The requester is told."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={!!req} onClose={onClose} title={req ? t("Issue {number}", { number: req.number }) : ""} description={req ? `${req.requester ?? ""} · ${req.place}` : undefined}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" onClick={go}>{t("Issue")}</Button></>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {errs.lines && <div className="form-alert err" role="alert">{errs.lines}</div>}
        <ul className="req-list">
          {(req?.lines ?? []).filter((l) => l.backOrder > 0).map((l) => (
            <li key={l.itemId}>
              <div><b>{l.name}</b><br /><small className="muted">{t("{n} outstanding · {m} in stock", { n: l.backOrder, m: l.available })} · {l.unit}</small>{errs[`lines.${l.itemId}`] && <><br /><small className="field-error">{errs[`lines.${l.itemId}`]}</small></>}</div>
              <TextInput aria-label={t("Issue now {name}", { name: l.name })} type="number" min={0} max={l.backOrder} inputMode="numeric" style={{ width: 90 }} value={qty[l.itemId] ?? ""} onChange={(e) => setQty((x) => ({ ...x, [l.itemId]: e.target.value }))} />
            </li>
          ))}
        </ul>
        <p className="muted">{t("What is not issued now stays on back-order.")}</p>
      </div>
    </Overlay>
  );
}

/** Approve or reject (manager), or close the rest (desk); both ask for a note when saying no. */
function Decide({ item, onClose, onDone }: { item: { r: SupplyRequest; action: "approve" | "reject" | "close" | "cancel" } | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [note, setNote] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({}); const [seed, setSeed] = useState<string | null>(null);
  const key = item ? `${item.r.id}:${item.action}` : null;
  if (key !== seed) { setSeed(key); setNote(""); setErrs({}); }
  async function go() {
    if (!item) return;
    const { r, action } = item;
    const call = action === "close" ? () => post(`/api/supplies/requests/${r.id}/close`, { note: note || null })
      : action === "cancel" ? () => post(`/api/supplies/requests/${r.id}/cancel`)
        : () => post(`/api/supplies/requests/${r.id}/decide`, { approve: action === "approve", note: note || null });
    const res = await run(call, action === "approve" ? t("Approved.") : action === "cancel" ? t("Request cancelled.") : action === "close" ? t("Closed.") : t("Not approved."), { silent: false });
    if (!res.ok) { setErrs(res.fields ?? {}); return; }
    onClose(); onDone();
  }
  const title = !item ? "" : item.action === "approve" ? t("Approve {number}?", { number: item.r.number }) : item.action === "reject" ? t("Reject {number}?", { number: item.r.number })
    : item.action === "cancel" ? t("Cancel {number}?", { number: item.r.number }) : t("Close {number}?", { number: item.r.number });
  return (
    <Overlay open={!!item} onClose={onClose} title={title} description={item ? `${item.r.requester ?? ""} · ${fmtMoney(item.r.estimatedCost)}` : undefined}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant={item?.action === "approve" ? "primary" : "danger"} onClick={go}>{t("Confirm")}</Button></>}>
      {item && item.action !== "cancel" && (
        <Field label={item.action === "approve" ? t("Note (optional)") : item.action === "close" ? t("Why the rest will not be issued") : t("Why not")} htmlFor="sd-note" required={item.action !== "approve"} error={errs.note}>
          <TextInput id="sd-note" data-autofocus maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
        </Field>
      )}
      {item?.action === "cancel" && <p>{t("Nothing was issued yet, so the request is simply withdrawn.")}</p>}
    </Overlay>
  );
}

function SettingsDialog({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const cur = useQuery(() => (open ? get<SupplySettings>("/api/supplies/settings") : Promise.resolve(null)), [open]);
  const locs = useQuery(() => (open ? places() : Promise.resolve([] as PortalLocation[])), [open]);
  const [limit, setLimit] = useState(""); const [loc, setLoc] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({}); const [seed, setSeed] = useState<SupplySettings | null>(null);
  if (cur.data && cur.data !== seed) { setSeed(cur.data); setLimit(cur.data.approvalAbove?.toString() ?? ""); setLoc(cur.data.defaultLocationId ?? ""); setErrs({}); }
  async function save() {
    const r = await run(() => put("/api/supplies/settings", { approvalAbove: limit === "" ? null : Number(limit), defaultLocationId: loc || null }), t("Saved."), { silent: false });
    if (!r.ok) { setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} title={t("Supply rules")} footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" onClick={save}>{t("Save")}</Button></>}>
      <div className="stack">
        <Field label={t("Manager approval from (amount)")} htmlFor="ss-limit" error={errs.approvalAbove}>
          <TextInput id="ss-limit" type="number" min={0} inputMode="numeric" value={limit} onChange={(e) => setLimit(e.target.value)} />
        </Field>
        <p className="muted">{t("Requests worth this much or more wait for the requester's manager (or, without one, for the supplies desk). Leave empty for never.")}</p>
        <Field label={t("Default place to collect from")} htmlFor="ss-place" error={errs.defaultLocationId}>
          <SelectInput id="ss-place" value={loc} onChange={(e) => setLoc(e.target.value)} options={[{ value: "", label: t("Not set") }, ...(locs.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} />
        </Field>
      </div>
    </Overlay>
  );
}

function RequestList({ rows, empty, onAct, onFulfil, testId }: { rows: SupplyRequest[]; empty: string; testId: string;
  onAct: (r: SupplyRequest, action: "approve" | "reject" | "close" | "cancel") => void; onFulfil: (r: SupplyRequest) => void }) {
  if (rows.length === 0) return <p className="tab-placeholder">{empty}</p>;
  return (
    <ul className="req-list" data-testid={testId}>
      {rows.map((r) => (
        <li key={r.id}>
          <div>
            <b>{r.number}</b>{r.requester ? ` · ${r.requester}` : ""} · {fmtMoney(r.estimatedCost)}
            <br /><small className="muted">{r.lines.map((l) => `${l.quantity} × ${l.name}${l.backOrder > 0 && l.issued > 0 ? ` (${t("{n} still to come", { n: l.backOrder })})` : ""}`).join(", ")}</small>
            <br /><small className="muted">{fmtDateTime(r.createdAt)} · {r.place}{r.approver && r.status === "pending" ? ` · ${t("waiting for {who}", { who: r.approver })}` : ""}{r.note ? ` · ${r.note}` : ""}{r.decisionNote ? ` · ${r.decisionNote}` : ""}</small>
          </div>
          <span className="row" style={{ gap: 6, flexWrap: "wrap", justifyContent: "flex-end" }}>
            <Badge tone={tone[r.status]}>{statusLabel(r.status)}</Badge>
            {r.canDecide && <><Button size="sm" variant="primary" onClick={() => onAct(r, "approve")}>{t("Approve")}</Button><Button size="sm" variant="danger-outline" onClick={() => onAct(r, "reject")}>{t("Reject")}</Button></>}
            {r.canFulfil && <Button size="sm" variant="primary" onClick={() => onFulfil(r)}><PackageCheck size={14} /> {t("Issue")}</Button>}
            {r.canFulfil && <Button size="sm" variant="danger-outline" onClick={() => onAct(r, "close")}>{t("Close")}</Button>}
            {r.canCancel && <Button size="sm" variant="danger-outline" onClick={() => onAct(r, "cancel")}>{t("Cancel")}</Button>}
          </span>
        </li>
      ))}
    </ul>
  );
}

/** INV-002. Everyone asks for supplies and follows their requests; managers approve their people's requests above the limit; the desk (supplies.fulfil) issues them. */
export default function SuppliesPage() {
  const { can } = useAuth();
  const desk = can("supplies.fulfil"); const rules = can("inventory.manage");
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const [view, setView] = useState<"active" | "">("active");
  const [asking, setAsking] = useState(false); const [config, setConfig] = useState(false);
  const [acting, setActing] = useState<{ r: SupplyRequest; action: "approve" | "reject" | "close" | "cancel" } | null>(null); const [issuing, setIssuing] = useState<SupplyRequest | null>(null);
  const settings = useQuery(() => get<SupplySettings>("/api/supplies/settings").catch(() => null), [tick]);
  const mine = useQuery(() => get<PagedSupplies>("/api/supplies/requests", { scope: "mine", status: view || undefined, pageSize: 50 }), [view, tick]);
  const approvals = useQuery(() => get<PagedSupplies>("/api/supplies/requests", { scope: "approvals", pageSize: 50 }), [tick]);
  const queue = useQuery(() => (desk ? get<PagedSupplies>("/api/supplies/requests", { scope: "desk", status: view || undefined, pageSize: 100 }) : Promise.resolve(null)), [desk, view, tick]);
  const sum = useQuery(() => (desk ? get<SupplySummary>("/api/supplies/summary") : Promise.resolve(null)), [desk, tick]);
  const s = sum.data; const waiting = approvals.data?.data ?? [];
  const act = (r: SupplyRequest, action: "approve" | "reject" | "close" | "cancel") => setActing({ r, action });
  return (
    <>
      <PageHeader title={t("Supplies")} description={desk ? t("Requests for office supplies and other stock: approve, issue from the store and follow back-orders.") : t("Ask for office supplies and other stock items, and follow your requests.")}
        actions={<>{rules && <Button onClick={() => setConfig(true)}><Settings2 size={16} /> {t("Rules")}</Button>}<Button variant="primary" onClick={() => setAsking(true)}><Plus size={16} /> {t("Ask for supplies")}</Button></>} />
      {s && (
        <KpiGrid label={t("Supplies at a glance")}>
          <KpiCard icon={<Clock size={18} />} label={t("Waiting for approval")} value={String(s.pending)} note={t("decided by managers")} />
          <KpiCard icon={<PackageOpen size={18} />} label={t("To be issued")} value={String(s.open)} note={t("{n} partly issued", { n: s.partlyFulfilled })} tone={s.open + s.partlyFulfilled > 0 ? "warn" : "good"} />
          <KpiCard icon={<CheckCircle2 size={18} />} label={t("Issued")} value={String(s.fulfilled30)} note={t("Last 30 days")} />
          <KpiCard icon={<Wallet size={18} />} label={t("Issued value")} value={fmtMoney(s.issued30Cost)} note={t("Last 30 days")} />
        </KpiGrid>
      )}
      {waiting.length > 0 && (
        <Panel title={t("Waiting for your approval")}>
          <RequestList rows={waiting} empty="" testId="supply-approvals" onAct={act} onFulfil={setIssuing} />
        </Panel>
      )}
      <div className="filters">
        <Field label={t("Show")} htmlFor="sr-view"><SelectInput id="sr-view" value={view} onChange={(e) => setView(e.target.value as "active" | "")} options={[{ value: "active", label: t("Still open") }, { value: "", label: t("Everything") }]} /></Field>
      </div>
      {desk && (
        <Panel title={<><ShoppingBasket size={16} aria-hidden /> {t("All requests")}</>}>
          <RequestList rows={queue.data?.data ?? []} empty={t("No requests to handle.")} testId="supply-desk" onAct={act} onFulfil={setIssuing} />
        </Panel>
      )}
      <Panel title={t("My requests")}>
        <RequestList rows={mine.data?.data ?? []} empty={view ? t("You have no open requests.") : t("You have not asked for supplies yet.")} testId="supply-mine" onAct={act} onFulfil={setIssuing} />
      </Panel>
      {s && s.topItems.length > 0 && (
        <div className="chart-grid">
          <ChartCard title={t("Most requested items")} sub={t("Issued, last 30 days")}><Bars items={s.topItems.map((x) => ({ label: x.label, value: x.count }))} color={PALETTE[1]} /></ChartCard>
        </div>
      )}
      <NewRequest open={asking} settings={settings.data ?? null} onClose={() => setAsking(false)} onDone={reload} />
      <Fulfil req={issuing} onClose={() => setIssuing(null)} onDone={reload} />
      <Decide item={acting} onClose={() => setActing(null)} onDone={reload} />
      <SettingsDialog open={config} onClose={() => setConfig(false)} onDone={reload} />
    </>
  );
}
