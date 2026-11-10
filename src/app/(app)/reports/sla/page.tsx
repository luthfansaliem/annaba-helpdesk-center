"use client";

import { useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { Kpi, PeriodSelect, minutes, percent } from "@/components/reports/parts";
import { PriorityBadge, TicketStatusBadge, priorityLabel } from "@/components/tickets/status";
import { get } from "@/lib/api/client";
import type { BreachItem, Priority, SlaReport, SlaRow } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const tone = (p: number | null) => (p === null ? "gray" : p >= 90 ? "green" : p >= 70 ? "orange" : "red") as "gray" | "green" | "orange" | "red";

function rowCols(first: string, label?: (r: SlaRow) => string): Column<SlaRow>[] {
  return [
    { key: "l", header: first, cell: (r) => <b>{label ? label(r) : r.label || t("No team")}</b> },
    { key: "m", header: t("Met"), cell: (r) => r.met },
    { key: "b", header: t("Missed"), cell: (r) => r.breached },
    { key: "p", header: t("Attainment"), cell: (r) => <Badge tone={tone(r.attainmentPercent)}>{percent(r.attainmentPercent)}</Badge> },
    { key: "a", header: t("Avg resolution"), className: "hide-sm", cell: (r) => minutes(r.avgResolutionMinutes) },
  ];
}

function Body({ r }: { r: SlaReport }) {
  const breachCols: Column<BreachItem>[] = [
    { key: "n", header: t("Ticket"), cell: (b) => <><Link href={`/tickets/${b.id}`}><b>{b.number}</b> · {b.subject}</Link><br /><small>{b.serviceName}</small></> },
    { key: "r", header: t("What was missed"), cell: (b) => (b.reason === "first_response" ? t("First response") : t("Resolution")) },
    { key: "w", header: t("When"), cell: (b) => fmtDateTime(b.breachedAt) },
    { key: "s", header: t("Status"), cell: (b) => <TicketStatusBadge status={b.status} /> },
    { key: "p", header: t("Priority"), className: "hide-sm", cell: (b) => <PriorityBadge priority={b.priority} /> },
    { key: "a", header: t("Assigned to"), className: "hide-sm", cell: (b) => b.assignee ?? <small>{b.team ?? t("Unassigned")}</small> },
  ];
  return (
    <>
      <section className="stats six" aria-label={t("Key metrics")}>
        <Kpi label={t("SLA attainment")} value={percent(r.attainmentPercent)} note={t("of tickets resolved in the period")} tone={r.attainmentPercent === null ? "default" : r.attainmentPercent >= 90 ? "good" : r.attainmentPercent >= 70 ? "warn" : "bad"} />
        <Kpi label={t("Met")} value={String(r.met)} note={t("resolved within their targets")} tone="good" />
        <Kpi label={t("Missed")} value={String(r.breached)} note={t("resolved after a target")} tone={r.breached > 0 ? "bad" : "default"} />
        <Kpi label={t("Overdue now")} value={String(r.openBreached)} note={t("open and past their SLA")} tone={r.openBreached > 0 ? "bad" : "good"} />
        <Kpi label={t("At risk now")} value={String(r.openAtRisk)} note={t("open and close to their SLA")} tone={r.openAtRisk > 0 ? "warn" : "good"} />
      </section>
      <section className="stack">   {/* full width: three narrow tables side by side would need horizontal scrolling */}
        <Panel title={t("By priority")}><DataTable label={t("SLA by priority")} columns={rowCols(t("Priority"), (x) => priorityLabel(x.key as Priority))} rows={r.byPriority} rowKey={(x) => x.key} emptyMessage={t("No data.")} /></Panel>
        <Panel title={t("By team")}><DataTable label={t("SLA by team")} columns={rowCols(t("Team"))} rows={r.byTeam} rowKey={(x) => x.key} emptyMessage={t("No resolved tickets with an SLA in this period.")} /></Panel>
        <Panel title={t("By service")} description={t("Lowest attainment first.")}><DataTable label={t("SLA by service")} columns={rowCols(t("Service"))} rows={r.byService} rowKey={(x) => x.key} emptyMessage={t("No resolved tickets with an SLA in this period.")} /></Panel>
      </section>
      <Panel title={t("Breaches")} description={t("Tickets that missed a target in the period, newest first (at most 50).")}>
        <DataTable label={t("Breaches")} columns={breachCols} rows={r.breaches} rowKey={(b) => b.id} emptyMessage={t("No breaches in this period.")} />
      </Panel>
    </>
  );
}

function Sla() {
  const [days, setDays] = useState(30);
  const q = useQuery(() => get<SlaReport>("/api/reports/sla", { days }), [days]);
  return (
    <>
      <PageHeader title={t("SLA Report")} description={t("How often tickets met their service-level targets, where they did not, and why.")} actions={<PeriodSelect days={days} onChange={setDays} />} />
      <QueryBoundary q={q} rows={6}>{(r) => <Body r={r} />}</QueryBoundary>
    </>
  );
}

export default function SlaReportPage() { return <Require perm="tickets.read_team"><Sla /></Require>; }
