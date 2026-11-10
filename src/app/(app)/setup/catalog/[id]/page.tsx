"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { ArrowDown, ArrowLeft, ArrowUp, Pencil, Plus, Trash2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";

import { Field, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { DynamicForm, type FormValues } from "@/components/forms/dynamic-form";
import { FieldDrawer } from "@/components/forms/field-drawer";
import { get, put } from "@/lib/api/client";
import type { FormField, FormVersionItem, FormView, LocationItem, ServiceListItem } from "@/lib/api/types";
import { describeErrors, newField, typeLabel } from "@/lib/forms";
import { useAuth } from "@/lib/auth";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function Builder({ id }: { id: string }) {
  const run = useRun(); const { can } = useAuth();
  const svc = useQuery(() => get<ServiceListItem>(`/api/catalog/services/${id}`), [id]);
  const form = useQuery(() => get<FormView>(`/api/catalog/services/${id}/form`), [id]);
  const versions = useQuery(() => get<FormVersionItem[]>(`/api/catalog/services/${id}/form/versions`), [id]);
  const locations = useQuery(() => get<LocationItem[]>("/api/locations").catch(() => [] as LocationItem[]), []); // the preview only needs names
  const [draft, setDraft] = useState<FormField[]>([]); const [seed, setSeed] = useState<string | null>(null);
  const [note, setNote] = useState(""); const [busy, setBusy] = useState(false); const [problems, setProblems] = useState<string[]>([]); const [err, setErr] = useState("");
  const [edit, setEdit] = useState<{ index: number; field: FormField } | null>(null);
  const [preview, setPreview] = useState<FormValues>({});

  const saved = form.data;
  if (saved && seed !== `${saved.serviceId}:${saved.version}`) { setSeed(`${saved.serviceId}:${saved.version}`); setDraft(saved.fields); setProblems([]); setErr(""); }
  const dirty = !!saved && JSON.stringify(draft) !== JSON.stringify(saved.fields);
  const archived = svc.data?.archived ?? false;
  const canEdit = can("forms.manage") && !archived;

  const move = (i: number, d: -1 | 1) => setDraft((x) => { const j = i + d; if (j < 0 || j >= x.length) return x; const c = [...x]; [c[i], c[j]] = [c[j], c[i]]; return c; });
  const remove = (i: number) => setDraft((x) => { const gone = x[i].key; return x.filter((_, k) => k !== i).map((f) => (f.visibleWhen?.field === gone ? { ...f, visibleWhen: null } : f)); }); // dependants are always shown instead of dangling
  const apply = (f: FormField) => setDraft((x) => (edit && edit.index >= 0 ? x.map((o, k) => (k === edit.index ? f : o)) : [...x, f]));

  async function save() {
    if (!svc.data) return; setBusy(true); setErr(""); setProblems([]);
    const r = await run(() => put<FormView>(`/api/catalog/services/${id}/form`, { fields: draft, note: note.trim() || null, version: svc.data!.version }), t("Form saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setProblems(r.fields ? describeErrors(r.fields, draft) : []); return; }
    setNote(""); form.reload(); svc.reload(); versions.reload();
  }
  async function loadVersion(n: number) {
    const r = await run(() => get<FormView>(`/api/catalog/services/${id}/form/versions/${n}`), undefined, { silent: false });
    if (r.ok) { setDraft(r.data.fields); setNote(t("Restored from version {n}", { n })); }
  }

  const locOptions = (locations.data ?? []).filter((l) => !l.archived).map((l) => ({ value: l.id, label: l.name }));
  const takenKeys = (skip: number) => draft.filter((_, k) => k !== skip).map((f) => f.key);

  return (
    <QueryBoundary q={form}>{(v) => (
      <>
        <div className="crud-top">
          <div>
            <Link href="/setup/catalog" className="back-link"><ArrowLeft size={14} /> {t("Service Catalog")}</Link>
            <h1>{v.serviceName}</h1>
            <p>{v.version ? t("Form version {n} is in force. Saving a change creates a new version; requests already made keep the version they used.", { n: v.version }) : t("This service has no form yet.")}</p>
          </div>
          {svc.data && <Badge tone={archived ? "gray" : svc.data.isPublished ? "green" : "orange"}>{archived ? t("Archived") : svc.data.isPublished ? t("Published") : t("Draft")}</Badge>}
        </div>
        {archived && <div className="form-alert" role="status">{t("This service is archived. Restore it to edit its form.")}</div>}
        {err && <div className="form-alert err" role="alert">{err}{problems.length > 0 && <ul>{problems.map((p) => <li key={p}>{p}</li>)}</ul>}</div>}

        <div className="grid-2" style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", alignItems: "start" }}>
          <Panel title={t("Questions")} description={t("{n} in this draft", { n: draft.length })}
            actions={canEdit ? <Button variant="primary" size="sm" onClick={() => setEdit({ index: -1, field: newField(takenKeys(-1)) })}><Plus size={14} /> {t("Add question")}</Button> : undefined}>
            {draft.length === 0 ? <p className="tab-placeholder">{t("No questions yet.")}</p> : (
              <ol className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                {draft.map((f, i) => (
                  <li key={f.key} className="row" style={{ gap: 8, justifyContent: "space-between", alignItems: "flex-start" }}>
                    <span><b>{i + 1}. {f.label || f.key}</b> {f.required && <Badge tone="blue" dot={false}>{t("Required")}</Badge>}<br />
                      <small>{typeLabel(f.type)} · <code>{f.key}</code>{f.visibleWhen && <> · {t("conditional")}</>}</small></span>
                    {canEdit && <span className="row" style={{ gap: 4 }}>
                      <IconButton label={`${t("Move up")} ${f.label}`} disabled={i === 0} onClick={() => move(i, -1)}><ArrowUp size={14} /></IconButton>
                      <IconButton label={`${t("Move down")} ${f.label}`} disabled={i === draft.length - 1} onClick={() => move(i, 1)}><ArrowDown size={14} /></IconButton>
                      <IconButton label={`${t("Edit")} ${f.label}`} onClick={() => setEdit({ index: i, field: f })}><Pencil size={14} /></IconButton>
                      <IconButton label={`${t("Remove")} ${f.label}`} onClick={() => remove(i)}><Trash2 size={14} /></IconButton>
                    </span>}
                  </li>
                ))}
              </ol>
            )}
            {canEdit && <div className="stack" style={{ marginTop: 16 }}>
              <Field label={t("What changed? (optional)")} htmlFor="fm-note"><TextInput id="fm-note" maxLength={200} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
              <span className="row" style={{ gap: 8 }}>
                <Button variant="primary" disabled={!dirty || busy} onClick={() => void save()}>{busy ? t("Saving…") : t("Save new version")}</Button>
                <Button disabled={!dirty || busy} onClick={() => { setDraft(v.fields); setProblems([]); setErr(""); }}>{t("Discard changes")}</Button>
              </span>
            </div>}
          </Panel>

          <div className="stack">
            <Panel title={t("Preview")} description={t("Exactly how the request form behaves. Nothing is sent.")} actions={<Button size="sm" onClick={() => setPreview({})}>{t("Reset")}</Button>}>
              <DynamicForm fields={draft} values={preview} onChange={setPreview} locations={locOptions} idPrefix="pv" />
            </Panel>
            <Panel title={t("Version history")}>
              <QueryBoundary q={versions} rows={3}>{(rows) => rows.length === 0 ? <p className="tab-placeholder">{t("No versions yet.")}</p> : (
                <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                  {rows.map((r) => (
                    <li key={r.number} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
                      <span><b>v{r.number}</b> {r.current && <Badge tone="green">{t("In force")}</Badge>}<br /><small>{t("{n} questions", { n: r.fieldCount })} · {fmtDateTime(r.createdAt)}{r.createdBy ? ` · ${r.createdBy}` : ""}{r.note ? ` · ${r.note}` : ""}</small></span>
                      {canEdit && !r.current && <Button size="sm" aria-label={`${t("Load into draft")} v${r.number}`} onClick={() => void loadVersion(r.number)}>{t("Load into draft")}</Button>}
                    </li>
                  ))}
                </ul>
              )}</QueryBoundary>
            </Panel>
          </div>
        </div>
        {edit && <FieldDrawer open onClose={() => setEdit(null)} onSave={apply} initial={edit.field} isNew={edit.index < 0}
          others={(edit.index < 0 ? draft : draft.slice(0, edit.index)).filter((o) => o.key !== edit.field.key)} />}
      </>
    )}</QueryBoundary>
  );
}

export default function FormBuilderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="catalog.read"><Suspense><Builder id={id} /></Suspense></Require>;
}
