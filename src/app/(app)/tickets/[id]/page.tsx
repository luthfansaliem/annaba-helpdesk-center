"use client";

import { Fragment, Suspense, use, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ArrowLeft, Paperclip, Pencil, Send, Trash2, Upload, UserPlus } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { QueryBoundary } from "@/components/query-boundary";
import { ApprovalBadge, PriorityBadge, RESOLUTION_CODES, SlaBadge, TicketStatusBadge, actionLabel, resolutionLabel, statusLabel } from "@/components/tickets/status";
import { DecideDialog, type Deciding } from "@/components/tickets/decide-dialog";
import { LegalHoldPanel } from "@/components/tickets/legal-hold";
import { TicketWorkOrdersPanel } from "@/components/workorders/ticket-work-orders";
import { RatingPanel } from "@/components/tickets/rating";
import { del, get, messageOf, patch, post } from "@/lib/api/client";
import type { AssetPick, AssigneeOption, AttachmentItem, PortalLocation, Priority, TeamItem, TicketAction, TicketAnswer, TicketDetail, TicketEvent } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { FILE_ACCEPT, MAX_FILE_BYTES, fmtSize, uploadFile } from "@/lib/files";
import { fmtMinutes } from "@/lib/duration";
import { useQuery } from "@/lib/hooks";
import { useAuth } from "@/lib/auth";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const NEEDS_REASON: TicketAction[] = ["hold", "cancel", "reopen"];
const reasonLabel = (a: TicketAction) => ({ hold: t("Why is it on hold?"), cancel: t("Why are you cancelling?"), reopen: t("Why does it need to be reopened?") } as Record<string, string>)[a] ?? t("Reason");

/** Shows an answer the way the person gave it: by name where the server resolved one, otherwise formatted by type. */
function answerText(a: TicketAnswer): string {
  if (a.display) return a.display;
  if (a.type === "yes_no") return a.value === true ? t("Yes") : t("No");
  if (a.type === "datetime" && typeof a.value === "string") return fmtDateTime(a.value);
  if (Array.isArray(a.value)) return a.value.join(", ");
  return String(a.value ?? "—");
}

function Entry({ e }: { e: TicketEvent }) {
  const what = e.kind === "status" && e.to ? (e.from ? `${statusLabel(e.from)} → ${statusLabel(e.to)}` : statusLabel(e.to))
    : e.kind === "created" ? t("Request created") : e.kind === "assigned" ? t("Assignment") : e.kind === "updated" ? t("Edited") : e.kind === "escalation" ? t("SLA") : e.kind === "approval" ? t("Approval") : e.internal ? t("Internal note") : t("Comment");
  return (
    <li style={{ borderLeft: `3px solid ${e.internal ? "var(--orange, #d97706)" : "var(--border, #d1d5db)"}`, paddingLeft: 10 }}>
      <b>{e.actor}</b> · <small>{what} · {fmtDateTime(e.at)}</small> {e.internal && <Badge tone="orange" dot={false}>{t("Staff only")}</Badge>}
      {e.body && <p style={{ margin: "4px 0 0", whiteSpace: "pre-wrap" }}>{e.body}</p>}
    </li>
  );
}

