import { AlertTriangle, Lock, RefreshCw } from "lucide-react";
import { Button } from "./button";
import { cn } from "@/lib/cn";
import { t } from "@/i18n";

export function Skeleton({ h = 14, w = "100%", className }: { h?: number; w?: number | string; className?: string }) {
  return <span className={cn("skeleton", className)} style={{ height: h, width: w }} aria-hidden />;
}
export function SkeletonRows({ rows = 5 }: { rows?: number }) {
  return <div className="sk-rows" role="status" aria-label={t("Loading")}>{Array.from({ length: rows }).map((_, i) => <Skeleton key={i} h={34} />)}</div>;
}
export function ErrorState({ title = "Something went wrong", message = "We couldn't load this data. Try again.", onRetry }: { title?: string; message?: string; onRetry?: () => void }) {
  return (
    <div className="state-box" role="alert">
      <span className="state-ic err"><AlertTriangle size={26} /></span><h3>{title}</h3><p>{message}</p>
      {onRetry && <Button onClick={onRetry}><RefreshCw size={14} /> {t("Retry")}</Button>}
    </div>
  );
}
export function AccessDenied({ permission }: { permission?: string }) {
  return (
    <div className="state-box" role="alert">
      <span className="state-ic lock"><Lock size={26} /></span><h3>{t("Access denied")}</h3>
      <p>{t("You don't have permission to view this page")}{permission ? <> {t("(requires")}{" "}<code>{permission}</code>)</> : null}{t(". Ask an administrator to grant access.")}</p>
      <Button href="/dashboard">{t("Back to dashboard")}</Button>
    </div>
  );
}
