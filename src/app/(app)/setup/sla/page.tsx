"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { ConditionFields, describeConditions, moved, useLookups, type ConditionValue, type Lookups } from "@/components/tickets/conditions";
import { get, patch, post } from "@/lib/api/client";
import type { Priority, SaveSlaPolicy, SlaPolicyItem, SlaPreview } from "@/lib/api/types";
import { fmtMinutes } from "@/lib/duration";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const blank: SaveSlaPolicy = { name: "", serviceId: null, categoryId: null, locationId: null, priority: null, calendarId: null, firstResponseMinutes: 60, resolutionMinutes: 480, escalate1Percent: 50, escalate2Percent: 80 };

function PolicyDrawer({ open, onClose, onDone, editing, lookups }: { open: boolean; onClose: () => void; onDone: () => void; editing: SlaPolicyItem | null; lookups: Lookups }) {
  const run = useRun();
  const [f, setF] = useState<SaveSlaPolicy>(blank); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) {
    setKey(current);
    setF(editing ? { name: editing.name, serviceId: editing.serviceId, categoryId: editing.categoryId, locationId: editing.locationId, priority: editing.priority, calendarId: editing.calendarId, firstResponseMinutes: editing.firstResponseMinutes, resolutionMinutes: editing.resolutionMinutes, escalate1Percent: editing.escalate1Percent, escalate2Percent: editing.escalate2Percent, version: editing.version } : blank);
    setErrs({}); setErr("");
  }
  const set = <K extends keyof SaveSlaPolicy>(k: K, v: SaveSlaPolicy[K]) => setF((x) => ({ ...x, [k]: v }));
  const num = (v: string) => Number(v) || 0;
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => (editing ? patch(`/api/sla/policies/${editing.id}`, f) : post("/api/sla/policies", f)), editing ? t("Policy saved.") : t("Policy created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  const cond: ConditionValue = { serviceId: f.serviceId, categoryId: f.categoryId, locationId: f.locationId, priority: f.priority };
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit SLA policy") : t("New SLA policy")} description={t("Targets are counted in working time of the calendar. The first matching policy wins.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="sla-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="sla-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="sl-name" required error={errs.name}><TextInput id="sl-name" data-autofocus value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, fontSize: 12, padding: 0 }}>{t("Applies to")}</legend>
          <ConditionFields value={cond} onChange={(c) => setF((x) => ({ ...x, ...c }))} lookups={lookups} errors={errs} idPrefix="sl" />
        </fieldset>
        <Field label={t("Calendar")} htmlFor="sl-cal" error={errs.calendarId}>
          <SelectInput id="sl-cal" value={f.calendarId ?? ""} onChange={(e) => set("calendarId", e.target.value || null)} options={[{ value: "", label: t("The place's calendar, else the default") }, ...lookups.calendars.filter((c) => !c.archived).map((c) => ({ value: c.id, label: c.name }))]} />
        </Field>
        <div className="row" style={{ gap: 12 }}>
          <Field label={t("First response (minutes)")} htmlFor="sl-fr" required error={errs.firstResponseMinutes}><TextInput id="sl-fr" type="number" min={1} value={f.firstResponseMinutes} onChange={(e) => set("firstResponseMinutes", num(e.target.value))} /></Field>
          <Field label={t("Resolution (minutes)")} htmlFor="sl-res" required error={errs.resolutionMinutes}><TextInput id="sl-res" type="number" min={1} value={f.resolutionMinutes} onChange={(e) => set("resolutionMinutes", num(e.target.value))} /></Field>
        </div>
        <p><small>{t("That is {first} to respond and {resolve} to resolve.", { first: fmtMinutes(f.firstResponseMinutes), resolve: fmtMinutes(f.resolutionMinutes) })}</small></p>
        <div className="row" style={{ gap: 12 }}>
          <Field label={t("Warn at (% of resolution time)")} htmlFor="sl-e1" error={errs.escalate1Percent}><TextInput id="sl-e1" type="number" min={1} max={98} value={f.escalate1Percent} onChange={(e) => set("escalate1Percent", num(e.target.value))} /></Field>
          <Field label={t("At risk from (%)")} htmlFor="sl-e2" error={errs.escalate2Percent}><TextInput id="sl-e2" type="number" min={2} max={99} value={f.escalate2Percent} onChange={(e) => set("escalate2Percent", num(e.target.value))} /></Field>
        </div>
      </form>
    </Overlay>
  );
}

