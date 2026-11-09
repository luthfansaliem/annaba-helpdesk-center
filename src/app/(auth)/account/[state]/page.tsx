"use client";

import { Suspense, use, useState } from "react";
import { useSearchParams } from "next/navigation";
import { Ban, Clock, KeyRound, Lock, MailWarning, UserX } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { Button } from "@/components/ui/button";
import { messageOf, post } from "@/lib/api/client";
import { useToast } from "@/lib/toast";
import { t } from "@/i18n";

const copy = {
  pending: { icon: <MailWarning size={26} />, tone: "warn" as const, title: "Verify your email to continue", sub: "Your account is waiting for email verification. Check your inbox or request a new link." },
  suspended: { icon: <Ban size={26} />, tone: "err" as const, title: "Account suspended", sub: "Your account has been suspended by an administrator. Contact your administrator to restore access." },
  deactivated: { icon: <UserX size={26} />, tone: "err" as const, title: "Account deactivated", sub: "This account is no longer active. Contact your administrator if you believe this is a mistake." },
  password_reset_required: { icon: <KeyRound size={26} />, tone: "warn" as const, title: "Choose a new password", sub: "An administrator asked you to choose a new password. We e-mailed you a link; if it has expired, request a new one." },
  locked: { icon: <Lock size={26} />, tone: "warn" as const, title: "Account temporarily locked", sub: "Too many failed attempts. Try again later, or reset your password to get back in sooner." },
};

function View({ id }: { id: string }) {
  const email = useSearchParams().get("email") ?? ""; const { notify } = useToast(); const [busy, setBusy] = useState(false);
  const c = copy[id as keyof typeof copy];
  if (!c) return <AuthFrame><AuthHeading icon={<Clock size={26} />} title={t("Page not found")} /><Button href="/login" variant="primary" style={{ width: "100%" }}>{t("Back to sign in")}</Button></AuthFrame>;
  async function resend() {
    setBusy(true);
    try { await post("/api/auth/verify-email/resend", { email }, { noRoute: true }); notify("success", t("If that address is waiting for verification, we sent a new link.")); } catch (e) { notify("error", messageOf(e)); } finally { setBusy(false); }
  }
  return (
    <AuthFrame>
      <AuthHeading icon={c.icon} tone={c.tone} title={t(c.title)} sub={t(c.sub)} />
      <div className="stack">
        {id === "pending" && email && <Button variant="primary" style={{ height: 46 }} onClick={resend} disabled={busy}>{t("Resend verification email")}</Button>}
        {(id === "locked" || id === "password_reset_required") && <Button variant="primary" href="/forgot-password" style={{ height: 46 }}>{id === "locked" ? t("Reset password") : t("Send me a new link")}</Button>}
        <Button href="/login">{t("Back to sign in")}</Button>
      </div>
    </AuthFrame>
  );
}
export default function AccountStatePage({ params }: { params: Promise<{ state: string }> }) { const { state } = use(params); return <Suspense><View id={state} /></Suspense>; }
