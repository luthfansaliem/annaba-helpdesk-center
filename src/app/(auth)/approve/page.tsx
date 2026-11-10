"use client";

import { Suspense, useState } from "react";
import { useSearchParams } from "next/navigation";
import { CircleAlert, CircleCheck, ClipboardCheck } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { SkeletonRows } from "@/components/ui/states";
import { ApiError, get, messageOf, post } from "@/lib/api/client";
import type { ApprovalLinkInfo } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/**
 * `/approve?token=…` — decide a request from the e-mail, without signing in. The token is the credential: bound to one person and one step, single use, expiring.
 * Opening this page only reads; nothing is decided until a button is pressed.
 */
function Approve() {
  const token = useSearchParams().get("token") ?? "";
  const q = useQuery(() => (token ? get<ApprovalLinkInfo>(`/api/approvals/link/${encodeURIComponent(token)}`, undefined, { noRoute: true }) : Promise.reject(new ApiError(410, "expired", "no token"))), [token]);
  const [comment, setComment] = useState(""); const [commentErr, setCommentErr] = useState(""); const [alert, setAlert] = useState("");
  const [busy, setBusy] = useState(false); const [done, setDone] = useState<"approved" | "rejected" | null>(null);

  if (q.loading && !q.data && !q.error) return <AuthFrame hero={false}><SkeletonRows rows={3} /></AuthFrame>;
  if (done) {
    return (
      <AuthFrame hero={false}>
        <AuthHeading icon={<CircleCheck size={26} />} title={done === "approved" ? t("Approved") : t("Rejected")} sub={done === "approved" ? t("Thank you. The request is on its way to the team.") : t("Thank you. The requester has been told.")} />
        <Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Go to sign in")}</Button>
      </AuthFrame>
    );
  }
  const info = q.data;
  if (!info) {
    return (
      <AuthFrame hero={false}>
        <AuthHeading icon={<CircleAlert size={26} />} tone="warn" title={t("This link is no longer valid")} sub={t("It has expired or was already used. If you still need to decide, open Approvals after signing in.")} />
        <Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Go to sign in")}</Button>
      </AuthFrame>
    );
  }
  async function decide(approve: boolean) {
    setAlert(""); setCommentErr(""); setBusy(true);
    try { await post(`/api/approvals/link/${encodeURIComponent(token)}`, { approve, comment: comment || null }, { noRoute: true }); setDone(approve ? "approved" : "rejected"); }
    catch (x) {
      if (x instanceof ApiError && x.fields?.comment) setCommentErr(x.fields.comment);
      else if (x instanceof ApiError && x.code === "expired") q.reload();
      else setAlert(messageOf(x));
    } finally { setBusy(false); }
  }
  return (
    <AuthFrame hero={false}>
      <AuthHeading icon={<ClipboardCheck size={26} />} title={t("Approval needed")} sub={t("{name}, please decide on this request.", { name: info.approverName })} />
      {alert && <div className="form-alert err" role="alert">{alert}</div>}
      <dl className="stack" style={{ margin: "0 0 16px" }}>
        <div><dt><b>{info.ticketNumber}</b></dt><dd style={{ margin: 0 }}>{info.subject}</dd></div>
        <div><dt><b>{t("Service")}</b></dt><dd style={{ margin: 0 }}>{info.serviceName}</dd></div>
        <div><dt><b>{t("Requested by")}</b></dt><dd style={{ margin: 0 }}>{info.requesterName ?? "—"}</dd></div>
        {info.description && <div><dt><b>{t("Details")}</b></dt><dd style={{ margin: 0, whiteSpace: "pre-wrap" }}>{info.description}</dd></div>}
        <div><dt><b>{t("Step")}</b></dt><dd style={{ margin: 0 }}>{info.stepName}</dd></div>
      </dl>
      <Field label={t("Comment (required to reject)")} htmlFor="ap-comment" error={commentErr}>
        <textarea id="ap-comment" rows={3} maxLength={1000} className="textarea" value={comment} onChange={(e) => setComment(e.target.value)} />
      </Field>
      <div className="row" style={{ gap: 8, marginTop: 12 }}>
        <Button variant="primary" disabled={busy} onClick={() => void decide(true)}>{t("Approve")}</Button>
        <Button variant="danger-outline" disabled={busy} onClick={() => void decide(false)}>{t("Reject")}</Button>
      </div>
      <p><small>{t("This link is only for you and works once. It expires {when}.", { when: fmtDateTime(info.expiresAt) })}</small></p>
    </AuthFrame>
  );
}

export default function ApprovePage() { return <Suspense><Approve /></Suspense>; }
