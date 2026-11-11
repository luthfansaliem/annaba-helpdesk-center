"use client";

import { useState } from "react";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Can, Require } from "@/components/page-guard";
import { del, get, post, put } from "@/lib/api/client";
import type { BudgetItem, BudgetList, CostCenterItem, CostSettings, PortalLocation, CategoryItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { fmtMoney } from "@/lib/format";
import { t } from "@/i18n";


function BudgetDrawer({ open, year, editing, onClose, onDone }: { open: boolean; year: number; editing: BudgetItem | null; onClose: () => void; onDone: (l: BudgetList) => void }) {
  const run = useRun();
  const [f, setF] = useState({ scope: "site", scopeId: "", year: String(year), amount: "", note: "" }); const [seed, setSeed] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) { setSeed(key); setErrs({}); setErr(""); setF(editing ? { scope: editing.scope, scopeId: editing.scopeId, year: String(editing.year), amount: String(editing.amount), note: editing.note ?? "" } : { scope: "site", scopeId: "", year: String(year), amount: "", note: "" }); }
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const centers = useQuery(() => (open ? get<CostCenterItem[]>("/api/costs/centers").catch(() => [] as CostCenterItem[]) : Promise.resolve([] as CostCenterItem[])), [open]);
  const cats = useQuery(() => (open ? get<CategoryItem[]>("/api/catalog/categories").catch(() => [] as CategoryItem[]) : Promise.resolve([] as CategoryItem[])), [open]);
  const targets = f.scope === "site" ? (places.data ?? []).filter((l) => l.type === "site").map((l) => ({ value: l.id, label: l.path }))
    : f.scope === "cost_center" ? (centers.data ?? []).filter((c) => !c.archived).map((c) => ({ value: c.id, label: `${c.code} · ${c.name}` }))
      : (cats.data ?? []).map((c) => ({ value: c.id, label: c.name }));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { scope: f.scope, scopeId: f.scopeId || null, year: Number(f.year), amount: f.amount === "" ? 0 : Number(f.amount), note: f.note || null, version: editing?.version };
    const r = await run(() => (editing ? put<BudgetList>(`/api/costs/budgets/${editing.id}`, body) : post<BudgetList>("/api/costs/budgets", body)), t("Budget saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit budget") : t("New budget")} description={t("The year's costs in this scope are compared with the amount; 80% and 100% alert the people who manage budgets.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="bg-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="bg-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("For")} htmlFor="bg-scope" error={errs.scope}><SelectInput id="bg-scope" value={f.scope} onChange={(e) => setF((x) => ({ ...x, scope: e.target.value, scopeId: "" }))} options={[{ value: "site", label: t("A site") }, { value: "cost_center", label: t("A cost centre") }, { value: "category", label: t("A service category") }]} /></Field>
        <Field label={t("Which")} htmlFor="bg-target" required error={errs.scopeId}><SelectInput id="bg-target" value={f.scopeId} onChange={(e) => setF((x) => ({ ...x, scopeId: e.target.value }))} options={[{ value: "", label: t("Choose…") }, ...targets]} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Year")} htmlFor="bg-year" required error={errs.year}><TextInput id="bg-year" type="number" min={2000} max={2100} style={{ width: 110 }} value={f.year} onChange={(e) => setF((x) => ({ ...x, year: e.target.value }))} /></Field>
          <Field label={t("Amount")} htmlFor="bg-amount" required error={errs.amount}><TextInput id="bg-amount" type="number" min={0} step="0.01" value={f.amount} onChange={(e) => setF((x) => ({ ...x, amount: e.target.value }))} /></Field>
        </div>
        <Field label={t("Note")} htmlFor="bg-note" error={errs.note}><TextInput id="bg-note" maxLength={300} value={f.note} onChange={(e) => setF((x) => ({ ...x, note: e.target.value }))} /></Field>
      </form>
    </Overlay>
  );
}

function SettingsPanel() {
  const run = useRun();
  const q = useQuery(() => get<CostSettings>("/api/costs/settings"), []);
  const [f, setF] = useState<{ currency: string; rate: string } | null>(null); const [errs, setErrs] = useState<Record<string, string>>({});
  const v = f ?? { currency: q.data?.currency ?? "IDR", rate: q.data?.labourRatePerHour?.toString() ?? "" };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setErrs({});
    const r = await run(() => put<CostSettings>("/api/costs/settings", { currency: v.currency, labourRatePerHour: v.rate === "" ? null : Number(v.rate) }), t("Settings saved."), { silent: true });
    if (!r.ok) { setErrs(r.fields ?? {}); return; }
    setF(null); q.setData(r.data);
  }
  return (
    <Panel title={t("Currency and labour rate")} description={t("Amounts are never converted; the currency is a label. With a labour rate, logged work-order time counts as cost.")}>
      <form className="row" style={{ gap: 12, flexWrap: "wrap", alignItems: "flex-end" }} noValidate onSubmit={save}>
        <Field label={t("Currency")} htmlFor="cs-cur" error={errs.currency}><TextInput id="cs-cur" maxLength={3} style={{ width: 90 }} value={v.currency} onChange={(e) => setF({ ...v, currency: e.target.value.toUpperCase() })} /></Field>
        <Field label={t("Labour rate per hour")} htmlFor="cs-rate" error={errs.labourRatePerHour}><TextInput id="cs-rate" type="number" min={0} step="0.01" placeholder={t("None")} value={v.rate} onChange={(e) => setF({ ...v, rate: e.target.value })} /></Field>
        <Button variant="primary" type="submit">{t("Save")}</Button>
      </form>
    </Panel>
  );
}

