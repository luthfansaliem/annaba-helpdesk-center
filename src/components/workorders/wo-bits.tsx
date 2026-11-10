"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Button, IconButton } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { get, post, put } from "@/lib/api/client";
import type { AssigneeOption, ChecklistTemplateItem, TeamItem, VendorPick, WorkOrderDetail, WorkOrderStatus } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const woStatusLabel = (s: WorkOrderStatus) => ({ open: t("New"), in_progress: t("In progress"), on_hold: t("On hold"), done: t("Done"), cancelled: t("Cancelled") })[s];
const tone: Record<WorkOrderStatus, BadgeTone> = { open: "gray", in_progress: "blue", on_hold: "orange", done: "green", cancelled: "gray" };
export function WoStatusBadge({ status }: { status: WorkOrderStatus }) { return <Badge tone={tone[status]}>{woStatusLabel(status)}</Badge>; }

export type TaskDraft = { id?: string; text: string; required: boolean };

/** Lines of a checklist: add, reorder, mark required, remove. Shared by work orders and checklist templates. */
export function TaskEditor({ tasks, onChange, idPrefix, errors = {} }: { tasks: TaskDraft[]; onChange: (t: TaskDraft[]) => void; idPrefix: string; errors?: Record<string, string> }) {
  const [text, setText] = useState("");
  const move = (i: number, d: -1 | 1) => { const j = i + d; if (j < 0 || j >= tasks.length) return; const c = [...tasks]; [c[i], c[j]] = [c[j], c[i]]; onChange(c); };
  const add = () => { const v = text.trim(); if (v.length < 2) return; onChange([...tasks, { text: v, required: true }]); setText(""); };
  return (
    <div className="stack" style={{ gap: 8 }}>
      {tasks.length > 0 && <ol className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 6 }}>
        {tasks.map((k, i) => (
          <li key={`${k.id ?? "n"}-${i}`} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
            <span style={{ flex: 1 }}>{i + 1}. {k.text}{errors[`tasks[${i}]`] && <span className="field-error" role="alert"> {errors[`tasks[${i}]`]}</span>}</span>
            <label className="row" style={{ gap: 4 }}><Checkbox checked={k.required} aria-label={`${t("Required")}: ${k.text}`} onChange={(e) => onChange(tasks.map((x, j) => (j === i ? { ...x, required: e.target.checked } : x)))} /> <small>{t("Required")}</small></label>
            <IconButton label={`${t("Move up")} ${k.text}`} disabled={i === 0} onClick={() => move(i, -1)}><ArrowUp size={14} /></IconButton>
            <IconButton label={`${t("Move down")} ${k.text}`} disabled={i === tasks.length - 1} onClick={() => move(i, 1)}><ArrowDown size={14} /></IconButton>
            <IconButton label={`${t("Remove")} ${k.text}`} onClick={() => onChange(tasks.filter((_, j) => j !== i))}><Trash2 size={14} /></IconButton>
          </li>
        ))}
      </ol>}
      <div className="row" style={{ gap: 8, alignItems: "flex-end" }}>
        <Field label={t("New checklist line")} htmlFor={`${idPrefix}-new`}><TextInput id={`${idPrefix}-new`} maxLength={300} value={text} onChange={(e) => setText(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); add(); } }} /></Field>
        <Button size="sm" onClick={add} disabled={text.trim().length < 2}><Plus size={14} /> {t("Add line")}</Button>
      </div>
    </div>
  );
}

