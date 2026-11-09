"use client";

import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect } from "@/components/data/filter-bar";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { EmailDelivery, EmailDeliveryPage } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { rel } from "@/lib/format";
import { t } from "@/i18n";

const STATES = ["All", "Queued", "Sent", "Failed"];

export default function EmailDeliveryPage() {
  const [state, setState] = useState("All");
  const [page, setPage] = useState(1); const [size, setSize] = useState(25);
  useEffect(() => setPage(1), [state, size]);
  const q = useQuery(() => get<EmailDeliveryPage>("/api/email-deliveries", { status: state === "All" ? undefined : state.toLowerCase(), page, pageSize: size }), [state, page, size]);
  const rows = q.data?.data ?? []; const total = q.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  const s = q.data?.summary;
  const cols: Column<EmailDelivery>[] = [
    { key: "w", header: t("Created"), cell: (e) => rel(e.createdAt) },
    { key: "t", header: t("Recipient"), cell: (e) => <>{e.to}<small className="sub">{e.subject}</small></> },
    { key: "s", header: t("Status"), width: 130, cell: (e) => e.status === "sent" ? <Badge tone="green">{t("Sent")}{" "}{rel(e.sentAt)}</Badge> : e.status === "failed" ? <Badge tone="red">{t("Failed")}</Badge> : <Badge tone="orange">{t("Queued")}</Badge> },
    { key: "a", header: t("Attempts"), width: 90, cell: (e) => e.attempts },
    { key: "e", header: t("Detail"), cell: (e) => e.status === "failed" ? <code>{e.lastError ?? "unknown error"}</code> : e.status === "queued" && e.attempts > 0 ? <>{t("retrying")}{" "}{e.nextAttemptAt ? rel(e.nextAttemptAt) : ""}{e.lastError && <small className="sub">{e.lastError}</small>}</> : "—" },
  ];
  return (
    <Require perm="settings.manage">
      <PageHeader title={t("Email Delivery")} description={t("Outgoing messages from the last 7 days. Message bodies are never stored here: they contain one-time links and are wiped once a message is sent or abandoned.")} />
      <Panel>
        <div role="status" style={{ display: "flex", gap: 8, flexWrap: "wrap", marginBottom: 12 }}>
          <Badge tone={s?.configured ? "green" : "red"}>{s ? `${s.provider}${s.configured ? "" : " — not configured"}` : "…"}</Badge>
          <Badge tone="orange">{s?.queued ?? 0} {t("queued")}</Badge>
          <Badge tone="green">{s?.sent24h ?? 0} {t("sent in 24 h")}</Badge>
          <Badge tone={s && s.failed7d > 0 ? "red" : "gray"}>{s?.failed7d ?? 0} {t("failed in 7 days")}</Badge>
        </div>
        {s && s.failed7d > 0 && <p className="field-help" style={{ margin: "0 0 12px" }}>{t("Failed messages are not retried automatically after 3 attempts. Check the SMTP/API credentials, then ask the user to repeat the action (for example resend an invitation).")}</p>}
        <FilterBar><FilterSelect label={t("Status")} value={state} onChange={setState} options={STATES} /></FilterBar>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No messages")} description={t("Nothing matches this filter.")} />
          : <DataTable label={t("Email delivery")} columns={cols} rows={rows} rowKey={(e) => e.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
        {rows.length > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={setSize} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total}`} />}
      </Panel>
    </Require>
  );
}
