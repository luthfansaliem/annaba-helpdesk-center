"use client";

import { useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { Button } from "@/components/ui/button";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { AlarmClock, CheckCircle2, Inbox, Plus, Search, SlidersHorizontal, TriangleAlert, UserX } from "lucide-react";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { Require } from "@/components/page-guard";
import { PriorityBadge, SLA_STATES, STATUSES, SlaBadge, TicketStatusBadge, slaLabel, statusLabel } from "@/components/tickets/status";
import { BulkBar } from "@/components/tickets/bulk-bar";
import { ViewsBar, type ViewFilter } from "@/components/tickets/views-bar";
import { get } from "@/lib/api/client";
import type { OperationsReport, Paged, PortalLocation, Priority, TicketListItem } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** The filters on screen. Their names are the ones a saved view stores, so a view can set them back exactly. */
type Filters = { q: string; status: string; priority: string; sla: string; sort: string; locationId: string; from: string; to: string; unassigned: boolean; assignedToMe: boolean };
const DEFAULTS: Filters = { q: "", status: "open", priority: "", sla: "", sort: "updated", locationId: "", from: "", to: "", unassigned: false, assignedToMe: false };

/** A day typed in the date box is a day on the person's own clock: from its first moment to the first moment of the next day. */
const dayStart = (d: string) => new Date(`${d}T00:00:00`).toISOString();
const dayAfter = (d: string) => { const x = new Date(`${d}T00:00:00`); x.setDate(x.getDate() + 1); return x.toISOString(); };

const toView = (f: Filters): ViewFilter => {
  const o: ViewFilter = {};
  if (f.q.trim()) o.q = f.q.trim();
  o.status = f.status || "";
  for (const k of ["priority", "sla", "locationId", "from", "to"] as const) if (f[k]) o[k] = f[k];
  if (f.sort === "due") o.sort = "due";
  if (f.unassigned) o.unassigned = "true";
  if (f.assignedToMe) o.assignedToMe = "true";
  return Object.fromEntries(Object.entries(o).filter(([k, v]) => v !== "" || k === "status"));
};
const fromView = (v: ViewFilter): Filters => ({
  q: v.q ?? "", status: v.status ?? "", priority: v.priority ?? "", sla: v.sla ?? "", sort: v.sort === "due" ? "due" : "updated", locationId: v.locationId ?? "", from: v.from ?? "", to: v.to ?? "",
  unassigned: v.unassigned === "true", assignedToMe: v.assignedToMe === "true",
});

function Queue() {
  const [f, setF] = useState<Filters>(DEFAULTS); const [page, setPage] = useState(1); const [size, setSize] = useState(25); const [selected, setSelected] = useState<string[]>([]); const [more, setMore] = useState(false);
  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const set = <K extends keyof Filters>(k: K, v: Filters[K]) => { setF((x) => ({ ...x, [k]: v })); setPage(1); setSelected([]); };
  const list = useQuery(() => get<Paged<TicketListItem>>("/api/tickets", {
    status: f.status || undefined, priority: f.priority || undefined, q: f.q || undefined, unassigned: f.unassigned || undefined, assignedToMe: f.assignedToMe || undefined, sla: f.sla || undefined,
    sort: f.sort === "due" ? "due" : undefined, locationId: f.locationId || undefined, from: f.from ? dayStart(f.from) : undefined, to: f.to ? dayAfter(f.to) : undefined, page, pageSize: size,
  }), [f, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));

  const cols: Column<TicketListItem>[] = [
    { key: "n", header: t("Number"), cell: (r) => <Link href={`/tickets/${r.id}`} className="nowrap"><b>{r.number}</b></Link> },
    { key: "s", header: t("Subject"), cell: (r) => <><Link href={`/tickets/${r.id}`}>{r.subject}</Link><br /><small>{r.serviceName}{r.locationName ? ` · ${r.locationName}` : ""}</small></> },
    { key: "st", header: t("Status"), cell: (r) => <TicketStatusBadge status={r.status} /> },
    { key: "p", header: t("Priority"), cell: (r) => <PriorityBadge priority={r.priority} /> },
    { key: "sl", header: t("SLA"), cell: (r) => <><SlaBadge state={r.slaState} />{r.resolutionDueAt && <small style={{ display: "block" }}>{t("due")} {fmtDateTime(r.resolutionDueAt)}</small>}</> },
    { key: "r", header: t("Requester"), className: "hide-sm", cell: (r) => r.requesterName ?? "—" },
    { key: "a", header: t("Assigned to"), className: "hide-sm", cell: (r) => r.assigneeName ?? <small>{r.teamName ? `${t("Team queue")}: ${r.teamName}` : t("Unassigned")}</small> },
    { key: "u", header: t("Updated"), className: "hide-sm", cell: (r) => fmtDateTime(r.updatedAt) },
  ];

  // quick views: one click sets the filters a desk uses most; the counts come from the same scope as the list (the operations summary)
  const ops = useQuery(() => get<OperationsReport>("/api/reports/operations", { days: 30 }).catch(() => undefined), [list.data?.total]);
  const k = ops.data?.kpis; const count = (st: string) => ops.data?.byStatus.find((x) => x.key === st)?.count ?? 0;
  const quick: { id: string; label: string; n?: number; f: Partial<Filters> }[] = [
    { id: "open", label: t("All open"), n: k?.open, f: { status: "open", sla: "", unassigned: false, assignedToMe: false } },
    { id: "mine", label: t("Assigned to me"), f: { status: "open", sla: "", unassigned: false, assignedToMe: true } },
    { id: "unassigned", label: t("Unassigned"), n: k?.unassigned, f: { status: "open", sla: "", unassigned: true, assignedToMe: false } },
    { id: "atrisk", label: t("At risk"), n: k?.atRisk, f: { status: "open", sla: "atrisk", unassigned: false, assignedToMe: false } },
    { id: "breached", label: t("Overdue"), n: k?.overdue, f: { status: "open", sla: "breached", unassigned: false, assignedToMe: false } },
    { id: "onhold", label: t("On hold"), n: ops.data ? count("onhold") : undefined, f: { status: "onhold", sla: "", unassigned: false, assignedToMe: false } },
    { id: "approval", label: t("Waiting for approval"), n: k?.pendingApproval, f: { status: "pendingapproval", sla: "", unassigned: false, assignedToMe: false } },
  ];
  const isOn = (q: (typeof quick)[number]) => Object.entries(q.f).every(([key, v]) => f[key as keyof Filters] === v);
  const extra = [f.locationId, f.from, f.to].filter(Boolean).length;

  return (
    <>
      <PageHeader help="work-the-queue" title={t("Ticket Queue")} description={t("Requests for your teams, assigned to you, or everything if you supervise.")}
        actions={<Button variant="primary" href="/requests/new"><Plus size={15} /> {t("New request")}</Button>} />
      <KpiGrid label={t("Queue at a glance")}>
        <KpiCard icon={<Inbox size={18} />} label={t("Open")} value={k ? String(k.open) : "…"} note={t("in the backlog")} />
        <KpiCard icon={<UserX size={18} />} label={t("Unassigned")} value={k ? String(k.unassigned) : "…"} note={t("nobody has them yet")} tone={k && k.unassigned > 0 ? "warn" : "default"} />
        <KpiCard icon={<AlarmClock size={18} />} label={t("At risk")} value={k ? String(k.atRisk) : "…"} note={t("close to their SLA")} tone={k && k.atRisk > 0 ? "warn" : "good"} />
        <KpiCard icon={<TriangleAlert size={18} />} label={t("Overdue")} value={k ? String(k.overdue) : "…"} note={t("past their SLA")} tone={k && k.overdue > 0 ? "bad" : "good"} />
        <KpiCard icon={<CheckCircle2 size={18} />} label={t("Resolved")} value={k ? String(k.resolved) : "…"} note={t("last 30 days")} />
      </KpiGrid>
      <Panel>
        <div className="qtabs" role="group" aria-label={t("Quick views")}>
          {quick.map((q) => <button key={q.id} type="button" aria-pressed={isOn(q)} onClick={() => { setF((x) => ({ ...x, ...q.f })); setPage(1); setSelected([]); }}>{q.label}{q.n !== undefined && <b>{q.n}</b>}</button>)}
        </div>
        <div className="filters" role="search">
          <div className="fl grow"><label htmlFor="tq-q">{t("Search")}</label><TextInput id="tq-q" icon={<Search size={14} />} placeholder={t("Number, subject, details or requester")} value={f.q} onChange={(e) => set("q", e.target.value)} /></div>
          <Field label={t("Status")} htmlFor="tq-st">
            <SelectInput id="tq-st" value={f.status} onChange={(e) => set("status", e.target.value)} options={[{ value: "open", label: t("All open") }, { value: "", label: t("Any status") }, ...STATUSES.map((st) => ({ value: st, label: statusLabel(st) }))]} />
          </Field>
          <Field label={t("Priority")} htmlFor="tq-pr">
            <SelectInput id="tq-pr" value={f.priority} onChange={(e) => set("priority", e.target.value)} options={[{ value: "", label: t("Any priority") }, ...(["urgent", "high", "medium", "low"] as Priority[]).map((pr) => ({ value: pr, label: { urgent: t("Urgent"), high: t("High"), medium: t("Medium"), low: t("Low") }[pr] }))]} />
          </Field>
          <Field label={t("SLA")} htmlFor="tq-sla">
            <SelectInput id="tq-sla" value={f.sla} onChange={(e) => set("sla", e.target.value)} options={[{ value: "", label: t("Any SLA state") }, ...SLA_STATES.map((st) => ({ value: st, label: slaLabel(st) }))]} />
          </Field>
          <Field label={t("Sort by")} htmlFor="tq-sort">
            <SelectInput id="tq-sort" value={f.sort} onChange={(e) => set("sort", e.target.value)} options={[{ value: "updated", label: t("Last updated") }, { value: "due", label: t("Deadline (soonest first)") }]} />
          </Field>
          <label className="chk"><input type="checkbox" checked={f.unassigned} onChange={(e) => set("unassigned", e.target.checked)} /> {t("Unassigned only")}</label>
          <label className="chk"><input type="checkbox" checked={f.assignedToMe} onChange={(e) => set("assignedToMe", e.target.checked)} /> {t("Assigned to me")}</label>
          <Button aria-expanded={more} onClick={() => setMore(!more)}><SlidersHorizontal size={14} /> {more ? t("Fewer filters") : t("More filters")}{extra > 0 && !more ? ` (${extra})` : ""}</Button>
        </div>
        {more && (
          <div className="filters">
            <Field label={t("Place")} htmlFor="tq-loc">
              <SelectInput id="tq-loc" value={f.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Anywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} />
            </Field>
            <Field label={t("Created from")} htmlFor="tq-from"><TextInput id="tq-from" type="date" value={f.from} onChange={(e) => set("from", e.target.value)} /></Field>
            <Field label={t("Created until")} htmlFor="tq-to"><TextInput id="tq-to" type="date" value={f.to} onChange={(e) => set("to", e.target.value)} /></Field>
          </div>
        )}
        <ViewsBar current={toView(f)} onApply={(v) => { setF(fromView(v)); setPage(1); setSelected([]); setMore(Boolean(v.locationId || v.from || v.to)); }} />
        <BulkBar ids={selected} onClear={() => setSelected([])} onDone={() => { setSelected([]); list.reload(); }} />
        <DataTable label={t("Tickets")} columns={cols} rows={rows} rowKey={(r) => r.id} selectable selected={selected} onSelectedChange={setSelected} emptyMessage={t("No tickets match.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={(n) => { setPage(n); setSelected([]); }} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); setSelected([]); }} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total: list.data?.capped ? `${total.toLocaleString()}+` : total })} />}
      </Panel>
    </>
  );
}

export default function TicketQueuePage() { return <Require perm="tickets.read_team"><Queue /></Require>; }