function ActionDialog({ ticket, action, onClose, onDone }: { ticket: TicketDetail; action: TicketAction | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [reason, setReason] = useState(""); const [code, setCode] = useState<string>("fixed"); const [note, setNote] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seen, setSeen] = useState<TicketAction | null>(null);
  if (action !== seen) { setSeen(action); setReason(""); setNote(""); setCode("fixed"); setErrs({}); setErr(""); }
  if (!action) return null;
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { action, reason: reason || null, resolutionCode: action === "resolve" ? code : null, resolutionNote: action === "resolve" ? note : null, version: ticket.version };
    const r = await run(() => post(`/api/tickets/${ticket.id}/transition`, body), t("Ticket updated."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open onClose={onClose} title={actionLabel(action)} description={`${ticket.number} · ${ticket.subject}`}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant={action === "cancel" ? "danger" : "primary"} type="submit" form="tk-action" disabled={busy}>{busy ? t("Saving…") : actionLabel(action)}</Button></>}>
      <form id="tk-action" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {NEEDS_REASON.includes(action) && <Field label={reasonLabel(action)} htmlFor="ta-reason" required error={errs.reason}><textarea id="ta-reason" data-autofocus rows={3} maxLength={500} className="textarea" value={reason} onChange={(e) => setReason(e.target.value)} /></Field>}
        {action === "resolve" && <>
          <Field label={t("How was it resolved?")} htmlFor="ta-code" required error={errs.resolutionCode}><SelectInput id="ta-code" value={code} onChange={(e) => setCode(e.target.value)} options={RESOLUTION_CODES.map((c) => ({ value: c, label: resolutionLabel(c) }))} /></Field>
          <Field label={t("What was done?")} htmlFor="ta-note" required error={errs.resolutionNote}><textarea id="ta-note" data-autofocus rows={4} maxLength={2000} className="textarea" value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        </>}
        {!NEEDS_REASON.includes(action) && action !== "resolve" && <p>{action === "confirm" ? t("This closes the request. You can still reopen it for a while.") : t("Continue?")}</p>}
      </form>
    </Overlay>
  );
}

function AssignDialog({ ticket, open, onClose, onDone }: { ticket: TicketDetail; open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [teamId, setTeamId] = useState(""); const [assigneeId, setAssigneeId] = useState(""); const [seen, setSeen] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${ticket.id}:${ticket.version}` : null;
  if (key !== seen) { setSeen(key); setTeamId(ticket.teamId ?? ""); setAssigneeId(ticket.assigneeId ?? ""); setErrs({}); setErr(""); }
  const teams = useQuery(() => (open ? get<TeamItem[]>("/api/teams").catch(() => [] as TeamItem[]) : Promise.resolve([] as TeamItem[])), [open]);
  const people = useQuery(() => (open ? get<AssigneeOption[]>("/api/tickets/assignees", { teamId: teamId || undefined }) : Promise.resolve([] as AssigneeOption[])), [open, teamId]);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => post(`/api/tickets/${ticket.id}/assign`, { assigneeId: assigneeId || null, teamId: teamId || null, version: ticket.version }), t("Ticket assigned."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Assign ticket")} description={`${ticket.number} · ${ticket.subject}`}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="tk-assign" disabled={busy}>{busy ? t("Saving…") : t("Assign")}</Button></>}>
      <form id="tk-assign" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Team")} htmlFor="as-team" error={errs.teamId}>
          <SelectInput id="as-team" value={teamId} onChange={(e) => { setTeamId(e.target.value); setAssigneeId(""); }} options={[{ value: "", label: t("No team") }, ...(teams.data ?? []).filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))]} />
        </Field>
        <Field label={t("Person")} htmlFor="as-person" error={errs.assigneeId}>
          <SelectInput id="as-person" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)} options={[{ value: "", label: t("Nobody yet (team queue)") }, ...(people.data ?? []).map((p) => ({ value: p.id, label: p.name }))]} />
        </Field>
      </form>
    </Overlay>
  );
}

function EditDialog({ ticket, open, onClose, onDone }: { ticket: TicketDetail; open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [subject, setSubject] = useState(""); const [priority, setPriority] = useState<Priority>("medium"); const [locationId, setLocationId] = useState(""); const [asset, setAsset] = useState<{ id: string; label: string } | null>(null); const [assetQ, setAssetQ] = useState(""); const [seen, setSeen] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${ticket.id}:${ticket.version}` : null;
  if (key !== seen) { setSeen(key); setSubject(ticket.subject); setPriority(ticket.priority); setLocationId(ticket.locationId ?? ""); setAsset(ticket.asset ? { id: ticket.asset.id, label: `${ticket.asset.tag} · ${ticket.asset.name}` } : null); setAssetQ(""); setErrs({}); setErr(""); }
  const term = assetQ.trim();
  const picks = useQuery(() => (open && term.length >= 2 ? get<AssetPick[]>("/api/assets/pick", { q: term }) : Promise.resolve([] as AssetPick[])), [open, term]);
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations") : Promise.resolve([] as PortalLocation[])), [open]);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => patch(`/api/tickets/${ticket.id}`, { subject, priority, locationId: locationId || null, assetId: asset?.id ?? null, version: ticket.version }), t("Ticket saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Edit ticket")} description={ticket.number}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="tk-edit" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="tk-edit" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Subject")} htmlFor="ed-subject" required error={errs.subject}><TextInput id="ed-subject" maxLength={200} value={subject} onChange={(e) => setSubject(e.target.value)} /></Field>
        <Field label={t("Priority")} htmlFor="ed-prio" error={errs.priority}>
          <SelectInput id="ed-prio" value={priority} onChange={(e) => setPriority(e.target.value as Priority)} options={[{ value: "low", label: t("Low") }, { value: "medium", label: t("Medium") }, { value: "high", label: t("High") }, { value: "urgent", label: t("Urgent") }]} />
        </Field>
        <Field label={t("Location")} htmlFor="ed-loc" error={errs.locationId}>
          <SelectInput id="ed-loc" value={locationId} onChange={(e) => setLocationId(e.target.value)} options={[{ value: "", label: t("None") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} />
        </Field>
        <Field label={t("Asset")} htmlFor="ed-asset" error={errs.assetId}>
          {asset
            ? <span className="row" style={{ gap: 8 }}><b>{asset.label}</b><Button size="sm" onClick={() => setAsset(null)}>{t("Remove")}</Button></span>
            : <div className="stack" style={{ gap: 6 }}>
              <TextInput id="ed-asset" placeholder={t("Search by tag, name or serial")} value={assetQ} onChange={(e) => setAssetQ(e.target.value)} />
              {(picks.data ?? []).length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 4 }} aria-label={t("Matching assets")}>
                {(picks.data ?? []).map((a) => <li key={a.id}><Button size="sm" onClick={() => { setAsset({ id: a.id, label: `${a.tag} · ${a.name}` }); setAssetQ(""); }}>{a.tag} · {a.name}</Button> <small>{a.typeName}{a.locationPath ? ` · ${a.locationPath}` : ""}</small></li>)}
              </ul>}
              {term.length >= 2 && picks.data && picks.data.length === 0 && <small className="sub">{t("No asset matches.")}</small>}
            </div>}
        </Field>
      </form>
    </Overlay>
  );
}

