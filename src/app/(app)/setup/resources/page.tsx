"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { FEATURES, KINDS, KindIcon, featureLabel, kindLabel } from "@/components/bookings/booking-bits";
import { DocumentsDrawer, LicencesPanel } from "@/components/bookings/vehicle-papers";
import { get, post, put } from "@/lib/api/client";
import type { PortalLocation, ResourceItem, ResourceKind, RoleListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const DAYS = () => [t("Mon"), t("Tue"), t("Wed"), t("Thu"), t("Fri"), t("Sat"), t("Sun")];
const daysText = (mask: number) => (mask === 127 ? t("Every day") : mask === 31 ? t("Mon–Fri") : DAYS().filter((_, i) => mask & (1 << i)).join(", "));
const hoursText = (r: ResourceItem) => (r.openFrom && r.openUntil ? `${r.openFrom}–${r.openUntil}` : t("Around the clock"));

type Draft = { kind: ResourceKind; name: string; code: string; description: string; locationId: string; capacity: string; features: string[]; openFrom: string; openUntil: string; openDays: number; bufferMinutes: string; maxHours: string; allowedRoleIds: string[]; needsApproval: boolean; checkIn: string };
const blank: Draft = { kind: "room", name: "", code: "", description: "", locationId: "", capacity: "", features: [], openFrom: "08:00", openUntil: "18:00", openDays: 31, bufferMinutes: "0", maxHours: "", allowedRoleIds: [], needsApproval: false, checkIn: "" };

function ResourceDrawer({ open, editing, onClose, onDone }: { open: boolean; editing: ResourceItem | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const roles = useQuery(() => (open ? get<RoleListItem[]>("/api/roles").catch(() => [] as RoleListItem[]) : Promise.resolve([] as RoleListItem[])), [open]);
  const [d, setD] = useState<Draft>(blank); const [seed, setSeed] = useState<string | null>(null); const [allDay, setAllDay] = useState(false);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr("");
    setD(editing ? { kind: editing.kind, name: editing.name, code: editing.code ?? "", description: editing.description ?? "", locationId: editing.locationId ?? "", capacity: editing.capacity ? String(editing.capacity) : "",
      features: editing.features, openFrom: editing.openFrom ?? "08:00", openUntil: editing.openUntil ?? "18:00", openDays: editing.openDays, bufferMinutes: String(editing.bufferMinutes),
      maxHours: editing.maxMinutes ? String(editing.maxMinutes / 60) : "", allowedRoleIds: editing.allowedRoleIds, needsApproval: editing.needsApproval, checkIn: editing.checkInMinutes ? String(editing.checkInMinutes) : "" } : blank);
    setAllDay(editing ? !editing.openFrom : false);
  }
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => setD((x) => ({ ...x, [k]: v }));
  const toggle = (list: string[], v: string) => (list.includes(v) ? list.filter((x) => x !== v) : [...list, v]);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { kind: d.kind, name: d.name, code: d.code || null, description: d.description || null, locationId: d.locationId || null, capacity: d.capacity ? Number(d.capacity) : null,
      features: d.features, openFrom: allDay ? null : d.openFrom, openUntil: allDay ? null : d.openUntil, openDays: d.openDays, bufferMinutes: Number(d.bufferMinutes || 0),
      maxMinutes: d.maxHours ? Math.round(Number(d.maxHours) * 60) : null, needsApproval: d.needsApproval, checkInMinutes: d.checkIn ? Number(d.checkIn) : null, allowedRoleIds: d.allowedRoleIds, version: editing?.version };
    const r = await run(() => (editing ? put<ResourceItem>(`/api/resources/${editing.id}`, body) : post<ResourceItem>("/api/resources", body)), editing ? t("Resource saved.") : t("Resource added."), { silent: false });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit resource") : t("New resource")} description={t("Something people book by the hour.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="resource-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="resource-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Kind")} htmlFor="rs-kind" required error={errs.kind}><SelectInput id="rs-kind" value={d.kind} onChange={(e) => set("kind", e.target.value as ResourceKind)} options={KINDS.map((k) => ({ value: k, label: kindLabel(k) }))} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Name")} htmlFor="rs-name" required error={errs.name}><TextInput id="rs-name" data-autofocus maxLength={120} placeholder={d.kind === "vehicle" ? t("e.g. Toyota Innova") : t("e.g. Garuda room")} value={d.name} onChange={(e) => set("name", e.target.value)} /></Field>
          <Field label={d.kind === "vehicle" ? t("Plate number") : t("Code")} htmlFor="rs-code" error={errs.code}><TextInput id="rs-code" maxLength={30} value={d.code} onChange={(e) => set("code", e.target.value)} /></Field>
        </div>
        <Field label={t("Place")} htmlFor="rs-place" error={errs.locationId}><SelectInput id="rs-place" value={d.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("No place") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
        {(d.kind === "room" || d.kind === "vehicle") && <Field label={d.kind === "vehicle" ? t("Seats") : t("Capacity (people)")} htmlFor="rs-cap" error={errs.capacity}><TextInput id="rs-cap" type="number" min={1} max={1000} value={d.capacity} onChange={(e) => set("capacity", e.target.value)} /></Field>}
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Features")}</legend>
          {errs.features && <p className="field-error" role="alert">{errs.features}</p>}
          <div className="filters" style={{ margin: 0 }}>{FEATURES.map((f) => <label key={f} className="row chk"><Checkbox checked={d.features.includes(f)} onChange={() => set("features", toggle(d.features, f))} /> {featureLabel(f)}</label>)}</div>
        </fieldset>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("When it can be booked")}</legend>
          <div className="filters" style={{ margin: 0 }} role="group" aria-label={t("Days")}>
            {DAYS().map((name, i) => <label key={i} className="row chk"><Checkbox checked={(d.openDays & (1 << i)) !== 0} onChange={() => set("openDays", d.openDays ^ (1 << i))} /> {name}</label>)}
          </div>
          {errs.openDays && <p className="field-error" role="alert">{errs.openDays}</p>}
          <label className="row" style={{ gap: 8, margin: "10px 0" }}><Checkbox checked={allDay} onChange={(e) => setAllDay(e.target.checked)} /> {t("Around the clock")}</label>
          {!allDay && (
            <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
              <Field label={t("Opens")} htmlFor="rs-from" error={errs.openFrom}><TextInput id="rs-from" type="time" step={300} value={d.openFrom} onChange={(e) => set("openFrom", e.target.value)} /></Field>
              <Field label={t("Closes")} htmlFor="rs-until" error={errs.openUntil}><TextInput id="rs-until" type="time" step={300} value={d.openUntil} onChange={(e) => set("openUntil", e.target.value)} /></Field>
            </div>
          )}
        </fieldset>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Keep free after each booking (minutes)")} htmlFor="rs-buf" error={errs.bufferMinutes}><TextInput id="rs-buf" type="number" min={0} max={240} step={5} value={d.bufferMinutes} onChange={(e) => set("bufferMinutes", e.target.value)} /></Field>
          <Field label={t("Longest booking (hours)")} htmlFor="rs-max" error={errs.maxMinutes}><TextInput id="rs-max" type="number" min={0.25} step={0.25} placeholder={t("No limit")} value={d.maxHours} onChange={(e) => set("maxHours", e.target.value)} /></Field>
        </div>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Rules")}</legend>
          <label className="row" style={{ gap: 8, marginBottom: 10 }}><Checkbox checked={d.needsApproval} onChange={(e) => set("needsApproval", e.target.checked)} /> {t("Bookings need approval by General Affairs")}</label>
          <Field label={t("Release it when nobody checks in within")} htmlFor="rs-ci" error={errs.checkInMinutes}>
            <SelectInput id="rs-ci" value={d.checkIn} onChange={(e) => set("checkIn", e.target.value)} options={[{ value: "", label: t("No check-in needed") }, ...[10, 15, 20, 30, 60].map((m) => ({ value: String(m), label: t("{n} minutes of the start", { n: m }) }))]} />
          </Field>
        </fieldset>
        <Field label={t("Description")} htmlFor="rs-desc" error={errs.description}><textarea id="rs-desc" className="textarea" rows={2} maxLength={1000} value={d.description} onChange={(e) => set("description", e.target.value)} /></Field>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 4 }}>{t("Who may book it")}</legend>
          <p className="field-help" style={{ marginTop: 0 }}>{t("Leave all unticked so everyone who may book can book it.")}</p>
          {errs.allowedRoleIds && <p className="field-error" role="alert">{errs.allowedRoleIds}</p>}
          {(roles.data ?? []).length === 0 ? <p className="field-help">{t("Restricting by role needs permission to see roles.")}</p>
            : <div className="filters" style={{ margin: 0 }}>{roles.data!.filter((r) => !r.archived).map((r) => <label key={r.id} className="row chk"><Checkbox checked={d.allowedRoleIds.includes(r.id)} onChange={() => set("allowedRoleIds", toggle(d.allowedRoleIds, r.id))} /> {r.name}</label>)}</div>}
        </fieldset>
      </form>
    </Overlay>
  );
}

