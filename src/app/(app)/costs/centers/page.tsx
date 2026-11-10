"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { CostCenterItem, PortalLocation } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function CenterDrawer({ open, editing, onClose, onDone }: { open: boolean; editing: CostCenterItem | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [code, setCode] = useState(""); const [name, setName] = useState(""); const [places, setPlaces] = useState<string[]>([]); const [placeQ, setPlaceQ] = useState(""); const [seed, setSeed] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) { setSeed(key); setErrs({}); setErr(""); setPlaceQ(""); setCode(editing?.code ?? ""); setName(editing?.name ?? ""); setPlaces(editing?.places.map((p) => p.locationId) ?? []); }
  const locs = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const shown = (locs.data ?? []).filter((l) => !placeQ || l.path.toLowerCase().includes(placeQ.toLowerCase())).slice(0, 60);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { code, name, locationIds: places, version: editing?.version };
    const r = await run(() => (editing ? put(`/api/costs/centers/${editing.id}`, body) : post("/api/costs/centers", body)), t("Cost centre saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit cost centre") : t("New cost centre")} description={t("Costs of work at its places (and the places below them) are charged to it.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cc-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="cc-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Code")} htmlFor="cc-code" required error={errs.code}><TextInput id="cc-code" data-autofocus maxLength={20} value={code} onChange={(e) => setCode(e.target.value)} /></Field>
        <Field label={t("Name")} htmlFor="cc-name" required error={errs.name}><TextInput id="cc-name" maxLength={100} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Places it pays for")}</legend>
          {errs.locationIds && <p className="field-error" role="alert">{errs.locationIds}</p>}
          <Field label={t("Find a place")} htmlFor="cc-pq"><TextInput id="cc-pq" value={placeQ} onChange={(e) => setPlaceQ(e.target.value)} /></Field>
          <p className="sub" style={{ margin: "6px 0" }}>{t("A place belongs to one cost centre. {n} chosen.", { n: places.length })}</p>
          <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 4, maxHeight: 260, overflow: "auto" }}>
            {shown.map((l) => <li key={l.id}><label className="row" style={{ gap: 8 }}><Checkbox checked={places.includes(l.id)} onChange={(e) => setPlaces((s) => (e.target.checked ? [...s, l.id] : s.filter((x) => x !== l.id)))} /> {l.path}</label></li>)}
          </ul>
        </fieldset>
      </form>
    </Overlay>
  );
}

function Centers() {
  const run = useRun(); const { can } = useAuth();
  const list = useQuery(() => get<CostCenterItem[]>("/api/costs/centers"), []);
  const [drawer, setDrawer] = useState<{ open: boolean; row: CostCenterItem | null }>({ open: false, row: null });
  const toggle = async (c: CostCenterItem) => { const r = await run(() => post(`/api/costs/centers/${c.id}/${c.archived ? "restore" : "archive"}`), c.archived ? t("Cost centre restored.") : t("Cost centre archived.")); if (r.ok) list.reload(); };
  const cols: Column<CostCenterItem>[] = [
    { key: "c", header: t("Code"), cell: (c) => <b>{c.code}</b> },
    { key: "n", header: t("Name"), cell: (c) => <>{c.name}{c.archived && <> <Badge tone="gray">{t("Archived")}</Badge></>}</> },
    { key: "p", header: t("Places"), cell: (c) => (c.places.length === 0 ? "—" : c.places.map((p) => p.path).join("; ")) },
    ...(can("budgets.manage") ? [{ key: "x", header: <span className="sr-only">{t("Actions")}</span>, cell: (c: CostCenterItem) => <span className="row" style={{ gap: 6 }}>
      {!c.archived && <Button size="sm" onClick={() => setDrawer({ open: true, row: c })}>{t("Edit")}</Button>}
      <Button size="sm" onClick={() => void toggle(c)}>{c.archived ? t("Restore") : t("Archive")}</Button></span> }] : []),
  ];
  return (
    <>
      <PageHeader title={t("Cost centres")} description={t("Who pays for the work: each cost centre covers places, and the cost report and budgets use them.")}
        actions={<Can perm="budgets.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New cost centre")}</Button></Can>} />
      <Panel>
        <DataTable label={t("Cost centres")} columns={cols} rows={list.data ?? []} rowKey={(c) => c.id} emptyMessage={t("No cost centres yet.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
      </Panel>
      <CenterDrawer open={drawer.open} editing={drawer.row} onClose={() => setDrawer({ open: false, row: null })} onDone={list.reload} />
    </>
  );
}

export default function CostCentersPage() { return <Require perm="budgets.read"><Centers /></Require>; }
