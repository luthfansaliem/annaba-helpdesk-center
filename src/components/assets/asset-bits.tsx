"use client";

import { useCallback, useState } from "react";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DynamicForm, PersonPicker, type FormValues } from "@/components/forms/dynamic-form";
import { get, post, put } from "@/lib/api/client";
import type { AssetDetail, AssetStatus, AssetTypeItem, PortalLocation } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const ASSET_STATUSES: AssetStatus[] = ["in_use", "in_repair", "stored", "retired"];
export const assetStatusLabel = (s: AssetStatus) => ({ in_use: t("In use"), in_repair: t("In repair"), stored: t("Stored"), retired: t("Retired") })[s];
const tone: Record<AssetStatus, BadgeTone> = { in_use: "green", in_repair: "orange", stored: "blue", retired: "gray" };
export function AssetStatusBadge({ status }: { status: AssetStatus }) { return <Badge tone={tone[status]}>{assetStatusLabel(status)}</Badge>; }

type PortalPerson = { id: string; name: string; department: string | null };
type Draft = { tag: string; name: string; typeId: string; serial: string; status: AssetStatus; locationId: string; assignedToId: string; assignedToName: string; department: string; purchaseDate: string; purchaseCost: string; warrantyEnd: string; vendorName: string; notes: string; fields: FormValues };
const empty = (typeId = ""): Draft => ({ tag: "", name: "", typeId, serial: "", status: "in_use", locationId: "", assignedToId: "", assignedToName: "", department: "", purchaseDate: "", purchaseCost: "", warrantyEnd: "", vendorName: "", notes: "", fields: {} });
const fromAsset = (a: AssetDetail): Draft => ({
  tag: a.tag, name: a.name, typeId: a.typeId, serial: a.serial ?? "", status: a.status, locationId: a.locationId ?? "", assignedToId: a.assignedToId ?? "", assignedToName: a.assignedToName ?? "", department: a.department ?? "",
  purchaseDate: a.purchaseDate ?? "", purchaseCost: a.purchaseCost?.toString() ?? "", warrantyEnd: a.warrantyEnd ?? "", vendorName: a.vendorName ?? "", notes: a.notes ?? "", fields: (a.values ?? {}) as FormValues,
});

