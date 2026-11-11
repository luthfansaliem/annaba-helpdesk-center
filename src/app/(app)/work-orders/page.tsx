"use client";

import { useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, SelectInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { Require } from "@/components/page-guard";
import { WoStatusBadge } from "@/components/workorders/wo-bits";
import { get } from "@/lib/api/client";
import type { Paged, WorkOrderListItem } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useAuth } from "@/lib/auth";
import { WorkOrderInsights } from "@/components/insights/modules";
import { t } from "@/i18n";

function List() {
  const { user } = useAuth(); const vendor = !!user?.vendorName;
  const [mine, setMine] = useState(true); const [status, setStatus] = useState("open"); const [page, setPage] = useState(1); const [size, setSize] = useState(25);
  const q = useQuery(() => get<Paged<WorkOrderListItem>>("/api/work-orders", { mine: mine || undefined, status, page, pageSize: size }), [mine, status, page, size]);
  const rows = q.data?.data ?? []; const total = q.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  const cols: Column<WorkOrderListItem>[] = [
    { key: "n", header: t("Work order"), cell: (w) => <><Link href={`/work-orders/${w.id}`}><b>{w.number}</b></Link><br /><small>{w.title}</small></> },
    { key: "s", header: t("Status"), cell: (w) => <WoStatusBadge status={w.status} /> },
    { key: "c", header: t("Checklist"), className: "hide-sm", cell: (w) => (w.tasksTotal ? `${w.tasksDone}/${w.tasksTotal}` : "—") },
    { key: "a", header: t("Person"), className: "hide-sm", cell: (w) => w.assigneeName ?? <small>{t("Nobody yet")}</small> },
    { key: "t", header: t("Ticket"), className: "hide-sm", cell: (w) => <>{vendor ? w.ticketNumber : <Link href={`/tickets/${w.ticketId}`}>{w.ticketNumber}</Link>}<br /><small>{w.ticketSubject}</small></> },
    { key: "d", header: t("Due"), cell: (w) => (w.dueAt ? fmtDateTime(w.dueAt) : "—") },
  ];
  return (
    <>
      <PageHeader title={t("Work Orders")} description={t("The work you are given, and the work orders of the tickets you can see.")} />
      <WorkOrderInsights />
      <Panel>
        <div className="filters">
          {!vendor && <div role="group" aria-label={t("Whose work")} className="row" style={{ gap: 8 }}>
            <Button variant={mine ? "primary" : "outline"} aria-pressed={mine} onClick={() => { setMine(true); setPage(1); }}>{t("Mine")}</Button>
            <Button variant={!mine ? "primary" : "outline"} aria-pressed={!mine} onClick={() => { setMine(false); setPage(1); }}>{t("Everything I can see")}</Button>
          </div>}
          <Field label={t("Status")} htmlFor="wl-status">
            <SelectInput id="wl-status" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}
              options={[{ value: "open", label: t("Not finished") }, { value: "new", label: t("New") }, { value: "in_progress", label: t("In progress") }, { value: "on_hold", label: t("On hold") }, { value: "done", label: t("Done") }, { value: "cancelled", label: t("Cancelled") }, { value: "all", label: t("All") }]} />
          </Field>
        </div>
        <DataTable label={t("Work orders")} columns={cols} rows={rows} rowKey={(w) => w.id} emptyMessage={t("No work orders here.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); }} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total })} />}
      </Panel>
    </>
  );
}

export default function WorkOrdersPage() { return <Require perm="workorders.read"><List /></Require>; }
