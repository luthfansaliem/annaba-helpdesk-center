"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Clock } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, PasswordInput } from "@/components/auth/auth";
import { PasswordStrength } from "@/components/ui/password-strength";
import { Button } from "@/components/ui/button";
import { ApiError, messageOf, post } from "@/lib/api/client";
import { t } from "@/i18n";

function Reset() {
  const token = useSearchParams().get("token"); const router = useRouter();
  const [pw, setPw] = useState(""); const [cf, setCf] = useState(""); const [er, setEr] = useState<{ pw?: string; cf?: string }>({}); const [expired, setExpired] = useState(!token); const [alert, setAlert] = useState(""); const [busy, setBusy] = useState(false);
  if (expired) return (
    <AuthFrame signInLink>
      <AuthHeading icon={<Clock size={26} />} tone="warn" title={t("Reset link is invalid or expired")} sub={t("Reset links are single-use and expire after 30 minutes.")} />
      <Button variant="primary" href="/forgot-password" style={{ width: "100%", height: 46 }}>{t("Request a new link")}</Button>
    </AuthFrame>
  );
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setAlert("");
    const n: typeof er = {}; if (!pw) n.pw = "Choose a new password."; if (cf !== pw) n.cf = "Passwords do not match."; setEr(n); if (Object.keys(n).length) return;
    setBusy(true);
    try { await post("/api/auth/reset-password", { token, password: pw }, { noRoute: true }); router.push("/login?reset=1"); }
    catch (x) {
      if (x instanceof ApiError && x.code === "expired") setExpired(true);
      else if (x instanceof ApiError && x.fields?.password) setEr({ pw: x.fields.password }); // the link is NOT consumed by a rejected password
      else setAlert(messageOf(x));
    } finally { setBusy(false); }
  }
  return (
    <AuthFrame signInLink>
      <AuthHeading title={t("Set a new password")} sub={t("Choose a strong password. All your other sessions will be signed out.")} />
      {alert && <div className="form-alert err" role="alert">{alert}</div>}
      <form onSubmit={submit} noValidate>
        <AuthField id="pw" label={t("New password")}><PasswordInput id="pw" autoComplete="new-password" placeholder={t("New password")} value={pw} onChange={(e) => setPw(e.target.value)} aria-invalid={!!er.pw} /></AuthField>
        <PasswordStrength value={pw} />{er.pw && <p className="field-error" role="alert">{er.pw}</p>}
        <AuthField id="cf" label={t("Confirm password")}><PasswordInput id="cf" autoComplete="new-password" placeholder={t("Repeat password")} value={cf} onChange={(e) => setCf(e.target.value)} aria-invalid={!!er.cf} /></AuthField>
        {er.cf && <p className="field-error" role="alert">{er.cf}</p>}
        <button className="signin" type="submit" disabled={busy}>{busy ? t("Updating…") : t("Update password")}</button>
      </form>
    </AuthFrame>
  );
}
export default function ResetPasswordPage() { return <Suspense><Reset /></Suspense>; }