function Comment({ ticket, onDone }: { ticket: TicketDetail; onDone: () => void }) {
  const run = useRun(); const [body, setBody] = useState(""); const [internal, setInternal] = useState(false); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => post(`/api/tickets/${ticket.id}/comments`, { body, internal }), internal ? t("Note added.") : t("Comment added."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.body ?? r.error); return; }
    setBody(""); setInternal(false); onDone(); // back to a public reply: a note must be chosen on purpose each time
  }
  return (
    <form className="stack" noValidate onSubmit={submit}>
      {err && <div className="form-alert err" role="alert">{err}</div>}
      <Field label={t("Add a comment")} htmlFor="tk-comment"><textarea id="tk-comment" rows={4} maxLength={4000} className="textarea" value={body} onChange={(e) => setBody(e.target.value)} /></Field>
      <div className="compose-actions">
        {ticket.canCommentInternal ? <label className="chk"><Checkbox checked={internal} onChange={(e) => setInternal(e.target.checked)} /> {t("Internal note (requester will not see it)")}</label> : <span />}
        <Button variant="primary" type="submit" disabled={busy || !body.trim()}><Send size={14} /> {busy ? t("Sending…") : internal ? t("Add internal note") : t("Send comment")}</Button>
      </div>
    </form>
  );
}

