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
import { strategyLabel } from "@/components/tickets/status";
import { get, patch, post } from "@/lib/api/client";
import type { AssignmentRuleItem, Priority, RoutePreview, SaveAssignmentRule, Strategy } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const STRATEGIES: Strategy[] = ["none", "roundrobin", "leastloaded"];
const blank: SaveAssignmentRule = { name: "", serviceId: null, categoryId: null, locationId: null, priority: null, teamId: null, strategy: "none" };

function RuleDrawer({ open, onClose, onDone, editing, lookups }: { open: boolean; onClose: () => void; onDone: () => void; editing: AssignmentRuleItem | null; lookups: Lookups }) {
  const run = useRun();
  const [f, setF] = useState<SaveAssignmentRule>(blank); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) {
    setKey(current);
    setF(editing ? { name: editing.name, serviceId: editing.serviceId, categoryId: editing.categoryId, locationId: editing.locationId, priority: editing.priority, teamId: editing.teamId, strategy: editing.strategy, version: editing.version } : blank);
    setErrs({}); setErr("");
  }
  const set = <K extends keyof SaveAssignmentRule>(k: K, v: SaveAssignmentRule[K]) => setF((x) => ({ ...x, [k]: v }));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => (editing ? patch(`/api/assignment-rules/${editing.id}`, f) : post("/api/assignment-rules", f)), editing ? t("Rule saved.") : t("Rule created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  const cond: ConditionValue = { serviceId: f.serviceId, categoryId: f.categoryId, locationId: f.locationId, priority: f.priority };
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit assignment rule") : t("New assignment rule")} description={t("New requests that match go to this team. The first matching rule wins.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rule-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="rule-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="ru-name" required error={errs.name}><TextInput id="ru-name" data-autofocus value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <fieldset className="stack" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, fontSize: 12, padding: 0 }}>{t("Applies to")}</legend>
          <ConditionFields value={cond} onChange={(c) => setF((x) => ({ ...x, ...c }))} lookups={lookups} errors={errs} idPrefix="ru" />
        </fieldset>
        <Field label={t("Send to team")} htmlFor="ru-team" required error={errs.teamId}>
          <SelectInput id="ru-team" value={f.teamId ?? ""} onChange={(e) => set("teamId", e.target.value || null)} placeholder={t("Choose a team")} options={lookups.teams.filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))} />
        </Field>
        <Field label={t("Then")} htmlFor="ru-strat" error={errs.strategy}>
          <SelectInput id="ru-strat" value={f.strategy} onChange={(e) => set("strategy", e.target.value as Strategy)} options={STRATEGIES.map((s) => ({ value: s, label: strategyLabel(s) }))} />
        </Field>
        <p><small>{t("Round robin gives the next ticket to whoever was given one longest ago; least loaded to whoever has the fewest open tickets. Only team members who work tickets are considered.")}</small></p>
      </form>
    </Overlay>
  );
}

