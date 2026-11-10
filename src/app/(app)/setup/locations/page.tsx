"use client";

import { useMemo, useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { get, patch, post } from "@/lib/api/client";
import type { CalendarItem, LocationItem, LocationType, SaveLocation } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TYPES: LocationType[] = ["region", "site", "building", "floor", "room"];
const RANK: Record<LocationType, number> = { region: 0, site: 1, building: 2, floor: 3, room: 4 };
const typeLabel = (x: LocationType) => ({ region: t("Region"), site: t("Site"), building: t("Building"), floor: t("Floor"), room: t("Room") })[x];
/** Mirrors the server rule: only a region or a site stands at the top; below a parent the type must be deeper. */
const canSit = (child: LocationType, parent: LocationItem | null) => (parent ? RANK[child] > RANK[parent.type] : child === "region" || child === "site");

/** Depth-first order so children follow their parent. */
function tree(rows: LocationItem[]): { row: LocationItem; depth: number }[] {
  const byParent = new Map<string | null, LocationItem[]>();
  const ids = new Set(rows.map((r) => r.id));
  for (const r of rows) { const k = r.parentId && ids.has(r.parentId) ? r.parentId : null; byParent.set(k, [...(byParent.get(k) ?? []), r]); }
  const out: { row: LocationItem; depth: number }[] = [];
  const walk = (k: string | null, depth: number) => { for (const r of (byParent.get(k) ?? []).sort((a, b) => RANK[a.type] - RANK[b.type] || a.name.localeCompare(b.name))) { out.push({ row: r, depth }); walk(r.id, depth + 1); } };
  walk(null, 0);
  return out;
}

const ZONES = ["Asia/Jakarta", "Asia/Makassar", "Asia/Jayapura", "Asia/Singapore", "Asia/Kuala_Lumpur", "Asia/Bangkok", "Asia/Manila", "Asia/Tokyo", "Asia/Dubai", "Europe/London", "Europe/Amsterdam", "America/New_York", "UTC"];

function LocationDrawer({ open, onClose, onDone, editing, all, calendars }: { open: boolean; onClose: () => void; onDone: () => void; editing: LocationItem | null; all: LocationItem[]; calendars: CalendarItem[] }) {
  const run = useRun();
  const blank: SaveLocation = { type: "site", parentId: null, code: "", name: "", address: null, timeZone: null, calendarId: null, managerUserId: null, capacity: null };
  const [f, setF] = useState<SaveLocation>(blank); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) { // load the form when the drawer opens for a different row
    setKey(current);
    setF(editing ? { type: editing.type, parentId: editing.parentId, code: editing.code, name: editing.name, address: editing.address, timeZone: editing.timeZone, calendarId: editing.calendarId, managerUserId: editing.managerUserId, capacity: editing.capacity, version: editing.version } : blank);
    setErrs({}); setErr("");
  }
  const parent = all.find((l) => l.id === f.parentId) ?? null;
  const parents = all.filter((l) => !l.archived && l.id !== editing?.id && RANK[l.type] < RANK[f.type]);
  const set = <K extends keyof SaveLocation>(k: K, v: SaveLocation[K]) => setF((x) => ({ ...x, [k]: v }));
  const typeOk = canSit(f.type, parent);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => (editing ? patch(`/api/locations/${editing.id}`, f) : post("/api/locations", f)), editing ? t("Location saved.") : t("Location created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit location") : t("New location")} description={t("Regions contain sites; sites contain buildings, floors and rooms.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="loc-form" disabled={busy || !typeOk}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="loc-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Type")} htmlFor="lc-type" required error={errs.type}>
          <SelectInput id="lc-type" value={f.type} onChange={(e) => { const ty = e.target.value as LocationType; setF((x) => ({ ...x, type: ty, parentId: all.find((l) => l.id === x.parentId && RANK[l.type] < RANK[ty]) ? x.parentId : null })); }} options={TYPES.map((x) => ({ value: x, label: typeLabel(x) }))} />
        </Field>
        <Field label={t("Inside")} htmlFor="lc-parent" error={errs.parentId ?? (typeOk ? undefined : t("Choose where this {type} is.", { type: typeLabel(f.type).toLowerCase() }))}>
          <SelectInput id="lc-parent" value={f.parentId ?? ""} onChange={(e) => set("parentId", e.target.value || null)} options={[{ value: "", label: t("— Top level —") }, ...parents.map((p) => ({ value: p.id, label: `${typeLabel(p.type)} · ${p.name}` }))]} />
        </Field>
        <Field label={t("Code")} htmlFor="lc-code" required error={errs.code}><TextInput id="lc-code" data-autofocus value={f.code} onChange={(e) => set("code", e.target.value.toUpperCase())} placeholder="JKT-HQ-L3" /></Field>
        <Field label={t("Name")} htmlFor="lc-name" required error={errs.name}><TextInput id="lc-name" value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label={t("Address")} htmlFor="lc-addr" error={errs.address}><TextInput id="lc-addr" value={f.address ?? ""} onChange={(e) => set("address", e.target.value || null)} /></Field>
        <Field label={t("Time zone")} htmlFor="lc-tz" error={errs.timeZone}>
          <TextInput id="lc-tz" list="lc-zones" value={f.timeZone ?? ""} onChange={(e) => set("timeZone", e.target.value || null)} placeholder={t("Inherited from the location above")} />
          <datalist id="lc-zones">{ZONES.map((z) => <option key={z} value={z} />)}</datalist>
        </Field>
        <Field label={t("Business calendar")} htmlFor="lc-cal" error={errs.calendarId}>
          <SelectInput id="lc-cal" value={f.calendarId ?? ""} onChange={(e) => set("calendarId", e.target.value || null)} options={[{ value: "", label: t("Default calendar") }, ...calendars.filter((c) => !c.archived).map((c) => ({ value: c.id, label: c.name }))]} />
        </Field>
        <Field label={t("Capacity")} htmlFor="lc-cap" error={errs.capacity}><TextInput id="lc-cap" type="number" min={0} value={f.capacity ?? ""} onChange={(e) => set("capacity", e.target.value === "" ? null : Number(e.target.value))} /></Field>
      </form>
    </Overlay>
  );
}

