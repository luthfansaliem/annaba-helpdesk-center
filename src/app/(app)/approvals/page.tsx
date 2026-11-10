"use client";

import { useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { DecideDialog, type Deciding } from "@/components/tickets/decide-dialog";
import { ApprovalBadge } from "@/components/tickets/status";
import { DelegationPanel, HandOverDialog } from "@/components/tickets/delegation";
import { get } from "@/lib/api/client";
import type { MyApprovalItem } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

function Inbox({ rows, reload }: { rows: MyApprovalItem[]; reload: () => void }) {
  const [deciding, setDeciding] = useState<Deciding | null>(null); const [handing, setHanding] = useState<MyApprovalItem | null>(null);
  const pending = rows.filter((r) => r.status === "pending"); const decided = rows.filter((r) => r.status !== "pending");
  const label = (r: MyApprovalItem) => `${r.ticketNumber} · ${r.ticketSubject}`;
  const cols: Column<MyApprovalItem>[] = [
    { key: "t", header: t("Request"), cell: (r) => <><Link href={`/tickets/${r.ticketId}`}><b>{r.ticketNumber}</b> · {r.ticketSubject}</Link><br /><small>{r.serviceName}{r.onBehalfOfName ? ` · ${t("for {name}", { name: r.onBehalfOfName })}` : ""}</small></> },
    { key: "r", header: t("Requested by"), className: "hide-sm", cell: (r) => r.requesterName ?? "—" },
    { key: "s", header: t("Step"), className: "hide-sm", cell: (r) => r.stepName },
    { key: "w", header: t("Since"), cell: (r) => fmtDateTime(r.createdAt) },
    { key: "a", header: t("Decision"), cell: (r) => (
      <span className="row" style={{ gap: 6 }}>
        <Button size="sm" variant="primary" aria-label={`${t("Approve")} ${r.ticketNumber}`} onClick={() => setDeciding({ approvalId: r.id, approve: true, label: label(r) })}>{t("Approve")}</Button>
        <Button size="sm" variant="danger-outline" aria-label={`${t("Reject")} ${r.ticketNumber}`} onClick={() => setDeciding({ approvalId: r.id, approve: false, label: label(r) })}>{t("Reject")}</Button>
        <Button size="sm" aria-label={`${t("Hand over")} ${r.ticketNumber}`} onClick={() => setHanding(r)}>{t("Hand over")}</Button>
      </span>) },
  ];
  const doneCols: Column<MyApprovalItem>[] = [
    { key: "t", header: t("Request"), cell: (r) => <Link href={`/tickets/${r.ticketId}`}><b>{r.ticketNumber}</b> · {r.ticketSubject}</Link> },
    { key: "s", header: t("Your decision"), cell: (r) => <ApprovalBadge status={r.status} /> },
    { key: "c", header: t("Comment"), className: "hide-sm", cell: (r) => r.comment ?? "—" },
    { key: "d", header: t("When"), cell: (r) => (r.decidedAt ? fmtDateTime(r.decidedAt) : "—") },
  ];
  return (
    <>
      <Panel title={t("Waiting for your decision")} description={t("The request cannot start until it is approved.")}>
        <DataTable label={t("Waiting for your decision")} columns={cols} rows={pending} rowKey={(r) => r.id} emptyMessage={t("Nothing is waiting for you.")} />
      </Panel>
      <Panel title={t("Decided")}>
        <DataTable label={t("Decided")} columns={doneCols} rows={decided} rowKey={(r) => r.id} emptyMessage={t("You have not decided anything yet.")} />
      </Panel>
      <DelegationPanel onChanged={reload} />
      <DecideDialog deciding={deciding} onClose={() => setDeciding(null)} onDone={reload} />
      <HandOverDialog approvalId={handing?.id ?? null} label={handing ? label(handing) : ""} onClose={() => setHanding(null)} onDone={reload} />
    </>
  );
}

export default function ApprovalsPage() {
  const q = useQuery(() => get<MyApprovalItem[]>("/api/approvals/mine"), []);
  return (
    <>
      <PageHeader help="approvals-and-cover" title={t("Approvals")} description={t("Requests that need your decision, and what you decided before.")} />
      <QueryBoundary q={q} rows={4}>{(rows) => <Inbox rows={rows} reload={q.reload} />}</QueryBoundary>
    </>
  );
}
