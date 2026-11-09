"use client";

import { useState } from "react";
import { Overlay } from "./overlay";
import { Button } from "./button";
import { t } from "@/i18n";

export type ConfirmSpec = { title: string; body: React.ReactNode; confirmLabel: string; danger?: boolean; reasonLabel?: string; onConfirm: (reason?: string) => void };

/** Generic confirmation for sensitive operations (optionally asks for a reason). */
export function ConfirmOverlay({ spec, onClose }: { spec: ConfirmSpec | null; onClose: () => void }) {
  const [reason, setReason] = useState("");
  return (
    <Overlay open={!!spec} onClose={() => { setReason(""); onClose(); }} title={spec?.title ?? ""}
      footer={<><Button onClick={() => { setReason(""); onClose(); }}>{t("Cancel")}</Button><Button variant={spec?.danger ? "danger" : "primary"} data-autofocus onClick={() => { spec?.onConfirm(reason || undefined); setReason(""); onClose(); }}>{spec?.confirmLabel}</Button></>}>
      <div className="confirm-body">{spec?.body}</div>
      {spec?.reasonLabel && <label className="fl" style={{ display: "block", marginTop: 12 }}><span style={{ fontWeight: 700, fontSize: 12 }}>{spec.reasonLabel}</span><div className="ctl" style={{ marginTop: 5 }}><input value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t("Optional")} /></div></label>}
    </Overlay>
  );
}
