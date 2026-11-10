"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput } from "@/components/ui/field";
import { priorityLabel } from "@/components/tickets/status";
import { get, post } from "@/lib/api/client";
import type { AssigneeOption, TicketBulkResult, Priority, TeamItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Kind = "assign" | "priority" | "hold" | "cancel";

/** What a bulk action needs from the person before it can be sent. */
function BulkDialog({ kind, ids, onClose, onResult }: { kind: Kind | null; ids: string[]; onClose: () => void; onResult: (r: TicketBulkResult) => void }) {
  const run = useRun();
  const [teamId, setTeamId] = useState(""); const [assigneeId, setAssigneeId] = useState(""); const [priority, setPriority] = useState<Priority>("high"); const [reason, setReason] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seen, setSeen] = useState<Kind | null>(null);
  if (kind !== seen) { setSeen(kind); setTeamId(""); setAssigneeId(""); setPriority("high"); setReason(""); setErrs({}); setErr(""); }
  const teams = useQuery(() => (kind === "assign" ? get<TeamItem[]>("/api/teams").catch(() => [] as TeamItem[]) : Promise.resolve([] as TeamItem[])), [kind]);
  const people = useQuery(() => (kind === "assign" ? get<AssigneeOption[]>("/api/tickets/assignees", { teamId: teamId || undefined }).catch(() => [] as AssigneeOption[]) : Promise.resolve([] as AssigneeOption[])), [kind, teamId]);
  if (!kind) return null;
  const title = { assign: t("Assign tickets"), priority: t("Set priority"), hold: t("Put on hold"), cancel: t("Cancel tickets") }[kind];

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { ids, action: kind, assigneeId: assigneeId || null, teamId: teamId || null, priority: kind === "priority" ? priority : null, reason: reason || null };
    const r = await run(() => post<TicketBulkResult>("/api/tickets/bulk", body), undefined, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onResult(r.data);
  }
  return (
    <Overlay open onClose={onClose} title={title} description={t("{n} tickets selected.", { n: ids.length })}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant={kind === "cancel" ? "danger" : "primary"} type="submit" form="bulk-form" disabled={busy}>{busy ? t("Working…") : title}</Button></>}>
      <form id="bulk-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {kind === "assign" && <>
          <Field label={t("Team")} htmlFor="bk-team" error={errs.teamId}>
            <SelectInput id="bk-team" value={teamId} onChange={(e) => { setTeamId(e.target.value); setAssigneeId(""); }} options={[{ value: "", label: t("No change of team") }, ...(teams.data ?? []).filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))]} />
          </Field>
          <Field label={t("Person")} htmlFor="bk-person" error={errs.assigneeId}>
            <SelectInput id="bk-person" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)} options={[{ value: "", label: t("Nobody (team queue)") }, ...(people.data ?? []).map((p) => ({ value: p.id, label: p.name }))]} />
          </Field>
        </>}
        {kind === "priority" && <Field label={t("Priority")} htmlFor="bk-prio" error={errs.priority}>
          <SelectInput id="bk-prio" value={priority} onChange={(e) => setPriority(e.target.value as Priority)} options={(["urgent", "high", "medium", "low"] as Priority[]).map((p) => ({ value: p, label: priorityLabel(p) }))} />
        </Field>}
        {(kind === "hold" || kind === "cancel") && <Field label={kind === "hold" ? t("Why are they on hold?") : t("Why are you cancelling?")} htmlFor="bk-reason" required error={errs.reason}>
          <textarea id="bk-reason" data-autofocus rows={3} maxLength={500} className="textarea" value={reason} onChange={(e) => setReason(e.target.value)} />
        </Field>}
        <p className="sub">{t("Each ticket is checked on its own. Tickets that cannot take the change are skipped and listed afterwards.")}</p>
      </form>
    </Overlay>
  );
}

/** Appears when tickets are ticked in the queue. */
export function BulkBar({ ids, onDone, onClear }: { ids: string[]; onDone: () => void; onClear: () => void }) {
  const { can } = useAuth(); const run = useRun();
  const [kind, setKind] = useState<Kind | null>(null); const [result, setResult] = useState<TicketBulkResult | null>(null); const [busy, setBusy] = useState(false);
  const failed = result?.items.filter((i) => !i.ok) ?? [];

  async function start() {
    setBusy(true);
    const r = await run(() => post<TicketBulkResult>("/api/tickets/bulk", { ids, action: "start" }), undefined, { silent: true });
    setBusy(false);
    if (r.ok) done(r.data);
  }
  function done(r: TicketBulkResult) { setResult(r); onDone(); }

  if (ids.length === 0 && !result) return null;
  return (
    <div className="stack" style={{ gap: 8, paddingBottom: 12 }}>
      {ids.length > 0 && <div className="row" role="toolbar" aria-label={t("Actions for the selected tickets")} style={{ gap: 8, flexWrap: "wrap", alignItems: "center" }}>
        <b>{t("{n} selected", { n: ids.length })}</b>
        {can("tickets.assign") && <Button size="sm" onClick={() => setKind("assign")}>{t("Assign…")}</Button>}
        {can("tickets.update") && <Button size="sm" onClick={() => setKind("priority")}>{t("Set priority…")}</Button>}
        {can("tickets.transition") && <>
          <Button size="sm" onClick={start} disabled={busy}>{t("Start work")}</Button>
          <Button size="sm" onClick={() => setKind("hold")}>{t("Put on hold…")}</Button>
          <Button size="sm" variant="danger-outline" onClick={() => setKind("cancel")}>{t("Cancel…")}</Button>
        </>}
        <Button size="sm" onClick={onClear}>{t("Clear selection")}</Button>
      </div>}
      {result && <div className={failed.length ? "form-alert err" : "form-alert"} role="status">
        <b>{t("{ok} of {total} tickets updated.", { ok: result.succeeded, total: result.requested })}</b>
        {failed.length > 0 && <ul style={{ margin: "6px 0 0", paddingLeft: 18 }}>{failed.map((i) => <li key={i.id}>{i.number ?? t("Unknown ticket")}: {i.error}</li>)}</ul>}
        <div style={{ marginTop: 6 }}><Button size="sm" onClick={() => setResult(null)}>{t("Dismiss")}</Button></div>
      </div>}
      <BulkDialog kind={kind} ids={ids} onClose={() => setKind(null)} onResult={done} />
    </div>
  );
}
