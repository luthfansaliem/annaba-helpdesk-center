"use client";

import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import Link from "next/link";
import QRCode from "qrcode";
import { Save } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, avatarUrl } from "@/components/ui/avatar";
import { Tabs } from "@/components/ui/tabs";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { OtpInput } from "@/components/ui/otp-input";
import { PasswordStrength, scorePassword } from "@/components/ui/password-strength";
import { OutcomeBadge } from "@/components/ui/status";
import { NotificationPrefs } from "@/components/iam/prefs";
import { PrivacyTab } from "@/components/iam/privacy";
import { useTheme, type ThemeChoice } from "@/lib/theme";
import { QueryBoundary } from "@/components/query-boundary";
import { api, del, get, patch, post } from "@/lib/api/client";
import type { MfaStatus, Profile, PublicSettings, UserDashboard } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { useToast } from "@/lib/toast";
import { t } from "@/i18n";

const tabs = [["profile", "Profile"], ["security", "Security"], ["notifications", "Notifications"], ["privacy", "Privacy"], ["activity", "Activity"]] as const;
type Tab = (typeof tabs)[number][0];

function Activity() {
  const q = useQuery(() => get<UserDashboard>("/api/dashboard/me"), []);
  return <QueryBoundary q={q} rows={3}>{(d) => d.recentActivity.length ? <ul className="mini-list">{d.recentActivity.map((a, i) => <li key={i}><div><strong>{a.action}</strong><small>{fmtDateTime(a.ts)}</small></div><OutcomeBadge outcome={a.outcome} /></li>)}</ul> : <p className="tab-placeholder">{t("No recent activity.")}</p>}</QueryBoundary>;
}

function ProfileTab({ p, onSaved }: { p: Profile; onSaved: () => void }) {
  const run = useRun(); const { notify } = useToast(); const { user, refresh } = useAuth();
  const [f, setF] = useState({ name: p.name, phone: p.phone ?? "", department: p.department ?? "", jobTitle: p.jobTitle ?? "" }); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => setF({ name: p.name, phone: p.phone ?? "", department: p.department ?? "", jobTitle: p.jobTitle ?? "" }), [p]);
  async function onAvatar(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]; e.target.value = ""; if (!file) return;
    if (!["image/png", "image/jpeg", "image/webp"].includes(file.type)) return notify("error", t("Use a PNG, JPEG or WebP image."));
    if (file.size > 256 * 1024) return notify("error", t("Image must be 256 KB or smaller."));
    if ((await run(() => api("/api/me/avatar", { method: "PUT", rawBody: file, contentType: file.type }), t("Avatar updated."))).ok) { onSaved(); refresh(); }
  }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    if (f.name.trim().length < 2) return setErr(t("Full name is required."));
    setBusy(true);
    const r = await run(() => patch("/api/me/profile", f), t("Profile saved."), { silent: true });
    setBusy(false); if (r.ok) { onSaved(); refresh(); } else setErr(r.error);
  }
  const src = p.hasAvatar && user ? avatarUrl(user.id, p.avatarVersion ?? 0) : undefined;
  return (
    <div>
      <div className="avatar-edit"><Avatar name={p.name} size={72} src={src} i={1} />
        <div><label className="btn sm" htmlFor="avatar-file" style={{ cursor: "pointer" }}>{t("Upload avatar")}</label><input id="avatar-file" type="file" accept="image/png,image/jpeg,image/webp" className="sr-only" onChange={onAvatar} />
          {p.hasAvatar && <button type="button" className="link-btn" style={{ marginLeft: 10 }} onClick={async () => { if ((await run(() => del("/api/me/avatar"), t("Avatar removed."))).ok) { onSaved(); refresh(); } }}>{t("Remove")}</button>}
          <p className="field-help" style={{ margin: "6px 0 0" }}>{t("PNG, JPEG or WebP, up to 256 KB.")}</p></div></div>
      <form className="fgrid" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" style={{ gridColumn: "1/-1" }} role="alert">{err}</div>}
        <Field label={t("Full name")} htmlFor="m-name" required><TextInput id="m-name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
        <Field label={t("Email")} htmlFor="m-email"><TextInput id="m-email" value={p.email} disabled readOnly /></Field>
        <Field label={t("Phone")} htmlFor="m-phone"><TextInput id="m-phone" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} /></Field>
        <Field label={t("Department")} htmlFor="m-dep"><TextInput id="m-dep" value={f.department} onChange={(e) => setF({ ...f, department: e.target.value })} /></Field>
        <Field label={t("Job title")} htmlFor="m-title" full><TextInput id="m-title" value={f.jobTitle} onChange={(e) => setF({ ...f, jobTitle: e.target.value })} /></Field>
        <div style={{ gridColumn: "1/-1", display: "flex", justifyContent: "flex-end" }}><Button variant="primary" type="submit" disabled={busy}><Save size={15} /> {t("Save changes")}</Button></div>
      </form>
      <p className="field-help">{t("Email verification:")}{" "}<Badge tone={p.emailVerified ? "green" : "orange"}>{p.emailVerified ? t("Verified") : t("Pending")}</Badge> {t("· Profile")}{" "}{p.completion.percent}{t("% complete")}</p>
    </div>
  );
}

