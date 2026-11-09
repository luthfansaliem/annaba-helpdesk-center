"use client";

import { useEffect, useState } from "react";
import { Check } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { SeverityBadge } from "@/components/ui/status";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect } from "@/components/data/filter-bar";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { Paged, SecurityEvent } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { rel } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export default function SecurityEventsPage() {
  const run = useRun();
  const [sev, setSev] = useState("All severities"); const [ack, setAck] = useState("All");
  const [page, setPage] = useState(1); const [size, setSize] = useState(25);
  useEffect(() => setPage(1), [sev, ack, size]);
  const q = useQuery(() => get<Paged<SecurityEvent>>("/api/security-events", { severity: sev === "All severities" ? undefined : sev.toLowerCase(), acknowledged: ack === "All" ? undefined : ack === "Reviewed", page, pageSize: size }), [sev, ack, page, size]);
  const rows = q.data?.data ?? []; const total = q.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  const cols: Column<SecurityEvent>[] = [
    { key: "t", header: t("When"), cell: (e) => rel(e.ts) },
    { key: "y", header: t("Event"), cell: (e) => <code>{e.type}</code> },
    { key: "d", header: t("Detail"), cell: (e) => <>{e.detail}{e.userName && <small className="sub">{e.userName}</small>}</> },
    { key: "s", header: t("Severity"), cell: (e) => <SeverityBadge severity={e.severity} /> },
    { key: "a", header: t("Review"), width: 130, cell: (e) => e.acknowledged ? <Badge tone="green" dot={false}><Check size={11} /> {t("Reviewed")}</Badge> : <Button size="sm" onClick={async () => { if ((await run(() => post(`/api/security-events/${e.id}/ack`), t("Event acknowledged."))).ok) q.reload(); }}>{t("Acknowledge")}</Button> },
  ];
  return (
    <Require perm="security.read">
      <PageHeader title={t("Security Events")} description={t("Failed logins, lockouts, resets, MFA changes and privilege changes.")} />
      <Panel>
        <FilterBar><FilterSelect label={t("Severity")} value={sev} onChange={setSev} options={["All severities", "High", "Medium", "Low"]} /><FilterSelect label={t("Review state")} value={ack} onChange={setAck} options={["All", "Needs review", "Reviewed"].map((x) => x === "Needs review" ? "Needs review" : x)} /></FilterBar>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No security events")} description={t("Nothing matches these filters.")} />
          : <DataTable label={t("Security events")} columns={cols} rows={rows} rowKey={(e) => e.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
        {rows.length > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={setSize} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total} events`} />}
      </Panel>
    </Require>
  );
}
