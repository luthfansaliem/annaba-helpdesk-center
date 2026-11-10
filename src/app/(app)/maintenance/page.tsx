"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { BarChart3, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { Can, Require } from "@/components/page-guard";
import { PlanDrawer, rhythm } from "@/components/maintenance/pm-bits";
import { get } from "@/lib/api/client";
import type { Paged, PlanListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

function Plans() {
  const router = useRouter();
  const [q, setQ] = useState(""); const [status, setStatus] = useState("all"); const [page, setPage] = useState(1); const [size, setSize] = useState(25); const [adding, setAdding] = useState(false);
  const list = useQuery(() => get<Paged<PlanListItem>>("/api/maintenance/plans", { q: q || undefined, status, page, pageSize: size }), [q, status, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  const cols: Column<PlanListItem>[] = [
    { key: "n", header: t("Plan"), cell: (p) => <><Link href={`/maintenance/${p.id}`}><b>{p.name}</b></Link>{p.paused && <> <Badge tone="gray">{t("Paused")}</Badge></>}<br /><small>{p.target}</small></> },
    { key: "r", header: t("How often"), cell: (p) => rhythm(p.unit, p.every) },
    { key: "d", header: t("Next date"), cell: (p) => p.paused ? "—" : p.nextDueOn ?? t("Ended") },
    { key: "w", header: t("Who"), className: "hide-sm", cell: (p) => [p.assigneeName ?? p.teamName, p.vendorName].filter(Boolean).join(" · ") || "—" },
    { key: "o", header: t("Open visits"), cell: (p) => <>{p.openVisits}{p.overdueVisits > 0 && <> <Badge tone="red">{t("{n} overdue", { n: p.overdueVisits })}</Badge></>}</> },
  ];
  return (
    <>
      <PageHeader title={t("Preventive maintenance")} description={t("Plans that make the routine work ahead of time: what is serviced, how often, by whom and with which checklist.")}
        actions={<span className="row" style={{ gap: 8 }}>
          <Link className="btn" href="/maintenance/compliance"><BarChart3 size={16} /> {t("Compliance")}</Link>
          <Can perm="pm.manage"><Button variant="primary" onClick={() => setAdding(true)}><Plus size={16} /> {t("New plan")}</Button></Can>
        </span>} />
      <Panel>
        <div className="row" style={{ gap: 12, paddingBottom: 12, flexWrap: "wrap", alignItems: "flex-end" }}>
          <Field label={t("Search plans")} htmlFor="pm-q"><TextInput id="pm-q" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} /></Field>
          <Field label={t("Status")} htmlFor="pm-status"><SelectInput id="pm-status" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} options={[{ value: "all", label: t("All") }, { value: "active", label: t("Active") }, { value: "paused", label: t("Paused") }]} /></Field>
        </div>
        <DataTable label={t("Maintenance plans")} columns={cols} rows={rows} rowKey={(p) => p.id} emptyMessage={t("No maintenance plans yet.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); }} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total })} />}
      </Panel>
      <PlanDrawer open={adding} editing={null} onClose={() => setAdding(false)} onDone={(p) => router.push(`/maintenance/${p.id}`)} />
    </>
  );
}

export default function MaintenancePage() { return <Require perm="pm.read"><Plans /></Require>; }
