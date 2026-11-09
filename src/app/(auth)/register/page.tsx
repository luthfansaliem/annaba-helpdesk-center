"use client";

import { useState } from "react";
import Link from "next/link";
import { Ban, Mail, MailCheck, UserRound } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, AuthInput, PasswordInput } from "@/components/auth/auth";
import { PasswordStrength } from "@/components/ui/password-strength";
import { Button } from "@/components/ui/button";
import { ApiError, get, messageOf, post } from "@/lib/api/client";
import type { PublicSettings } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { SkeletonRows } from "@/components/ui/states";
import { t } from "@/i18n";

export default function RegisterPage() {
  const pub = useQuery(() => get<PublicSettings>("/api/settings/public", undefined, { noRoute: true }), []);
  const [f, setF] = useState({ name: "", email: "", password: "", confirm: "", terms: false });
  const [er, setEr] = useState<Record<string, string>>({}); const [alert, setAlert] = useState(""); const [done, setDone] = useState(false); const [busy, setBusy] = useState(false);

  if (pub.loading && !pub.data) return <AuthFrame><SkeletonRows rows={4} /></AuthFrame>;
  if (!pub.data?.registrationOpen) {
    return (
      <AuthFrame>
        <AuthHeading icon={<Ban size={26} />} tone="warn" title={t("Registration is by invitation")} sub={t("Public sign-up is disabled for this workspace. Ask an administrator to send you an invitation.")} />
        <Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Back to sign in")}</Button>
      </AuthFrame>
    );
  }
  if (pub.data.emailEnabled === false) {
    return (
      <AuthFrame>
        <AuthHeading icon={<Ban size={26} />} tone="warn" title={t("Registration is unavailable")} sub={t("Sign-up needs a confirmation e-mail and this workspace cannot send e-mail right now. Contact an administrator.")} />
        <Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Back to sign in")}</Button>
      </AuthFrame>
    );
  }
  if (done) {
    return (
      <AuthFrame>
        <AuthHeading icon={<MailCheck size={26} />} tone="ok" title={t("Check your inbox")} sub={<>{t("If")}{" "}<b>{f.email}</b> {t("can be registered, we sent a verification link that expires in 24 hours.")}</>} />
        <Button variant="primary" href={`/account/pending?email=${encodeURIComponent(f.email)}`} style={{ width: "100%", height: 46 }}>{t("Resend options")}</Button>
      </AuthFrame>
    );
  }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setAlert("");
    const n: Record<string, string> = {};
    if (f.name.trim().length < 2) n.name = "Enter your full name.";
    if (!/^\S+@\S+\.\S+$/.test(f.email)) n.email = "Enter a valid email address.";
    if (!f.password) n.password = "Choose a password.";
    if (f.confirm !== f.password) n.confirm = "Passwords do not match.";
    if (!f.terms) n.terms = "You must accept the terms to continue.";
    setEr(n); if (Object.keys(n).length) return;
    setBusy(true);
    try { await post("/api/auth/register", { name: f.name, email: f.email, password: f.password }, { noRoute: true }); setDone(true); }
    catch (x) {
      if (x instanceof ApiError && x.fields) setEr(x.fields); else setAlert(messageOf(x));
    } finally { setBusy(false); }
  }
  return (
    <AuthFrame>
      <AuthHeading title={t("Create your account")} sub={t("Join your organisation's workspace")} />
      {alert && <div className="form-alert err" role="alert">{alert}</div>}
      <form onSubmit={submit} noValidate>
        <AuthField id="name" label={t("Full name")}><AuthInput id="name" icon={<UserRound size={18} />} placeholder={t("Your name")} value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} aria-invalid={!!er.name} /></AuthField>
        {er.name && <p className="field-error" role="alert">{er.name}</p>}
        <AuthField id="email" label={t("Work email")}><AuthInput id="email" type="email" icon={<Mail size={18} />} placeholder={t("name@company.com")} value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} aria-invalid={!!er.email} /></AuthField>
        {er.email && <p className="field-error" role="alert">{er.email}</p>}
        <AuthField id="password" label={t("Password")}><PasswordInput id="password" autoComplete="new-password" placeholder={t("Create a password")} value={f.password} onChange={(e) => setF({ ...f, password: e.target.value })} aria-invalid={!!er.password} /></AuthField>
        <PasswordStrength value={f.password} />
        {er.password && <p className="field-error" role="alert">{er.password}</p>}
        <AuthField id="confirm" label={t("Confirm password")}><PasswordInput id="confirm" autoComplete="new-password" placeholder={t("Repeat password")} value={f.confirm} onChange={(e) => setF({ ...f, confirm: e.target.value })} aria-invalid={!!er.confirm} /></AuthField>
        {er.confirm && <p className="field-error" role="alert">{er.confirm}</p>}
        <label className="rem"><input type="checkbox" checked={f.terms} onChange={(e) => setF({ ...f, terms: e.target.checked })} /> {t("I accept the Terms of Use and Privacy Policy")}</label>
        {er.terms && <p className="field-error" role="alert">{er.terms}</p>}
        <button className="signin" type="submit" disabled={busy}>{busy ? t("Creating…") : t("Create account")}</button>
      </form>
      <p className="create">{t("Already registered?")}{" "}<Link href="/login">{t("Sign in")}</Link></p>
    </AuthFrame>
  );
}
