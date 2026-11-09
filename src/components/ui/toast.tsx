import { AlertTriangle, Check, Info, X, XCircle } from "lucide-react";
import { cn } from "@/lib/cn";
import { t } from "@/i18n";

export type ToastTone = "success" | "info" | "warning" | "error";

const tone: Record<ToastTone, { cls: string; icon: React.ReactNode }> = {
  success: { cls: "s", icon: <Check size={15} /> },
  info: { cls: "i", icon: <Info size={15} /> },
  warning: { cls: "w", icon: <AlertTriangle size={14} /> },
  error: { cls: "e", icon: <XCircle size={15} /> },
};

export function Toast({ tone: kind = "info", children, onDismiss }: { tone?: ToastTone; children: React.ReactNode; onDismiss?: () => void }) {
  return (
    <div className={cn("toast", tone[kind].cls)} role={kind === "error" || kind === "warning" ? "alert" : "status"}>
      <span className="ti">{tone[kind].icon}</span>{children}
      {onDismiss && <button type="button" className="tx" aria-label={t("Dismiss")} onClick={onDismiss}><X size={14} /></button>}
    </div>
  );
}
