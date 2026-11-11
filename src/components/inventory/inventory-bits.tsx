"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { get, post, put } from "@/lib/api/client";
import type { InventoryItem, PortalLocation, StockMove } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const CATEGORIES = ["atk", "pantry", "cleaning", "uniform", "toiletries", "it", "other"] as const;
export const categoryLabel = (c: string) => ({ atk: t("Office supplies (ATK)"), pantry: t("Pantry"), cleaning: t("Cleaning"), uniform: t("Uniforms"), toiletries: t("Toiletries"), it: t("IT supplies"), other: t("Other") } as Record<string, string>)[c] ?? c;
export const moveLabel = (k: StockMove["kind"]) => ({ receipt: t("Received"), issue: t("Issued"), adjustment: t("Adjusted"), transfer_out: t("Transferred out"), transfer_in: t("Transferred in"), count: t("Stock-take") })[k];

const blank = { sku: "", name: "", category: "atk", unit: "pcs", reorderLevel: "0", reorderQuantity: "", unitCost: "", description: "" };
/** INV-001. Add or edit a stock item. */
export function ItemDrawer({ open, editing, onClose, onDone }: { open: boolean; editing: InventoryItem | null; onClose: () => void; onDone: (i: InventoryItem) => void }) {
  const run = useRun();
  const [f, setF] = useState(blank); const [seed, setSeed] = useState<string | null>(null); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState("");
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr("");
    setF(editing ? { sku: editing.sku, name: editing.name, category: editing.category, unit: editing.unit, reorderLevel: String(editing.reorderLevel), reorderQuantity: editing.reorderQuantity ? String(editing.reorderQuantity) : "",
      unitCost: editing.unitCost !== null ? String(editing.unitCost) : "", description: editing.description ?? "" } : blank);
  }
  const set = (k: keyof typeof blank, v: string) => setF((x) => ({ ...x, [k]: v }));
  async function save(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({});
    const body = { sku: f.sku, name: f.name, category: f.category, unit: f.unit, reorderLevel: Number(f.reorderLevel || 0), reorderQuantity: f.reorderQuantity ? Number(f.reorderQuantity) : null,
      unitCost: f.unitCost ? Number(f.unitCost) : null, description: f.description || null, version: editing?.version };
    const r = await run(() => (editing ? put<InventoryItem>(`/api/inventory/items/${editing.id}`, body) : post<InventoryItem>("/api/inventory/items", body)), editing ? t("Item saved.") : t("Item added."), { silent: false });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit item") : t("New stock item")} description={t("Something General Affairs keeps in stock and hands out.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="item-form">{t("Save")}</Button></>}>
      <form id="item-form" className="stack" noValidate onSubmit={save}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Code (SKU)")} htmlFor="it-sku" required error={errs.sku}><TextInput id="it-sku" data-autofocus maxLength={40} placeholder="ATK-PEN-BLK" value={f.sku} onChange={(e) => set("sku", e.target.value)} /></Field>
          <Field label={t("Unit")} htmlFor="it-unit" error={errs.unit}><TextInput id="it-unit" maxLength={20} placeholder="pcs, box, ream…" value={f.unit} onChange={(e) => set("unit", e.target.value)} /></Field>
        </div>
        <Field label={t("Name")} htmlFor="it-name" required error={errs.name}><TextInput id="it-name" maxLength={150} value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label={t("Category")} htmlFor="it-cat" error={errs.category}><SelectInput id="it-cat" value={f.category} onChange={(e) => set("category", e.target.value)} options={CATEGORIES.map((c) => ({ value: c, label: categoryLabel(c) }))} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Reorder at")} htmlFor="it-level" error={errs.reorderLevel}><TextInput id="it-level" type="number" min={0} value={f.reorderLevel} onChange={(e) => set("reorderLevel", e.target.value)} /></Field>
          <Field label={t("Usual order quantity")} htmlFor="it-rq" error={errs.reorderQuantity}><TextInput id="it-rq" type="number" min={1} value={f.reorderQuantity} onChange={(e) => set("reorderQuantity", e.target.value)} /></Field>
          <Field label={t("Cost per unit")} htmlFor="it-cost" error={errs.unitCost}><TextInput id="it-cost" type="number" min={0} value={f.unitCost} onChange={(e) => set("unitCost", e.target.value)} /></Field>
        </div>
        <Field label={t("Description")} htmlFor="it-desc" error={errs.description}><textarea id="it-desc" className="textarea" rows={2} maxLength={1000} value={f.description} onChange={(e) => set("description", e.target.value)} /></Field>
      </form>
    </Overlay>
  );
}

