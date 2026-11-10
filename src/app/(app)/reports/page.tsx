"use client";

import { useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Card, Panel } from "@/components/ui/card";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { ChartLegend, LineChart } from "@/components/charts/line-chart";
import { DonutChart, DonutLegend } from "@/components/charts/donut-chart";
import { BarList, Kpi, PeriodSelect, minutes, percent } from "@/components/reports/parts";
import { PriorityBadge, SlaBadge, priorityLabel, statusLabel } from "@/components/tickets/status";
import { get } from "@/lib/api/client";
import type { AttentionItem, OperationsReport, Priority, TicketStatus } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const agingLabel = (k: string) => ({ lt1d: t("Under 1 day"), d1_3: t("1–3 days"), d3_7: t("3–7 days"), d7_14: t("1–2 weeks"), gt14d: t("Over 2 weeks") } as Record<string, string>)[k] ?? k;
const statusColors: Record<string, string> = { pendingapproval: "#f59e0b", submitted: "#94a3b8", assigned: "#0b62e6", inprogress: "#14b8a6", onhold: "#f4694f", reopened: "#8b5cf6" };

function Body({ r }: { r: OperationsReport }) {
  const k = r.kpis;
  const labels = (() => { const d = r.daily; if (d.length < 2) return d.map((x) => x.day.slice(5)); const idx = [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(f * (d.length - 1))); return idx.map((i) => d[i].day.slice(5)); })();
  const max = Math.max(5, ...r.daily.flatMap((d) => [d.created, d.resolved]));
  const top = Math.ceil(max / 5) * 5;
  const segs = r.byStatus.map((s) => ({ label: statusLabel(s.key as TicketStatus), value: s.count, color: statusColors[s.key] ?? "#94a3b8" })).filter((s) => s.value > 0);
  const cols: Column<AttentionItem>[] = [
    { key: "n", header: t("Ticket"), cell: (a) => <Link href={`/tickets/${a.id}`}><b>{a.number}</b> · {a.subject}</Link> },
    { key: "s", header: t("SLA"), cell: (a) => <SlaBadge state={a.slaState} /> },
    { key: "p", header: t("Priority"), className: "hide-sm", cell: (a) => <PriorityBadge priority={a.priority} /> },
    { key: "a", header: t("Assigned to"), className: "hide-sm", cell: (a) => a.assignee ?? <small>{a.team ?? t("Unassigned")}</small> },
    { key: "d", header: t("Deadline"), cell: (a) => (a.dueAt ? fmtDateTime(a.dueAt) : "—") },
  ];
  return (
    <>
      <section className="stats six" aria-label={t("Key metrics")}>
        <Kpi label={t("Open")} value={String(k.open)} note={t("tickets in the backlog")} />
        <Kpi label={t("Overdue")} value={String(k.overdue)} note={t("open and past their SLA")} tone={k.overdue > 0 ? "bad" : "good"} />
        <Kpi label={t("At risk")} value={String(k.atRisk)} note={t("close to their SLA")} tone={k.atRisk > 0 ? "warn" : "good"} />
        <Kpi label={t("Unassigned")} value={String(k.unassigned)} note={t("nobody has them yet")} tone={k.unassigned > 0 ? "warn" : "default"} />
        <Kpi label={t("Waiting for approval")} value={String(k.pendingApproval)} note={t("not started yet")} />
        <Kpi label={t("SLA attainment")} value={percent(k.slaAttainmentPercent)} note={t("of tickets resolved in the period")} tone={k.slaAttainmentPercent === null ? "default" : k.slaAttainmentPercent >= 90 ? "good" : k.slaAttainmentPercent >= 70 ? "warn" : "bad"} />
      </section>
      <section className="stats six" aria-label={t("Flow and speed")}>
        <Kpi label={t("Created")} value={String(k.created)} note={t("in the period")} />
        <Kpi label={t("Resolved")} value={String(k.resolved)} note={t("in the period")} />
        <Kpi label={t("Avg first response")} value={minutes(k.avgFirstResponseMinutes)} note={t("clock time, tickets created in the period")} />
        <Kpi label={t("Avg resolution")} value={minutes(k.avgResolutionMinutes)} note={t("clock time, tickets resolved in the period")} />
      </section>

      <section className="g3 charts">
        <Card className="panel" style={{ gridColumn: "span 2" }}>
          <h2 style={{ fontSize: 15, margin: "0 0 8px" }}>{t("Created and resolved per day")}</h2>
          {r.daily.length >= 2
            ? <><LineChart series={[{ name: t("Created"), color: "#0b62e6", data: r.daily.map((d) => d.created) }, { name: t("Resolved"), color: "#16a37f", data: r.daily.map((d) => d.resolved) }]} labels={labels} max={top} ticks={[0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(f * top))} label={t("Created and resolved tickets per day")} />
              <ChartLegend items={[{ name: t("Created"), color: "#0b62e6" }, { name: t("Resolved"), color: "#16a37f" }]} /></>
            : <p>{t("Today: {created} created, {resolved} resolved.", { created: r.daily[0]?.created ?? 0, resolved: r.daily[0]?.resolved ?? 0 })}</p>}
        </Card>
        <Card className="panel">
          <h2 style={{ fontSize: 15, margin: "0 0 8px" }}>{t("Backlog by status")}</h2>
          {segs.length === 0 ? <p className="tab-placeholder">{t("No open tickets.")}</p> : <div className="donut-wrap"><DonutChart segments={segs} centerLabel={t("Open")} label={t("Open tickets by status")} /><DonutLegend segments={segs} /></div>}
        </Card>
      </section>

      <section className="g3 charts">
        <Panel title={t("Backlog by team")}><BarList items={r.backlogByTeam.map((x) => ({ label: x.label || t("No team"), count: x.count }))} empty={t("No open tickets.")} /></Panel>
        <Panel title={t("Open by priority")}><BarList items={r.byPriority.map((x) => ({ label: priorityLabel(x.key as Priority), count: x.count }))} color="#f59e0b" empty={t("No open tickets.")} /></Panel>
        <Panel title={t("Age of open tickets")}><BarList items={r.aging.map((x) => ({ label: agingLabel(x.key), count: x.count }))} color="#8b5cf6" empty={t("No open tickets.")} /></Panel>
      </section>

      <Panel title={t("Needs attention now")} description={t("Open tickets that are overdue or close to their SLA, most urgent first.")}>
        <DataTable label={t("Needs attention now")} columns={cols} rows={r.attention} rowKey={(a) => a.id} emptyMessage={t("Nothing is overdue or at risk.")} />
      </Panel>
    </>
  );
}

function Operations() {
  const [days, setDays] = useState(30);
  const q = useQuery(() => get<OperationsReport>("/api/reports/operations", { days }), [days]);
  return (
    <>
      <PageHeader help="reports" title={t("Operations")} description={t("How the helpdesk is doing: backlog, flow, speed and what needs attention. You see the tickets of your teams, or all of them if you supervise.")}
        actions={<PeriodSelect days={days} onChange={setDays} />} />
      <QueryBoundary q={q} rows={6}>{(r) => <Body r={r} />}</QueryBoundary>
    </>
  );
}

export default function OperationsPage() { return <Require perm="tickets.read_team"><Operations /></Require>; }
