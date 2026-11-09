"use client";

import { Suspense, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { AlertCircle, ArrowRight, Info, KeyRound, Mail } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, AuthInput, PasswordInput } from "@/components/auth/auth";
import { ApiError, get, messageOf } from "@/lib/api/client";
import type { PublicSettings } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { safeReturnTo } from "@/lib/format";
import { t } from "@/i18n";

const SSO_ERRORS: Record<string, string> = {
  failed: "Single sign-on didn't complete. Please try again.",
  denied: "We couldn't match that account to an active user. Ask your administrator for an invitation.",
  suspended: "This account is suspended. Contact your administrator.",
  locked: "This account is temporarily locked. Try again later.",
};

function LoginForm() {
  const { signIn } = useAuth(); const router = useRouter(); const params = useSearchParams();
  const returnTo = safeReturnTo(params.get("returnTo"));
  const pub = useQuery(() => get<PublicSettings>("/api/settings/public", undefined, { noRoute: true }), []);
  const [email, setEmail] = useState(""); const [password, setPassword] = useState(""); const [remember, setRemember] = useState(true);
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});
  const [alert, setAlert] = useState<{ tone: "err" | "warn"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setAlert(null);
    const er: typeof errors = {};
    if (!/^\S+@\S+\.\S+$/.test(email)) er.email = "Enter a valid email address.";
    if (!password) er.password = "Enter your password.";
    setErrors(er); if (Object.keys(er).length) return;
    setBusy(true);
    try {
      const r = await signIn(email, password, remember, returnTo);
      switch (r.status) {
        case "ok": router.push(r.next ?? returnTo); break;
        case "mfa_required": router.push(`/mfa?returnTo=${encodeURIComponent(returnTo)}`); break;
        case "mfa_enrollment_required": router.push("/profile?tab=security"); break;
        case "verify_email": router.push(`/account/pending?email=${encodeURIComponent(email)}`); break;
        default: router.push(`/account/${r.status}`);
      }
    } catch (x) {
      if (x instanceof ApiError && x.code === "rate_limited") setAlert({ tone: "warn", text: messageOf(x) });
      else setAlert({ tone: "err", text: messageOf(x) });
    } finally { setBusy(false); }
  }

  return (
    <AuthFrame>
      <AuthHeading title={t("Welcome back 👋")} sub={t("Sign in to your Annaba Digital Workspace")} />
      {params.get("expired") && <div className="form-alert warn" role="status"><Info size={16} /> {t("Your session expired. Sign in again to continue.")}</div>}
      {params.get("reset") && <div className="form-alert ok" role="status">{t("Password updated. Sign in with your new password.")}</div>}
      {params.get("accepted") && <div className="form-alert ok" role="status">{t("Account activated. Sign in to continue.")}</div>}
      {params.get("sso") && <div className="form-alert err" role="alert"><AlertCircle size={16} /> {SSO_ERRORS[params.get("sso")!] ?? SSO_ERRORS.failed}</div>}
      {alert && <div className={"form-alert " + alert.tone} role="alert"><AlertCircle size={16} /> {alert.text}</div>}
      <form onSubmit={submit} noValidate>
        <AuthField id="email" label={t("Email")}>
          <AuthInput id="email" type="email" icon={<Mail size={18} />} placeholder={t("Enter your email")} autoComplete="email" value={email} onChange={(e) => setEmail(e.target.value)} aria-invalid={!!errors.email} />
        </AuthField>
        {errors.email && <p className="field-error" role="alert">{errors.email}</p>}
        <AuthField id="password" label={t("Password")} aside={pub.data?.emailEnabled === false ? undefined : <Link href="/forgot-password">{t("Forgot password?")}</Link>}>
          <PasswordInput id="password" placeholder={t("Enter your password")} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} aria-invalid={!!errors.password} />
        </AuthField>
        {errors.password && <p className="field-error" role="alert">{errors.password}</p>}
        <label className="rem"><input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} /> {t("Remember me")}</label>
        <button className="signin" type="submit" disabled={busy}>{busy ? t("Signing in…") : <>{t("Sign In")}{" "}<ArrowRight size={20} /></>}</button>
      </form>
      {pub.data?.ssoEnabled && <>
        <div className="or-sep" role="separator"><span>{t("or")}</span></div>
        <a className="sso-btn" href={`/api/auth/sso/start?returnTo=${encodeURIComponent(returnTo)}`}><KeyRound size={18} /> {t("Sign in with")}{" "}{pub.data.ssoName ?? "single sign-on"}</a>
      </>}
      <p className="create">{pub.data?.registrationOpen ? <>{t("New here?")}{" "}<Link href="/register">{t("Create an account")}</Link></> : <span style={{ fontSize: 13 }}>{t("Access is by invitation only. Ask your administrator.")}</span>}</p>
    </AuthFrame>
  );
}
export default function LoginPage() { return <Suspense><LoginForm /></Suspense>; }
