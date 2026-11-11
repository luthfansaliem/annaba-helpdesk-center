"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { VisitDrawer, VisitStatusBadge, visitWhen } from "@/components/visitors/visitor-bits";
import { get, post } from "@/lib/api/client";
import type { PagedVisits, VisitItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** VIS-001. Everyone registers their own visitors; reception expects them, and the host is told when they arrive. */
export default function MyVisitorsPage() {
  const run = useRun();
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const [drawer, setDrawer] = useState<{ editing: VisitItem | null } | null>(null);
  const q = useQuery(() => get<PagedVisits>("/api/visits", { scope: "mine", pageSize: 50 }), [tick]);
  const rows = q.data?.data ?? [];
  const upcoming = rows.filter((v) => v.status === "expected" || v.status === "checked_in");
  const past = rows.filter((v) => !(v.status === "expected" || v.status === "checked_in"));
  const cancel = async (v: VisitItem) => { const r = await run(() => post(`/api/visits/${v.id}/cancel`), t("Visit cancelled."), { silent: false }); if (r.ok) reload(); };
  const list = (items: VisitItem[], id: string) => (
    <ul className="req-list" data-testid={id}>
      {items.map((v) => (
        <li key={v.id}>
          <div><b>{v.visitorName}</b>{v.company ? ` · ${v.company}` : ""}<br /><small className="muted">{visitWhen(v)}{v.place ? ` · ${v.place}` : ""}{v.purpose ? ` · ${v.purpose}` : ""}</small></div>
          <span className="row" style={{ gap: 6 }}>
            <VisitStatusBadge s={v.status} />
            {v.canChange && <Button size="sm" onClick={() => setDrawer({ editing: v })}>{t("Change")}</Button>}
            {v.canChange && <Button size="sm" variant="danger-outline" onClick={() => cancel(v)}>{t("Cancel")}</Button>}
          </span>
        </li>
      ))}
    </ul>
  );
  return (
    <>
      <PageHeader title={t("My visitors")} description={t("Tell reception who is coming to see you. They check your visitor in, and you are told when they arrive.")}
        actions={<Button variant="primary" onClick={() => setDrawer({ editing: null })}><Plus size={16} /> {t("Register a visitor")}</Button>} />
      <Panel title={t("Expected and on site")}>{upcoming.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No visitors expected.")}</p> : list(upcoming, "my-visits")}</Panel>
      {past.length > 0 && <Panel title={t("Earlier visits")}>{list(past.slice(0, 20), "past-visits")}</Panel>}
      <VisitDrawer open={!!drawer} editing={drawer?.editing ?? null} onClose={() => setDrawer(null)} onDone={reload} />
    </>
  );
}
