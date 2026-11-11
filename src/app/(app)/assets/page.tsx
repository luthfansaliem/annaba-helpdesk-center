"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Download, Plus, QrCode, Upload } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { Can, Require } from "@/components/page-guard";
import { AssetDrawer, AssetStatusBadge, ASSET_STATUSES, assetStatusLabel } from "@/components/assets/asset-bits";
import { get } from "@/lib/api/client";
import type { AssetListItem, AssetTypeItem, Paged, PortalLocation } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { AssetInsights } from "@/components/insights/modules";
import { t } from "@/i18n";

type Filters = { q: string; typeId: string; status: string; locationId: string; warranty: string; frequent: boolean };
const params = (f: Filters) => ({ q: f.q || undefined, typeId: f.typeId || undefined, status: f.status || undefined, locationId: f.locationId || undefined, warrantyWithinDays: f.warranty || undefined, frequent: f.frequent || undefined });

function Register() {
  const router = useRouter(); const run = useRun(); const sp = useSearchParams();
  const [f, setF] = useState<Filters>({ q: "", typeId: "", status: "", locationId: "", warranty: sp.get("warranty") ?? "", frequent: sp.get("frequent") === "true" }); const [page, setPage] = useState(1); const [size, setSize] = useState(25); const [adding, setAdding] = useState(false); const [busy, setBusy] = useState(false);
  const set = <K extends keyof Filters>(k: K, v: Filters[K]) => { setF((x) => ({ ...x, [k]: v })); setPage(1); };
  const types = useQuery(() => get<AssetTypeItem[]>("/api/asset-types"), []);
  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const list = useQuery(() => get<Paged<AssetListItem>>("/api/assets", { ...params(f), page, pageSize: size }), [f, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));

  async function exportCsv() {
    setBusy(true);
    const r = await run(async () => {
      const qs = new URLSearchParams(Object.entries(params(f)).filter(([, v]) => v !== undefined) as [string, string][]).toString();
      const res = await fetch(`/api/assets/export${qs ? `?${qs}` : ""}`, { credentials: "same-origin" });
      if (!res.ok) throw new Error(t("The export failed."));
      return { blob: await res.blob(), name: /filename="?([^";]+)/.exec(res.headers.get("content-disposition") ?? "")?.[1] ?? "assets.csv", cut: res.headers.get("x-export-truncated") === "true" };
    }, t("Export ready."));
    setBusy(false);
    if (!r.ok) return;
    const url = URL.createObjectURL(r.data.blob); const a = document.createElement("a"); a.href = url; a.download = r.data.name; a.click(); URL.revokeObjectURL(url);
  }

  const cols: Column<AssetListItem>[] = [
    { key: "t", header: t("Tag"), cell: (a) => <Link href={`/assets/${a.id}`}><b>{a.tag}</b></Link> },
    { key: "n", header: t("Asset"), cell: (a) => <><Link href={`/assets/${a.id}`}>{a.name}</Link><br /><small>{a.typeName}{a.serial ? ` · ${a.serial}` : ""}</small></> },
    { key: "s", header: t("Status"), cell: (a) => <AssetStatusBadge status={a.status} /> },
    { key: "p", header: t("Place"), className: "hide-sm", cell: (a) => a.locationPath ?? "—" },
    { key: "w", header: t("With"), className: "hide-sm", cell: (a) => a.assignedToName ?? "—" },
    { key: "e", header: t("Warranty ends"), className: "hide-sm", cell: (a) => a.warrantyEnd ?? "—" },
  ];

  return (
    <>
      <PageHeader title={t("Assets")} description={t("Everything the organisation looks after: where it is, who has it, and the requests about it.")}
        actions={<span className="row" style={{ gap: 8 }}>
          <Button href={`/assets/labels${f.typeId || f.locationId ? `?${new URLSearchParams(Object.entries({ typeId: f.typeId, locationId: f.locationId }).filter(([, v]) => v)).toString()}` : ""}`}><QrCode size={14} /> {t("Labels")}</Button>
          <Button onClick={() => void exportCsv()} disabled={busy}><Download size={14} /> {t("Export CSV")}</Button>
          <Can perm="assets.import"><Button href="/assets/import"><Upload size={14} /> {t("Import")}</Button></Can>
          <Can perm="assets.manage"><Button variant="primary" onClick={() => setAdding(true)}><Plus size={16} /> {t("New asset")}</Button></Can>
        </span>} />
      <AssetInsights onAdd={() => setAdding(true)} />
      <Panel>
        <div className="filters">
          <Field label={t("Search")} htmlFor="ar-q"><TextInput id="ar-q" placeholder={t("Tag, name or serial")} value={f.q} onChange={(e) => set("q", e.target.value)} /></Field>
          <Field label={t("Type")} htmlFor="ar-type"><SelectInput id="ar-type" value={f.typeId} onChange={(e) => set("typeId", e.target.value)} options={[{ value: "", label: t("All types") }, ...(types.data ?? []).map((x) => ({ value: x.id, label: x.name }))]} /></Field>
          <Field label={t("Status")} htmlFor="ar-status"><SelectInput id="ar-status" value={f.status} onChange={(e) => set("status", e.target.value)} options={[{ value: "", label: t("In service") }, { value: "all", label: t("All, including retired") }, ...ASSET_STATUSES.map((s) => ({ value: s, label: assetStatusLabel(s) }))]} /></Field>
          <Field label={t("Place")} htmlFor="ar-place"><SelectInput id="ar-place" value={f.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Anywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
          <Field label={t("Warranty")} htmlFor="ar-warranty"><SelectInput id="ar-warranty" value={f.warranty} onChange={(e) => set("warranty", e.target.value)} options={[{ value: "", label: t("Any") }, { value: "30", label: t("Ends within 30 days (or ended)") }, { value: "90", label: t("Ends within 90 days (or ended)") }]} /></Field>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.frequent} onChange={(e) => set("frequent", e.target.checked)} /> {t("Fails often (3+ requests in 12 months)")}</label>
        </div>
        <DataTable label={t("Assets")} columns={cols} rows={rows} rowKey={(a) => a.id} emptyMessage={t("No assets match.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); }} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total })} />}
      </Panel>
      <AssetDrawer open={adding} onClose={() => setAdding(false)} onDone={(a) => router.push(`/assets/${a.id}`)} editing={null} />
    </>
  );
}

export default function AssetsPage() { return <Require perm="assets.read"><Suspense><Register /></Suspense></Require>; }