/** Files on the ticket. Anyone who can see the ticket can open them; adding is allowed until the ticket is finished; the uploader or staff can remove one. */
function Attachments({ ticket, onDone }: { ticket: TicketDetail; onDone: () => void }) {
  const run = useRun(); const [busy, setBusy] = useState(false); const [problem, setProblem] = useState(""); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  async function pick(e: React.ChangeEvent<HTMLInputElement>) {
    const chosen = Array.from(e.target.files ?? []); e.target.value = "";
    if (chosen.length === 0) return;
    setProblem(""); setBusy(true); const issues: string[] = [];
    for (const f of chosen) {
      if (f.size > MAX_FILE_BYTES) { issues.push(`${f.name}: ${t("The file is larger than 8 MB.")}`); continue; }
      try { await uploadFile<AttachmentItem>(`/api/tickets/${ticket.id}/attachments`, f); } catch (x) { issues.push(`${f.name}: ${messageOf(x)}`); }
    }
    setBusy(false); setProblem(issues.join(" ")); onDone();
  }
  function remove(a: AttachmentItem) {
    setConfirm({ title: t("Remove this file?"), body: <p>{t("{name} will be deleted from the ticket and cannot be recovered.", { name: a.fileName })}</p>, confirmLabel: t("Remove"), danger: true,
      onConfirm: () => { void run(() => del(`/api/tickets/${ticket.id}/attachments/${a.id}`), t("File removed.")).then((r) => { if (r.ok) onDone(); }); } });
  }
  return (
    <Panel title={t("Attachments")} description={ticket.attachments.length ? undefined : t("No files yet.")}>
      {ticket.attachments.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 6 }}>
        {ticket.attachments.map((a) => (
          <li key={a.id} className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            <Paperclip size={14} aria-hidden />
            <a href={`/api/tickets/${ticket.id}/attachments/${a.id}`} target="_blank" rel="noopener noreferrer">{a.fileName}</a>
            <small className="sub">{fmtSize(a.size)} · {a.uploadedBy ?? "—"} · {fmtDateTime(a.createdAt)}</small>
            {a.canDelete && ticket.canAttach && <button type="button" className="btn sm" aria-label={t("Remove {name}", { name: a.fileName })} onClick={() => remove(a)}><Trash2 size={12} /></button>}
          </li>
        ))}
      </ul>}
      {ticket.canAttach && <div className="stack" style={{ marginTop: ticket.attachments.length ? 12 : 0, gap: 6 }}>
        <label className="dropzone" htmlFor="tk-files">
          <Upload size={18} aria-hidden />
          <span><b>{t("Add files")}</b><br /><small className="sub">{t("Click or drop photos, PDF, Office documents and text, up to 8 MB each.")}</small></span>
          <input id="tk-files" type="file" multiple accept={FILE_ACCEPT} onChange={pick} disabled={busy} aria-label={t("Add files")} />
        </label>
        {busy && <small className="sub" role="status">{t("Uploading…")}</small>}
        {problem && <p className="field-error" role="alert">{problem}</p>}
      </div>}
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Panel>
  );
}

