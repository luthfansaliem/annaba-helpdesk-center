"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { MyChannelsPanel } from "@/components/notifications/my-channels";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { get, post, put } from "@/lib/api/client";
import type { NotificationPrefItem, NotificationPrefs } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const KIND: Record<string, () => { title: string; hint: string }> = {
  "ticket.assigned": () => ({ title: t("A ticket is assigned to you"), hint: t("You are the person who should work on it.") }),
  "ticket.reply": () => ({ title: t("Someone replies on a ticket"), hint: t("A public answer on your request, or a reply on a ticket you work.") }),
  "ticket.resolved": () => ({ title: t("Your request is resolved"), hint: t("Time to confirm that it is solved, or reopen it.") }),
  "ticket.started": () => ({ title: t("Work starts on your request"), hint: t("An agent has picked it up.") }),
  "ticket.hold": () => ({ title: t("Your request is put on hold"), hint: t("With the reason it is waiting.") }),
  "ticket.closed": () => ({ title: t("Your request is closed"), hint: t("It was confirmed or closed automatically.") }),
  "ticket.cancelled": () => ({ title: t("Your request is cancelled"), hint: t("Withdrawn or cancelled by staff.") }),
  "ticket.reopened": () => ({ title: t("A ticket you work is reopened"), hint: t("The requester says it is not solved yet.") }),
  "ticket.note": () => ({ title: t("An internal note on a ticket you work"), hint: t("Visible to staff only.") }),
  "sla.at_risk": () => ({ title: t("A ticket is at risk of missing its deadline"), hint: t("For tickets you work or supervise.") }),
  "sla.breached": () => ({ title: t("A ticket missed its deadline"), hint: t("For tickets you work or supervise.") }),
  "approval.needed": () => ({ title: t("Your approval is needed"), hint: t("Work cannot start until you decide, so this is always on.") }),
  "approval.approved": () => ({ title: t("Your request is approved"), hint: t("It goes to the team.") }),
  "approval.rejected": () => ({ title: t("Your request is rejected"), hint: t("With the reason.") }),
  "approval.delegated": () => ({ title: t("Someone asks you to cover their approvals"), hint: t("While they are away.") }),
  "workorder.assigned": () => ({ title: t("A work order is given to you"), hint: t("You are the person who should do it.") }),
  "workorder.done": () => ({ title: t("A work order on your ticket is done"), hint: t("For the person who works the ticket.") }),
  "ticket.low_rating": () => ({ title: t("A request was rated low (1–2)"), hint: t("For team leads, or the people who see every ticket when the team has no lead.") }),
  "booking.approval": () => ({ title: t("A booking needs your approval"), hint: t("For people who approve bookings of rooms and vehicles that need it.") }),
  "booking.decided": () => ({ title: t("Your booking is approved or not"), hint: t("Also when nobody approved it before it started.") }),
  "booking.reminder": () => ({ title: t("A booking starts in 30 minutes"), hint: t("With a reminder to check in when the room asks for it.") }),
  "stock.low": () => ({ title: t("A stock item runs low"), hint: t("Once each time an item at a place falls to its reorder level.") }),
  "supply.new": () => ({ title: t("A supply request to handle"), hint: t("For the supplies desk: a new request, or one that was approved.") }),
  "supply.approval": () => ({ title: t("A supply request needs your approval"), hint: t("When someone who reports to you asks for supplies above the approval amount.") }),
  "supply.update": () => ({ title: t("News on my supply request"), hint: t("Approved, not approved, ready to collect or closed.") }),
  "mail.arrived": () => ({ title: t("Mail or a parcel for you is at reception"), hint: t("At once, and once more if it is still waiting after two days.") }),
  "mail.sent": () => ({ title: t("Your outgoing mail has left"), hint: t("With the courier and the waybill number.") }),
  "visitor.arrived": () => ({ title: t("Your visitor has arrived"), hint: t("When reception checks in someone who is visiting you.") }),
  "visitor.flagged": () => ({ title: t("Someone on the block list is registered"), hint: t("For people who keep the visitor block list.") }),
  "vehicle.alert": () => ({ title: t("A vehicle needs a driver or came back damaged"), hint: t("For people who manage bookings.") }),
  "resource.document": () => ({ title: t("A vehicle document or driving licence expires"), hint: t("30 days before and on the day: STNK, insurance, KIR, and your own licence.") }),
  "booking.changed": () => ({ title: t("Someone books for you, or cancels your booking"), hint: t("When a colleague books a room or vehicle in your name, or General Affairs cancels your booking.") }),
  "budget.threshold": () => ({ title: t("A budget reaches 80% or is used up"), hint: t("For people who manage budgets, once at each level.") }),
  "vendor.contract": () => ({ title: t("A vendor contract ends soon or has ended"), hint: t("For people who manage vendors: 30 days before, and when it ends.") }),
  "asset.warranty": () => ({ title: t("An asset's warranty ends soon or has ended"), hint: t("For people who manage assets: 30 days before, and when it ends.") }),
};
const GROUPS: [string, () => string][] = [["tickets", () => t("Tickets and requests")], ["sla", () => t("Service levels")], ["approvals", () => t("Approvals")], ["work", () => t("Work orders")], ["assets", () => t("Assets")], ["vendors", () => t("Vendors")], ["costs", () => t("Costs")], ["bookings", () => t("Bookings")], ["visitors", () => t("Visitors")], ["inventory", () => t("Inventory and supplies")]];