function Resources() {
  const run = useRun();
  const [archived, setArchived] = useState(false); const [kind, setKind] = useState(""); const [tick, setTick] = useState(0);
  const [drawer, setDrawer] = useState<{ editing: ResourceItem | null } | null>(null); const [papers, setPapers] = useState<ResourceItem | null>(null);
  const list = useQuery(() => get<ResourceItem[]>("/api/resources", { archived, kind: kind || undefined }), [archived, kind, tick]);
  const reload = () => setTick((n) => n + 1);
  const toggleArchive = async (r: ResourceItem) => {
    const res = await run(() => post<ResourceItem>(`/api/resources/${r.id}/${r.archived ? "restore" : "archive"}`), r.archived ? t("Resource restored.") : t("Resource archived."), { silent: false });
    if (res.ok) reload();
  };
  const cols: Column<ResourceItem>[] = [
    { key: "n", header: t("Resource"), cell: (r) => <><button type="button" className="link-btn" onClick={() => setDrawer({ editing: r })}><b>{r.name}</b></button>{r.code && <> <small className="muted">{r.code}</small></>}{r.archived && <> <Badge tone="gray">{t("Archived")}</Badge></>}<br /><small className="muted"><KindIcon kind={r.kind} size={12} /> {kindLabel(r.kind)}{r.capacity ? ` · ${t("{n} people", { n: r.capacity })}` : ""}</small></> },
    { key: "p", header: t("Place"), className: "hide-sm", cell: (r) => r.place ?? "—" },
    { key: "h", header: t("Bookable"), cell: (r) => <>{daysText(r.openDays)}<br /><small className="muted">{hoursText(r)}{r.bufferMinutes ? ` · ${t("{n} min buffer", { n: r.bufferMinutes })}` : ""}</small></> },
    { key: "f", header: t("Features"), className: "hide-sm", cell: (r) => (r.features.length ? r.features.map(featureLabel).join(", ") : "—") },
    { key: "w", header: t("Who"), className: "hide-sm", cell: (r) => <>{r.allowedRoleIds.length ? t("{n} roles", { n: r.allowedRoleIds.length }) : t("Everyone")}{(r.needsApproval || r.checkInMinutes) && <><br /><small className="muted">{[r.needsApproval ? t("Needs approval") : null, r.checkInMinutes ? t("Check-in within {n} min", { n: r.checkInMinutes }) : null].filter(Boolean).join(" · ")}</small></>}</> },
    { key: "a", header: <span className="sr-only">{t("Actions")}</span>, cell: (r) => <span className="row" style={{ gap: 6, flexWrap: "nowrap" }}>{r.kind === "vehicle" && <Button size="sm" onClick={() => setPapers(r)} aria-label={`${t("Documents")} ${r.name}`}>{t("Documents")}</Button>}<Button size="sm" variant={r.archived ? "outline" : "danger-outline"} onClick={() => toggleArchive(r)}>{r.archived ? t("Restore") : t("Archive")}</Button></span> },
  ];
  return (
    <>
      <PageHeader title={t("Bookable resources")} description={t("Meeting rooms, pool vehicles, parking slots and shared equipment that people book from Bookings.")}
        actions={<Button variant="primary" onClick={() => setDrawer({ editing: null })}><Plus size={16} /> {t("New resource")}</Button>} />
      <Panel>
        <div className="filters">
          <Field label={t("Kind")} htmlFor="rl-kind"><SelectInput id="rl-kind" value={kind} onChange={(e) => setKind(e.target.value)} options={[{ value: "", label: t("All kinds") }, ...KINDS.map((k) => ({ value: k, label: kindLabel(k) }))]} /></Field>
          <label className="row chk"><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        </div>
        <DataTable label={t("Bookable resources")} columns={cols} rows={list.data ?? []} rowKey={(r) => r.id} emptyMessage={t("No resources yet. Add the first meeting room or vehicle.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
      </Panel>
      <LicencesPanel />
      <ResourceDrawer open={!!drawer} editing={drawer?.editing ?? null} onClose={() => setDrawer(null)} onDone={reload} />
      <DocumentsDrawer resource={papers} onClose={() => setPapers(null)} />
    </>
  );
}

export default function ResourcesPage() { return <Require perm="resources.manage"><Resources /></Require>; }
