"use client";

import { useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { get, post } from "@/lib/api/client";
import type { PortalLocation, SupplyCatalogItem, WorkOrderDetail } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtMoney } from "@/lib/format";
import { t } from "@/i18n";

/** INV-005. Draws a part for a work order from stock: the stock goes down through the ledger and a part line is added at the item's cost.
 *  Removing that line later puts the part back. */
export function StockPartDialog({ wo, open, onClose, onDone }: { wo: WorkOrderDetail; open: boolean; onClose: () => void; onDone: (w: WorkOrderDetail) => void }) {
  const run = useRun();
  const [loc, setLoc] = useState(""); const [item, setItem] = useState(""); const [qty, setQty] = useState("1");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [seed, setSeed] = useState(false);
  if (open !== seed) { setSeed(open); setItem(""); setQty("1"); setErrs({}); setErr(""); }
  const locs = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const cat = useQuery(() => (open && loc ? get<SupplyCatalogItem[]>("/api/supplies/catalog", { locationId: loc }) : Promise.resolve([] as SupplyCatalogItem[])), [open, loc]);
  const inStock = (cat.data ?? []).filter((i) => i.available > 0);
  const chosen = inStock.find((i) => i.id === item);
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const r = await run(() => post<WorkOrderDetail>(`/api/work-orders/${wo.id}/parts`, { itemId: item || null, locationId: loc || null, quantity: Number(qty || 0) }), t("Part taken from stock."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} title={t("Part from stock")} description={`${wo.number} · ${wo.title}`}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="wo-part" disabled={!item}>{t("Take from stock")}</Button></>}>
      <form id="wo-part" className="stack" noValidate onSubmit={save}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("From the stock at")} htmlFor="wp-place" required error={errs.locationId}>
          <SelectInput id="wp-place" data-autofocus value={loc} onChange={(e) => { setLoc(e.target.value); setItem(""); }} options={[{ value: "", label: t("Choose a place") }, ...(locs.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} />
        </Field>
        <Field label={t("Item")} htmlFor="wp-item" required error={errs.itemId}>
          <SelectInput id="wp-item" value={item} disabled={!loc} onChange={(e) => setItem(e.target.value)}
            options={[{ value: "", label: loc ? (inStock.length ? t("Choose an item") : t("Nothing in stock here")) : t("Choose a place first") }, ...inStock.map((i) => ({ value: i.id, label: `${i.name} · ${i.available} ${i.unit}` }))]} />
        </Field>
        <Field label={t("Quantity")} htmlFor="wp-qty" required error={errs.quantity}><TextInput id="wp-qty" type="number" min={1} max={chosen?.available} inputMode="numeric" value={qty} onChange={(e) => setQty(e.target.value)} /></Field>
        {chosen && <p className="muted">{t("Costs {cost} ({each} each).", { cost: fmtMoney((chosen.unitCost ?? 0) * Number(qty || 0)), each: fmtMoney(chosen.unitCost ?? 0) })}</p>}
      </form>
    </Overlay>
  );
}
