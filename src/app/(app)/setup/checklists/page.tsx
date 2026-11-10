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
import { TaskEditor, type TaskDraft } from "@/components/workorders/wo-bits";
import { get, post, put } from "@/lib/api/client";
import type { ChecklistTemplateItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function TemplateDrawer({ open, onClose, onDone, editing }: { open: boolean; onClose: () => void; onDone: () => void; editing: ChecklistTemplateItem | null }) {
  const run = useRun();
  const [name, setName] = useState(""); const [items, setItems] = useState<TaskDraft[]>([]); const [seed, setSeed] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) { setSeed(key); setName(editing?.name ?? ""); setItems(editing?.items.map((i) => ({ text: i.text, required: i.required })) ?? []); setErrs({}); setErr(""); }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { name, items: items.map((i) => ({ text: i.text, required: i.required })), version: editing?.version };
    const r = await run(() => (editing ? put(`/api/checklist-templates/${editing.id}`, body) : post("/api/checklist-templates", body)), t("Checklist saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit checklist") : t("New checklist")} description={t("Copied into a work order when chosen; changing it later does not change work orders that already use it.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="tpl-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="tpl-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="tpl-name" required error={errs.name}><TextInput id="tpl-name" data-autofocus maxLength={100} placeholder={t("e.g. AC service")} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        {errs.items && <p className="field-error" role="alert">{errs.items}</p>}
        <TaskEditor tasks={items} onChange={setItems} idPrefix="tpl" errors={Object.fromEntries(Object.entries(errs).map(([k, v]) => [k.replace("items", "tasks"), v]))} />
      </form>
    </Overlay>
  );
}

function Templates() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const q = useQuery(() => get<ChecklistTemplateItem[]>("/api/checklist-templates", { includeArchived: archived }), [archived]);
  const [drawer, setDrawer] = useState<{ open: boolean; row: ChecklistTemplateItem | null }>({ open: false, row: null });
  const act = async (x: ChecklistTemplateItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/checklist-templates/${x.id}/${what}`), what === "archive" ? t("Checklist archived.") : t("Checklist restored."))).ok) q.reload(); };
  const cols: Column<ChecklistTemplateItem>[] = [
    { key: "n", header: t("Checklist"), cell: (x) => <><b>{x.name}</b><br /><small>{x.items.slice(0, 3).map((i) => i.text).join(" · ")}{x.items.length > 3 ? " …" : ""}</small></> },
    { key: "c", header: t("Lines"), cell: (x) => x.items.length },
    { key: "s", header: t("Status"), cell: (x) => <Badge tone={x.archived ? "gray" : "green"}>{x.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: (x) => <Can perm="workorders.manage"><span className="row" style={{ gap: 6 }}>
      {!x.archived && <Button size="sm" aria-label={`${t("Edit")} ${x.name}`} onClick={() => setDrawer({ open: true, row: x })}>{t("Edit")}</Button>}
      <Button size="sm" aria-label={`${x.archived ? t("Restore") : t("Archive")} ${x.name}`} onClick={() => void act(x, x.archived ? "restore" : "archive")}>{x.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Checklists")} description={t("Saved checklists for work orders, such as an AC service or a generator check.")}
        actions={<Can perm="workorders.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New checklist")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("Checklists")} columns={cols} rows={q.data ?? []} rowKey={(x) => x.id} emptyMessage={t("No checklists yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <TemplateDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} />
    </>
  );
}

export default function ChecklistsPage() { return <Require perm="workorders.read"><Templates /></Require>; }