/** Create a work order on a ticket, or edit one. */
export function WorkOrderDrawer({ open, onClose, onDone, ticketId, editing, resolveTicket }: { open: boolean; onClose: () => void; onDone: (w: WorkOrderDetail) => void; ticketId: string; editing: WorkOrderDetail | null; resolveTicket: boolean }) {
  const run = useRun();
  const teams = useQuery(() => (open ? get<TeamItem[]>("/api/teams").catch(() => [] as TeamItem[]) : Promise.resolve([] as TeamItem[])), [open]);
  const templates = useQuery(() => (open && !editing ? get<ChecklistTemplateItem[]>("/api/checklist-templates") : Promise.resolve([] as ChecklistTemplateItem[])), [open]);
  const [title, setTitle] = useState(""); const [instructions, setInstructions] = useState(""); const [teamId, setTeamId] = useState(""); const [assigneeId, setAssigneeId] = useState(""); const [due, setDue] = useState("");
  const [required, setRequired] = useState(true); const [photo, setPhoto] = useState(false); const [resolve, setResolve] = useState(resolveTicket); const [tasks, setTasks] = useState<TaskDraft[]>([]); const [templateId, setTemplateId] = useState("");
  const [vendorId, setVendorId] = useState(""); const [po, setPo] = useState("");
  const vendors = useQuery(() => (open ? get<VendorPick[]>("/api/vendors/pick").catch(() => [] as VendorPick[]) : Promise.resolve([] as VendorPick[])), [open]);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seed, setSeed] = useState<string | null>(null);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setVendorId(editing?.vendorId ?? ""); setPo(editing?.purchaseOrder ?? ""); setTitle(editing?.title ?? ""); setInstructions(editing?.instructions ?? ""); setTeamId(editing?.teamId ?? ""); setAssigneeId(editing?.assigneeId ?? ""); setDue(editing?.dueAt ? editing.dueAt.slice(0, 10) : "");
    setRequired(editing?.required ?? true); setPhoto(editing?.requireAfterPhoto ?? false); setResolve(resolveTicket); setTasks(editing ? editing.tasks.map((k) => ({ id: k.id, text: k.text, required: k.required })) : []); setTemplateId(""); setErrs({}); setErr("");
  }
  const people = useQuery(() => (open ? get<AssigneeOption[]>("/api/tickets/assignees", { teamId: teamId || undefined }).catch(() => [] as AssigneeOption[]) : Promise.resolve([] as AssigneeOption[])), [open, teamId]);
  const tpl = (templates.data ?? []).find((x) => x.id === templateId);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { title, instructions: instructions || null, teamId: teamId || null, assigneeId: assigneeId || null, dueAt: due ? new Date(`${due}T17:00:00`).toISOString() : null, required, requireAfterPhoto: photo,
      tasks: tasks.map((k) => ({ id: k.id ?? null, text: k.text, required: k.required })), templateId: templateId || null, resolveTicket: resolve, version: editing?.version, vendorId: vendorId || null, purchaseOrder: po || null };
    const r = await run(() => (editing ? put<WorkOrderDetail>(`/api/work-orders/${editing.id}`, body) : post<WorkOrderDetail>(`/api/tickets/${ticketId}/work-orders`, body)), editing ? t("Work order saved.") : t("Work order created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit work order") : t("New work order")} description={editing ? `${editing.number} · ${editing.title}` : t("A piece of work for this ticket, with its own person, checklist, time and costs.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="wo-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="wo-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("What needs doing")} htmlFor="wo-title" required error={errs.title}><TextInput id="wo-title" data-autofocus maxLength={150} value={title} onChange={(e) => setTitle(e.target.value)} /></Field>
        <Field label={t("Instructions")} htmlFor="wo-instr" error={errs.instructions}><textarea id="wo-instr" rows={3} maxLength={4000} className="textarea" value={instructions} onChange={(e) => setInstructions(e.target.value)} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Team")} htmlFor="wo-team" error={errs.teamId}><SelectInput id="wo-team" value={teamId} onChange={(e) => { setTeamId(e.target.value); setAssigneeId(""); }} options={[{ value: "", label: t("No team") }, ...(teams.data ?? []).filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))]} /></Field>
          <Field label={t("Person")} htmlFor="wo-person" error={errs.assigneeId}><SelectInput id="wo-person" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)} options={[{ value: "", label: t("Nobody yet (anyone may take it)") }, ...(people.data ?? []).map((p) => ({ value: p.id, label: p.name })), ...(assigneeId && !(people.data ?? []).some((p) => p.id === assigneeId) && editing?.assigneeName ? [{ value: assigneeId, label: editing.assigneeName }] : [])]} /></Field>
          <Field label={t("Due")} htmlFor="wo-due"><TextInput id="wo-due" type="date" value={due} onChange={(e) => setDue(e.target.value)} /></Field>
        </div>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Vendor")} htmlFor="wo-vendor" error={errs.vendorId}><SelectInput id="wo-vendor" value={vendorId} onChange={(e) => setVendorId(e.target.value)} options={[{ value: "", label: t("None (done in-house)") }, ...(vendors.data ?? []).map((v) => ({ value: v.id, label: v.name })), ...(vendorId && !(vendors.data ?? []).some((v) => v.id === vendorId) && editing?.vendorName ? [{ value: vendorId, label: editing.vendorName }] : [])]} /></Field>
          <Field label={t("Purchase order")} htmlFor="wo-po" error={errs.purchaseOrder}><TextInput id="wo-po" maxLength={50} value={po} onChange={(e) => setPo(e.target.value)} /></Field>
        </div>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={required} onChange={(e) => setRequired(e.target.checked)} /> {t("Required: the ticket can't be resolved before this is done")}</label>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={photo} onChange={(e) => setPhoto(e.target.checked)} /> {t("Needs a photo of the finished work")}</label>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={resolve} onChange={(e) => setResolve(e.target.checked)} /> {t("Resolve the ticket when its required work orders are done")}</label>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Checklist")}</legend>
          {!editing && (templates.data ?? []).length > 0 && <Field label={t("Start from a saved checklist")} htmlFor="wo-tpl" error={errs.templateId}>
            <SelectInput id="wo-tpl" value={templateId} onChange={(e) => setTemplateId(e.target.value)} options={[{ value: "", label: t("None") }, ...(templates.data ?? []).map((x) => ({ value: x.id, label: `${x.name} (${x.items.length})` }))]} />
            {tpl && <p className="field-help">{t("Its {n} lines come first; lines you add below follow.", { n: tpl.items.length })}</p>}
          </Field>}
          <TaskEditor tasks={tasks} onChange={setTasks} idPrefix="wo" errors={errs} />
        </fieldset>
      </form>
    </Overlay>
  );
}
