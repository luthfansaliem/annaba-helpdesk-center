"use client";

import { useCallback, useState } from "react";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { Panel } from "@/components/ui/card";
import { DataTable, type Column } from "@/components/data/data-table";
import { PersonPicker } from "@/components/forms/dynamic-form";
import { get, post } from "@/lib/api/client";
import type { DelegationItem, DelegationResult, DelegationState, PortalPerson } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };
/** Days are days on the person's own clock: a delegation runs from the first moment of the first day to the end of the last. */
const dayStart = (d: string) => new Date(`${d}T00:00:00`).toISOString();
const dayEnd = (d: string) => { const x = new Date(`${d}T00:00:00`); x.setDate(x.getDate() + 1); return x.toISOString(); };

const stateLabel = (s: DelegationState) => ({ upcoming: t("Upcoming"), active: t("Active"), ended: t("Finished"), revoked: t("Ended early") })[s];
const stateTone: Record<DelegationState, BadgeTone> = { upcoming: "blue", active: "green", ended: "gray", revoked: "gray" };

function usePeople() {
  return useCallback(async (q: string) => (await get<PortalPerson[]>("/api/portal/people", { q })).map((p) => ({ value: p.id, label: p.department ? `${p.name} · ${p.department}` : p.name })), []);
}

/** Out of the office: someone else approves for you between two days. */
export function DelegationPanel({ onChanged }: { onChanged: () => void }) {
  const run = useRun(); const search = usePeople();
  const list = useQuery(() => get<DelegationItem[]>("/api/approvals/delegations"), []);
  const [to, setTo] = useState(""); const [from, setFrom] = useState(today()); const [until, setUntil] = useState(""); const [note, setNote] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [msg, setMsg] = useState("");

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setMsg(""); setBusy(true);
    const body = { delegateId: to || null, startsAt: from ? dayStart(from) : null, endsAt: until ? dayEnd(until) : null, note: note || null };
    const r = await run(() => post<DelegationResult>("/api/approvals/delegations", body), t("Delegation saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields ? "" : r.error); setErrs(r.fields ?? {}); return; }
    setTo(""); setUntil(""); setNote(""); list.reload(); onChanged();
    if (r.data.handedOver > 0 || r.data.kept > 0) setMsg(t("{moved} waiting approvals moved to your cover; {kept} stayed with you.", { moved: r.data.handedOver, kept: r.data.kept }));
  }
  async function end(d: DelegationItem) {
    const r = await run(() => post<DelegationItem>(`/api/approvals/delegations/${d.id}/end`), t("Delegation ended."));
    if (r.ok) list.reload();
  }

  const cols: Column<DelegationItem>[] = [
    { key: "d", header: t("Covered by"), cell: (d) => <b>{d.delegateName}</b> },
    { key: "p", header: t("Period"), cell: (d) => `${fmtDateTime(d.startsAt)} → ${fmtDateTime(d.endsAt)}` },
    { key: "s", header: t("State"), cell: (d) => <Badge tone={stateTone[d.state]}>{stateLabel(d.state)}</Badge> },
    { key: "n", header: t("Note"), className: "hide-sm", cell: (d) => d.note ?? "—" },
    { key: "x", header: t("Actions"), cell: (d) => (d.state === "active" || d.state === "upcoming") ? <Button size="sm" variant="danger-outline" aria-label={`${t("End delegation to")} ${d.delegateName}`} onClick={() => end(d)}>{t("End")}</Button> : null },
  ];

  return (
    <Panel title={t("Out of office")} description={t("While you are away, approvals that would come to you go to someone you choose. Nobody can approve their own request, so a cover never decides a request they made.")}>
      <form className="stack" noValidate onSubmit={submit} aria-label={t("Out of office")}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {msg && <div className="form-alert" role="status">{msg}</div>}
        <div className="row" style={{ gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}>
          <Field label={t("Who covers for you")} htmlFor="dg-to" required error={errs.delegateId}><PersonPicker id="dg-to" value={to} onChange={(v) => setTo(v ?? "")} search={search} /></Field>
          <Field label={t("From")} htmlFor="dg-from" error={errs.startsAt}><TextInput id="dg-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field label={t("Until (including)")} htmlFor="dg-until" required error={errs.endsAt}><TextInput id="dg-until" type="date" min={from || undefined} value={until} onChange={(e) => setUntil(e.target.value)} /></Field>
          <Field label={t("Note (optional)")} htmlFor="dg-note" error={errs.note}><TextInput id="dg-note" maxLength={300} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
          <Button variant="primary" type="submit" disabled={busy}>{busy ? t("Saving…") : t("Start delegation")}</Button>
        </div>
      </form>
      <div style={{ marginTop: 16 }}>
        <DataTable label={t("My delegations")} columns={cols} rows={list.data ?? []} rowKey={(d) => d.id} emptyMessage={t("You have not delegated your approvals.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
      </div>
    </Panel>
  );
}

/** Hand one waiting approval to somebody else. */
export function HandOverDialog({ approvalId, label, onClose, onDone }: { approvalId: string | null; label: string; onClose: () => void; onDone: () => void }) {
  const run = useRun(); const search = usePeople();
  const [to, setTo] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seen, setSeen] = useState<string | null>(null);
  if (approvalId !== seen) { setSeen(approvalId); setTo(""); setErr(""); }
  if (!approvalId) return null;
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => post(`/api/approvals/${approvalId}/reassign`, { toUserId: to || null }), t("Approval handed over."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.toUserId ?? r.error); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open onClose={onClose} title={t("Hand over this approval")} description={label}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant="primary" type="submit" form="handover-form" disabled={busy || !to}>{busy ? t("Saving…") : t("Hand over")}</Button></>}>
      <form id="handover-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Who should decide instead?")} htmlFor="ho-to" required><PersonPicker id="ho-to" value={to} onChange={(v) => setTo(v ?? "")} search={search} /></Field>
        <p className="sub">{t("They get a new link and the old one stops working. The approval trail shows that it was handed over from you.")}</p>
      </form>
    </Overlay>
  );
}
