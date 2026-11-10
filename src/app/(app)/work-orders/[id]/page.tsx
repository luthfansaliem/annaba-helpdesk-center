"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Camera, Pencil, Trash2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { WoStatusBadge, WorkOrderDrawer } from "@/components/workorders/wo-bits";
import { del, get, post } from "@/lib/api/client";
import type { WorkOrderDetail } from "@/lib/api/types";
import { FILE_ACCEPT, MAX_FILE_BYTES, uploadFile } from "@/lib/files";
import { fmtDateTime } from "@/lib/format";
import { fmtMinutes } from "@/lib/duration";
import { useQuery } from "@/lib/hooks";
import { useAuth } from "@/lib/auth";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Act = "start" | "hold" | "resume" | "complete" | "cancel";
const actLabel = (a: Act) => ({ start: t("Start"), hold: t("Put on hold"), resume: t("Resume"), complete: t("Complete"), cancel: t("Cancel work order") })[a];
const money = (n: number) => n.toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: 2 });
const kindLabel = (k: string) => ({ part: t("Part"), labour: t("Labour"), service: t("Service"), invoice: t("Vendor invoice"), other: t("Other") } as Record<string, string>)[k] ?? k;

function ActionDialog({ wo, action, onClose, onDone }: { wo: WorkOrderDetail; action: Act | null; onClose: () => void; onDone: (w: WorkOrderDetail) => void }) {
  const run = useRun(); const [text, setText] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seen, setSeen] = useState<Act | null>(null);
  if (action !== seen) { setSeen(action); setText(""); setErr(""); }
  if (!action) return null;
  const needs = action === "complete" ? t("What was done?") : action === "hold" ? t("What are you waiting for?") : action === "cancel" ? t("Why is it cancelled?") : null;
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => post<WorkOrderDetail>(`/api/work-orders/${wo.id}/action`, { action, reason: action === "complete" ? null : text || null, note: action === "complete" ? text || null : null, version: wo.version }), t("Work order updated."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.complete ?? r.fields?.note ?? r.fields?.reason ?? r.error); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open onClose={onClose} title={actLabel(action)} description={`${wo.number} · ${wo.title}`}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant={action === "cancel" ? "danger" : "primary"} type="submit" form="wo-act" disabled={busy}>{busy ? t("Saving…") : actLabel(action)}</Button></>}>
      <form id="wo-act" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {needs ? <Field label={needs} htmlFor="wo-act-text" required><textarea id="wo-act-text" data-autofocus rows={3} maxLength={2000} className="textarea" value={text} onChange={(e) => setText(e.target.value)} /></Field> : <p style={{ margin: 0 }}>{t("Continue?")}</p>}
      </form>
    </Overlay>
  );
}

