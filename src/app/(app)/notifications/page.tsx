"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { CheckCheck } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/field";
import { Pagination } from "@/components/data/pagination";
import { QueryBoundary } from "@/components/query-boundary";
import { get, post } from "@/lib/api/client";
import type { NotificationItem, Paged } from "@/lib/api/types";
import { rel } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export default function NotificationsPage() {
  const run = useRun(); const router = useRouter();
  const [unread, setUnread] = useState(false); const [page, setPage] = useState(1);
  const q = useQuery(() => get<Paged<NotificationItem>>("/api/notifications", { unread: unread || undefined, page, pageSize: 20 }), [unread, page]);
  const open = async (n: NotificationItem) => {
    if (!n.read) await run(() => post(`/api/notifications/${n.id}/read`), undefined, { silent: true });
    window.dispatchEvent(new Event("annaba:notifications"));
    if (n.link) router.push(n.link); else q.reload();
  };
  const readAll = async () => { if ((await run(() => post("/api/notifications/read-all"), t("All marked as read."))).ok) { window.dispatchEvent(new Event("annaba:notifications")); q.reload(); } };
  return (
    <>
      <PageHeader title={t("Notifications")} description={t("What happened on your requests, tickets and approvals.")}
        actions={<span className="row" style={{ gap: 8 }}><Button href="/notifications/settings">{t("Settings")}</Button><Button onClick={() => void readAll()}><CheckCheck size={16} /> {t("Mark all as read")}</Button></span>} />
      <Panel>
        <label className="row" style={{ gap: 8, paddingBottom: 12 }}><Checkbox checked={unread} onChange={(e) => { setUnread(e.target.checked); setPage(1); }} /> {t("Unread only")}</label>
        <QueryBoundary q={q} rows={5}>{(r) => (
          <>
            {r.data.length === 0 ? <p className="tab-placeholder">{t("Nothing here yet.")}</p> : (
              <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                {r.data.map((n) => (
                  <li key={n.id} className="row" style={{ gap: 12, justifyContent: "space-between", alignItems: "flex-start", fontWeight: n.read ? 400 : 700 }}>
                    <span>{n.title} {!n.read && <Badge tone="blue" dot={false}>{t("New")}</Badge>}<br /><small style={{ fontWeight: 400 }}>{n.body ? `${n.body} · ` : ""}{rel(n.createdAt)}</small></span>
                    <Button size="sm" variant="outline" aria-label={`${t("Open")} ${n.title}`} onClick={() => void open(n)}>{t("Open")}</Button>
                  </li>
                ))}
              </ul>
            )}
            {r.total > r.pageSize && <Pagination page={page} pageCount={Math.ceil(r.total / r.pageSize)} onPageChange={setPage} pageSize={r.pageSize} onPageSizeChange={() => {}} pageSizeOptions={[20]} summary={t("{n} notifications", { n: r.total })} />}
          </>
        )}</QueryBoundary>
      </Panel>
    </>
  );
}