function Settings({ prefs, reload }: { prefs: NotificationPrefs; reload: () => void }) {
  const run = useRun();
  const [draft, setDraft] = useState<Record<string, { inApp: boolean; email: boolean }>>({}); const [busy, setBusy] = useState(false);
  const value = (i: NotificationPrefItem) => draft[i.kind] ?? { inApp: i.inApp, email: i.email };
  const dirty = Object.keys(draft).length > 0;
  const set = (i: NotificationPrefItem, patch: Partial<{ inApp: boolean; email: boolean }>) => setDraft((d) => {
    const next = { ...value(i), ...patch };
    const copy = { ...d }; if (next.inApp === i.inApp && next.email === i.email) delete copy[i.kind]; else copy[i.kind] = next; return copy;
  });
  async function save() {
    setBusy(true);
    const r = await run(() => put<NotificationPrefs>("/api/notifications/preferences", { items: Object.entries(draft).map(([kind, v]) => ({ kind, ...v })) }), t("Notification settings saved."));
    setBusy(false); if (r.ok) { setDraft({}); reload(); }
  }
  async function reset() {
    setBusy(true);
    const r = await run(() => post<NotificationPrefs>("/api/notifications/preferences/reset"), t("Notification settings reset."));
    setBusy(false); if (r.ok) { setDraft({}); reload(); }
  }
  return (
    <div className="stack">
      {!prefs.emailMaster && <div className="form-alert" role="note">
        {t("E-mail is switched off in your profile, so only approval requests are still sent by e-mail.")} <Link href="/profile">{t("Open your profile")}</Link>
      </div>}
      {GROUPS.map(([g, title]) => (
        <Panel key={g} title={title()}>
          <div className="tbl-wrap"><table className="tbl" aria-label={title()}>
            <thead><tr><th scope="col">{t("When")}</th><th scope="col">{t("In the app")}</th><th scope="col">{t("By e-mail")}</th></tr></thead>
            <tbody>
              {prefs.items.filter((i) => i.group === g).map((i) => {
                const v = value(i); const text = KIND[i.kind]?.() ?? { title: i.kind, hint: "" };
                return (
                  <tr key={i.kind}>
                    <td><b>{text.title}</b><br /><small className="sub">{text.hint}</small></td>
                    <td><Checkbox aria-label={`${text.title} — ${t("In the app")}`} checked={v.inApp} disabled={i.locked} onChange={(e) => set(i, { inApp: e.target.checked })} /></td>
                    <td><Checkbox aria-label={`${text.title} — ${t("By e-mail")}`} checked={v.email} disabled={i.locked} onChange={(e) => set(i, { email: e.target.checked })} /></td>
                  </tr>
                );
              })}
            </tbody>
          </table></div>
        </Panel>
      ))}
      <span className="row" style={{ gap: 8 }}>
        <Button variant="primary" onClick={save} disabled={!dirty || busy}>{busy ? t("Saving…") : t("Save changes")}</Button>
        <Button onClick={reset} disabled={busy || (!dirty && !prefs.items.some((i) => i.custom))}>{t("Back to the usual settings")}</Button>
      </span>
    </div>
  );
}

export default function NotificationSettingsPage() {
  const q = useQuery(() => get<NotificationPrefs>("/api/notifications/preferences"), []);
  return (
    <>
      <PageHeader title={t("Notification settings")} description={t("Choose what you are told about, and whether it also reaches you by e-mail.")}
        actions={<Button href="/notifications"><ArrowLeft size={14} /> {t("Notifications")}</Button>} />
      <MyChannelsPanel />
      <QueryBoundary q={q} rows={6}>{(p) => <Settings prefs={p} reload={q.reload} />}</QueryBoundary>
    </>
  );
}
