"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { get, post, put } from "@/lib/api/client";
import type { AssetPick, AssetTypeItem, AssigneeOption, ChecklistTemplateItem, PlanDetail, PmUnit, PortalLocation, ServiceListItem, TeamItem, VendorPick, VisitState } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** "Monthly", "Every 3 months"… */
export function rhythm(unit: PmUnit, every: number): string {
  if (every === 1) return { day: t("Daily"), week: t("Weekly"), month: t("Monthly"), year: t("Yearly") }[unit];
  return { day: t("Every {n} days", { n: every }), week: t("Every {n} weeks", { n: every }), month: t("Every {n} months", { n: every }), year: t("Every {n} years", { n: every }) }[unit];
}

const VISIT: Record<VisitState, [BadgeTone, () => string]> = {
  open: ["blue", () => t("Planned")], overdue: ["red", () => t("Overdue")], on_time: ["green", () => t("Done on time")], late: ["orange", () => t("Done late")],
  cancelled: ["gray", () => t("Cancelled")], missed: ["red", () => t("Missed")], removed: ["gray", () => t("Removed")],
};
export function VisitBadge({ state }: { state: VisitState }) { const [tone, label] = VISIT[state]; return <Badge tone={tone}>{label()}</Badge>; }

type Kind = "asset" | "type" | "place";
const blank = { name: "", instructions: "", serviceId: "", typeId: "", locationId: "", unit: "month" as PmUnit, every: "1", startsOn: "", endsOn: "", leadDays: "7", checklistId: "", teamId: "", assigneeId: "", vendorId: "", photo: false };

