"use client";

import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { Download } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Overlay } from "@/components/ui/overlay";
import { OutcomeBadge } from "@/components/ui/status";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect, SearchBox } from "@/components/data/filter-bar";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { AuditDetail, AuditItem, Paged } from "@/lib/api/types";
import { useDebounced, useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { t } from "@/i18n";

const ALL = { o: "All outcomes", a: "All categories" };
const CATEGORIES = ["auth", "user", "role", "permission", "session", "mfa", "invitation", "settings", "profile", "security", "audit"];

function Detail({ id, onClose }: { id: number | null; onClose: () => void }) {
  const q = useQuery(() => (id === null ? Promise.resolve(null) : get<AuditDetail>(`/api/audit/${id}`)), [id]);
  const d = q.data;
  return (
    <Overlay open={id !== null} onClose={onClose} side="right" title={t("Audit event")} description={id !== null ? `#${id}` : undefined}>
      {q.error ? <p className="tab-placeholder">{q.error.message}</p> : !d ? <p className="tab-placeholder">{t("Loading…")}</p> : (
        <div className="stack">
          <div className="kv wide"><span>{t("Action")}</span><code>{d.action}</code><span>{t("Actor")}</span><b>{d.actor}</b><span>{t("Target")}</span><b>{d.targetType}{d.targetId ? `:${d.targetId.slice(0, 8)}` : ""}</b><span>{t("Time")}</span><b>{fmtDateTime(d.ts)}</b><span>{t("Outcome")}</span><OutcomeBadge outcome={d.outcome} /><span>{t("IP")}</span><b>{d.ip ?? "—"}</b><span>{t("Request")}</span><b>{d.requestId ?? "—"}</b></div>
          {d.meta && Object.keys(d.meta).length > 0 && <><h3 className="sec-t">{t("Metadata")}</h3><div className="kv wide">{Object.entries(d.meta).map(([k, v]) => <span key={k} style={{ display: "contents" }}><span>{k}</span><b>{v}</b></span>)}</div></>}
          {d.diff && d.diff.length > 0 && <><h3 className="sec-t">{t("Changes")}</h3><ul className="diff-list">{d.diff.map((x) => <li key={x.Field} className="rem"><b>{x.Field}</b><br />{t("Before:")}{" "}{x.Before}<br />{t("After:")}{" "}{x.After}</li>)}</ul></>}
          <p className="field-help">{t("Passwords, tokens and MFA secrets are never stored in the audit log.")}</p>
        </div>
      )}
    </Overlay>
  );
}

function Audit() {
  const params = useSearchParams();
  const [q, setQ] = useState(""); const dq = useDebounced(q, 250);
  const [o, setO] = useState(ALL.o); const [a, setA] = useState(ALL.a);
  const [page, setPage] = useState(1); const [size, setSize] = useState(25); const [sel, setSel] = useState<number | null>(null);
  useEffect(() => { const c = params.get("category"); if (c && CATEGORIES.includes(c)) setA(c); }, [params]);
  useEffect(() => setPage(1), [dq, o, a, size]);
  const list = useQuery(() => get<Paged<AuditItem>>("/api/audit", { q: dq.trim() || undefined, outcome: o === ALL.o ? undefined : o.toLowerCase(), category: a === ALL.a ? undefined : a, page, pageSize: size }), [dq, o, a, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  const exportUrl = (format: "csv" | "json") => {
    const p = new URLSearchParams({ format });
    if (dq.trim()) p.set("q", dq.trim()); if (o !== ALL.o) p.set("outcome", o.toLowerCase()); if (a !== ALL.a) p.set("category", a);
    return `/api/audit/export?${p}`;
  };
  const cols: Column<AuditItem>[] = [
    { key: "t", header: t("Time"), cell: (e) => fmtDateTime(e.ts) },
    { key: "a", header: t("Actor"), cell: (e) => e.actor },
    { key: "x", header: t("Action"), cell: (e) => <button type="button" className="link-btn" onClick={() => setSel(e.id)}><code>{e.action}</code></button> },
    { key: "g", header: t("Target"), className: "hide-sm", cell: (e) => e.targetType },
    { key: "o", header: t("Outcome"), cell: (e) => <OutcomeBadge outcome={e.outcome} /> },
  ];
  return (
    <Require perm="audit.read">
      <PageHeader title={t("Audit Logs")} description={t("Append-only record of who did what. Credentials and secrets are never logged.")}
        actions={<div className="row" style={{ gap: 8 }}>{(["csv", "json"] as const).map((f) => <a key={f} className="btn" href={exportUrl(f)} download aria-label={`Export ${f.toUpperCase()} (current filters, up to 10,000 events)`}><Download size={16} /> {f.toUpperCase()}</a>)}</div>} />
      <Panel>
        <FilterBar><SearchBox label={t("Search audit log")} placeholder={t("Search actor, action or target…")} value={q} onChange={setQ} />
          <FilterSelect label={t("Outcome")} value={o} onChange={setO} options={[ALL.o, "Success", "Failure", "Denied"]} />
          <FilterSelect label={t("Category")} value={a} onChange={setA} options={[ALL.a, ...CATEGORIES]} /></FilterBar>
        {!list.loading && !list.error && rows.length === 0 ? <EmptyState title={t("No audit events")} description={t("No events match your filters.")} />
          : <DataTable label={t("Audit events")} columns={cols} rows={rows} rowKey={(e) => String(e.id)} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />}
        {rows.length > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={setSize} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total} events`} />}
      </Panel>
      <Detail id={sel} onClose={() => setSel(null)} />
    </Require>
  );
}
export default function AuditPage() { return <Suspense><Audit /></Suspense>; }