function Locations() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const q = useQuery(() => get<LocationItem[]>("/api/locations", { includeArchived: archived }), [archived]);
  const cals = useQuery(() => get<CalendarItem[]>("/api/calendars"), []);
  const [drawer, setDrawer] = useState<{ open: boolean; row: LocationItem | null }>({ open: false, row: null });
  const rows = useMemo(() => tree(q.data ?? []), [q.data]);
  const act = async (l: LocationItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/locations/${l.id}/${what}`), what === "archive" ? t("Location archived.") : t("Location restored."))).ok) q.reload(); };

  const cols: Column<{ row: LocationItem; depth: number }>[] = [
    { key: "n", header: t("Location"), cell: ({ row, depth }) => <span style={{ paddingLeft: depth * 20 }}><b>{row.name}</b> <small>{row.code}</small></span> },
    { key: "t", header: t("Type"), cell: ({ row }) => <Badge tone="blue" dot={false}>{typeLabel(row.type)}</Badge> },
    { key: "z", header: t("Time zone"), className: "hide-sm", cell: ({ row }) => row.timeZone ?? "—" },
    { key: "c", header: t("Capacity"), className: "hide-sm", cell: ({ row }) => row.capacity ?? "—" },
    { key: "s", header: t("Status"), cell: ({ row }) => <Badge tone={row.archived ? "gray" : "green"}>{row.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: ({ row }) => <Can perm="locations.manage"><span className="row" style={{ gap: 6 }}>
      {!row.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${row.name}`} onClick={() => setDrawer({ open: true, row })}>{t("Edit")}</Button>}
      <Button size="sm" variant="outline" aria-label={`${row.archived ? t("Restore") : t("Archive")} ${row.name}`} onClick={() => void act(row, row.archived ? "restore" : "archive")}>{row.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Locations")} description={t("Sites, buildings, floors and rooms. Requests, assets and bookings are tied to a location.")}
        actions={<Can perm="locations.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New location")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("Locations")} columns={cols} rows={rows} rowKey={({ row }) => row.id} emptyMessage={t("No locations yet. Add your first site.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <LocationDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} all={q.data ?? []} calendars={cals.data ?? []} />
    </>
  );
}

export default function LocationsPage() { return <Require perm="locations.read"><Locations /></Require>; }