/** Create or change a maintenance plan. */
export function PlanDrawer({ open, editing, onClose, onDone }: { open: boolean; editing: PlanDetail | null; onClose: () => void; onDone: (p: PlanDetail) => void }) {
  const run = useRun();
  const [f, setF] = useState(blank); const [kind, setKind] = useState<Kind>("type"); const [asset, setAsset] = useState<{ id: string; label: string } | null>(null); const [assetQ, setAssetQ] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seed, setSeed] = useState<string | null>(null);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr(""); setAssetQ("");
    const e = editing;
    setF(e ? { name: e.name, instructions: e.instructions ?? "", serviceId: e.serviceId, typeId: e.assetTypeId ?? "", locationId: e.locationId ?? "", unit: e.unit, every: String(e.every), startsOn: e.startsOn, endsOn: e.endsOn ?? "",
      leadDays: String(e.leadDays), checklistId: e.checklistTemplateId ?? "", teamId: e.teamId ?? "", assigneeId: e.assigneeId ?? "", vendorId: e.vendorId ?? "", photo: e.requireAfterPhoto } : blank);
    setKind(e ? (e.assetId ? "asset" : e.assetTypeId ? "type" : "place") : "type");
    setAsset(e?.assetId ? { id: e.assetId, label: e.assetLabel ?? "" } : null);
  }
  const set = <K extends keyof typeof blank>(k: K, v: (typeof blank)[K]) => setF((x) => ({ ...x, [k]: v }));
  const services = useQuery(() => (open ? get<ServiceListItem[]>("/api/catalog/services").catch(() => [] as ServiceListItem[]) : Promise.resolve([] as ServiceListItem[])), [open]);
  const types = useQuery(() => (open ? get<AssetTypeItem[]>("/api/asset-types").catch(() => [] as AssetTypeItem[]) : Promise.resolve([] as AssetTypeItem[])), [open]);
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const lists = useQuery(() => (open ? get<ChecklistTemplateItem[]>("/api/checklist-templates").catch(() => [] as ChecklistTemplateItem[]) : Promise.resolve([] as ChecklistTemplateItem[])), [open]);
  const teams = useQuery(() => (open ? get<TeamItem[]>("/api/teams").catch(() => [] as TeamItem[]) : Promise.resolve([] as TeamItem[])), [open]);
  const people = useQuery(() => (open ? get<AssigneeOption[]>("/api/tickets/assignees", { teamId: f.teamId || undefined }).catch(() => [] as AssigneeOption[]) : Promise.resolve([] as AssigneeOption[])), [open, f.teamId]);
  const vendors = useQuery(() => (open ? get<VendorPick[]>("/api/vendors/pick").catch(() => [] as VendorPick[]) : Promise.resolve([] as VendorPick[])), [open]);
  const term = assetQ.trim();
  const picks = useQuery(() => (open && term.length >= 2 ? get<AssetPick[]>("/api/assets/pick", { q: term }).catch(() => [] as AssetPick[]) : Promise.resolve([] as AssetPick[])), [open, term]);
  const keep = <T extends { id: string; name: string }>(rows: T[] | undefined, id: string, name: string | null | undefined) => [...(rows ?? []).map((r) => ({ value: r.id, label: r.name })), ...(id && name && !(rows ?? []).some((r) => r.id === id) ? [{ value: id, label: name }] : [])];

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = {
      name: f.name, instructions: f.instructions || null, serviceId: f.serviceId || null,
      assetId: kind === "asset" ? asset?.id ?? null : null, assetTypeId: kind === "type" ? f.typeId || null : null, locationId: kind === "asset" ? null : f.locationId || null,
      unit: f.unit, every: Number(f.every) || 0, startsOn: f.startsOn || null, endsOn: f.endsOn || null, leadDays: f.leadDays === "" ? null : Number(f.leadDays),
      checklistTemplateId: f.checklistId || null, teamId: f.teamId || null, assigneeId: f.assigneeId || null, vendorId: f.vendorId || null, requireAfterPhoto: f.photo, version: editing?.version,
    };
    const r = await run(() => (editing ? put<PlanDetail>(`/api/maintenance/plans/${editing.id}`, body) : post<PlanDetail>("/api/maintenance/plans", body)), t("Plan saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  const targetErr = errs.target ?? errs.assetId ?? errs.assetTypeId ?? errs.locationId;
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit plan") : t("New maintenance plan")} description={t("Work orders are made ahead of each date, with the checklist you choose.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="pm-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="pm-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="pm-name" required error={errs.name}><TextInput id="pm-name" data-autofocus maxLength={120} placeholder={t("e.g. AC quarterly service")} value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label={t("Filed under service")} htmlFor="pm-svc" required error={errs.serviceId}>
          <SelectInput id="pm-svc" value={f.serviceId} onChange={(e) => set("serviceId", e.target.value)} options={[{ value: "", label: t("Choose…") }, ...keep((services.data ?? []).filter((s) => !s.archived), f.serviceId, editing?.serviceName)]} /><p className="field-help">{t("The visits become tickets of this service. It may be unpublished, so only staff see it.")}</p></Field>

        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("What is serviced")}</legend>
          {targetErr && <p className="field-error" role="alert">{targetErr}</p>}
          <div className="row" style={{ gap: 16, flexWrap: "wrap" }} role="radiogroup" aria-label={t("What is serviced")}>
            {([["type", t("Every asset of a type")], ["asset", t("One asset")], ["place", t("A place")]] as [Kind, string][]).map(([k, label]) => (
              <label key={k} className="row" style={{ gap: 6 }}><input type="radio" name="pm-kind" checked={kind === k} onChange={() => setKind(k)} /> {label}</label>
            ))}
          </div>
          <div className="stack" style={{ marginTop: 8 }}>
            {kind === "type" && <>
              <Field label={t("Asset type")} htmlFor="pm-type"><SelectInput id="pm-type" value={f.typeId} onChange={(e) => set("typeId", e.target.value)} options={[{ value: "", label: t("Choose…") }, ...keep((types.data ?? []).filter((x) => !x.archived), f.typeId, editing?.assetTypeName)]} /></Field>
              <Field label={t("Only in this place")} htmlFor="pm-typeplace"><SelectInput id="pm-typeplace" value={f.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Anywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /><p className="field-help">{t("A place includes everything below it.")}</p></Field>
            </>}
            {kind === "asset" && <Field label={t("Asset")} htmlFor="pm-asset">
              {asset
                ? <span className="row" style={{ gap: 8 }}><b>{asset.label}</b><Button size="sm" onClick={() => setAsset(null)}>{t("Change")}</Button></span>
                : <div className="stack" style={{ gap: 6 }}>
                  <TextInput id="pm-asset" placeholder={t("Search by tag, name or serial")} value={assetQ} onChange={(e) => setAssetQ(e.target.value)} />
                  {(picks.data ?? []).length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 4 }} aria-label={t("Matching assets")}>
                    {(picks.data ?? []).map((a) => <li key={a.id}><Button size="sm" onClick={() => { setAsset({ id: a.id, label: `${a.tag} · ${a.name}` }); setAssetQ(""); }}>{a.tag} · {a.name}</Button> <small>{a.typeName}{a.locationPath ? ` · ${a.locationPath}` : ""}</small></li>)}
                  </ul>}
                  {term.length >= 2 && picks.data && picks.data.length === 0 && <small className="sub">{t("No asset matches.")}</small>}
                </div>}
            </Field>}
            {kind === "place" && <Field label={t("Place")} htmlFor="pm-place"><SelectInput id="pm-place" value={f.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Choose…") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>}
          </div>
        </fieldset>

        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Every")} htmlFor="pm-every" required error={errs.every}><TextInput id="pm-every" type="number" min={1} max={365} style={{ width: 90 }} value={f.every} onChange={(e) => set("every", e.target.value)} /></Field>
          <Field label={t("Unit")} htmlFor="pm-unit" error={errs.unit}><SelectInput id="pm-unit" value={f.unit} onChange={(e) => set("unit", e.target.value as PmUnit)} options={[{ value: "day", label: t("days") }, { value: "week", label: t("weeks") }, { value: "month", label: t("months") }, { value: "year", label: t("years") }]} /></Field>
          <Field label={t("First date")} htmlFor="pm-start" required error={errs.startsOn}><TextInput id="pm-start" type="date" value={f.startsOn} onChange={(e) => set("startsOn", e.target.value)} /></Field>
          <Field label={t("Last date")} htmlFor="pm-end" error={errs.endsOn}><TextInput id="pm-end" type="date" value={f.endsOn} onChange={(e) => set("endsOn", e.target.value)} /></Field>
        </div>
        <Field label={t("Make the work this many days before")} htmlFor="pm-lead" error={errs.leadDays}>
          <TextInput id="pm-lead" type="number" min={0} max={90} style={{ width: 90 }} value={f.leadDays} onChange={(e) => set("leadDays", e.target.value)} /><p className="field-help">{t("0–90 days. The work order is due at the end of the date itself.")}</p></Field>
        <Field label={t("Checklist")} htmlFor="pm-list" error={errs.checklistTemplateId}>
          <SelectInput id="pm-list" value={f.checklistId} onChange={(e) => set("checklistId", e.target.value)} options={[{ value: "", label: t("None") }, ...keep((lists.data ?? []).filter((x) => !x.archived), f.checklistId, editing?.checklistName)]} />
        </Field>
        <Field label={t("Instructions")} htmlFor="pm-instr" error={errs.instructions}><textarea id="pm-instr" rows={3} maxLength={4000} className="textarea" value={f.instructions} onChange={(e) => set("instructions", e.target.value)} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Team")} htmlFor="pm-team" error={errs.teamId}><SelectInput id="pm-team" value={f.teamId} onChange={(e) => { set("teamId", e.target.value); set("assigneeId", ""); }} options={[{ value: "", label: t("The service's team") }, ...keep((teams.data ?? []).filter((x) => !x.archived), f.teamId, editing?.teamName)]} /></Field>
          <Field label={t("Person")} htmlFor="pm-person" error={errs.assigneeId}><SelectInput id="pm-person" value={f.assigneeId} onChange={(e) => set("assigneeId", e.target.value)} options={[{ value: "", label: t("Nobody yet") }, ...keep(people.data, f.assigneeId, editing?.assigneeName)]} /></Field>
          <Field label={t("Vendor")} htmlFor="pm-vendor" error={errs.vendorId}><SelectInput id="pm-vendor" value={f.vendorId} onChange={(e) => set("vendorId", e.target.value)} options={[{ value: "", label: t("None (done in-house)") }, ...keep(vendors.data, f.vendorId, editing?.vendorName)]} /></Field>
        </div>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={f.photo} onChange={(e) => set("photo", e.target.checked)} /> {t("Needs a photo of the finished work")}</label>
      </form>
    </Overlay>
  );
}
