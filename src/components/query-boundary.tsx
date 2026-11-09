"use client";

import { ApiError } from "@/lib/api/client";
import type { Query } from "@/lib/hooks";
import { AccessDenied, ErrorState, SkeletonRows } from "@/components/ui/states";
import { t } from "@/i18n";

/** Standard loading / error / permission-denied handling around a Query. Renders children only when data is available. */
export function QueryBoundary<T>({ q, children, rows = 6, bare = false }: { q: Query<T>; children: (data: T) => React.ReactNode; rows?: number; bare?: boolean }) {
  if (q.error) {
    if (q.error instanceof ApiError && q.error.status === 403 && q.error.code === "denied") return <AccessDenied />;
    if (q.error instanceof ApiError && q.error.status === 404) return <ErrorState title={t("Not found")} message={q.error.message} />;
    return <ErrorState message={q.error.message} onRetry={q.reload} />;
  }
  if (q.data === undefined) return bare ? null : <SkeletonRows rows={rows} />;
  return <>{children(q.data)}</>;
}