function Tester({ lookups }: { lookups: Lookups }) {
  const run = useRun();
  const [serviceId, setServiceId] = useState(""); const [locationId, setLocationId] = useState(""); const [priority, setPriority] = useState<Priority | "">("");
  const [result, setResult] = useState<RoutePreview | null>(null); const [err, setErr] = useState("");
  async function check(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setResult(null);
    const r = await run(() => post<RoutePreview>("/api/assignment-rules/preview", { serviceId: serviceId || null, locationId: locationId || null, priority: priority || null }), undefined, { silent: true });
    if (r.ok) setResult(r.data); else setErr(r.fields?.serviceId ?? r.error);
  }
  return (
    <Panel title={t("Test a ticket")} description={t("See where a request would go. Nothing is assigned.")}>
      <form className="stack" noValidate onSubmit={check}>
        <div className="filters">
          <Field label={t("Service")} htmlFor="rt-svc"><SelectInput id="rt-svc" value={serviceId} onChange={(e) => setServiceId(e.target.value)} placeholder={t("Choose a service")} options={lookups.services.filter((s) => !s.archived).map((s) => ({ value: s.id, label: `${s.categoryName} › ${s.name}` }))} /></Field>
          <Field label={t("Place")} htmlFor="rt-loc"><SelectInput id="rt-loc" value={locationId} onChange={(e) => setLocationId(e.target.value)} options={[{ value: "", label: t("Not given") }, ...lookups.locations.filter((l) => !l.archived).map((l) => ({ value: l.id, label: l.name }))]} /></Field>
          <Field label={t("Priority")} htmlFor="rt-prio"><SelectInput id="rt-prio" value={priority} onChange={(e) => setPriority(e.target.value as Priority | "")} options={[{ value: "", label: t("The service's default") }, { value: "urgent", label: t("Urgent") }, { value: "high", label: t("High") }, { value: "medium", label: t("Medium") }, { value: "low", label: t("Low") }]} /></Field>
          <Button type="submit">{t("Check")}</Button>
        </div>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {result && <div role="status" className="stack">
          <p>{result.ruleName ? <>{t("Rule")}: <b>{result.ruleName}</b></> : t("No rule matches.")} {result.teamName ? <>→ {t("Team")}: <b>{result.teamName}</b></> : <>→ {t("no team: it would wait unrouted")}</>}</p>
          {result.teamName && <p>{result.assigneeName ? <>{t("Would be assigned to")} <b>{result.assigneeName}</b>.</> : t("Nobody is assigned: it would wait in the team's queue.")}</p>}
          {result.candidates.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
            {result.candidates.map((c) => <li key={c.id}>{c.name} — {t("{n} open", { n: c.openTickets })}{c.lastAssignedAt ? ` · ${t("last given a ticket")} ${fmtDateTime(c.lastAssignedAt)}` : ` · ${t("never assigned")}`}</li>)}
          </ul>}
        </div>}
      </form>
    </Panel>
  );
}

function Rules() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const lookups = useLookups();
  const q = useQuery(() => get<AssignmentRuleItem[]>("/api/assignment-rules", { includeArchived: archived }), [archived]);
  const [drawer, setDrawer] = useState<{ open: boolean; row: AssignmentRuleItem | null }>({ open: false, row: null });
  const live = (q.data ?? []).filter((r) => !r.archived).map((r) => r.id);
  const move = async (id: string, by: -1 | 1) => { const next = moved(live, id, by); if (next && (await run(() => post("/api/assignment-rules/reorder", { ids: next }), t("Order saved."))).ok) q.reload(); };
  const act = async (r: AssignmentRuleItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/assignment-rules/${r.id}/${what}`), what === "archive" ? t("Rule archived.") : t("Rule restored."))).ok) q.reload(); };

  const cols: Column<AssignmentRuleItem>[] = [
    { key: "o", header: t("Order"), cell: (r) => (r.archived ? "—" : live.indexOf(r.id) + 1) },
    { key: "n", header: t("Rule"), cell: (r) => <><b>{r.name}</b><br /><small>{describeConditions(r)}</small></> },
    { key: "t", header: t("Team"), cell: (r) => r.teamName ?? "—" },
    { key: "s", header: t("Then"), className: "hide-sm", cell: (r) => strategyLabel(r.strategy) },
    { key: "st", header: t("Status"), cell: (r) => <Badge tone={r.archived ? "gray" : "green"}>{r.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: (r) => <Can perm="rules.manage"><span className="row" style={{ gap: 6 }}>
      {!r.archived && <IconButton label={`${t("Move up")} ${r.name}`} disabled={live.indexOf(r.id) === 0} onClick={() => void move(r.id, -1)}><ArrowUp size={14} /></IconButton>}
      {!r.archived && <IconButton label={`${t("Move down")} ${r.name}`} disabled={live.indexOf(r.id) === live.length - 1} onClick={() => void move(r.id, 1)}><ArrowDown size={14} /></IconButton>}
      {!r.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${r.name}`} onClick={() => setDrawer({ open: true, row: r })}>{t("Edit")}</Button>}
      <Button size="sm" variant="outline" aria-label={`${r.archived ? t("Restore") : t("Archive")} ${r.name}`} onClick={() => void act(r, r.archived ? "restore" : "archive")}>{r.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Assignment Rules")} description={t("Where new requests go. Without a matching rule a request goes to its service's team and waits there.")}
        actions={<Can perm="rules.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New rule")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("Assignment rules")} columns={cols} rows={q.data ?? []} rowKey={(r) => r.id} emptyMessage={t("No rules yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <Tester lookups={lookups} />
      <RuleDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} lookups={lookups} />
    </>
  );
}

export default function AssignmentRulesPage() { return <Require perm="rules.read"><Rules /></Require>; }