/** Add or edit an asset. The type decides the extra questions (they are answered with the same form renderer as requests). */
export function AssetDrawer({ open, onClose, onDone, editing }: { open: boolean; onClose: () => void; onDone: (a: AssetDetail) => void; editing: AssetDetail | null }) {
  const run = useRun();
  const types = useQuery(() => (open ? get<AssetTypeItem[]>("/api/asset-types", { includeArchived: !!editing }) : Promise.resolve([] as AssetTypeItem[])), [open]);
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const [d, setD] = useState<Draft>(empty()); const [seed, setSeed] = useState<string | null>(null); const [changePerson, setChangePerson] = useState(false);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) { setSeed(key); setD(editing ? fromAsset(editing) : empty()); setErrs({}); setErr(""); setChangePerson(false); }
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }));
  const type = (types.data ?? []).find((x) => x.id === d.typeId);
  const offered = (types.data ?? []).filter((x) => !x.archived || x.id === editing?.typeId);
  const searchPeople = useCallback(async (q: string) => (await get<PortalPerson[]>("/api/portal/people", { q })).map((p) => ({ value: p.id, label: p.department ? `${p.name} · ${p.department}` : p.name })), []);
  const fieldErrors = Object.fromEntries(Object.entries(errs).filter(([k]) => k.startsWith("fields.")).map(([k, v]) => [k.slice(7), v]));

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = {
      tag: d.tag || null, name: d.name, typeId: d.typeId || null, serial: d.serial || null, status: d.status, locationId: d.locationId || null, assignedToId: d.assignedToId || null, department: d.department || null,
      purchaseDate: d.purchaseDate || null, purchaseCost: d.purchaseCost === "" ? null : Number(d.purchaseCost), warrantyEnd: d.warrantyEnd || null, vendorName: d.vendorName || null, notes: d.notes || null,
      fields: d.fields, version: editing?.version,
    };
    const r = await run(() => (editing ? put<AssetDetail>(`/api/assets/${editing.id}`, body) : post<AssetDetail>("/api/assets", body)), editing ? t("Asset saved.") : t("Asset added."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit asset") : t("New asset")} description={editing ? `${editing.tag} · ${editing.name}` : t("Leave the tag empty to get the next automatic one.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="asset-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="asset-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Type")} htmlFor="as-type" required error={errs.typeId}>
          <SelectInput id="as-type" value={d.typeId} onChange={(e) => { set("typeId", e.target.value); set("fields", {}); }} placeholder={t("Choose…")} options={offered.map((x) => ({ value: x.id, label: x.name }))} />
        </Field>
        <Field label={t("Name")} htmlFor="as-name" required error={errs.name}><TextInput id="as-name" data-autofocus maxLength={150} value={d.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Tag")} htmlFor="as-tag" error={errs.tag}><TextInput id="as-tag" maxLength={40} placeholder="AST-000123" value={d.tag} onChange={(e) => set("tag", e.target.value)} /></Field>
          <Field label={t("Serial number")} htmlFor="as-serial" error={errs.serial}><TextInput id="as-serial" maxLength={100} value={d.serial} onChange={(e) => set("serial", e.target.value)} /></Field>
        </div>
        <Field label={t("Status")} htmlFor="as-status" error={errs.status}>
          <SelectInput id="as-status" value={d.status} onChange={(e) => set("status", e.target.value as AssetStatus)} options={ASSET_STATUSES.filter((s) => s !== "retired").map((s) => ({ value: s, label: assetStatusLabel(s) }))} />
        </Field>
        <Field label={t("Place")} htmlFor="as-place" error={errs.locationId}>
          <SelectInput id="as-place" value={d.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Not set") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} />
        </Field>
        <Field label={t("With whom")} htmlFor="as-person" error={errs.assignedToId}>
          {d.assignedToId && !changePerson
            ? <span className="row" style={{ gap: 8 }}><b>{d.assignedToName || t("Someone")}</b><Button size="sm" onClick={() => setChangePerson(true)}>{t("Change")}</Button><Button size="sm" onClick={() => setD((x) => ({ ...x, assignedToId: "", assignedToName: "" }))}>{t("Take back")}</Button></span>
            : <PersonPicker id="as-person" value={d.assignedToId} onChange={(v) => setD((x) => ({ ...x, assignedToId: v ?? "", assignedToName: "" }))} search={searchPeople} />}
        </Field>
        <Field label={t("Department")} htmlFor="as-dept" error={errs.department}><TextInput id="as-dept" maxLength={100} value={d.department} onChange={(e) => set("department", e.target.value)} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Purchase date")} htmlFor="as-bought" error={errs.purchaseDate}><TextInput id="as-bought" type="date" value={d.purchaseDate} onChange={(e) => set("purchaseDate", e.target.value)} /></Field>
          <Field label={t("Purchase cost")} htmlFor="as-cost" error={errs.purchaseCost}><TextInput id="as-cost" type="number" min={0} step="0.01" value={d.purchaseCost} onChange={(e) => set("purchaseCost", e.target.value)} /></Field>
          <Field label={t("Warranty ends")} htmlFor="as-warranty" error={errs.warrantyEnd}><TextInput id="as-warranty" type="date" value={d.warrantyEnd} onChange={(e) => set("warrantyEnd", e.target.value)} /></Field>
        </div>
        <Field label={t("Vendor")} htmlFor="as-vendor" error={errs.vendorName}><TextInput id="as-vendor" maxLength={120} value={d.vendorName} onChange={(e) => set("vendorName", e.target.value)} /></Field>
        <Field label={t("Notes")} htmlFor="as-notes" error={errs.notes}><textarea id="as-notes" rows={3} maxLength={2000} className="textarea" value={d.notes} onChange={(e) => set("notes", e.target.value)} /></Field>
        {type && type.fields.length > 0 && <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Details for {type}", { type: type.name })}</legend>
          <DynamicForm fields={type.fields} values={d.fields} onChange={(v) => set("fields", v)} errors={fieldErrors} idPrefix="af" />
        </fieldset>}
      </form>
    </Overlay>
  );
}

/** Retiring takes an asset out of service for good (it can be restored); a reason is required. */
export function RetireDialog({ asset, onClose, onDone }: { asset: AssetDetail | null; onClose: () => void; onDone: (a: AssetDetail) => void }) {
  const run = useRun();
  const [reason, setReason] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seed, setSeed] = useState<string | null>(null);
  if ((asset?.id ?? null) !== seed) { setSeed(asset?.id ?? null); setReason(""); setErr(""); }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!asset) return; setErr(""); setBusy(true);
    const r = await run(() => post<AssetDetail>(`/api/assets/${asset.id}/retire`, { reason, version: asset.version }), t("Asset retired."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.reason ?? r.error); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={!!asset} onClose={onClose} title={t("Retire this asset?")} description={asset ? `${asset.tag} · ${asset.name}` : undefined}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant="danger" type="submit" form="retire-form" disabled={busy}>{busy ? t("Saving…") : t("Retire")}</Button></>}>
      <form id="retire-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <p style={{ margin: 0 }}>{t("It leaves the register's usual list and can no longer be chosen on a request. Its history stays, and it can be restored.")}</p>
        <Field label={t("Why?")} htmlFor="rt-reason" required><textarea id="rt-reason" data-autofocus rows={3} maxLength={300} className="textarea" value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
      </form>
    </Overlay>
  );
}
