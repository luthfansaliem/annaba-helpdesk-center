"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, Pencil, Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { FieldDrawer } from "@/components/forms/field-drawer";
import { Can, Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { AssetTypeItem, FieldType, FormField } from "@/lib/api/types";
import { describeErrors, newField, typeLabel } from "@/lib/forms";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Asset questions are simple: the asset has its own place and person, and files belong to requests. */
const ASSET_FIELD_TYPES: FieldType[] = ["text", "long_text", "number", "date", "select", "yes_no"];

function TypeDrawer({ open, onClose, onDone, editing }: { open: boolean; onClose: () => void; onDone: () => void; editing: AssetTypeItem | null }) {
  const run = useRun();
  const [name, setName] = useState(""); const [description, setDescription] = useState(""); const [fields, setFields] = useState<FormField[]>([]); const [seed, setSeed] = useState<string | null>(null);
  const [edit, setEdit] = useState<{ index: number; field: FormField } | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [problems, setProblems] = useState<string[]>([]); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) { setSeed(key); setName(editing?.name ?? ""); setDescription(editing?.description ?? ""); setFields(editing?.fields ?? []); setErrs({}); setErr(""); setProblems([]); setEdit(null); }
  const taken = (skip: number) => fields.filter((_, k) => k !== skip).map((f) => f.key);
  const move = (i: number, d: -1 | 1) => setFields((x) => { const j = i + d; if (j < 0 || j >= x.length) return x; const c = [...x]; [c[i], c[j]] = [c[j], c[i]]; return c; });
  const remove = (i: number) => setFields((x) => { const gone = x[i].key; return x.filter((_, k) => k !== i).map((f) => (f.visibleWhen?.field === gone ? { ...f, visibleWhen: null } : f)); });

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setProblems([]); setBusy(true);
    const body = { name, description: description || null, fields, version: editing?.version };
    const r = await run(() => (editing ? put(`/api/asset-types/${editing.id}`, body) : post("/api/asset-types", body)), editing ? t("Asset type saved.") : t("Asset type created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); setProblems(r.fields ? describeErrors(Object.fromEntries(Object.entries(r.fields).filter(([k]) => k.startsWith("fields"))), fields) : []); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit asset type") : t("New asset type")} description={t("The extra questions every asset of this type answers. Changing them never rewrites what assets already have.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="atype-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="atype-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}{problems.length > 0 && <ul>{problems.map((p) => <li key={p}>{p}</li>)}</ul>}</div>}
        <Field label={t("Name")} htmlFor="at-name" required error={errs.name}><TextInput id="at-name" data-autofocus maxLength={100} placeholder={t("e.g. Air conditioner")} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label={t("Description")} htmlFor="at-desc" error={errs.description}><TextInput id="at-desc" maxLength={300} value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Questions")}</legend>
          {fields.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No questions: assets of this type have only the standard details.")}</p> : (
            <ol className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
              {fields.map((f, i) => (
                <li key={f.key} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
                  <span><b>{f.label || f.key}</b> {f.required && <Badge tone="blue" dot={false}>{t("Required")}</Badge>}<br /><small>{typeLabel(f.type)} · <code>{f.key}</code></small></span>
                  <span className="row" style={{ gap: 4 }}>
                    <IconButton label={`${t("Move up")} ${f.label}`} disabled={i === 0} onClick={() => move(i, -1)}><ArrowUp size={14} /></IconButton>
                    <IconButton label={`${t("Move down")} ${f.label}`} disabled={i === fields.length - 1} onClick={() => move(i, 1)}><ArrowDown size={14} /></IconButton>
                    <IconButton label={`${t("Edit")} ${f.label}`} onClick={() => setEdit({ index: i, field: f })}><Pencil size={14} /></IconButton>
                    <IconButton label={`${t("Remove")} ${f.label}`} onClick={() => remove(i)}><Trash2 size={14} /></IconButton>
                  </span>
                </li>
              ))}
            </ol>
          )}
          <div style={{ marginTop: 8 }}><Button size="sm" onClick={() => setEdit({ index: -1, field: newField(taken(-1)) })}><Plus size={14} /> {t("Add question")}</Button></div>
        </fieldset>
      </form>
      {edit && <FieldDrawer open onClose={() => setEdit(null)} types={ASSET_FIELD_TYPES} isNew={edit.index < 0} initial={edit.field}
        onSave={(f) => setFields((x) => (edit.index >= 0 ? x.map((o, k) => (k === edit.index ? f : o)) : [...x, f]))}
        others={(edit.index < 0 ? fields : fields.slice(0, edit.index)).filter((o) => o.key !== edit.field.key)} />}
    </Overlay>
  );
}

function Types() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const q = useQuery(() => get<AssetTypeItem[]>("/api/asset-types", { includeArchived: archived }), [archived]);
  const [drawer, setDrawer] = useState<{ open: boolean; row: AssetTypeItem | null }>({ open: false, row: null });
  const act = async (x: AssetTypeItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/asset-types/${x.id}/${what}`), what === "archive" ? t("Asset type archived.") : t("Asset type restored."))).ok) q.reload(); };
  const cols: Column<AssetTypeItem>[] = [
    { key: "n", header: t("Type"), cell: (x) => <><b>{x.name}</b>{x.description && <><br /><small>{x.description}</small></>}</> },
    { key: "f", header: t("Questions"), cell: (x) => x.fields.length },
    { key: "a", header: t("Assets in service"), cell: (x) => x.assets },
    { key: "s", header: t("Status"), cell: (x) => <Badge tone={x.archived ? "gray" : "green"}>{x.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "x", header: t("Actions"), cell: (x) => <Can perm="assets.manage"><span className="row" style={{ gap: 6 }}>
      {!x.archived && <Button size="sm" aria-label={`${t("Edit")} ${x.name}`} onClick={() => setDrawer({ open: true, row: x })}>{t("Edit")}</Button>}
      <Button size="sm" aria-label={`${x.archived ? t("Restore") : t("Archive")} ${x.name}`} onClick={() => void act(x, x.archived ? "restore" : "archive")}>{x.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Asset Types")} description={t("Kinds of things you look after, such as air conditioners, vehicles or generators, and the details you keep for each.")}
        actions={<Can perm="assets.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New asset type")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("Asset types")} columns={cols} rows={q.data ?? []} rowKey={(x) => x.id} emptyMessage={t("No asset types yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <TypeDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} />
    </>
  );
}

export default function AssetTypesPage() { return <Require perm="assets.read"><Types /></Require>; }