/** INV-003. Receive, issue, adjust or transfer stock of one item. Each kind needs its own permission; only the allowed ones are offered. */
export function MoveDialog({ item, onClose, onDone }: { item: InventoryItem | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const { can } = useAuth();
  const places = useQuery(() => (item ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [item?.id]);
  const kinds = [...(can("inventory.manage") ? (["receipt", "issue", "transfer"] as const) : []), ...(can("inventory.adjust") ? (["adjustment"] as const) : [])];
  const [kind, setKind] = useState<string>(""); const [loc, setLoc] = useState(""); const [to, setTo] = useState(""); const [qty, setQty] = useState(""); const [cost, setCost] = useState(""); const [ref, setRef] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [seed, setSeed] = useState<string | null>(null);
  if ((item?.id ?? null) !== seed) { setSeed(item?.id ?? null); setKind(kinds[0] ?? ""); setLoc(item?.stock[0]?.locationId ?? ""); setTo(""); setQty(""); setCost(""); setRef(""); setErrs({}); setErr(""); }
  async function go() {
    if (!item) return;
    setErr(""); setErrs({});
    const r = await run(() => post<StockMove[]>(`/api/inventory/items/${item.id}/moves`, { kind, locationId: loc || null, toLocationId: kind === "transfer" ? to || null : null, quantity: qty ? Number(qty) : null, unitCost: kind === "receipt" && cost ? Number(cost) : null, reference: ref || null }),
      t("Stock updated."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  const placeOptions = (places.data ?? []).map((l) => ({ value: l.id, label: l.path }));
  return (
    <Overlay open={!!item} onClose={onClose} title={t("Move stock")} description={item ? `${item.name} · ${item.sku}` : undefined}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" onClick={go} disabled={!kind}>{t("Save")}</Button></>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("What happened")} htmlFor="mv-kind" error={errs.kind}><SelectInput id="mv-kind" value={kind} onChange={(e) => setKind(e.target.value)} options={kinds.map((k) => ({ value: k, label: { receipt: t("Received a delivery"), issue: t("Handed out"), transfer: t("Moved to another place"), adjustment: t("Correction") }[k] }))} /></Field>
        <Field label={kind === "transfer" ? t("From") : t("Place")} htmlFor="mv-loc" error={errs.locationId}><SelectInput id="mv-loc" value={loc} onChange={(e) => setLoc(e.target.value)} options={[{ value: "", label: t("Choose…") }, ...placeOptions]} /></Field>
        {kind === "transfer" && <Field label={t("To")} htmlFor="mv-to" error={errs.toLocationId}><SelectInput id="mv-to" value={to} onChange={(e) => setTo(e.target.value)} options={[{ value: "", label: t("Choose…") }, ...placeOptions]} /></Field>}
        <Field label={kind === "adjustment" ? t("Quantity (minus to take away)") : t("Quantity ({unit})", { unit: item?.unit ?? "" })} htmlFor="mv-qty" error={errs.quantity}><TextInput id="mv-qty" type="number" value={qty} onChange={(e) => setQty(e.target.value)} /></Field>
        {kind === "receipt" && <Field label={t("Cost per unit")} htmlFor="mv-cost" error={errs.unitCost}><TextInput id="mv-cost" type="number" min={0} value={cost} onChange={(e) => setCost(e.target.value)} /></Field>}
        <Field label={kind === "adjustment" ? t("Reason") : kind === "receipt" ? t("Supplier or invoice") : t("For whom or what")} htmlFor="mv-ref" required={kind === "adjustment"} error={errs.reference}><TextInput id="mv-ref" maxLength={300} value={ref} onChange={(e) => setRef(e.target.value)} /></Field>
      </div>
    </Overlay>
  );
}
