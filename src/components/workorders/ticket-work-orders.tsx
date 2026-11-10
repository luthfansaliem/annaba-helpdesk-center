"use client";

import { useState } from "react";
import Link from "next/link";
import { Plus } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/field";
import { WoStatusBadge, WorkOrderDrawer } from "@/components/workorders/wo-bits";
import { get, put } from "@/lib/api/client";
import type { TicketDetail, TicketWorkOrders } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** The work orders of a ticket, for staff who may read them. Managers add them and decide whether the ticket resolves when they are done. */
export function TicketWorkOrdersPanel({ ticket, onChange }: { ticket: TicketDetail; onChange: () => void }) {
  const run = useRun();
  const q = useQuery(() => get<TicketWorkOrders>(`/api/tickets/${ticket.id}/work-orders`), [ticket.id, ticket.version]);
  const [adding, setAdding] = useState(false);
  const open = ticket.status !== "closed" && ticket.status !== "cancelled" && ticket.status !== "rejected";
  const d = q.data;
  const toggle = async (v: boolean) => { if ((await run(() => put<TicketWorkOrders>(`/api/tickets/${ticket.id}/work-orders/settings`, { resolveTicket: v }), v ? t("The ticket will resolve when its required work orders are done.") : t("The ticket will not resolve by itself."))).ok) q.reload(); };
  if (!d) return null;
  return (
    <Panel title={t("Work orders")} description={d.items.length === 0 ? t("No work orders yet.") : undefined}
      actions={d.canManage && open ? <Button size="sm" variant="primary" onClick={() => setAdding(true)}><Plus size={14} /> {t("New work order")}</Button> : undefined}>
      <div className="stack">
        {d.items.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("Work orders")}>
          {d.items.map((w) => (
            <li key={w.id}>
              <Link href={`/work-orders/${w.id}`}><b>{w.number}</b> · {w.title}</Link> <WoStatusBadge status={w.status} /> {!w.required && <Badge tone="gray" dot={false}>{t("Optional")}</Badge>}
              <br /><small>{w.vendorName ? `${w.vendorName} · ` : ""}{w.assigneeName ?? (w.teamName ? `${t("Team")}: ${w.teamName}` : t("Nobody yet"))}{w.tasksTotal > 0 ? ` · ${t("{done} of {total} checklist lines", { done: w.tasksDone, total: w.tasksTotal })}` : ""}{w.dueAt ? ` · ${t("due")} ${fmtDateTime(w.dueAt)}` : ""}</small>
            </li>
          ))}
        </ul>}
        {d.canManage && open && <label className="row" style={{ gap: 8 }}><Checkbox checked={d.resolveTicket} onChange={(e) => void toggle(e.target.checked)} /> {t("Resolve the ticket when its required work orders are done")}</label>}
      </div>
      <WorkOrderDrawer open={adding} onClose={() => setAdding(false)} onDone={() => { q.reload(); onChange(); }} ticketId={ticket.id} editing={null} resolveTicket={d.resolveTicket} />
    </Panel>
  );
}
