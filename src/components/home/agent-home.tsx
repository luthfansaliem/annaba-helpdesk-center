"use client";

import Link from "next/link";
import { AlarmClock, CheckCircle2, FilePlus2, Hourglass, Inbox, ListTodo, Ticket, TriangleAlert, UserX, Wrench } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { ChartCard, Donut } from "@/components/insights/charts";
import { ChartLegend, LineChart } from "@/components/charts/line-chart";
import { ModuleEmpty } from "@/components/insights/module-empty";
import { PriorityBadge, SlaBadge, statusLabel } from "@/components/tickets/status";
import { Skeleton } from "@/components/ui/states";
import { get } from "@/lib/api/client";
import type { OperationsReport, Paged, TicketListItem, TicketStatus, WorkOrderListItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { minutes, percent } from "@/components/reports/parts";
import { t } from "@/i18n";

const STATUS_COLORS: Record<string, string> = { pendingapproval: "#f59e0b", submitted: "#94a3b8", assigned: "#0b62e6", inprogress: "#14b8a6", onhold: "#f4694f", reopened: "#8b5cf6" };

function MyQueue() {
  const mine = useQuery(() => get<Paged<TicketListItem>>("/api/tickets", { assignedToMe: true, status: "open", sort: "due", pageSize: 6 }), []);
  if (mine.data === undefined) return <div className="stack"><Skeleton h={38} /><Skeleton h={38} /><Skeleton h={38} /></div>;
  if (mine.data.data.length === 0) return <ModuleEmpty icon={<CheckCircle2 size={24} />} title={t("Nothing assigned to you")} text={t("Pick up the next ticket from the team queue.")}><Button size="sm" href="/tickets">{t("Open the queue")}</Button></ModuleEmpty>;
  return (
    <ul className="req-list">
      {mine.data.data.map((r) => (
        <li key={r.id}>
          <div><Link href={`/tickets/${r.id}`}><b>{r.subject}</b></Link><small>{r.number} · {r.serviceName}{r.locationName ? ` · ${r.locationName}` : ""}{r.resolutionDueAt ? ` · ${t("due")} ${fmtDateTime(r.resolutionDueAt)}` : ""}</small></div>
          <PriorityBadge priority={r.priority} />{r.slaState && <SlaBadge state={r.slaState} />}
        </li>
      ))}
    </ul>
  );
}

function MyWork() {
  const wo = useQuery(() => get<Paged<WorkOrderListItem>>("/api/work-orders", { mine: true, pageSize: 5 }), []);
  if (wo.data === undefined) return <div className="stack"><Skeleton h={38} /><Skeleton h={38} /></div>;
  if (wo.data.data.length === 0) return <p className="chart-empty">{t("No work orders given to you.")}</p>;
  return (
    <ul className="req-list">
      {wo.data.data.map((w) => (
        <li key={w.id}><div><Link href={`/work-orders/${w.id}`}><b>{w.title}</b></Link><small>{w.number} · {w.ticketNumber}{w.dueAt ? ` · ${t("due")} ${fmtDateTime(w.dueAt)}` : ""}</small></div></li>
      ))}
    </ul>
  );
}

export function AgentHome() {
  const { user, can } = useAuth();
  const ops = useQuery(() => get<OperationsReport>("/api/reports/operations", { days: 30 }), []);
  const k = ops.data?.kpis;
  const first = user?.name.split(" ")[0] ?? "";
  const d = ops.data?.daily ?? [];
  const top = Math.max(5, ...d.flatMap((x) => [x.created, x.resolved])); const max = Math.ceil(top / 5) * 5;
  const idx = d.length > 1 ? [0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(f * (d.length - 1))) : [];
  const segs = (ops.data?.byStatus ?? []).map((s) => ({ label: statusLabel(s.key as TicketStatus), value: s.count, color: STATUS_COLORS[s.key] ?? "#94a3b8" }));
  return (
    <>
      <div className="home-head">
        <div><h1>{t("Hello, {name}", { name: first })}</h1><p>{t("Your service desk today: what is waiting, what is late and what is yours.")}</p></div>
        <div className="row"><Button href="/tickets"><Ticket size={16} /> {t("Ticket Queue")}</Button><Button variant="primary" href="/requests/new"><FilePlus2 size={16} /> {t("New request")}</Button></div>
      </div>
      <KpiGrid label={t("Service desk now")}>
        <KpiCard icon={<Inbox size={18} />} label={t("Open tickets")} value={k ? String(k.open) : "…"} note={t("in your teams' backlog")} href="/tickets" />
        <KpiCard icon={<UserX size={18} />} label={t("Unassigned")} value={k ? String(k.unassigned) : "…"} note={t("nobody has them yet")} tone={k && k.unassigned > 0 ? "warn" : "default"} href="/tickets" />
        <KpiCard icon={<AlarmClock size={18} />} label={t("At risk")} value={k ? String(k.atRisk) : "…"} note={t("close to their SLA")} tone={k && k.atRisk > 0 ? "warn" : "good"} href="/tickets" />
        <KpiCard icon={<TriangleAlert size={18} />} label={t("Overdue")} value={k ? String(k.overdue) : "…"} note={t("past their SLA")} tone={k && k.overdue > 0 ? "bad" : "good"} href="/tickets" />
        <KpiCard icon={<Hourglass size={18} />} label={t("Avg resolution")} value={k ? minutes(k.avgResolutionMinutes) : "…"} note={t("last 30 days")} />
        <KpiCard icon={<CheckCircle2 size={18} />} label={t("SLA attainment")} value={k ? percent(k.slaAttainmentPercent) : "…"} note={t("resolved in the last 30 days")} tone={!k || k.slaAttainmentPercent === null ? "default" : k.slaAttainmentPercent >= 90 ? "good" : k.slaAttainmentPercent >= 70 ? "warn" : "bad"} />
      </KpiGrid>
      <div className="home-grid">
        <Panel title={t("My tickets")} description={t("Assigned to you, soonest deadline first.")} actions={<Link className="link-btn" href="/tickets">{t("Open the queue")}</Link>}><MyQueue /></Panel>
        <ChartCard title={t("Backlog by status")} sub={t("Open tickets in your teams")}><Donut segments={segs} center={t("Open")} label={t("Open tickets by status")} /></ChartCard>
      </div>
      <div className="chart-grid wide-first">
        <ChartCard title={t("Created and resolved per day")} sub={t("Last 30 days")} action={<Link className="link-btn" href="/reports">{t("Operations")}</Link>}>
          {d.length > 1 ? <><LineChart series={[{ name: t("Created"), color: "#0b62e6", data: d.map((x) => x.created) }, { name: t("Resolved"), color: "#16a37f", data: d.map((x) => x.resolved) }]} labels={idx.map((i) => d[i].day.slice(5))} max={max} ticks={[0, 0.5, 1].map((f) => Math.round(f * max))} label={t("Created and resolved tickets per day")} height={180} />
            <ChartLegend items={[{ name: t("Created"), color: "#0b62e6" }, { name: t("Resolved"), color: "#16a37f" }]} /></> : <Skeleton h={180} />}
        </ChartCard>
        {can("workorders.read")
          ? <Panel title={t("My work orders")} actions={<Link className="link-btn" href="/work-orders"><Wrench size={13} /> {t("All")}</Link>}><MyWork /></Panel>
          : <Panel title={t("My requests")} actions={<Link className="link-btn" href="/requests"><ListTodo size={13} /> {t("All")}</Link>}><p className="chart-empty">{t("Requests you made yourself are under My requests.")}</p></Panel>}
      </div>
    </>
  );
}
