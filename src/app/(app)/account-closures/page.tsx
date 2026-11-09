"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Overlay } from "@/components/ui/overlay";
import { Field } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { ClosureItem, Paged } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TONE: Record<ClosureItem["status"], BadgeTone> = { pending: "orange", approved: "red", rejected: "gray", cancelled: "gray", completed: "gray" };
const LABEL: Record<ClosureItem["status"], string> = { pending: "Pending", approved: "Approved · waiting", rejected: "Declined", cancelled: "Cancelled", completed: "Closed" };

function Queue() {
  const run = useRun(); const [status, setStatus] = useState<"pending" | "all">("pending");
  const q = useQuery(() => get<Paged<ClosureItem>>("/api/account-closures", { status, pageSize: 100 }), [status]);
  const [sel, setSel] = useState<{ item: ClosureItem; approve: boolean } | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const open = (item: ClosureItem, approve: boolean) => { setSel({ item, approve }); setNote(""); setErr(""); };
  async function decide() {
    if (!sel) return; setBusy(true); setErr("");
    const r = await run(() => post(`/api/account-closures/${sel.item.id}/${sel.approve ? "approve" : "reject"}`, { note: note.trim() || null }), sel.approve ? "Approved. The 14-day waiting period has started." : "Request declined.", { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    setSel(null); q.reload();
  }
  const cols: Column<ClosureItem>[] = [
    { key: "p", header: t("Person"), cell: (r) => <><b>{r.userName}</b><br /><small>{r.userEmail}</small></> },
    { key: "w", header: t("Reason"), className: "hide-sm", cell: (r) => r.reason || "—" },
    { key: "s", header: t("Status"), cell: (r) => <Badge tone={TONE[r.status]}>{t(LABEL[r.status])}</Badge> },
    { key: "e", header: t("Closes on"), className: "hide-sm", cell: (r) => (r.status === "approved" && r.executeAfter ? fmtDateTime(r.executeAfter) : "—") },
    { key: "t", header: t("Requested"), className: "hide-md", cell: (r) => fmtDateTime(r.requestedAt) },
    { key: "a", header: t("Actions"), cell: (r) => r.status === "pending" ? <span className="row" style={{ gap: 6 }}><Button size="sm" variant="danger-outline" onClick={() => open(r, true)}>{t("Approve")}</Button><Button size="sm" onClick={() => open(r, false)}>{t("Decline")}</Button></span> : null },
  ];
  const rows = q.data?.data ?? [];
  return (
    <Panel title={t("Closure requests")} description={t("Approving starts a 14-day waiting period in which the person can still cancel; then the account is closed and their personal data erased. You can't decide your own request, and the last Super Admin can't be closed.")}>
      <Tabs variant="segmented" label={t("Status")} value={status} onChange={setStatus} items={[{ key: "pending", label: t("Pending") }, { key: "all", label: t("All") }]} />
      {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("Nothing to decide")} description={t("No closure requests match.")} />
        : <DataTable label={t("Account closure requests")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      <Overlay open={!!sel} onClose={() => setSel(null)} side="right" title={sel?.approve ? t("Approve closure") : t("Decline closure")} description={sel ? `${sel.item.userName} (${sel.item.userEmail})` : undefined}
        footer={<><Button onClick={() => setSel(null)}>{t("Cancel")}</Button><Button variant={sel?.approve ? "danger" : "primary"} disabled={busy} onClick={decide}>{busy ? t("Saving…") : sel?.approve ? t("Approve closure") : t("Decline")}</Button></>}>
        <div className="stack">
          {err && <div className="form-alert err" role="alert">{err}</div>}
          {sel?.item.reason && <p>“{sel.item.reason}”</p>}
          {sel?.approve && <div className="form-alert warn" role="status">{t("After 14 days this account is deactivated and its name, e-mail, profile, sign-in and security data are erased. This cannot be undone.")}</div>}
          <Field label={t("Note to the person (optional)")} htmlFor="ac-note"><textarea id="ac-note" rows={3} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} className="textarea" /></Field>
        </div>
      </Overlay>
    </Panel>
  );
}

export default function AccountClosuresPage() {
  return (
    <Require perm="users.suspend">
      <PageHeader title={t("Account Closures")} description={t("People who asked to close their account and have their personal data erased.")} />
      <Queue />
    </Require>
  );
}
