"use client";

import { Suspense, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { ShieldCheck } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { OtpInput } from "@/components/ui/otp-input";
import { ApiError, messageOf, post } from "@/lib/api/client";
import type { LoginResponse } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { safeReturnTo } from "@/lib/format";
import { t } from "@/i18n";

function Mfa() {
  const { status, setUser } = useAuth(); const router = useRouter(); const params = useSearchParams();
  const [code, setCode] = useState(""); const [rec, setRec] = useState(false); const [recCode, setRecCode] = useState(""); const [err, setErr] = useState(""); const busy = useRef(false);
  useEffect(() => { if (status === "anon") router.replace("/login"); }, [status, router]);

  async function verify(body: { code?: string; recoveryCode?: string }) {
    if (busy.current) return; busy.current = true; setErr("");
    try {
      const r = await post<LoginResponse>("/api/auth/mfa/verify", { ...body, returnTo: safeReturnTo(params.get("returnTo")) }, { noRoute: true });
      if (r.user) setUser(r.user);
      router.push(r.next ?? "/dashboard");
    } catch (e) {
      setCode("");
      if (e instanceof ApiError && e.code === "unauthenticated") router.replace("/login?expired=1");
      else setErr(e instanceof ApiError && e.code === "invalid_code" ? (rec ? "That recovery code is invalid or was already used." : "Incorrect code. Check your authenticator app and try again.") : messageOf(e));
    } finally { busy.current = false; }
  }
  return (
    <AuthFrame signInLink>
      <AuthHeading icon={<ShieldCheck size={26} />} title={t("Two-step verification")} sub={rec ? t("Enter one of your single-use recovery codes.") : t("Enter the 6-digit code from your authenticator app.")} />
      {err && <div className="form-alert err" role="alert">{err}</div>}
      {rec ? (
        <form onSubmit={(e) => { e.preventDefault(); void verify({ recoveryCode: recCode }); }}>
          <div className="inp"><input aria-label={t("Recovery code")} placeholder={t("XXXX-XXXX-XXXX-XXXX")} autoComplete="off" value={recCode} onChange={(e) => setRecCode(e.target.value)} data-autofocus /></div>
          <button className="signin" type="submit">{t("Verify")}</button>
        </form>
      ) : (
        <OtpInput value={code} onChange={(v) => { setCode(v); setErr(""); if (v.length === 6) void verify({ code: v }); }} invalid={!!err} />
      )}
      <p className="create"><button type="button" className="link-btn" onClick={() => { setRec(!rec); setErr(""); }}>{rec ? t("Use authenticator app") : t("Use a recovery code")}</button></p>
    </AuthFrame>
  );
}
export default function MfaPage() { return <Suspense><Mfa /></Suspense>; }
