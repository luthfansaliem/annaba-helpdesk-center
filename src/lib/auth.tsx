"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { ApiError, api, get, messageOf, post, resetCsrf, setApiHandlers } from "./api/client";
import type { LoginResponse, Me } from "./api/types";
import { useToast } from "./toast";
import { t, useLang } from "@/i18n";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { OtpInput } from "@/components/ui/otp-input";

export type AuthStatus = "loading" | "authed" | "anon" | "mfa" | "locked";
type Ctx = {
  status: AuthStatus; user: Me | null; can: (perm: string) => boolean;
  refresh: () => Promise<void>; setUser: (u: Me | null) => void;
  signIn: (email: string, password: string, remember: boolean, returnTo?: string) => Promise<LoginResponse>;
  signOut: () => Promise<void>; lock: () => Promise<void>;
};
const C = createContext<Ctx | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const router = useRouter(); const pathname = usePathname(); const { notify } = useToast(); useLang(); // re-render the re-authentication dialog when the language changes
  const [status, setStatus] = useState<AuthStatus>("loading");
  const [user, setUserState] = useState<Me | null>(null);
  const pathRef = useRef(pathname); pathRef.current = pathname;
  const [reauth, setReauth] = useState<{ resolve: (ok: boolean) => void } | null>(null);

  const setUser = useCallback((u: Me | null) => { setUserState(u); setStatus(u ? (u.locked ? "locked" : "authed") : "anon"); }, []);

  const refresh = useCallback(async () => {
    try { setUser(await api<Me>("/api/auth/me", { noRoute: true })); }
    catch (e) {
      if (e instanceof ApiError && e.code === "mfa_required") { setUserState(null); setStatus("mfa"); }
      else if (e instanceof ApiError && e.code === "locked") { setStatus("locked"); }
      else { setUserState(null); setStatus("anon"); }
    }
  }, [setUser]);

  useEffect(() => { void refresh(); }, [refresh]);

  // Session-level failures from any API call are routed here (see lib/api/client.ts).
  useEffect(() => {
    setApiHandlers({
      unauthenticated: () => { setUserState(null); setStatus("anon"); resetCsrf(); const p = pathRef.current; if (!p.startsWith("/login")) router.replace(`/login?expired=1&returnTo=${encodeURIComponent(p)}`); },
      locked: () => { setStatus("locked"); router.replace("/locked"); },
      mfaRequired: () => { setStatus("mfa"); router.replace("/mfa"); },
      enrollmentRequired: () => { notify("warning", t("Your organisation requires two-step verification. Set it up to continue.")); router.replace("/profile?tab=security"); },
      reauth: () => new Promise<boolean>((resolve) => setReauth({ resolve })),
    });
  }, [router, notify]);

  const signIn = useCallback(async (email: string, password: string, remember: boolean, returnTo?: string) => {
    resetCsrf();
    const r = await post<LoginResponse>("/api/auth/login", { email, password, remember, returnTo }, { noRoute: true });
    if (r.user && (r.status === "ok" || r.status === "mfa_enrollment_required")) setUser(r.user);
    else if (r.status === "mfa_required") setStatus("mfa");
    return r;
  }, [setUser]);

  const signOut = useCallback(async () => {
    try { await post("/api/auth/logout", {}, { noRoute: true, noReauth: true }); } catch { /* already signed out */ }
    resetCsrf(); setUserState(null); setStatus("anon"); router.replace("/login");
  }, [router]);

  const lock = useCallback(async () => {
    await post("/api/auth/lock");
    setStatus("locked"); router.push("/locked");
  }, [router]);

  // Tenant management belongs to the platform organisation only: other tenants' Super Admins hold the permission but must not see the page.
  // "settings.system" is not a server permission: it is settings.manage held by the platform organisation, because those settings apply to every tenant.
  const perms = useMemo(() => {
    const set = new Set((user?.permissions ?? []).filter((p) => p !== "tenants.manage" || user?.isPlatform));
    if (user?.isPlatform && set.has("settings.manage")) set.add("settings.system");
    // an organisation's own policy: for its administrators, and for a platform administrator while supporting it
    if (set.has("settings.manage") && (!user?.isPlatform || user?.actingTenant)) set.add("settings.tenant");
    return set;
  }, [user]);
  const value = useMemo<Ctx>(() => ({ status, user, can: (p) => perms.has(p), refresh, setUser, signIn, signOut, lock }), [status, user, perms, refresh, setUser, signIn, signOut, lock]);

  return (
    <C.Provider value={value}>
      {children}
      <ReauthDialog pending={reauth} mfa={!!user?.mfaEnrolled} onDone={(ok) => { reauth?.resolve(ok); setReauth(null); }} />
    </C.Provider>
  );
}

export function useAuth() {
  const c = useContext(C);
  if (!c) throw new Error("useAuth must be used inside <AuthProvider>");
  return c;
}

/** "Confirm it's you" — shown whenever the server demands a recent re-authentication; the interrupted request is retried automatically. */
function ReauthDialog({ pending, mfa, onDone }: { pending: { resolve: (ok: boolean) => void } | null; mfa: boolean; onDone: (ok: boolean) => void }) {
  const [password, setPassword] = useState(""); const [code, setCode] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { if (pending) { setPassword(""); setCode(""); setErr(""); } }, [pending]);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    try { await post("/api/auth/reauth", { password, code: mfa ? code : undefined }, { noReauth: true, noRoute: true }); onDone(true); }
    catch (x) { setErr(messageOf(x)); setCode(""); }
    finally { setBusy(false); }
  }
  return (
    <Overlay open={!!pending} onClose={() => onDone(false)} title={t("Confirm it's you")} description={t("For your security, please confirm your password to continue.")}
      footer={<><Button onClick={() => onDone(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="reauth-form" disabled={busy || !password || (mfa && code.length < 6)}>{busy ? t("Checking…") : t("Confirm")}</Button></>}>
      <form id="reauth-form" className="stack" onSubmit={submit} noValidate>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <label className="fl" htmlFor="reauth-pw"><span style={{ fontWeight: 700, fontSize: 12 }}>{t("Password")}</span>
          <div className="ctl" style={{ marginTop: 5 }}><input id="reauth-pw" data-autofocus type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} /></div></label>
        {mfa && <><span style={{ fontWeight: 700, fontSize: 12 }}>{t("Authenticator code")}</span><OtpInput value={code} onChange={setCode} label={t("Authenticator code")} /></>}
      </form>
    </Overlay>
  );
}

/** Convenience for pages that need to re-run after a failed call routed the user away. */
export const apiGet = get;
