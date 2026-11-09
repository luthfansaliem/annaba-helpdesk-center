"use client";

import { useEffect } from "react";
import { usePathname, useRouter } from "next/navigation";
import { AppShell } from "@/components/app-shell";
import { useAuth } from "@/lib/auth";
import { SkeletonRows } from "@/components/ui/states";

/** Route guard. The real enforcement is on the API; this only decides where to send people whose session is missing, locked or half-signed-in. */
export default function AppLayout({ children }: { children: React.ReactNode }) {
  const { status } = useAuth(); const router = useRouter(); const pathname = usePathname();
  useEffect(() => {
    if (status === "anon") router.replace(`/login?returnTo=${encodeURIComponent(pathname)}`);
    else if (status === "locked") router.replace("/locked");
    else if (status === "mfa") router.replace("/mfa");
  }, [status, pathname, router]);
  if (status !== "authed") return <div style={{ padding: 40 }}><SkeletonRows rows={4} /></div>;
  return <AppShell>{children}</AppShell>;
}
