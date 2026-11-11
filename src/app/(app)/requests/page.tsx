"use client";

import { useState } from "react";
import Link from "next/link";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { TicketStatusBadge } from "@/components/tickets/status";
import { get } from "@/lib/api/client";
import type { Paged, TicketListItem } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

export default function MyRequestsPage() {
  const [status, setStatus] = useState("open"); const [q, setQ] = useState(""); const [page, setPage] = useState(1); const [size, setSize] = useState(10);
  const list = useQuery(() => get<Paged<TicketListItem>>("/api/tickets/mine", { status: status || undefined, q: q || undefined, page, pageSize: size }), [status, q, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));

  const cols: Column<TicketListItem>[] = [
    { key: "n", header: t("Number"), cell: (r) => <Link href={`/tickets/${r.id}`}><b>{r.number}</b></Link> },
    { key: "s", header: t("Request"), cell: (r) => <><Link href={`/tickets/${r.id}`}>{r.subject}</Link><br /><small>{r.serviceName}</small></> },
    { key: "st", header: t("Status"), cell: (r) => <TicketStatusBadge status={r.status} /> },
    { key: "a", header: t("Handled by"), className: "hide-sm", cell: (r) => r.assigneeName ?? r.teamName ?? "—" },
    { key: "u", header: t("Updated"), className: "hide-sm", cell: (r) => fmtDateTime(r.updatedAt) },
  ];

  return (
    <>
      <PageHeader help="follow-your-requests" title={t("My requests")} description={t("Everything you have asked for, and where it stands.")}
        actions={<Button variant="primary" href="/requests/new"><Plus size={16} /> {t("New request")}</Button>} />
      <Panel>
        <div className="filters">
          <Field label={t("Search")} htmlFor="rq-q"><TextInput id="rq-q" placeholder={t("Number or subject")} value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} /></Field>
          <Field label={t("Show")} htmlFor="rq-st">
            <SelectInput id="rq-st" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} options={[{ value: "open", label: t("Open requests") }, { value: "", label: t("All requests") }, { value: "closed", label: t("Closed") }, { value: "cancelled", label: t("Cancelled") }]} />
          </Field>
        </div>
        <DataTable label={t("My requests")} columns={cols} rows={rows} rowKey={(r) => r.id} emptyMessage={t("No requests yet. Start with “New request”.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); }} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total: list.data?.capped ? `${total.toLocaleString()}+` : total })} />}
      </Panel>
    </>
  );
}
