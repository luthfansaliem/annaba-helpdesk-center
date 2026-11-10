"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { Can, Require } from "@/components/page-guard";
import { VendorDrawer } from "@/components/vendors/vendor-bits";
import { get } from "@/lib/api/client";
import type { Paged, PortalLocation, VendorListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

function Directory() {
  const router = useRouter();
  const [q, setQ] = useState(""); const [status, setStatus] = useState("active"); const [place, setPlace] = useState(""); const [page, setPage] = useState(1); const [size, setSize] = useState(25); const [adding, setAdding] = useState(false);
  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const list = useQuery(() => get<Paged<VendorListItem>>("/api/vendors", { q: q || undefined, status, locationId: place || undefined, page, pageSize: size }), [q, status, place, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  const cols: Column<VendorListItem>[] = [
    { key: "n", header: t("Vendor"), cell: (v) => <><Link href={`/vendors/${v.id}`}><b>{v.name}</b></Link>{v.blocked && <> <Badge tone="red">{t("Blocked")}</Badge></>}{v.services && <><br /><small>{v.services}</small></>}</> },
    { key: "c", header: t("Contact"), className: "hide-sm", cell: (v) => <>{v.phone ?? "—"}{v.email && <><br /><small>{v.email}</small></>}</> },
    { key: "w", header: t("Open work orders"), cell: (v) => v.openWorkOrders },
    { key: "e", header: t("Next contract end"), className: "hide-sm", cell: (v) => v.nextContractEnd ?? "—" },
  ];
  return (
    <>
      <PageHeader title={t("Vendors")} description={t("The companies you buy services from: who to call, where they work, their contracts and how their work goes.")}
        actions={<Can perm="vendors.manage"><Button variant="primary" onClick={() => setAdding(true)}><Plus size={16} /> {t("New vendor")}</Button></Can>} />
      <Panel>
        <div className="row" style={{ gap: 12, paddingBottom: 12, flexWrap: "wrap", alignItems: "flex-end" }}>
          <Field label={t("Search")} htmlFor="vl-q"><TextInput id="vl-q" placeholder={t("Name or what they do")} value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} /></Field>
          <Field label={t("Status")} htmlFor="vl-status"><SelectInput id="vl-status" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} options={[{ value: "active", label: t("Active") }, { value: "blocked", label: t("Blocked") }, { value: "all", label: t("All") }]} /></Field>
          <Field label={t("Covers")} htmlFor="vl-place"><SelectInput id="vl-place" value={place} onChange={(e) => { setPlace(e.target.value); setPage(1); }} options={[{ value: "", label: t("Anywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
        </div>
        <DataTable label={t("Vendors")} columns={cols} rows={rows} rowKey={(v) => v.id} emptyMessage={t("No vendors match.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); }} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total })} />}
      </Panel>
      <VendorDrawer open={adding} onClose={() => setAdding(false)} onDone={(v) => router.push(`/vendors/${v.id}`)} editing={null} />
    </>
  );
}

export default function VendorsPage() { return <Require perm="vendors.read"><Directory /></Require>; }
