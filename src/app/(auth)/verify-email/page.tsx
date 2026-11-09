"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import { CheckCircle2, Clock } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, AuthInput } from "@/components/auth/auth";
import { Button } from "@/components/ui/button";
import { ApiError, messageOf, post } from "@/lib/api/client";
import { Mail } from "lucide-react";
import { t } from "@/i18n";

/** Consumes the one-time token from the emailed link (`/verify-email?token=…`). Also confirms an email *change*. */
function Verify() {
  const token = useSearchParams().get("token");
  const [stage, setStage] = useState<"loading" | "ok" | "expired" | "error" | "none">(token ? "loading" : "none");
  const [msg, setMsg] = useState(""); const ran = useRef(false);
  const [email, setEmail] = useState(""); const [sent, setSent] = useState(false); const [busy, setBusy] = useState(false); const [err, setErr] = useState("");
  useEffect(() => {
    if (!token || ran.current) return; ran.current = true; // single-use token: never submit twice (React strict mode)
    post("/api/auth/verify-email", { token }, { noRoute: true }).then(() => setStage("ok"), (e) => { if (e instanceof ApiError && e.code === "expired") setStage("expired"); else { setMsg(messageOf(e)); setStage("error"); } });
  }, [token]);
  async function resend(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    try { await post("/api/auth/verify-email/resend", { email }, { noRoute: true }); setSent(true); } catch (x) { setErr(messageOf(x)); } finally { setBusy(false); }
  }
  if (stage === "loading") return <AuthFrame><AuthHeading title={t("Verifying…")} sub={t("Checking your link")} /></AuthFrame>;
  if (stage === "ok") return <AuthFrame><AuthHeading icon={<CheckCircle2 size={28} />} tone="ok" title={t("Email verified")} sub={t("Thanks — your email address is confirmed. You can sign in now.")} /><Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Continue to sign in")}</Button></AuthFrame>;
  if (stage === "error") return <AuthFrame><AuthHeading icon={<Clock size={26} />} tone="err" title={t("Something went wrong")} sub={msg} /><Button href="/login">{t("Back to sign in")}</Button></AuthFrame>;
  return (
    <AuthFrame>
      <AuthHeading icon={<Clock size={26} />} tone="warn" title={stage === "expired" ? t("Link expired or already used") : t("Verify your email")} sub={stage === "expired" ? t("Verification links work once and expire after 24 hours. Request a new one below.") : t("Open the link we emailed you, or request a new one.")} />
      {sent ? <div className="form-alert ok" role="status">{t("If that address is waiting for verification, we sent a new link.")}</div> : (
        <form onSubmit={resend} noValidate>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <AuthField id="email" label={t("Email")}><AuthInput id="email" type="email" icon={<Mail size={18} />} value={email} onChange={(e) => setEmail(e.target.value)} placeholder={t("Enter your email")} /></AuthField>
          <button className="signin" type="submit" disabled={busy || !email}>{busy ? t("Sending…") : t("Send a new link")}</button>
        </form>
      )}
    </AuthFrame>
  );
}
export default function VerifyEmailPage() { return <Suspense><Verify /></Suspense>; }
