"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ArrowLeft, Pause, Pencil, Play, Trash2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { PlanDrawer, VisitBadge, rhythm } from "@/components/maintenance/pm-bits";
import { del, get, post } from "@/lib/api/client";
import type { PlanDetail, PlanVisit } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><dt><b>{label}</b></dt><dd style={{ margin: 0 }}>{children}</dd></div>;
}

const visitCols = (): Column<PlanVisit>[] => [
  { key: "d", header: t("Date"), cell: (v) => v.dueOn },
  { key: "a", header: t("Asset or place"), cell: (v) => (v.assetId ? <Link href={`/assets/${v.assetId}`}>{v.assetTag} · {v.assetName}</Link> : v.locationName ?? "—") },
  { key: "w", header: t("Work order"), cell: (v) => (v.workOrderId ? <Link href={`/work-orders/${v.workOrderId}`}>{v.workOrderNumber}</Link> : "—") },
  { key: "s", header: t("Status"), cell: (v) => <VisitBadge state={v.state} /> },
];

function Detail({ id }: { id: string }) {
  const run = useRun(); const router = useRouter();
  const q = useQuery(() => get<PlanDetail>(`/api/maintenance/plans/${id}`), [id]);
  const [editing, setEditing] = useState(false); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  return (
    <QueryBoundary q={q}>{(p) => {
      const set = (d: PlanDetail) => q.setData(d);
      const toggle = async () => { const r = await run(() => post<PlanDetail>(`/api/maintenance/plans/${p.id}/${p.paused ? "resume" : "pause"}`), p.paused ? t("Plan resumed.") : t("Plan paused.")); if (r.ok) set(r.data); };
      const remove = () => setConfirm({ title: t("Remove {what}?", { what: p.name }), confirmLabel: t("Remove"), danger: true, body: t("This can't be undone."),
        onConfirm: async () => { const r = await run(() => del(`/api/maintenance/plans/${p.id}`), t("Removed.")); if (r.ok) router.push("/maintenance"); } });
      const y = p.lastYear;
      return (
        <>
          <div className="crud-top">
            <div>
              <Link href="/maintenance" className="back-link"><ArrowLeft size={14} /> {t("Preventive maintenance")}</Link>
              <h1>{p.name}</h1>
              <p>{p.target} · {rhythm(p.unit, p.every)}</p>
            </div>
            {p.paused ? <Badge tone="gray">{t("Paused")}</Badge> : p.nextDueOn ? <Badge tone="green">{t("Active")}</Badge> : <Badge tone="gray">{t("Ended")}</Badge>}
          </div>
          {p.canManage && <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            <Button onClick={() => setEditing(true)}><Pencil size={14} /> {t("Edit")}</Button>
            <Button onClick={() => void toggle()}>{p.paused ? <><Play size={14} /> {t("Resume")}</> : <><Pause size={14} /> {t("Pause")}</>}</Button>
            {p.visits.length === 0 && <Button variant="danger-outline" onClick={remove}><Trash2 size={14} /> {t("Remove")}</Button>}
          </span>}

          <div style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", alignItems: "start" }}>
            <Panel title={t("The plan")}>
              <dl className="stack" style={{ margin: 0 }}>
                <Row label={t("What is serviced")}>{p.target}{p.assetTypeId ? ` (${t("{n} now", { n: p.targets })})` : ""}</Row>
                <Row label={t("How often")}>{rhythm(p.unit, p.every)}, {t("from {date}", { date: p.startsOn })}{p.endsOn ? ` ${t("until {date}", { date: p.endsOn })}` : ""}</Row>
                <Row label={t("Work is made")}>{t("{n} days before the date", { n: p.leadDays })}</Row>
                <Row label={t("Filed under service")}>{p.serviceName}</Row>
                <Row label={t("Checklist")}>{p.checklistName ?? "—"}</Row>
                <Row label={t("Who")}>{[p.assigneeName, p.teamName ?? t("The service's team"), p.vendorName].filter(Boolean).join(" · ")}</Row>
                {p.instructions && <Row label={t("Instructions")}><span style={{ whiteSpace: "pre-wrap" }}>{p.instructions}</span></Row>}
                <Row label={t("Owner")}>{p.ownerName ?? "—"}</Row>
              </dl>
            </Panel>
            <div className="stack">
              <Panel title={t("Next dates")}>
                {p.paused ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("Paused: no work is made until it is resumed.")}</p>
                  : p.upcoming.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("The plan has ended.")}</p>
                    : <ul style={{ margin: 0, paddingLeft: 18 }} data-testid="pm-upcoming">{p.upcoming.map((d) => <li key={d}>{d}</li>)}</ul>}
              </Panel>
              <Panel title={t("Last 12 months")} description={t("Visits due so far, and how they went.")}>
                <dl className="row" style={{ gap: 24, flexWrap: "wrap", margin: 0 }} data-testid="pm-year">
                  <div><dt><small>{t("Planned")}</small></dt><dd style={{ margin: 0 }}><b>{y.planned}</b></dd></div>
                  <div><dt><small>{t("Done on time")}</small></dt><dd style={{ margin: 0 }}><b>{y.onTime}</b></dd></div>
                  <div><dt><small>{t("Done late")}</small></dt><dd style={{ margin: 0 }}><b>{y.late}</b></dd></div>
                  <div><dt><small>{t("Overdue")}</small></dt><dd style={{ margin: 0 }}><b>{y.overdue}</b></dd></div>
                  <div><dt><small>{t("Missed")}</small></dt><dd style={{ margin: 0 }}><b>{y.missed + y.cancelled}</b></dd></div>
                  <div><dt><small>{t("Compliance")}</small></dt><dd style={{ margin: 0 }}><b>{y.percent === null ? "—" : `${y.percent}%`}</b></dd></div>
                </dl>
              </Panel>
            </div>
          </div>

          <Panel title={t("Visits")} description={t("The latest 50. Each visit is a ticket with one work order.")}>
            <DataTable label={t("Visits")} columns={visitCols()} rows={p.visits} rowKey={(v) => v.id} emptyMessage={t("No visits yet. They appear when a date comes within the lead time.")} />
          </Panel>
          <PlanDrawer open={editing} editing={p} onClose={() => setEditing(false)} onDone={set} />
          <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
        </>
      );
    }}</QueryBoundary>
  );
}

export default function PlanPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="pm.read"><Suspense><Detail id={id} /></Suspense></Require>;
}