function Tester({ lookups }: { lookups: Lookups }) {
  const run = useRun();
  const [serviceId, setServiceId] = useState(""); const [locationId, setLocationId] = useState(""); const [priority, setPriority] = useState<Priority | "">("medium");
  const [result, setResult] = useState<SlaPreview | null>(null); const [err, setErr] = useState("");
  async function check(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setResult(null);
    const r = await run(() => post<SlaPreview>("/api/sla/preview", { serviceId: serviceId || null, locationId: locationId || null, priority: priority || null }), undefined, { silent: true });
    if (r.ok) setResult(r.data); else setErr(r.fields?.serviceId ?? r.error);
  }
  return (
    <Panel title={t("Test a ticket")} description={t("See which policy a request would get.")}>
      <form className="stack" noValidate onSubmit={check}>
        <div className="filters">
          <Field label={t("Service")} htmlFor="st-svc"><SelectInput id="st-svc" value={serviceId} onChange={(e) => setServiceId(e.target.value)} placeholder={t("Choose a service")} options={lookups.services.filter((s) => !s.archived).map((s) => ({ value: s.id, label: `${s.categoryName} › ${s.name}` }))} /></Field>
          <Field label={t("Place")} htmlFor="st-loc"><SelectInput id="st-loc" value={locationId} onChange={(e) => setLocationId(e.target.value)} options={[{ value: "", label: t("Not given") }, ...lookups.locations.filter((l) => !l.archived).map((l) => ({ value: l.id, label: l.name }))]} /></Field>
          <Field label={t("Priority")} htmlFor="st-prio"><SelectInput id="st-prio" value={priority} onChange={(e) => setPriority(e.target.value as Priority)} options={[{ value: "urgent", label: t("Urgent") }, { value: "high", label: t("High") }, { value: "medium", label: t("Medium") }, { value: "low", label: t("Low") }]} /></Field>
          <Button type="submit">{t("Check")}</Button>
        </div>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {result && <div role="status">{result.policyId
          ? <p><b>{result.policyName}</b> — {t("respond in {first}, resolve in {resolve}", { first: fmtMinutes(result.firstResponseMinutes), resolve: fmtMinutes(result.resolutionMinutes) })}{result.calendarName ? ` · ${result.calendarName}` : ""}</p>
          : <p>{t("No policy matches: this ticket would have no SLA.")}</p>}</div>}
      </form>
    </Panel>
  );
}

function Policies() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const lookups = useLookups();
  const q = useQuery(() => get<SlaPolicyItem[]>("/api/sla/policies", { includeArchived: archived }), [archived]);
  const [drawer, setDrawer] = useState<{ open: boolean; row: SlaPolicyItem | null }>({ open: false, row: null });
  const live = (q.data ?? []).filter((p) => !p.archived).map((p) => p.id);
  const move = async (id: string, by: -1 | 1) => { const next = moved(live, id, by); if (next && (await run(() => post("/api/sla/policies/reorder", { ids: next }), t("Order saved."))).ok) q.reload(); };
  const act = async (p: SlaPolicyItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/sla/policies/${p.id}/${what}`), what === "archive" ? t("Policy archived.") : t("Policy restored."))).ok) q.reload(); };

  const cols: Column<SlaPolicyItem>[] = [
    { key: "o", header: t("Order"), cell: (p) => (p.archived ? "—" : live.indexOf(p.id) + 1) },
    { key: "n", header: t("Policy"), cell: (p) => <><b>{p.name}</b><br /><small>{describeConditions(p)}</small></> },
    { key: "c", header: t("Calendar"), className: "hide-sm", cell: (p) => p.calendarName ?? <small>{t("From the place / default")}</small> },
    { key: "f", header: t("First response"), cell: (p) => fmtMinutes(p.firstResponseMinutes) },
    { key: "r", header: t("Resolution"), cell: (p) => fmtMinutes(p.resolutionMinutes) },
    { key: "e", header: t("Escalation"), className: "hide-sm", cell: (p) => `${p.escalate1Percent}% / ${p.escalate2Percent}%` },
    { key: "s", header: t("Status"), cell: (p) => <Badge tone={p.archived ? "gray" : "green"}>{p.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: (p) => <Can perm="sla.manage"><span className="row" style={{ gap: 6 }}>
      {!p.archived && <IconButton label={`${t("Move up")} ${p.name}`} disabled={live.indexOf(p.id) === 0} onClick={() => void move(p.id, -1)}><ArrowUp size={14} /></IconButton>}
      {!p.archived && <IconButton label={`${t("Move down")} ${p.name}`} disabled={live.indexOf(p.id) === live.length - 1} onClick={() => void move(p.id, 1)}><ArrowDown size={14} /></IconButton>}
      {!p.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${p.name}`} onClick={() => setDrawer({ open: true, row: p })}>{t("Edit")}</Button>}
      <Button size="sm" variant="outline" aria-label={`${p.archived ? t("Restore") : t("Archive")} ${p.name}`} onClick={() => void act(p, p.archived ? "restore" : "archive")}>{p.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("SLA Policies")} description={t("How fast each kind of request must be answered and resolved. The first policy that matches is used.")}
        actions={<Can perm="sla.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New policy")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("SLA policies")} columns={cols} rows={q.data ?? []} rowKey={(p) => p.id} emptyMessage={t("No policies yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <Tester lookups={lookups} />
      <PolicyDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} lookups={lookups} />
    </>
  );
}

export default function SlaPage() { return <Require perm="sla.read"><Policies /></Require>; }