function Security() {
  const run = useRun(); const { notify } = useToast(); const { refresh } = useAuth();
  const mfa = useQuery(() => get<MfaStatus>("/api/me/mfa"), []);
  const policy = useQuery(() => get<PublicSettings>("/api/settings/public"), []);
  const [pw, setPw] = useState({ next: "", conf: "" }); const [pwErr, setPwErr] = useState<Record<string, string>>({});
  const [email, setEmail] = useState(""); const [emailMsg, setEmailMsg] = useState(""); const [emailErr, setEmailErr] = useState("");
  const [dlg, setDlg] = useState<null | "enroll" | "disable" | "regen">(null); const [code, setCode] = useState(""); const [mpw, setMpw] = useState(""); const [mErr, setMErr] = useState("");
  const [enroll, setEnroll] = useState<{ secret: string; qr: string } | null>(null); const [codes, setCodes] = useState<string[] | null>(null); const [busy, setBusy] = useState(false);
  const min = policy.data?.passwordMinLength ?? 12;

  async function changePw(e: React.FormEvent) {
    e.preventDefault(); const n: Record<string, string> = {};
    if (pw.next.length < min || scorePassword(pw.next).score < 3) n.next = `Use at least ${min} characters with mixed case, a number and a symbol.`;
    if (pw.conf !== pw.next) n.conf = "Passwords do not match.";
    setPwErr(n); if (Object.keys(n).length) return;
    const r = await run(() => post("/api/me/password", { newPassword: pw.next }), t("Password changed. Other sessions were signed out."), { silent: true });
    if (r.ok) setPw({ next: "", conf: "" }); else setPwErr({ next: r.error });
  }
  async function changeEmail(e: React.FormEvent) {
    e.preventDefault(); setEmailErr("");
    if (!/^\S+@\S+\.\S+$/.test(email)) return setEmailErr("Enter a valid email address.");
    const r = await run(() => post("/api/me/email", { newEmail: email }), undefined, { silent: true });
    if (r.ok) { setEmailMsg(email); setEmail(""); } else setEmailErr(r.error);
  }
  async function openEnroll() {
    setMErr(""); setCode("");
    const r = await run(() => post<{ secret: string; otpAuthUri: string }>("/api/me/mfa/enroll"));
    if (!r.ok) return;
    setEnroll({ secret: r.data.secret, qr: await QRCode.toDataURL(r.data.otpAuthUri, { margin: 1, width: 192 }) }); setDlg("enroll");
  }
  const close = () => { setDlg(null); setCode(""); setMpw(""); setMErr(""); setEnroll(null); };
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setMErr(""); setBusy(true);
    if (dlg === "enroll") {
      const r = await run(() => post<{ recoveryCodes: string[] }>("/api/me/mfa/confirm", { code }), t("MFA enabled. Save your recovery codes now."), { silent: true });
      if (r.ok) { setCodes(r.data.recoveryCodes); close(); mfa.reload(); refresh(); } else setMErr(r.error);
    } else if (dlg === "disable") {
      const r = await run(() => post("/api/me/mfa/disable", { password: mpw, code }), t("MFA disabled."), { silent: true });
      if (r.ok) { close(); mfa.reload(); refresh(); } else setMErr(r.error);
    } else if (dlg === "regen") {
      const r = await run(() => post<{ recoveryCodes: string[] }>("/api/me/mfa/recovery-codes"), t("New recovery codes generated. Old codes no longer work."), { silent: true });
      if (r.ok) { setCodes(r.data.recoveryCodes); close(); mfa.reload(); } else setMErr(r.error);
    }
    setBusy(false);
  }
  return (
    <div className="section-gap">
      <section className="sect"><h3>{t("Change password")}</h3>
        <form className="fgrid" noValidate onSubmit={changePw}>
          <Field label={t("New password")} htmlFor="pw-new" required error={pwErr.next}><TextInput id="pw-new" type="password" autoComplete="new-password" value={pw.next} invalid={!!pwErr.next} onChange={(e) => setPw({ ...pw, next: e.target.value })} /></Field>
          <Field label={t("Confirm")} htmlFor="pw-conf" required error={pwErr.conf}><TextInput id="pw-conf" type="password" autoComplete="new-password" value={pw.conf} invalid={!!pwErr.conf} onChange={(e) => setPw({ ...pw, conf: e.target.value })} /></Field>
          <div style={{ gridColumn: "1/-1" }}><PasswordStrength value={pw.next} /></div>
          <p className="field-help" style={{ gridColumn: "1/-1", margin: 0 }}>{t("You'll be asked to confirm your current password.")}</p>
          <div style={{ gridColumn: "1/-1", display: "flex", justifyContent: "flex-end" }}><Button variant="primary" type="submit">{t("Update password")}</Button></div>
        </form></section>
      <section className="sect"><h3>{t("Change email")}</h3>
        {emailMsg && <div className="form-alert info" role="status">{t("We sent a confirmation link to")}{" "}<b>{emailMsg}</b>{t(". Your email changes only after you confirm it; your current address was notified.")}</div>}
        {emailErr && <div className="form-alert err" role="alert">{emailErr}</div>}
        <form className="fgrid" noValidate onSubmit={changeEmail}>
          <Field label={t("New email")} htmlFor="em-new" required full><TextInput id="em-new" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
          <div style={{ gridColumn: "1/-1", display: "flex", justifyContent: "flex-end" }}><Button type="submit">{t("Send confirmation")}</Button></div>
        </form></section>
      <section className="sect"><h3>{t("Two-step verification (TOTP)")}</h3>
        <QueryBoundary q={mfa} rows={1}>{(m) => (
          <>
            <p className="field-help" style={{ margin: "0 0 10px" }}>{t("Status:")}{" "}<Badge tone={m.enrolled ? "green" : "gray"}>{m.enrolled ? t("Enabled") : t("Not enabled")}</Badge>{m.enrolled && <> · {m.recoveryCodesRemaining} {t("recovery code(s) left")}</>}</p>
            <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
              {!m.enrolled ? <Button variant="primary" onClick={openEnroll}>{t("Set up authenticator")}</Button> : <><Button onClick={() => { setDlg("regen"); setMErr(""); }}>{t("Regenerate recovery codes")}</Button>{!m.required && <Button variant="danger-outline" onClick={() => { setDlg("disable"); setMErr(""); }}>{t("Disable MFA")}</Button>}</>}
            </div>
            {m.required && <p className="field-help">{t("Your organisation requires MFA.")}</p>}
          </>
        )}</QueryBoundary>
        {codes && <div style={{ marginTop: 14 }}><div className="form-alert warn" role="alert">{t("Save these recovery codes now — each works once and they won't be shown again.")}</div><div className="codes">{codes.map((c) => <span key={c}>{c}</span>)}</div>
          <div style={{ marginTop: 8, display: "flex", gap: 8 }}><Button size="sm" onClick={() => { navigator.clipboard?.writeText(codes.join("\n")); notify("info", t("Recovery codes copied.")); }}>{t("Copy")}</Button><Button size="sm" onClick={() => setCodes(null)}>{t("I've saved them")}</Button></div></div>}
      </section>
      <p className="field-help">{t("Sessions:")}{" "}<Link href="/sessions" className="link-btn">{t("Manage sessions & devices")}</Link></p>
      <Overlay open={!!dlg} onClose={close} title={dlg === "enroll" ? t("Set up authenticator") : dlg === "disable" ? t("Disable MFA") : t("Regenerate recovery codes")}
        footer={<><Button onClick={close}>{t("Cancel")}</Button><Button variant={dlg === "disable" ? "danger" : "primary"} type="submit" form="mfa-form" disabled={busy}>{dlg === "enroll" ? t("Verify & enable") : dlg === "disable" ? t("Disable MFA") : t("Generate codes")}</Button></>}>
        <form id="mfa-form" className="stack" noValidate onSubmit={submit}>
          {mErr && <div className="form-alert err" role="alert">{mErr}</div>}
          {dlg === "enroll" && enroll && <>
            <p>{t("Scan this QR code with an authenticator app, then enter the 6-digit code.")}</p>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img className="qr" src={enroll.qr} width={192} height={192} alt={t("QR code for your authenticator app")} style={{ margin: "0 auto", display: "block" }} />
            <p className="field-help center">{t("Can't scan? Enter key")}{" "}<code>{enroll.secret}</code></p>
            <OtpInput value={code} onChange={setCode} /></>}
          {dlg === "disable" && <><p>{t("Confirm your password and authenticator code to turn MFA off.")}</p><Field label={t("Password")} htmlFor="mf-pw"><TextInput id="mf-pw" type="password" autoComplete="current-password" value={mpw} onChange={(e) => setMpw(e.target.value)} data-autofocus /></Field><OtpInput value={code} onChange={setCode} /></>}
          {dlg === "regen" && <p>{t("Existing recovery codes stop working immediately. You may be asked to confirm your password.")}</p>}
        </form>
      </Overlay>
    </div>
  );
}

