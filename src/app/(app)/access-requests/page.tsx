"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import type { AccessRequestItem, Paged, RequestableRole } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TONE: Record<AccessRequestItem["status"], BadgeTone> = { pending: "orange", approved: "green", rejected: "red", cancelled: "gray" };
const StatusBadge = ({ s }: { s: AccessRequestItem["status"] }) => <Badge tone={TONE[s]}>{s[0].toUpperCase() + s.slice(1)}</Badge>;

function RequestForm({ onDone }: { onDone: () => void }) {
  const run = useRun(); const roles = useQuery(() => get<RequestableRole[]>("/api/access-requests/roles"), []);
  const [roleId, setRoleId] = useState(""); const [reason, setReason] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const list = roles.data ?? []; const chosen = list.find((r) => r.id === roleId);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    if (!roleId) { setErr(t("Choose a role.")); return; }
    if (reason.trim().length < 10) { setErr(t("Explain why you need it (at least 10 characters).")); return; }
    setBusy(true);
    const r = await run(() => post("/api/access-requests", { roleId, reason }), t("Request sent. An administrator will review it."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    setRoleId(""); setReason(""); roles.reload(); onDone();
  }
  return (
    <Panel title={t("Request a role")} description={t("Ask for access you don't have yet. An administrator who can assign roles reviews it.")}>
      <form className="stack" noValidate onSubmit={submit} aria-label={t("Request a role")}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {roles.data && list.length === 0 ? <p className="field-help">{t("There are no other roles you can request.")}</p> : <>
          <Field label={t("Role")} htmlFor="ar-role" required>
            <SelectInput id="ar-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} placeholder={t("Choose a role…")} options={list.map((r) => ({ value: r.id, label: r.name }))} />
          </Field>
          {chosen && <p className="field-help">{chosen.description || "No description."} {t("Includes")}{" "}{chosen.permissionCount} {t("permission")}{chosen.permissionCount === 1 ? "" : "s"}.</p>}
          <Field label={t("Why do you need it?")} htmlFor="ar-reason" required>
            <textarea id="ar-reason" rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} className="textarea" />
          </Field>
          <div><Button variant="primary" type="submit" disabled={busy || !roles.data}>{busy ? t("Sending…") : t("Send request")}</Button></div>
        </>}
      </form>
    </Panel>
  );
}

function Mine({ refreshKey, onChange }: { refreshKey: number; onChange: () => void }) {
  const run = useRun(); const q = useQuery(() => get<AccessRequestItem[]>("/api/access-requests/mine"), [refreshKey]);
  const cols: Column<AccessRequestItem>[] = [
    { key: "r", header: t("Role"), cell: (r) => <b>{r.role.name}</b> },
    { key: "s", header: t("Status"), cell: (r) => <StatusBadge s={r.status} /> },
    { key: "w", header: t("Reason"), className: "hide-sm", cell: (r) => r.reason },
    { key: "d", header: t("Decision"), className: "hide-sm", cell: (r) => (r.decidedAt ? `${r.decidedByName ?? "—"}${r.decisionNote ? `: ${r.decisionNote}` : ""}` : "—") },
    { key: "t", header: t("Requested"), cell: (r) => fmtDateTime(r.createdAt) },
    { key: "a", header: t("Actions"), cell: (r) => r.status === "pending" ? <Button size="sm" onClick={async () => { const x = await run(() => post(`/api/access-requests/${r.id}/cancel`), t("Request cancelled.")); if (x.ok) { q.reload(); onChange(); } }}>{t("Cancel")}</Button> : null },
  ];
  const rows = q.data ?? [];
  return (
    <Panel title={t("My requests")}>
      {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No requests yet")} description={t("Requests you send appear here.")} />
        : <DataTable label={t("My access requests")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
    </Panel>
  );
}

function Review({ refreshKey, onChange }: { refreshKey: number; onChange: () => void }) {
  const run = useRun(); const [status, setStatus] = useState<"pending" | "all">("pending");
  const q = useQuery(() => get<Paged<AccessRequestItem>>("/api/access-requests", { status, pageSize: 100 }), [status, refreshKey]);
  const [sel, setSel] = useState<{ item: AccessRequestItem; approve: boolean } | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const open = (item: AccessRequestItem, approve: boolean) => { setSel({ item, approve }); setNote(""); setErr(""); };
  async function decide() {
    if (!sel) return; setBusy(true); setErr("");
    const r = await run(() => post(`/api/access-requests/${sel.item.id}/${sel.approve ? "approve" : "reject"}`, { note: note.trim() || null }), sel.approve ? "Request approved." : "Request rejected.", { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    setSel(null); q.reload(); onChange();
  }
  const cols: Column<AccessRequestItem>[] = [
    { key: "p", header: t("Person"), cell: (r) => <><b>{r.requesterName}</b><br /><small>{r.requesterEmail}</small></> },
    { key: "r", header: t("Role"), cell: (r) => r.role.name },
    { key: "w", header: t("Reason"), className: "hide-sm", cell: (r) => r.reason },
    { key: "s", header: t("Status"), cell: (r) => <StatusBadge s={r.status} /> },
    { key: "t", header: t("Requested"), className: "hide-sm", cell: (r) => fmtDateTime(r.createdAt) },
    { key: "a", header: t("Actions"), cell: (r) => r.status === "pending" ? <span className="row" style={{ gap: 6 }}><Button size="sm" variant="primary" onClick={() => open(r, true)}>{t("Approve")}</Button><Button size="sm" variant="danger-outline" onClick={() => open(r, false)}>{t("Reject")}</Button></span> : null },
  ];
  const rows = q.data?.data ?? [];
  return (
    <Panel title={t("Review requests")} description={t("Approving assigns the role through the normal role-assignment path. You can't decide your own request.")}>
      <Tabs variant="segmented" label={t("Status")} value={status} onChange={setStatus} items={[{ key: "pending", label: t("Pending") }, { key: "all", label: t("All") }]} />
      {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("Nothing to review")} description={t("No requests match.")} />
        : <DataTable label={t("Access requests to review")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      <Overlay open={!!sel} onClose={() => setSel(null)} side="right" title={sel?.approve ? t("Approve request") : t("Reject request")}
        description={sel ? `${sel.item.requesterName} → ${sel.item.role.name}` : undefined}
        footer={<><Button onClick={() => setSel(null)}>{t("Cancel")}</Button><Button variant={sel?.approve ? "primary" : "danger"} disabled={busy} onClick={decide}>{busy ? t("Saving…") : sel?.approve ? t("Approve") : t("Reject")}</Button></>}>
        <div className="stack">
          {err && <div className="form-alert err" role="alert">{err}</div>}
          {sel && <p>“{sel.item.reason}”</p>}
          <Field label={t("Note (optional)")} htmlFor="ar-note"><textarea id="ar-note" rows={3} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} className="textarea" /></Field>
        </div>
      </Overlay>
    </Panel>
  );
}

export default function AccessRequestsPage() {
  const { can } = useAuth(); const [k, setK] = useState(0); const bump = () => setK((x) => x + 1);
  return (
    <>
      <PageHeader title={t("Access Requests")} description={t("Ask for a role, track your requests, and (for administrators) review what others asked for.")} />
      <div className="stack">
        <RequestForm onDone={bump} />
        <Mine refreshKey={k} onChange={bump} />
        {can("users.assign_role") && <Review refreshKey={k} onChange={bump} />}
      </div>
    </>
  );
}