function Detail({ id }: { id: string }) {
  const q = useQuery(() => get<TicketDetail>(`/api/tickets/${id}`), [id]); const { can } = useAuth();
  const created = useSearchParams().get("created") === "1";
  const staff = can("tickets.read_team");
  const [action, setAction] = useState<TicketAction | null>(null); const [assign, setAssign] = useState(false); const [edit, setEdit] = useState(false); const [deciding, setDeciding] = useState<Deciding | null>(null);
  return (
    <QueryBoundary q={q}>{(tk) => (
      <>
        <div className="tk-head">
          <div style={{ minWidth: 0 }}>
            {staff ? <Link href="/tickets" className="back-link"><ArrowLeft size={14} /> {t("Ticket Queue")}</Link> : <Link href="/requests" className="back-link"><ArrowLeft size={14} /> {t("My requests")}</Link>}
            <span className="num">{tk.serviceName}</span>
            <h1><span className="tk-no">{tk.number}</span> · {tk.subject}</h1>
          </div>
          <span className="row" style={{ gap: 8 }}><TicketStatusBadge status={tk.status} /><PriorityBadge priority={tk.priority} /><SlaBadge state={tk.sla?.state} /></span>
        </div>
        {created && <div className="form-alert" role="status">{t("Your request has been sent. We will keep you posted here.")}</div>}
        {tk.anonymisedAt && <div className="form-alert warn" role="status">
          {t("What was written on this ticket was removed on {when} under your organisation's retention policy. Only its number, service, dates and service-level figures remain, and it can no longer change.", { when: fmtDateTime(tk.anonymisedAt) })}
        </div>}

        {tk.myApprovalId && <div className="form-alert" role="status">
          <b>{t("This request is waiting for your decision.")}</b>{" "}
          <span className="row" style={{ gap: 8, display: "inline-flex" }}>
            <Button size="sm" variant="primary" onClick={() => setDeciding({ approvalId: tk.myApprovalId!, approve: true, label: `${tk.number} · ${tk.subject}` })}>{t("Approve")}</Button>
            <Button size="sm" variant="danger-outline" onClick={() => setDeciding({ approvalId: tk.myApprovalId!, approve: false, label: `${tk.number} · ${tk.subject}` })}>{t("Reject")}</Button>
          </span>
        </div>}

        <div className="tk-layout">
          <div className="tk-main">
            <Panel title={t("Details")}>
              {tk.description && <p style={{ whiteSpace: "pre-wrap", marginBottom: tk.answers.length ? 12 : 0 }}>{tk.description}</p>}
              {tk.answers.length > 0 && <dl className="props">
                {tk.answers.map((a) => <Fragment key={a.key}><dt>{a.label}</dt><dd>{answerText(a)}</dd></Fragment>)}
              </dl>}
              {!tk.description && tk.answers.length === 0 && <p className="tab-placeholder">{t("No further details.")}</p>}
            </Panel>
            {(tk.resolutionNote || tk.holdReason || tk.cancelReason) && <Panel title={tk.resolutionNote ? t("Resolution") : tk.holdReason ? t("On hold") : t("Cancelled")}>
              {tk.resolutionNote && <p><b>{resolutionLabel(tk.resolutionCode ?? "other")}</b>: {tk.resolutionNote}</p>}
              {tk.holdReason && <p>{tk.holdReason}</p>}
              {tk.cancelReason && <p>{tk.cancelReason}</p>}
            </Panel>}
            <RatingPanel ticket={tk} onDone={() => q.reload()} />
            {tk.approvals.length > 0 && <Panel title={t("Approval")} description={t("Nothing starts until every step is approved.")}>
              <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                {tk.approvals.map((a) => (
                  <li key={a.id}><b>{a.stepName}</b> · {a.approverName}{a.onBehalfOfName ? <small> ({t("for {name}", { name: a.onBehalfOfName })})</small> : null} <ApprovalBadge status={a.status} />
                    {(a.comment || a.decidedAt) && <><br /><small>{a.comment ? `“${a.comment}” · ` : ""}{a.decidedAt ? fmtDateTime(a.decidedAt) : ""}{a.decidedVia === "email" ? ` · ${t("by e-mail link")}` : ""}</small></>}</li>
                ))}
              </ul>
            </Panel>}
            <Panel title={t("Activity")}>
              <ol className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{tk.timeline.map((e) => <Entry key={e.id} e={e} />)}</ol>
              {tk.canComment && <div style={{ marginTop: 16, paddingTop: 14, borderTop: "1px solid var(--line)" }}><Comment ticket={tk} onDone={q.reload} /></div>}
            </Panel>
            {can("workorders.read") && !tk.anonymisedAt && <TicketWorkOrdersPanel ticket={tk} onChange={q.reload} />}
            {(tk.attachments.length > 0 || tk.canAttach) && <Attachments ticket={tk} onDone={q.reload} />}
          </div>
          <aside className="tk-side" aria-label={t("Ticket facts and actions")}>
            {(tk.allowed.some((x) => x !== "assign") || tk.canAssign || tk.canEdit) && <Panel title={t("Actions")}>
              <div className="btn-group">
                {tk.allowed.filter((x) => x !== "assign").map((x) => <Button key={x} variant={x === "cancel" ? "danger-outline" : x === "reopen" || x === "hold" ? "outline" : "primary"} onClick={() => setAction(x)}>{actionLabel(x)}</Button>)}
                {tk.canAssign && <Button onClick={() => setAssign(true)}><UserPlus size={14} /> {tk.assigneeId ? t("Reassign") : t("Assign")}</Button>}
                {tk.canEdit && <Button onClick={() => setEdit(true)}><Pencil size={14} /> {t("Edit")}</Button>}
              </div>
            </Panel>}
            <Panel title={t("Summary")}>
              <dl className="props">
                <dt>{t("Requester")}</dt><dd>{tk.requesterName ?? "—"}{tk.createdByName && tk.createdByName !== tk.requesterName ? <small> ({t("filed by {name}", { name: tk.createdByName })})</small> : null}</dd>
                <dt>{t("Assigned to")}</dt><dd>{tk.assigneeName ?? t("Nobody yet")}{tk.teamName ? <small className="sub"> · {tk.teamName}</small> : null}</dd>
                <dt>{t("Location")}</dt><dd>{tk.locationName ?? "—"}</dd>
                {tk.asset && <><dt>{t("Asset")}</dt><dd>{can("assets.read") ? <Link href={`/assets/${tk.asset.id}`}>{tk.asset.tag} · {tk.asset.name}</Link> : <>{tk.asset.tag} · {tk.asset.name}</>}{tk.asset.retired && <small> ({t("retired")})</small>}</dd></>}
                <dt>{t("Created")}</dt><dd>{fmtDateTime(tk.createdAt)}</dd>
                <dt>{t("Last update")}</dt><dd>{fmtDateTime(tk.updatedAt)}</dd>
                {tk.reopenCount > 0 && <><dt>{t("Reopened")}</dt><dd>{tk.reopenCount}×</dd></>}
                {!tk.sla && tk.expectedBy && tk.status !== "closed" && tk.status !== "cancelled" && <><dt>{t("Expected by")}</dt><dd>{fmtDateTime(tk.expectedBy)}</dd></>}
              </dl>
            </Panel>
            {tk.sla && <Panel title={t("Service level")} description={tk.sla.policyName ?? undefined}>
              <dl className="props">
                <dt>{t("State")}</dt><dd><SlaBadge state={tk.sla.state} /> {tk.sla.pausedAt && <small className="sub">{t("clock stopped since {when}", { when: fmtDateTime(tk.sla.pausedAt) })}</small>}</dd>
                <dt>{t("First response")}</dt><dd>{tk.sla.firstRespondedAt ? `${t("given")} ${fmtDateTime(tk.sla.firstRespondedAt)}` : tk.sla.firstResponseDueAt ? `${t("due")} ${fmtDateTime(tk.sla.firstResponseDueAt)}` : "—"} <small className="sub">({t("target")} {fmtMinutes(tk.sla.firstResponseMinutes)})</small></dd>
                <dt>{t("Resolution")}</dt><dd>{tk.sla.resolutionDueAt ? `${t("due")} ${fmtDateTime(tk.sla.resolutionDueAt)}` : "—"} <small className="sub">({t("target")} {fmtMinutes(tk.sla.resolutionMinutes)})</small></dd>
                {tk.sla.breachedAt && <><dt>{t("Breached")}</dt><dd>{fmtDateTime(tk.sla.breachedAt)}</dd></>}
              </dl>
            </Panel>}
            {tk.canHold && !tk.anonymisedAt && <LegalHoldPanel ticket={tk} onDone={q.reload} />}
          </aside>
        </div>
        <DecideDialog deciding={deciding} onClose={() => setDeciding(null)} onDone={q.reload} />
        <ActionDialog ticket={tk} action={action} onClose={() => setAction(null)} onDone={q.reload} />
        {tk.canAssign && <AssignDialog ticket={tk} open={assign} onClose={() => setAssign(false)} onDone={q.reload} />}
        {tk.canEdit && <EditDialog ticket={tk} open={edit} onClose={() => setEdit(false)} onDone={q.reload} />}
      </>
    )}</QueryBoundary>
  );
}

export default function TicketPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Suspense><Detail id={id} /></Suspense>;
}
