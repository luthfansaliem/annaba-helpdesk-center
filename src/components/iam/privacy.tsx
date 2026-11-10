"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { get, post } from "@/lib/api/client";
import type { ClosureView, RetentionNotice } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Profile → Privacy: download everything we hold about you, and ask to close your account. */
export function PrivacyTab() {
  const run = useRun(); const q = useQuery(() => get<ClosureView | null>("/api/me/closure"), []);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null); const [busy, setBusy] = useState(false);
  const c = q.data ?? null; const open = c && (c.status === "pending" || c.status === "approved");

  async function download() {
    setBusy(true);
    const r = await run(() => get<unknown>("/api/me/export"), t("Your data was downloaded."));
    setBusy(false);
    if (!r.ok) return;
    const url = URL.createObjectURL(new Blob([JSON.stringify(r.data, null, 2)], { type: "application/json" }));
    const a = document.createElement("a"); a.href = url; a.download = `annaba-digital-my-data-${new Date().toISOString().slice(0, 10)}.json`; a.click(); URL.revokeObjectURL(url);
  }

  const ask = () => setConfirm({
    title: t("Ask to close your account?"), confirmLabel: t("Send request"), danger: true, reasonLabel: t("Reason (optional)"),
    body: <>{t("An administrator reviews your request. If it is approved there is a")}{" "}<b>{t("14-day waiting period")}</b> {t("in which you can still cancel; after that your account is closed and your personal data (name, e-mail, profile, picture, sign-in and security data) is erased. This cannot be undone. Security logs about your activity are kept for your organisation's retention period.")}</>,
    onConfirm: async (reason) => { if ((await run(() => post("/api/me/closure", { reason: reason ?? "" }), t("Request sent. An administrator will review it."))).ok) q.reload(); },
  });
  const cancel = async () => { if ((await run(() => post("/api/me/closure/cancel"), t("Closure cancelled. Your account stays."))).ok) q.reload(); };

  const keep = useQuery(() => get<RetentionNotice>("/api/retention/notice"), []);
  const n = keep.data;

  return (
    <div className="stack" style={{ paddingTop: 16 }}>
      <Panel title={t("How long your data is kept")} description={t("What your organisation's helpdesk keeps about your requests, and for how long.")}>
        {!n ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("Loading…")}</p> : !n.enabled || (n.finishedTicketDays === null && n.attachmentDays === null && n.notificationDays === null)
          ? <p style={{ margin: 0 }}>{t("Your organisation has not set a time limit yet, so requests, files and notifications are kept until an administrator removes them.")}</p>
          : <ul className="stack" style={{ margin: 0 }} data-testid="retention-notice">
            {n.finishedTicketDays !== null && <li>{n.ticketAction === "delete"
              ? t("Requests are deleted {n} days after they were finished.", { n: n.finishedTicketDays })
              : t("What you wrote on a request is removed {n} days after it was finished. Only its number, service and dates remain for statistics.", { n: n.finishedTicketDays })}</li>}
            {n.attachmentDays !== null && <li>{t("Files attached to a request are removed {n} days after it was finished.", { n: n.attachmentDays })}</li>}
            {n.notificationDays !== null && <li>{t("Notifications are removed after {n} days.", { n: n.notificationDays })}</li>}
            <li>{t("Security logs about your activity are kept for {n} days.", { n: n.auditDays })}</li>
          </ul>}
      </Panel>
      <Panel title={t("Download my data")} description={t("A copy of what the system holds about you: profile, roles, sessions, your recorded activity, and the requests, comments and files you sent to the helpdesk. It never contains passwords, authenticator secrets or other people's data. You are asked for your password first.")}>
        <Button onClick={download} disabled={busy}><Download size={15} /> {busy ? t("Preparing…") : t("Download my data (JSON)")}</Button>
      </Panel>
      <Panel title={t("Close my account")} description={t("Ask an administrator to close your account and erase your personal data.")}>
        {!c || !open ? (
          <div className="stack">
            {c?.status === "rejected" && <div className="form-alert warn" role="status">{t("Your last request was declined")}{c.decisionNote ? `: ${c.decisionNote}` : "."}</div>}
            {c?.status === "cancelled" && <p className="field-help" style={{ margin: 0 }}>{t("Your last request was cancelled.")}</p>}
            <div><Button variant="danger-outline" onClick={ask}>{t("Ask to close my account")}</Button></div>
          </div>
        ) : (
          <div className="stack">
            <p style={{ margin: 0 }}>
              {c.status === "pending"
                ? <><Badge tone="orange">{t("Waiting for an administrator")}</Badge> {t("Sent")}{" "}{fmtDateTime(c.requestedAt)}.</>
                : <><Badge tone="red">{t("Approved")}</Badge> {t("Your account will be closed and your data erased on")}{" "}<b>{c.executeAfter ? fmtDateTime(c.executeAfter) : "—"}</b>{t(". You can still sign in until then.")}</>}
            </p>
            <div><Button onClick={cancel}>{c.status === "pending" ? t("Cancel my request") : t("Cancel the closure")}</Button></div>
          </div>
        )}
      </Panel>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </div>
  );
}
