"use client";

import { useState } from "react";
import { Gavel } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field } from "@/components/ui/field";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { del, put } from "@/lib/api/client";
import type { TicketDetail } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** ADM-006. Shown only to people who manage retention: keeps a ticket (and its files) out of every clean-up until the hold is released. */
export function LegalHoldPanel({ ticket, onDone }: { ticket: TicketDetail; onDone: () => void }) {
  const run = useRun();
  const [open, setOpen] = useState(false); const [reason, setReason] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const hold = ticket.legalHold;

  async function place(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => put(`/api/tickets/${ticket.id}/legal-hold`, { reason }), t("The ticket is under legal hold."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.reason ?? r.error); return; }
    setOpen(false); setReason(""); onDone();
  }
  const release = () => setConfirm({
    title: t("Release the legal hold?"), confirmLabel: t("Release hold"), danger: true,
    body: t("From the next clean-up on, this ticket and its files are treated like any other and are removed when your retention periods say so."),
    onConfirm: async () => { if ((await run(() => del(`/api/tickets/${ticket.id}/legal-hold`), t("The legal hold was released."))).ok) onDone(); },
  });

  return (
    <Panel title={t("Legal hold")} description={hold ? undefined : t("Keep this ticket and its files out of the automatic clean-up.")}>
      {hold ? (
        <div className="stack">
          <p style={{ margin: 0 }}><Badge tone="orange">{t("Under legal hold")}</Badge> {hold.placedBy ? t("by {name}", { name: hold.placedBy }) : ""} · {fmtDateTime(hold.placedAt)}</p>
          <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{hold.reason}</p>
          <div><Button onClick={release}>{t("Release hold")}</Button></div>
        </div>
      ) : <div><Button onClick={() => setOpen(true)}><Gavel size={14} /> {t("Place legal hold")}</Button></div>}
      <Overlay open={open} onClose={() => { setOpen(false); setErr(""); }} title={t("Place a legal hold")} description={`${ticket.number} · ${ticket.subject}`}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="lh-form" disabled={busy}>{busy ? t("Saving…") : t("Place hold")}</Button></>}>
        <form id="lh-form" className="stack" noValidate onSubmit={place}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <p style={{ margin: 0 }}>{t("While the hold lasts, retention never anonymises or deletes this ticket, and nobody can remove its files. Placing and releasing a hold is recorded in the audit log.")}</p>
          <Field label={t("Reason")} htmlFor="lh-reason" required>
            <textarea id="lh-reason" data-autofocus rows={3} maxLength={300} className="textarea" value={reason} onChange={(e) => setReason(e.target.value)} />
            <p className="field-help">{t("For example a dispute, an investigation or a legal request. 3–300 characters.")}</p>
          </Field>
        </form>
      </Overlay>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Panel>
  );
}
