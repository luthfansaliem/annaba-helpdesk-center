"use client";

import { useState } from "react";
import Link from "next/link";
import { ArrowLeft, Mail, MailCheck } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, AuthInput } from "@/components/auth/auth";
import { Button } from "@/components/ui/button";
import { ApiError, get, messageOf, post } from "@/lib/api/client";
import type { PublicSettings } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

export default function ForgotPasswordPage() {
  const pub = useQuery(() => get<PublicSettings>("/api/settings/public", undefined, { noRoute: true }), []);
  const [email, setEmail] = useState(""); const [err, setErr] = useState(""); const [alert, setAlert] = useState(""); const [sent, setSent] = useState(false); const [busy, setBusy] = useState(false);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setAlert("");
    if (!/^\S+@\S+\.\S+$/.test(email)) { setErr(t("Enter a valid email address.")); return; }
    setBusy(true);
    try { await post("/api/auth/forgot-password", { email }, { noRoute: true }); setSent(true); } // identical answer whether or not the account exists
    catch (x) { if (x instanceof ApiError && x.code === "validation") setErr(messageOf(x)); else setAlert(messageOf(x)); }
    finally { setBusy(false); }
  }
  if (pub.data?.emailEnabled === false) return (
    <AuthFrame>
      <AuthHeading icon={<Mail size={26} />} tone="warn" title={t("Reset by e-mail is unavailable")} sub={t("This workspace cannot send e-mail right now, so a reset link cannot be delivered. Contact an administrator to have your password reset.")} />
      <Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Back to sign in")}</Button>
    </AuthFrame>
  );
  if (sent) return (
    <AuthFrame>
      <AuthHeading icon={<MailCheck size={26} />} tone="ok" title={t("Check your email")} sub={t("If an account exists for that address, we sent a link to reset your password. It expires in 30 minutes.")} />
      <div className="stack"><Button onClick={() => setSent(false)}>{t("Use a different email")}</Button><Button href="/login">{t("Back to sign in")}</Button></div>
    </AuthFrame>
  );
  return (
    <AuthFrame>
      <AuthHeading title={t("Forgot password?")} sub={t("Enter your email and we'll send reset instructions.")} />
      {alert && <div className="form-alert warn" role="alert">{alert}</div>}
      <form onSubmit={submit} noValidate>
        <AuthField id="email" label={t("Email")}><AuthInput id="email" type="email" icon={<Mail size={18} />} placeholder={t("Enter your email")} value={email} onChange={(e) => setEmail(e.target.value)} aria-invalid={!!err} /></AuthField>
        {err && <p className="field-error" role="alert">{err}</p>}
        <button className="signin" type="submit" disabled={busy}>{busy ? t("Sending…") : t("Send reset link")}</button>
      </form>
      <p className="create"><Link href="/login"><ArrowLeft size={14} style={{ verticalAlign: -2 }} /> {t("Back to sign in")}</Link></p>
    </AuthFrame>
  );
}
