"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Lock } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { PasswordInput } from "@/components/auth/auth";
import { OtpInput } from "@/components/ui/otp-input";
import { Avatar, avatarUrl } from "@/components/ui/avatar";
import { ApiError, messageOf, post } from "@/lib/api/client";
import { useAuth } from "@/lib/auth";
import { t } from "@/i18n";

/** The lock itself lives on the server (every other endpoint answers 423 until this succeeds). */
export default function LockedPage() {
  const { status, user, refresh, signOut } = useAuth(); const router = useRouter();
  const [pw, setPw] = useState(""); const [code, setCode] = useState(""); const [rec, setRec] = useState(false); const [recCode, setRecCode] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { if (status === "anon") router.replace("/login"); if (status === "authed") router.replace("/dashboard"); }, [status, router]);
  if (!user) return null;
  async function unlock(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    try {
      await post("/api/auth/unlock", { password: pw, code: user!.mfaEnrolled && !rec ? code : undefined, recoveryCode: user!.mfaEnrolled && rec ? recCode : undefined }, { noRoute: true });
      await refresh(); router.push("/dashboard");
    } catch (x) {
      setPw(""); setCode("");
      if (x instanceof ApiError && x.code === "unauthenticated") router.replace("/login?expired=1");
      else setErr(x instanceof ApiError && x.code === "invalid_credentials" ? "Password is incorrect." : x instanceof ApiError && x.code === "invalid_code" ? "That code is incorrect or already used." : messageOf(x));
    } finally { setBusy(false); }
  }
  return (
    <AuthFrame hero={false}>
      <AuthHeading icon={<Lock size={26} />} tone="warn" title={t("Session locked")} sub={t("Your session is locked on the server. Re-enter your password to continue.")} />
      <div className="center" style={{ marginBottom: 16 }}><Avatar name={user.name} size={56} i={1} src={avatarUrl(user.id, user.avatarVersion)} /><div style={{ fontWeight: 700, marginTop: 8 }}>{user.name}</div><small style={{ color: "var(--muted)" }}>{user.email}</small></div>
      {err && <div className="form-alert err" role="alert">{err}</div>}
      <form onSubmit={unlock}>
        <PasswordInput id="unlock-pw" placeholder={t("Password")} autoComplete="current-password" value={pw} onChange={(e) => setPw(e.target.value)} data-autofocus />
        {user.mfaEnrolled && (rec
          ? <div className="inp"><input aria-label={t("Recovery code")} placeholder={t("XXXX-XXXX-XXXX-XXXX")} value={recCode} onChange={(e) => setRecCode(e.target.value)} /></div>
          : <><p className="field-help center" style={{ margin: "0 0 6px" }}>{t("Authenticator code")}</p><OtpInput value={code} onChange={setCode} /></>)}
        <button className="signin" type="submit" disabled={busy || !pw || (user.mfaEnrolled && (rec ? !recCode : code.length < 6))}>{busy ? t("Unlocking…") : t("Unlock")}</button>
      </form>
      {user.mfaEnrolled && <p className="create"><button type="button" className="link-btn" onClick={() => setRec(!rec)}>{rec ? t("Use authenticator app") : t("Use a recovery code")}</button></p>}
      <p className="create"><button type="button" className="link-btn" onClick={() => void signOut()}>{t("Sign in as a different user")}</button></p>
    </AuthFrame>
  );
}