function Appearance() {
  const { choice, set } = useTheme();
  const opts: [ThemeChoice, string][] = [["system", "Same as device"], ["light", "Light"], ["dark", "Dark"]];
  return (
    <div className="stack" style={{ paddingTop: 16 }}>
      <Panel title={t("Appearance")} description={t("Light or dark. “Same as device” follows your operating system. Saved on this device.")}>
        <div className="seg-theme" role="group" aria-label={t("Appearance")}>
          {opts.map(([k, l]) => <button key={k} type="button" aria-pressed={choice === k} onClick={() => set(k)}>{l}</button>)}
        </div>
      </Panel>
    </div>
  );
}

function ProfilePage() {
  const params = useSearchParams();
  const [tab, setTab] = useState<Tab>("profile");
  useEffect(() => { const t = params.get("tab") as Tab | null; if (t && tabs.some((x) => x[0] === t)) setTab(t); }, [params]);
  const q = useQuery(() => get<Profile>("/api/me/profile"), []);
  return (
    <>
      <PageHeader title={t("My Profile")} description={t("Your personal details, security and preferences.")} />
      <Panel>
        <Tabs items={tabs.map(([k, l]) => ({ key: k, label: t(l) }))} value={tab} onChange={setTab} label={t("Profile sections")} />
        {tab === "profile" && <QueryBoundary q={q} rows={4}>{(p) => <ProfileTab p={p} onSaved={q.reload} />}</QueryBoundary>}
        {tab === "security" && <Security />}
        {tab === "notifications" && <><Appearance /><NotificationPrefs /></>}
        {tab === "privacy" && <PrivacyTab />}
        {tab === "activity" && <Activity />}
      </Panel>
    </>
  );
}
export default function Page() { return <Suspense><ProfilePage /></Suspense>; }
