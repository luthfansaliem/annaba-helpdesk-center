"use client";

import { Trash2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

/** Destructive confirmation. `modal` renders it in a fixed overlay; otherwise it's inline (e.g. design specimens). */
export function ConfirmDialog({ title, children, confirmLabel = "Delete", onConfirm, onCancel, modal, icon = <Trash2 size={32} /> }: {
  title: string; children: React.ReactNode; confirmLabel?: string; onConfirm: () => void; onCancel: () => void; modal?: boolean; icon?: React.ReactNode;
}) {
  const body = (
    <div className="del" role="alertdialog" aria-modal={modal || undefined} aria-label={title}>
      <button type="button" className="x" aria-label={t("Close")} onClick={onCancel}><X size={18} /></button>
      <div className="big">{icon}</div>
      <h3>{title}</h3>
      <p>{children}</p>
      <div className="acts">
        <Button onClick={onCancel}>{t("Cancel")}</Button>
        <Button variant="danger" onClick={onConfirm}><Trash2 size={14} /> {confirmLabel}</Button>
      </div>
    </div>
  );
  return modal ? <div className="overlay"><div className="card dialog">{body}</div></div> : body;
}