function Detail({ id }: { id: string }) {
  const run = useRun(); const { user } = useAuth();
  const q = useQuery(() => get<WorkOrderDetail>(`/api/work-orders/${id}`), [id]);
  const [action, setAction] = useState<Act | null>(null); const [editing, setEditing] = useState(false);
  const [minutes, setMinutes] = useState(""); const [date, setDate] = useState(""); const [timeNote, setTimeNote] = useState("");
  const [kind, setKind] = useState("part"); const [desc, setDesc] = useState(""); const [qty, setQty] = useState("1"); const [unit, setUnit] = useState("");
  const [fe, setFe] = useState<Record<string, string>>({}); const [upload, setUpload] = useState(""); const [busy, setBusy] = useState(false);
  const set = (w: WorkOrderDetail) => q.setData(w);

  return (
    <QueryBoundary q={q}>{(w) => {
      const working = w.canWork && w.status === "in_progress";
      async function tick(taskId: string, done: boolean) { const r = await run(() => post<WorkOrderDetail>(`/api/work-orders/${w.id}/tasks/${taskId}`, { done })); if (r.ok) set(r.data); }
      async function logTime(e: React.FormEvent) {
        e.preventDefault(); setFe({});
        const r = await run(() => post<WorkOrderDetail>(`/api/work-orders/${w.id}/time`, { minutes: Number(minutes || 0), date: date || null, note: timeNote || null }), t("Time logged."), { silent: true });
        if (!r.ok) { setFe(r.fields ?? { minutes: r.error }); return; }
        setMinutes(""); setTimeNote(""); set(r.data);
      }
      async function addCost(e: React.FormEvent) {
        e.preventDefault(); setFe({});
        const r = await run(() => post<WorkOrderDetail>(`/api/work-orders/${w.id}/costs`, { kind, description: desc, quantity: Number(qty || 0), unitCost: Number(unit || 0) }), t("Cost added."), { silent: true });
        if (!r.ok) { setFe(r.fields ?? { description: r.error }); return; }
        setDesc(""); setQty("1"); setUnit(""); set(r.data);
      }
      async function addPhoto(stage: "before" | "after", file: File | undefined) {
        if (!file) return; setUpload("");
        if (file.size > MAX_FILE_BYTES) { setUpload(t("The file is larger than 8 MB.")); return; }
        setBusy(true); const r = await run(() => uploadFile<WorkOrderDetail>(`/api/work-orders/${w.id}/files?stage=${stage}`, file), t("Photo added."), { silent: true }); setBusy(false);
        if (!r.ok) { setUpload(r.fields?.file ?? r.error); return; }
        set(r.data);
      }
      const remove = async (path: string) => { const r = await run(() => del<WorkOrderDetail>(path)); if (r.ok && r.data) set(r.data); else q.reload(); };

      return (
        <>
          <div className="crud-top">
            <div>
              {user?.vendorName   // a vendor's person can't open the ticket: back to their list, with the request's subject as context
                ? <><Link href="/work-orders" className="back-link"><ArrowLeft size={14} /> {t("My work orders")}</Link><p style={{ margin: "4px 0 0" }}>{w.ticketNumber} · {w.ticketSubject}</p></>
                : <Link href={`/tickets/${w.ticketId}`} className="back-link"><ArrowLeft size={14} /> {w.ticketNumber} · {w.ticketSubject}</Link>}
              <h1>{w.number} · {w.title}</h1>
              <p>{w.assigneeName ?? t("Nobody yet")}{w.teamName ? ` · ${w.teamName}` : ""}{w.dueAt ? ` · ${t("due")} ${fmtDateTime(w.dueAt)}` : ""}{w.assetTag ? ` · ${w.assetTag} ${w.assetName}` : ""}</p>
              {w.vendorName && <p data-testid="wo-vendor">{t("Vendor")}: <b>{w.vendorName}</b>{w.vendorPhone ? ` · ${w.vendorPhone}` : ""}{w.purchaseOrder ? ` · ${t("PO")} ${w.purchaseOrder}` : ""}</p>}
            </div>
            <span className="row" style={{ gap: 8 }}><WoStatusBadge status={w.status} />{!w.required && <Badge tone="gray" dot={false}>{t("Optional")}</Badge>}</span>
          </div>
          {w.holdReason && <div className="form-alert warn" role="status">{t("On hold")}: {w.holdReason}</div>}
          {w.completionNote && <div className="form-alert" role="status">{t("Done")} {w.completedAt ? fmtDateTime(w.completedAt) : ""}: {w.completionNote}</div>}
          {w.cancelReason && <div className="form-alert warn" role="status">{t("Cancelled")}: {w.cancelReason}</div>}
          {w.missing.length > 0 && <div className="form-alert" role="note" data-testid="wo-missing"><b>{t("Before you can complete:")}</b><ul style={{ margin: "4px 0 0" }}>{w.missing.map((m) => <li key={m}>{m}</li>)}</ul></div>}
          <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            {w.allowed.map((a) => <Button key={a} variant={a === "cancel" ? "danger-outline" : a === "complete" || a === "start" ? "primary" : "outline"} onClick={() => setAction(a)}>{actLabel(a)}</Button>)}
            {w.canEdit && <Button onClick={() => setEditing(true)}><Pencil size={14} /> {t("Edit")}</Button>}
          </span>

          <div className="grid-2" style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", alignItems: "start" }}>
            <div className="stack">
              {w.instructions && <Panel title={t("Instructions")}><p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{w.instructions}</p></Panel>}
              <Panel title={t("Checklist")} description={w.tasks.length ? t("{done} of {total} done", { done: w.tasks.filter((k) => k.doneAt).length, total: w.tasks.length }) : t("No checklist.")}>
                {w.tasks.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                  {w.tasks.map((k) => (
                    <li key={k.id}>
                      <label className="row" style={{ gap: 8 }}>
                        <Checkbox checked={!!k.doneAt} disabled={!working} onChange={(e) => void tick(k.id, e.target.checked)} />
                        <span>{k.text}{k.required && <> <Badge tone="blue" dot={false}>{t("Required")}</Badge></>}</span>
                      </label>
                      {k.doneAt && <small className="sub" style={{ marginLeft: 28 }}>{k.doneBy} · {fmtDateTime(k.doneAt)}</small>}
                    </li>
                  ))}
                </ul>}
                {w.canWork && w.status !== "in_progress" && w.tasks.length > 0 && <p className="field-help" style={{ marginBottom: 0 }}>{t("Start the work order to tick its checklist.")}</p>}
              </Panel>
              <Panel title={t("Photos")} description={w.requireAfterPhoto ? t("A photo of the finished work is required.") : undefined}>
                <div className="stack">
                  {w.files.length > 0 && <ul className="row" style={{ listStyle: "none", margin: 0, padding: 0, gap: 12, flexWrap: "wrap" }}>
                    {w.files.map((f) => <li key={f.id} className="stack" style={{ gap: 2 }}>
                      {/* eslint-disable-next-line @next/next/no-img-element -- served by our API with a sandboxing policy; nothing for next/image to optimise */}
                      {f.inline ? <a href={`/api/work-orders/${w.id}/files/${f.id}`} target="_blank" rel="noreferrer"><img src={`/api/work-orders/${w.id}/files/${f.id}`} alt={`${f.stage === "after" ? t("After") : t("Before")}: ${f.fileName}`} style={{ width: 120, height: 90, objectFit: "cover", borderRadius: 6 }} /></a>
                        : <a href={`/api/work-orders/${w.id}/files/${f.id}`}>{f.fileName}</a>}
                      <small>{f.stage === "after" ? t("After") : t("Before")}</small>
                    </li>)}
                  </ul>}
                  {upload && <div className="form-alert err" role="alert">{upload}</div>}
                  {w.canWork && <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                    <label className="btn sm"><Camera size={14} /> {t("Add a before photo")}<input type="file" accept={FILE_ACCEPT} className="sr-only" disabled={busy} onChange={(e) => void addPhoto("before", e.target.files?.[0])} /></label>
                    <label className="btn sm"><Camera size={14} /> {t("Add an after photo")}<input type="file" accept={FILE_ACCEPT} className="sr-only" disabled={busy} onChange={(e) => void addPhoto("after", e.target.files?.[0])} /></label>
                  </span>}
                </div>
              </Panel>
            </div>
            <div className="stack">
              <Panel title={t("Time")} description={t("{time} in total", { time: fmtMinutes(w.totalMinutes) })}>
                <div className="stack">
                  {w.times.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                    {w.times.map((x) => <li key={x.id} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
                      <span><b>{fmtMinutes(x.minutes)}</b> · {x.person} · {x.date}{x.note ? <><br /><small>{x.note}</small></> : null}</span>
                      {x.canDelete && <IconButton label={`${t("Remove")} ${fmtMinutes(x.minutes)}`} onClick={() => void remove(`/api/work-orders/${w.id}/time/${x.id}`)}><Trash2 size={14} /></IconButton>}
                    </li>)}
                  </ul>}
                  {w.canWork && <form className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }} noValidate onSubmit={logTime} aria-label={t("Log time")}>
                    <Field label={t("Minutes")} htmlFor="wt-min" error={fe.minutes}><TextInput id="wt-min" type="number" min={1} max={1440} value={minutes} onChange={(e) => setMinutes(e.target.value)} /></Field>
                    <Field label={t("Day")} htmlFor="wt-day" error={fe.date}><TextInput id="wt-day" type="date" value={date} onChange={(e) => setDate(e.target.value)} /></Field>
                    <Field label={t("Note")} htmlFor="wt-note" error={fe.note}><TextInput id="wt-note" maxLength={500} value={timeNote} onChange={(e) => setTimeNote(e.target.value)} /></Field>
                    <Button type="submit" size="sm" disabled={!minutes}>{t("Log time")}</Button>
                  </form>}
                </div>
              </Panel>
              {w.costs && <Panel title={t("Parts and costs")} description={w.labourCost != null ? t("Total: {total} · labour from logged time: {labour}", { total: money(w.costTotal ?? 0), labour: money(w.labourCost) }) : t("Total: {total}", { total: money(w.costTotal ?? 0) })}>
                <div className="stack">
                  {w.costs.length > 0 && <div className="tbl-wrap"><table className="tbl" aria-label={t("Parts and costs")}>
                    <thead><tr><th scope="col">{t("What")}</th><th scope="col">{t("Qty")}</th><th scope="col">{t("Unit cost")}</th><th scope="col">{t("Total")}</th><th scope="col"><span className="sr-only">{t("Actions")}</span></th></tr></thead>
                    <tbody>{w.costs.map((c) => <tr key={c.id}><td>{c.description}<br /><small>{kindLabel(c.kind)} · {c.by}</small></td><td>{c.quantity}</td><td>{money(c.unitCost)}</td><td>{money(c.total)}</td>
                      <td>{c.canDelete && w.canCosts && <IconButton label={`${t("Remove")} ${c.description}`} onClick={() => void remove(`/api/work-orders/${w.id}/costs/${c.id}`)}><Trash2 size={14} /></IconButton>}</td></tr>)}</tbody>
                  </table></div>}
                  {w.canCosts && <form className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }} noValidate onSubmit={addCost} aria-label={t("Add a cost")}>
                    <Field label={t("Kind")} htmlFor="wc-kind" error={fe.kind}><SelectInput id="wc-kind" value={kind} onChange={(e) => setKind(e.target.value)} options={["part", "labour", "service", "invoice", "other"].map((k) => ({ value: k, label: kindLabel(k) }))} /></Field>
                    <Field label={t("Description")} htmlFor="wc-desc" error={fe.description}><TextInput id="wc-desc" maxLength={200} value={desc} onChange={(e) => setDesc(e.target.value)} /></Field>
                    <Field label={t("Qty")} htmlFor="wc-qty" error={fe.quantity}><TextInput id="wc-qty" type="number" min={0.01} step="0.01" value={qty} onChange={(e) => setQty(e.target.value)} /></Field>
                    <Field label={t("Unit cost")} htmlFor="wc-unit" error={fe.unitCost}><TextInput id="wc-unit" type="number" min={0} step="0.01" value={unit} onChange={(e) => setUnit(e.target.value)} /></Field>
                    <Button type="submit" size="sm" disabled={!desc}>{t("Add")}</Button>
                  </form>}
                </div>
              </Panel>}
            </div>
          </div>
          <ActionDialog wo={w} action={action} onClose={() => setAction(null)} onDone={set} />
          <WorkOrderDrawer open={editing} onClose={() => setEditing(false)} onDone={set} ticketId={w.ticketId} editing={w} resolveTicket={false} />
        </>
      );
    }}</QueryBoundary>
  );
}

export default function WorkOrderPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="workorders.read"><Suspense><Detail id={id} /></Suspense></Require>;
}
