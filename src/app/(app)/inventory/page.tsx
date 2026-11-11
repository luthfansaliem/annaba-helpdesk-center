"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { AlertTriangle, ArrowDownToLine, ArrowUpFromLine, ClipboardList, Package, Plus, Wallet } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Bars, ChartCard, ColumnChart, PALETTE } from "@/components/insights/charts";
import { ModuleEmpty } from "@/components/insights/module-empty";
import { CATEGORIES, ItemDrawer, MoveDialog, categoryLabel } from "@/components/inventory/inventory-bits";
import { get } from "@/lib/api/client";
import type { InventoryItem, InventorySummary, PortalLocation, ReorderLine } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtMoney, fmtMoneyShort } from "@/lib/format";
import { formatLocale, t } from "@/i18n";

/** INV-001/003/004. Stock items with what is where, the low-stock reorder list, and the stock KPIs. */
function Inventory() {
  const router = useRouter();
  const { can } = useAuth();
  const [q, setQ] = useState(""); const [cat, setCat] = useState(""); const [place, setPlace] = useState(""); const [low, setLow] = useState(false); const [tick, setTick] = useState(0);
  const [adding, setAdding] = useState(false); const [moving, setMoving] = useState<InventoryItem | null>(null);
  const reload = () => setTick((n) => n + 1);
  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const list = useQuery(() => get<InventoryItem[]>("/api/inventory/items", { q: q || undefined, category: cat || undefined, locationId: place || undefined, low: low || undefined }), [q, cat, place, low, tick]);
  const sum = useQuery(() => get<InventorySummary>("/api/inventory/summary"), [tick]);
  const reorder = useQuery(() => get<ReorderLine[]>("/api/inventory/reorder"), [tick]);
  const currency = useQuery(() => get<{ currency: string }>("/api/costs/settings").catch(() => ({ currency: "IDR" })), []).data?.currency ?? "IDR";
  const s = sum.data; const rows = list.data ?? [];
  const cols: Column<InventoryItem>[] = [
    { key: "n", header: t("Item"), cell: (i) => <><Link href={`/inventory/${i.id}`}><b>{i.name}</b></Link> <small className="muted">{i.sku}</small>{i.low && <> <Badge tone="orange">{t("Low")}</Badge></>}<br /><small className="muted">{categoryLabel(i.category)}</small></> },
    { key: "q", header: t("In stock"), cell: (i) => <span className="nowrap"><b>{i.total.toLocaleString()}</b> {i.unit}</span> },
    { key: "w", header: t("Where"), className: "hide-sm", cell: (i) => (i.stock.length === 0 ? "—" : i.stock.map((x) => `${x.place.split(" › ").pop()}: ${x.quantity}`).join(" · ")) },
    { key: "r", header: t("Reorder at"), className: "hide-sm", cell: (i) => i.reorderLevel },
    { key: "c", header: t("Cost per unit"), className: "hide-sm", cell: (i) => (i.unitCost === null ? "—" : fmtMoney(i.unitCost, currency)) },
    { key: "a", header: <span className="sr-only">{t("Actions")}</span>, cell: (i) => (can("inventory.manage") || can("inventory.adjust")) && !i.archived ? <Button size="sm" onClick={() => setMoving(i)} aria-label={`${t("Move stock")} ${i.name}`}>{t("Move stock")}</Button> : null },
  ];
  return (
    <>
      <PageHeader title={t("Inventory")} description={t("Office supplies, pantry, cleaning and other stock: how much is where, what runs low, and every movement.")}
        actions={<><Button href="/inventory/stock-takes"><ClipboardList size={16} /> {t("Stock-takes")}</Button><Can perm="inventory.manage"><Button variant="primary" onClick={() => setAdding(true)}><Plus size={16} /> {t("New item")}</Button></Can></>} />
      {s && s.items === 0 ? (
        <div className="card" style={{ marginBottom: 12 }}>
          <ModuleEmpty icon={<Package size={26} />} title={t("Start keeping stock")} text={t("Add the supplies General Affairs hands out (paper, pens, coffee, soap…) with a reorder level, then record deliveries. You are told when something runs low.")}>
            {can("inventory.manage") && <Button variant="primary" onClick={() => setAdding(true)}>{t("Add the first item")}</Button>}
          </ModuleEmpty>
        </div>
      ) : s && (
        <>
          <KpiGrid label={t("Inventory at a glance")}>
            <KpiCard icon={<Package size={18} />} label={t("Items")} value={String(s.items)} note={t("kept in stock")} />
            <KpiCard icon={<AlertTriangle size={18} />} label={t("Running low")} value={String(s.lowLines)} note={t("item and place pairs at or below the reorder level")} tone={s.lowLines > 0 ? "warn" : "good"} />
            <KpiCard icon={<Wallet size={18} />} label={t("Stock value")} value={s.stockValue === null ? "—" : fmtMoneyShort(s.stockValue, currency)} note={t("at the last purchase cost")} />
            <KpiCard icon={<ArrowUpFromLine size={18} />} label={t("Handed out")} value={s.issued30.toLocaleString()} note={t("units, last 30 days")} />
            <KpiCard icon={<ArrowDownToLine size={18} />} label={t("Received")} value={s.received30.toLocaleString()} note={t("units, last 30 days")} />
          </KpiGrid>
          {s.issued30 > 0 && (
            <div className="chart-grid wide-first">
              <ChartCard title={t("Handed out per day")} sub={t("Units, last 30 days")}>
                <ColumnChart labels={s.issued30Days.map((d) => new Date(`${d.day}T00:00:00`).toLocaleDateString(formatLocale(), { day: "2-digit", month: "short" }))} series={[{ name: t("Handed out"), color: PALETTE[0], data: s.issued30Days.map((d) => d.bookings) }]} label={t("Units handed out per day")} />
              </ChartCard>
              <ChartCard title={t("Used most")} sub={t("Units handed out, last 30 days")}><Bars items={s.topIssued.map((x) => ({ label: x.label, value: x.count, href: `/inventory/${x.key}` }))} color={PALETTE[2]} /></ChartCard>
            </div>
          )}
        </>
      )}
      {(reorder.data ?? []).length > 0 && (
        <Panel title={t("To reorder")} description={t("At or below the reorder level, with a suggested quantity.")}>
          <ul className="req-list" data-testid="reorder">
            {reorder.data!.map((l) => (
              <li key={`${l.itemId}:${l.locationId}`}>
                <div><Link href={`/inventory/${l.itemId}`}><b>{l.name}</b></Link> <small className="muted">{l.sku}</small><br /><small className="muted">{l.place} · {t("{n} {unit} left, reorder at {level}", { n: l.quantity, unit: l.unit, level: l.reorderLevel })}</small></div>
                <span className="nowrap"><b>{t("Order {n} {unit}", { n: l.suggested, unit: l.unit })}</b>{l.cost !== null && <><br /><small className="muted">≈ {fmtMoney(l.cost, currency)}</small></>}</span>
              </li>
            ))}
          </ul>
        </Panel>
      )}
      <Panel>
        <div className="filters">
          <Field label={t("Search")} htmlFor="iv-q"><TextInput id="iv-q" placeholder={t("Name or code")} value={q} onChange={(e) => setQ(e.target.value)} /></Field>
          <Field label={t("Category")} htmlFor="iv-cat"><SelectInput id="iv-cat" value={cat} onChange={(e) => setCat(e.target.value)} options={[{ value: "", label: t("All categories") }, ...CATEGORIES.map((c) => ({ value: c, label: categoryLabel(c) }))]} /></Field>
          <Field label={t("Place")} htmlFor="iv-place"><SelectInput id="iv-place" value={place} onChange={(e) => setPlace(e.target.value)} options={[{ value: "", label: t("Everywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
          <label className="row chk"><Checkbox checked={low} onChange={(e) => setLow(e.target.checked)} /> {t("Only low")}</label>
        </div>
        <DataTable label={t("Stock items")} columns={cols} rows={rows} rowKey={(i) => i.id} emptyMessage={t("No items match.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
      </Panel>
      <ItemDrawer open={adding} editing={null} onClose={() => setAdding(false)} onDone={(i) => router.push(`/inventory/${i.id}`)} />
      <MoveDialog item={moving} onClose={() => setMoving(null)} onDone={reload} />
    </>
  );
}

export default function InventoryPage() { return <Require perm="inventory.read"><Inventory /></Require>; }