function Budgets() {
  const run = useRun();
  const [year, setYear] = useState(new Date().getFullYear()); const [drawer, setDrawer] = useState<{ open: boolean; row: BudgetItem | null }>({ open: false, row: null }); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const q = useQuery(() => get<BudgetList>("/api/costs/budgets", { year }), [year]);
  const scopeLabel = (s: BudgetItem["scope"]) => ({ site: t("Site"), cost_center: t("Cost centre"), category: t("Category") })[s];
  return (
    <>
      <PageHeader title={t("Budgets")} description={t("A year's budget per site, cost centre or service category, against the costs recorded that year.")}
        actions={<Can perm="budgets.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New budget")}</Button></Can>} />
      <div className="filters">
        <Field label={t("Year")} htmlFor="bl-year"><SelectInput id="bl-year" value={String(year)} onChange={(e) => setYear(Number(e.target.value))} options={[-2, -1, 0, 1].map((d) => { const y = new Date().getFullYear() + d; return { value: String(y), label: String(y) }; })} /></Field>
      </div>
      <QueryBoundary q={q}>{(l) => (
        <>
        {l.items.length > 0 && (() => {
          const amount = l.items.reduce((a, b) => a + b.amount, 0); const spent = l.items.reduce((a, b) => a + b.spent, 0);
          const used = amount > 0 ? Math.round((100 * spent) / amount) : 0; const over = l.items.filter((b) => b.state === "over").length; const near = l.items.filter((b) => b.state === "near").length;
          return (
            <KpiGrid label={t("Budgets at a glance")}>
              <KpiCard label={t("Budgeted")} value={fmtMoney(amount, l.currency)} note={t("{n} budgets in {year}", { n: l.items.length, year: l.year })} />
              <KpiCard label={t("Spent so far")} value={fmtMoney(spent, l.currency)} note={t("{p}% of the total", { p: used })} tone={used >= 100 ? "bad" : used >= 80 ? "warn" : "default"} />
              <KpiCard label={t("Near the limit")} value={String(near)} note={t("80% or more used")} tone={near > 0 ? "warn" : "default"} />
              <KpiCard label={t("Used up")} value={String(over)} note={t("100% or more used")} tone={over > 0 ? "bad" : "good"} />
            </KpiGrid>
          );
        })()}
        <Panel title={t("Budgets for {year}", { year: l.year })} description={t("Amounts in {currency}.", { currency: l.currency })}>
          {l.items.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No budgets for this year.")}</p>
            : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("Budgets")}>{l.items.map((b) => (
              <li key={b.id} data-testid="budget-row">
                <div className="row" style={{ gap: 8, justifyContent: "space-between", flexWrap: "wrap" }}>
                  <span><b>{b.scopeName}</b> <small className="sub">{scopeLabel(b.scope)}</small> {b.state === "over" ? <Badge tone="red">{t("Used up")}</Badge> : b.state === "near" ? <Badge tone="orange">{t("Near the limit")}</Badge> : <Badge tone="green">{t("On track")}</Badge>}
                    {b.note && <><br /><small className="sub">{b.note}</small></>}</span>
                  <span className="row" style={{ gap: 6 }}>
                    <span>{t("{spent} of {amount} ({percent}%)", { spent: fmtMoney(b.spent, l.currency), amount: fmtMoney(b.amount, l.currency), percent: b.percent })}</span>
                    {l.canManage && <>
                      <IconButton label={`${t("Edit")} ${b.scopeName}`} onClick={() => setDrawer({ open: true, row: b })}><Pencil size={14} /></IconButton>
                      <IconButton label={`${t("Remove")} ${b.scopeName}`} onClick={() => setConfirm({ title: t("Remove {what}?", { what: b.scopeName }), confirmLabel: t("Remove"), danger: true, body: t("This can't be undone."),
                        onConfirm: async () => { const r = await run(() => del(`/api/costs/budgets/${b.id}`), t("Removed.")); if (r.ok) q.reload(); } })}><Trash2 size={14} /></IconButton>
                    </>}
                  </span>
                </div>
                <div role="progressbar" aria-label={`${b.scopeName}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.min(100, b.percent)} style={{ height: 8, borderRadius: 4, background: "var(--surface-2, #e5e7eb)", marginTop: 6 }}>
                  <div style={{ height: 8, borderRadius: 4, width: `${Math.min(100, b.percent)}%`, background: b.state === "over" ? "#dc2626" : b.state === "near" ? "#d97706" : "#16a34a" }} />
                </div>
              </li>))}</ul>}
        </Panel>
        </>
      )}</QueryBoundary>
      <Can perm="budgets.manage"><SettingsPanel /></Can>
      <BudgetDrawer open={drawer.open} year={year} editing={drawer.row} onClose={() => setDrawer({ open: false, row: null })} onDone={(l) => { if (l.year === year) q.setData(l); else setYear(l.year); }} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </>
  );
}

export default function BudgetsPage() { return <Require perm="budgets.read"><Budgets /></Require>; }
