"use client";

import { useAuth } from "@/lib/auth";
import { AccessDenied } from "@/components/ui/states";

/** UI mirror of server-side authorization. The server remains the source of truth — a bypassed gate only shows an empty page, never data. */
export function Require({ perm, children }: { perm: string; children: React.ReactNode }) {
  const { can } = useAuth();
  return can(perm) ? <>{children}</> : <AccessDenied permission={perm} />;
}
export function Can({ perm, children, fallback = null }: { perm: string; children: React.ReactNode; fallback?: React.ReactNode }) {
  const { can } = useAuth();
  return can(perm) ? <>{children}</> : <>{fallback}</>;
}
