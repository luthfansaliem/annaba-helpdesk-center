"use client";

import { useState } from "react";
import { useParams } from "next/navigation";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { QueryBoundary } from "@/components/query-boundary";
import { ItemDrawer, MoveDialog, categoryLabel, moveLabel } from "@/components/inventory/inventory-bits";
import { get, post } from "@/lib/api/client";
import type { InventoryItem, StockMove } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime, fmtMoney } from "@/lib/format";
import { t } from "@/i18n";

/** INV-003. One stock item: how much is where, and its ledger (every movement with the balance after it). */
function Item() {
  const { id } = useParams<{ id: string }>();
  const run = useRun();
  const { can } = useAuth();
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const [editing, setEditing] = useState(false); const [moving, setMoving] = useState(false);
  const q = useQuery(() => get<InventoryItem>(`/api/inventory/items/${id}`), [id, tick]);
  const moves = useQuery(() => get<StockMove[]>("/api/inventory/movements", { itemId: id, pageSize: 100 }), [id, tick]);
  const cols: Column<StockMove>[] = [
    { key: "d", header: t("When"), cell: (m) => <span className="nowrap">{fmtDateTime(m.at)}</span> },
    { key: "k", header: t("What"), cell: (m) => moveLabel(m.kind) },
    { key: "p", header: t("Place"), className: "hide-sm", cell: (m) => m.place },
    { key: "q", header: t("Change"), cell: (m) => <b style={{ color: m.quantity < 0 ? "var(--red-ink)" : "var(--green-ink)" }}>{m.quantity > 0 ? `+${m.quantity}` : m.quantity}</b> },
    { key: "b", header: t("Balance"), cell: (m) => m.balanceAfter },
    { key: "r", header: t("Reference"), className: "hide-sm", cell: (m) => m.reference ?? "—" },
    { key: "u", header: t("By"), className: "hide-sm", cell: (m) => m.by ?? "—" },
  ];
  return (
    <QueryBoundary q={q}>{(i) => (
      <>
        <PageHeader title={i.name} description={`${i.sku} · ${categoryLabel(i.category)}${i.description ? ` · ${i.description}` : ""}`}
          actions={<>
            {can("inventory.manage") && <Button onClick={() => setEditing(true)}>{t("Edit")}</Button>}
            {can("inventory.manage") && i.total === 0 && <Button variant="danger-outline" onClick={async () => { const r = await run(() => post(`/api/inventory/items/${i.id}/${i.archived ? "restore" : "archive"}`), i.archived ? t("Item restored.") : t("Item archived."), { silent: false }); if (r.ok) reload(); }}>{i.archived ? t("Restore") : t("Archive")}</Button>}
            {(can("inventory.manage") || can("inventory.adjust")) && !i.archived && <Button variant="primary" onClick={() => setMoving(true)}>{t("Move stock")}</Button>}
          </>} />
        <div className="kpis">
          <div className="card" style={{ padding: 16 }}><small className="muted">{t("In stock")}</small><div style={{ fontSize: 26, fontWeight: 800 }}>{i.total.toLocaleString()} <small>{i.unit}</small></div>{i.low && <Badge tone="orange">{t("Low somewhere")}</Badge>}</div>
          <div className="card" style={{ padding: 16 }}><small className="muted">{t("Reorder at")}</small><div style={{ fontSize: 26, fontWeight: 800 }}>{i.reorderLevel}</div><small className="muted">{i.reorderQuantity ? t("order {n} at a time", { n: i.reorderQuantity }) : t("no usual order quantity")}</small></div>
          <div className="card" style={{ padding: 16 }}><small className="muted">{t("Cost per unit")}</small><div style={{ fontSize: 26, fontWeight: 800 }}>{i.unitCost === null ? "—" : fmtMoney(i.unitCost)}</div><small className="muted">{t("from the last delivery")}</small></div>
        </div>
        <Panel title={t("Where it is")}>
          {i.stock.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("Nothing received yet.")}</p> : (
            <ul className="req-list" data-testid="stock-places">{i.stock.map((s) => <li key={s.locationId}><div>{s.place}</div><span className="row" style={{ gap: 6 }}><b>{s.quantity} {i.unit}</b>{s.low && <Badge tone="orange">{t("Low")}</Badge>}</span></li>)}</ul>
          )}
        </Panel>
        <Panel title={t("Movements")}><DataTable label={t("Movements")} columns={cols} rows={moves.data ?? []} rowKey={(m) => m.id} emptyMessage={t("No movements yet.")} /></Panel>
        <ItemDrawer open={editing} editing={i} onClose={() => setEditing(false)} onDone={reload} />
        <MoveDialog item={moving ? i : null} onClose={() => setMoving(false)} onDone={reload} />
      </>
    )}</QueryBoundary>
  );
}

export default function ItemPage() { return <Require perm="inventory.read"><Item /></Require>; }
