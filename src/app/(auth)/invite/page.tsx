"use client";

import { Suspense, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { MailX, UserRound } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { AuthField, AuthInput, PasswordInput } from "@/components/auth/auth";
import { PasswordStrength } from "@/components/ui/password-strength";
import { Button } from "@/components/ui/button";
import { SkeletonRows } from "@/components/ui/states";
import { ApiError, get, messageOf, post } from "@/lib/api/client";
import type { InvitationLookup } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** `/invite?token=…` — the token is the emailed one-time secret (never an id). */
function Invite() {
  const token = useSearchParams().get("token") ?? ""; const router = useRouter();
  const q = useQuery(() => (token ? get<InvitationLookup>("/api/invitations/lookup", { token }, { noRoute: true }) : Promise.resolve({ state: "invalid", email: null, roles: null } as InvitationLookup)), [token]);
  const [name, setName] = useState(""); const [pw, setPw] = useState(""); const [er, setEr] = useState<Record<string, string>>({}); const [alert, setAlert] = useState(""); const [busy, setBusy] = useState(false);
  if (q.loading && !q.data) return <AuthFrame><SkeletonRows rows={3} /></AuthFrame>;
  const inv = q.data;
  if (!inv || inv.state !== "valid") {
    const why = { expired: "This invitation has expired.", used: "This invitation was already used.", revoked: "This invitation was revoked." }[inv?.state as string] ?? "This invitation link is not valid.";
    return <AuthFrame><AuthHeading icon={<MailX size={26} />} tone="warn" title={t("Invitation unavailable")} sub={t("{why} Ask your administrator to send a new invitation.", { why: why })} /><Button variant="primary" href="/login" style={{ width: "100%", height: 46 }}>{t("Go to sign in")}</Button></AuthFrame>;
  }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setAlert("");
    const n: Record<string, string> = {}; if (name.trim().length < 2) n.name = "Enter your full name."; if (!pw) n.password = "Choose a password."; setEr(n); if (Object.keys(n).length) return;
    setBusy(true);
    try { await post("/api/invitations/accept", { token, name, password: pw }, { noRoute: true }); router.push("/login?accepted=1"); }
    catch (x) {
      if (x instanceof ApiError && x.fields) setEr(x.fields); else if (x instanceof ApiError && x.code === "expired") q.reload(); else setAlert(messageOf(x));
    } finally { setBusy(false); }
  }
  return (
    <AuthFrame signInLink>
      <AuthHeading title={t("Accept your invitation")} sub={<>{t("You've been invited as")}{" "}<b>{inv.roles?.join(", ")}</b> ({inv.email}).</>} />
      {alert && <div className="form-alert err" role="alert">{alert}</div>}
      <form onSubmit={submit} noValidate>
        <AuthField id="name" label={t("Full name")}><AuthInput id="name" icon={<UserRound size={18} />} placeholder={t("Your name")} value={name} onChange={(e) => setName(e.target.value)} aria-invalid={!!er.name} /></AuthField>
        {er.name && <p className="field-error" role="alert">{er.name}</p>}
        <AuthField id="pw" label={t("Create password")}><PasswordInput id="pw" autoComplete="new-password" placeholder={t("Password")} value={pw} onChange={(e) => setPw(e.target.value)} aria-invalid={!!er.password} /></AuthField>
        <PasswordStrength value={pw} />{er.password && <p className="field-error" role="alert">{er.password}</p>}
        <button className="signin" type="submit" disabled={busy}>{busy ? t("Activating…") : t("Activate account")}</button>
      </form>
    </AuthFrame>
  );
}
export default function InvitePage() { return <Suspense><Invite /></Suspense>; }
