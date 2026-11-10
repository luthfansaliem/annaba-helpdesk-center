"use client";

import { useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { post } from "@/lib/api/client";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export type Deciding = { approvalId: string; approve: boolean; label: string };

/** Approve or reject one approval. A rejection needs a reason; an approval may carry a note. */
export function DecideDialog({ deciding, onClose, onDone }: { deciding: Deciding | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [comment, setComment] = useState(""); const [err, setErr] = useState(""); const [commentErr, setCommentErr] = useState(""); const [busy, setBusy] = useState(false); const [seen, setSeen] = useState<string | null>(null);
  const key = deciding ? `${deciding.approvalId}:${deciding.approve}` : null;
  if (key !== seen) { setSeen(key); setComment(""); setErr(""); setCommentErr(""); }
  if (!deciding) return null;
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!deciding) return; setErr(""); setCommentErr(""); setBusy(true);
    const r = await run(() => post(`/api/approvals/${deciding.approvalId}/decide`, { approve: deciding.approve, comment: comment || null }), deciding.approve ? t("Approved.") : t("Rejected."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.comment ? "" : r.error); setCommentErr(r.fields?.comment ?? ""); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open onClose={onClose} title={deciding.approve ? t("Approve this request") : t("Reject this request")} description={deciding.label}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant={deciding.approve ? "primary" : "danger"} type="submit" form="decide-form" disabled={busy}>{busy ? t("Saving…") : deciding.approve ? t("Approve") : t("Reject")}</Button></>}>
      <form id="decide-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={deciding.approve ? t("Comment (optional)") : t("Why are you rejecting it?")} htmlFor="dd-comment" required={!deciding.approve} error={commentErr}>
          <textarea id="dd-comment" data-autofocus rows={4} maxLength={1000} className="textarea" value={comment} onChange={(e) => setComment(e.target.value)} />
        </Field>
      </form>
    </Overlay>
  );
}
