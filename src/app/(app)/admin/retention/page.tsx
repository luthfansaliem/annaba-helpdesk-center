"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { ToggleRow } from "@/components/iam/prefs";
import { DataTable, type Column } from "@/components/data/data-table";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { del, get, post, put } from "@/lib/api/client";
import type { FileCheck, FileClean, LegalHoldItem, Paged, RetentionAction, RetentionPreview, RetentionRunResult, RetentionView } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { fmtSize } from "@/lib/files";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Draft = { enabled: boolean; tickets: string; action: RetentionAction; files: string; notifications: string };
const toDraft = (v: RetentionView): Draft => ({ enabled: v.enabled, tickets: v.finishedTicketDays?.toString() ?? "", action: v.ticketAction, files: v.attachmentDays?.toString() ?? "", notifications: v.notificationDays?.toString() ?? "" });
const num = (s: string) => (s.trim() === "" ? null : Number(s));
const body = (d: Draft) => ({ enabled: d.enabled, finishedTicketDays: num(d.tickets), ticketAction: d.action, attachmentDays: num(d.files), notificationDays: num(d.notifications) });

function Policy({ saved, reload }: { saved: RetentionView; reload: () => void }) {
  const run = useRun();
  const [d, setD] = useState<Draft>(toDraft(saved)); const [err, setErr] = useState(""); const [fe, setFe] = useState<Record<string, string>>({}); const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<RetentionPreview | null>(null); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  useEffect(() => { setD(toDraft(saved)); setPreview(null); }, [saved]);
  const was = toDraft(saved); const dirty = (Object.keys(was) as (keyof Draft)[]).some((k) => was[k] !== d[k]);
  const set = <K extends keyof Draft>(k: K, v: Draft[K]) => { setD((x) => ({ ...x, [k]: v })); setPreview(null); };

  async function save() {
    setErr(""); setFe({}); setBusy(true);
    const r = await run(() => put<RetentionView>("/api/retention", body(d)), t("Retention policy saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setFe(r.fields ?? {}); return; }
    reload();
  }
  async function check() {
    setErr(""); setFe({}); setBusy(true);
    const r = await run(() => post<RetentionPreview>("/api/retention/preview", body(d)), undefined, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setFe(r.fields ?? {}); return; }
    setPreview(r.data);
  }
  const runNow = () => setConfirm({
    title: t("Run the clean-up now?"), confirmLabel: t("Run now"), danger: true,
    body: <>{t("This applies the saved policy to everything that is due: tickets, files and notifications that are older than your periods are")}{" "}<b>{saved.ticketAction === "delete" ? t("deleted") : t("anonymised or deleted")}</b>{" "}{t("and this cannot be undone. Tickets under legal hold are skipped.")}</>,
    onConfirm: async () => {
      setBusy(true); const r = await run(() => post<RetentionRunResult>("/api/retention/run"), undefined); setBusy(false);
      if (r.ok) { reload(); notifyDone(r.data); }
    },
  });
  const [done, setDone] = useState<RetentionRunResult | null>(null); const notifyDone = (r: RetentionRunResult) => setDone(r);

  const last = saved.lastRun;
  return (
    <div className="stack">
      {err && <div className="form-alert err" role="alert">{err}</div>}
      <Panel title={t("Retention policy")} description={t("Nothing is ever removed until you switch this on. Only finished tickets (closed, cancelled or rejected) are counted, from the day they finished; open tickets are never touched. Everything is checked every day.")}>
        <div className="stack">
          <ToggleRow id="rt-on" label={t("Apply this policy every day")} hint={t("Off: nothing is removed, whatever the periods below say.")} checked={d.enabled} onChange={(v) => set("enabled", v)} />
          {fe.enabled && <p className="field-error" role="alert">{fe.enabled}</p>}
          <Field label={t("Finished tickets: act after (days)")} htmlFor="rt-tickets" error={fe.finishedTicketDays}>
            <TextInput id="rt-tickets" type="number" min={30} max={3650} placeholder={t("Keep")} value={d.tickets} invalid={!!fe.finishedTicketDays} onChange={(e) => set("tickets", e.target.value)} />
            <p className="field-help">{t("30–3650 days. Leave empty to keep tickets.")}</p>
          </Field>
          <Field label={t("What happens to those tickets")} htmlFor="rt-action" error={fe.ticketAction}>
            <SelectInput id="rt-action" value={d.action} onChange={(e) => set("action", e.target.value as RetentionAction)}
              options={[{ value: "anonymise", label: t("Keep as a statistic, remove what people wrote") }, { value: "delete", label: t("Delete the ticket completely") }]} />
            <p className="field-help">{d.action === "anonymise"
              ? t("The subject, description, answers, comments, resolution notes and files are removed. The number, service, place, dates and service-level figures stay for your reports. The ticket still points to the accounts involved, which are erased when those accounts are closed.")
              : t("The ticket, its timeline, approvals, notifications and files are removed and no longer count in reports.")}</p>
          </Field>
          <Field label={t("Files: remove after (days)")} htmlFor="rt-files" error={fe.attachmentDays}>
            <TextInput id="rt-files" type="number" min={7} max={3650} placeholder={t("Keep")} value={d.files} invalid={!!fe.attachmentDays} onChange={(e) => set("files", e.target.value)} />
            <p className="field-help">{t("7–3650 days after the ticket finished, and never later than the period above. Leave empty to keep files as long as their ticket.")}</p>
          </Field>
          <Field label={t("Notifications: remove after (days)")} htmlFor="rt-notes" error={fe.notificationDays}>
            <TextInput id="rt-notes" type="number" min={7} max={3650} placeholder={t("Keep")} value={d.notifications} invalid={!!fe.notificationDays} onChange={(e) => set("notifications", e.target.value)} />
            <p className="field-help">{t("In-app notifications, read or not. Leave empty to keep them.")}</p>
          </Field>
          <p className="field-help" style={{ margin: 0 }}>{t("Audit and security logs are kept for {n} days and are managed by the platform administrators.", { n: saved.auditDays })}</p>
        </div>
      </Panel>

      <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
        <Button onClick={check} disabled={busy}>{t("Preview what it would remove")}</Button>
        <Button variant="primary" onClick={save} disabled={!dirty || busy}>{busy ? t("Saving…") : t("Save policy")}</Button>
        <Button variant="danger-outline" onClick={runNow} disabled={busy || dirty || !saved.enabled}>{t("Run now")}</Button>
      </div>

      {preview && <Panel title={t("If the policy ran now")} description={t("Counted from the values above, whether or not they are saved. Nothing has been changed.")}>
        <ul className="stack" style={{ margin: 0 }} data-testid="retention-preview">
          <li><b>{preview.tickets}</b> {d.action === "delete" ? t("tickets would be deleted") : t("tickets would be anonymised")}</li>
          <li><b>{preview.heldTickets}</b> {t("tickets are spared by a legal hold")}</li>
          <li><b>{preview.files}</b> {t("files would be removed")} ({fmtSize(preview.fileBytes)})</li>
          <li><b>{preview.notifications}</b> {t("notifications would be removed")}</li>
        </ul>
      </Panel>}
      {done && <div className="form-alert" role="status" data-testid="retention-done">
        {t("Done: {tickets} tickets, {files} files and {notes} notifications removed.", { tickets: done.tickets, files: done.files, notes: done.notifications })}
        {done.filesFailed > 0 && <> <b>{t("{n} files could not be removed and will be tried again.", { n: done.filesFailed })}</b></>}
        {done.more && <> {t("There is more to do; the next daily run continues.")}</>}
      </div>}
      <Panel title={t("Last run")}>
        {last ? <p style={{ margin: 0 }}>{fmtDateTime(last.at)} · {t("{tickets} tickets, {files} files, {notes} notifications", { tickets: last.tickets, files: last.files, notes: last.notifications })}{last.filesFailed > 0 && <> · <Badge tone="red">{t("{n} files could not be removed", { n: last.filesFailed })}</Badge></>}</p>
          : <p className="tab-placeholder" style={{ margin: 0 }}>{t("The policy has not run yet.")}</p>}
      </Panel>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </div>
  );
}

function Holds({ onChange }: { onChange: () => void }) {
  const run = useRun(); const q = useQuery(() => get<Paged<LegalHoldItem>>("/api/retention/holds", { pageSize: 100 }), []);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const release = (h: LegalHoldItem) => setConfirm({
    title: t("Release the legal hold?"), confirmLabel: t("Release hold"), danger: true,
    body: t("From the next clean-up on, {number} is treated like any other ticket.", { number: h.number }),
    onConfirm: async () => { if ((await run(() => del(`/api/tickets/${h.ticketId}/legal-hold`), t("The legal hold was released."))).ok) { q.reload(); onChange(); } },
  });
  const cols: Column<LegalHoldItem>[] = [
    { key: "n", header: t("Ticket"), cell: (h) => <><Link href={`/tickets/${h.ticketId}`}><b>{h.number}</b></Link><br /><small>{h.subject}</small></> },
    { key: "r", header: t("Reason"), cell: (h) => h.reason },
    { key: "w", header: t("Placed"), cell: (h) => <>{fmtDateTime(h.placedAt)}{h.placedBy ? <><br /><small>{h.placedBy}</small></> : null}</> },
    { key: "a", header: t("Actions"), cell: (h) => <Button size="sm" onClick={() => release(h)}>{t("Release hold")}</Button> },
  ];
  return (
    <Panel title={t("Legal holds")} description={t("Tickets that no clean-up may touch. Place a hold from the ticket's own page.")}>
      <DataTable label={t("Legal holds")} columns={cols} rows={q.data?.data ?? []} rowKey={(h) => h.id} emptyMessage={t("No tickets are under legal hold.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Panel>
  );
}

function StoredFiles() {
  const run = useRun(); const [check, setCheck] = useState<FileCheck | null>(null); const [busy, setBusy] = useState(false); const [cleaned, setCleaned] = useState<FileClean | null>(null);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  async function scan() { setBusy(true); setCleaned(null); const r = await run(() => get<FileCheck>("/api/retention/files")); setBusy(false); if (r.ok) setCheck(r.data); }
  const clean = () => setConfirm({
    title: t("Remove unreferenced files?"), confirmLabel: t("Remove files"), danger: true,
    body: t("Only stored files that no request refers to and that are more than an hour old are removed. This cannot be undone."),
    onConfirm: async () => { setBusy(true); const r = await run(() => post<FileClean>("/api/retention/files/clean")); setBusy(false); if (r.ok) { setCleaned(r.data); await scan(); setCleaned(r.data); } },
  });
  return (
    <Panel title={t("Stored files")} description={t("Compares the files on the storage volume with the database. Use it after restoring a backup.")}>
      <div className="stack">
        <div><Button onClick={scan} disabled={busy}>{busy ? t("Checking…") : t("Check stored files")}</Button></div>
        {check && <div className="stack" data-testid="files-check">
          <p style={{ margin: 0 }}>{t("{n} files are recorded.", { n: check.files })} {check.missing === 0 ? <Badge tone="green">{t("None are missing")}</Badge> : <Badge tone="red">{t("{n} are missing from the volume", { n: check.missing })}</Badge>}</p>
          {check.missing > 0 && <ul className="stack" style={{ margin: 0 }}>{check.missingSample.map((m) => <li key={m.attachmentId}><b>{m.fileName}</b>{m.ticketId && m.number ? <> · <Link href={`/tickets/${m.ticketId}`}>{m.number}</Link></> : null}</li>)}</ul>}
          {check.missing > check.missingSample.length && <p className="field-help" style={{ margin: 0 }}>{t("Showing the first {n}.", { n: check.missingSample.length })}</p>}
          <p style={{ margin: 0 }}>{check.orphans === 0 ? t("No unreferenced files.") : t("{n} unreferenced files ({size}) can be removed.", { n: check.orphans, size: fmtSize(check.orphanBytes) })}</p>
          {check.orphans > 0 && <div><Button variant="danger-outline" onClick={clean} disabled={busy}>{t("Remove unreferenced files")}</Button></div>}
        </div>}
        {cleaned && <div className="form-alert" role="status" data-testid="files-cleaned">{t("Removed {n} files ({size}).", { n: cleaned.removed, size: fmtSize(cleaned.bytes) })}{cleaned.failed > 0 && <> <b>{t("{n} could not be removed.", { n: cleaned.failed })}</b></>}</div>}
      </div>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Panel>
  );
}

function Page() {
  const q = useQuery(() => get<RetentionView>("/api/retention"), []);
  return (
    <>
      <PageHeader help="keeping-data" title={t("Data Retention")} description={t("Decide how long finished tickets, their files and notifications are kept, and protect the ones you must keep.")} />
      <div className="stack">
        <QueryBoundary q={q} rows={6}>{(v) => <Policy saved={v} reload={q.reload} />}</QueryBoundary>
        <Holds onChange={q.reload} />
        <StoredFiles />
      </div>
    </>
  );
}

export default function RetentionPage() { return <Require perm="retention.manage"><Page /></Require>; }
