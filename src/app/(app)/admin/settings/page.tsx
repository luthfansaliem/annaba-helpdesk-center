"use client";

import { useEffect, useState } from "react";
import { KeyRound } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { ToggleRow } from "@/components/iam/prefs";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { del, get, patch, put } from "@/lib/api/client";
import type { SystemSettings } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Editable = Omit<SystemSettings, "emailProvider" | "emailApiKeySet" | "emailApiKeySource">;
const EDITABLE: (keyof Editable)[] = ["registrationMode", "passwordMinLength", "requireBreachCheck", "sessionIdleMinutes", "sessionAbsoluteHours", "rememberIdleDays", "rememberAbsoluteDays", "lockoutAttempts", "lockoutMinutes", "mfaPolicy", "invitationExpiryDays", "auditRetentionDays", "emailFrom", "requireReauthForCritical"];

function Form({ saved, reload }: { saved: SystemSettings; reload: () => void }) {
  const run = useRun();
  const [s, setS] = useState(saved); const [err, setErr] = useState(""); const [confirm, setConfirm] = useState(false); const [busy, setBusy] = useState(false);
  const [newKey, setNewKey] = useState(""); const [editKey, setEditKey] = useState(false); const [keyConfirm, setKeyConfirm] = useState<ConfirmSpec | null>(null);
  useEffect(() => setS(saved), [saved]);
  const changed = EDITABLE.filter((k) => s[k] !== saved[k]);
  const num = (k: keyof Editable) => ({ type: "number" as const, value: s[k] as number, onChange: (e: React.ChangeEvent<HTMLInputElement>) => setS({ ...s, [k]: Number(e.target.value) }) });
  const risky = changed.includes("registrationMode") && s.registrationMode === "open";
  const dirty = changed.length > 0 || !!newKey;

  async function save() {
    setErr(""); setBusy(true);
    const body = Object.fromEntries(changed.map((k) => [k, s[k]]));
    let ok = true;
    if (changed.length) ok = (await run(() => patch("/api/settings", body), undefined, { silent: true }).then((r) => { if (!r.ok) setErr(r.error); return r.ok; }));
    if (ok && newKey) ok = (await run(() => put("/api/settings/email-api-key", { value: newKey }), undefined, { silent: true }).then((r) => { if (!r.ok) setErr(r.error); return r.ok; }));
    setBusy(false); setConfirm(false);
    if (ok) { setNewKey(""); setEditKey(false); run(async () => 0, t("System settings saved.")); reload(); }
  }
  async function clearKey() { if ((await run(() => del("/api/settings/email-api-key"), t("API key removed."))).ok) reload(); }

  return (
    <>
      <PageHeader title={t("System Settings")} description={t("Security policy for the whole workspace. Changes are audited.")}
        actions={<div style={{ display: "flex", gap: 8 }}><Button disabled={!dirty} onClick={() => { setS(saved); setNewKey(""); setEditKey(false); setErr(""); }}>{t("Discard")}</Button><Button variant="primary" disabled={!dirty} onClick={() => setConfirm(true)}>{t("Save changes")}</Button></div>} />
      <div className="section-gap">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Panel title={t("Registration & invitations")}>
          <div className="stack">
            <Field label={t("Public registration")} htmlFor="ss-reg"><SelectInput id="ss-reg" options={[{ value: "invitation-only", label: t("Invitation only (recommended)") }, { value: "open", label: t("Open registration") }]} value={s.registrationMode} onChange={(e) => setS({ ...s, registrationMode: e.target.value as SystemSettings["registrationMode"] })} /></Field>
            <Field label={t("Invitation expiry (days)")} htmlFor="ss-inv"><TextInput id="ss-inv" min={1} max={30} {...num("invitationExpiryDays")} /></Field>
          </div>
        </Panel>
        <Panel title={t("Password & MFA policy")}>
          <div className="fgrid">
            <Field label={t("Minimum password length")} htmlFor="ss-pw"><TextInput id="ss-pw" min={8} max={128} {...num("passwordMinLength")} /></Field>
            <Field label={t("MFA policy")} htmlFor="ss-mfa"><SelectInput id="ss-mfa" options={[{ value: "off", label: t("Off") }, { value: "optional", label: t("Optional") }, { value: "required", label: t("Required for everyone") }]} value={s.mfaPolicy} onChange={(e) => setS({ ...s, mfaPolicy: e.target.value as SystemSettings["mfaPolicy"] })} /></Field>
            <div style={{ gridColumn: "1/-1" }}><ToggleRow id="ss-breach" label={t("Check passwords against known breaches")} hint={t("k-anonymity lookup; the password never leaves the server")} checked={s.requireBreachCheck} onChange={(v) => setS({ ...s, requireBreachCheck: v })} /></div>
            <div style={{ gridColumn: "1/-1" }}><ToggleRow id="ss-reauth" label={t("Require recent re-authentication for critical actions")} hint={t("Role/permission changes, settings and similar need a fresh password confirmation")} checked={s.requireReauthForCritical} onChange={(v) => setS({ ...s, requireReauthForCritical: v })} /></div>
          </div>
        </Panel>
        <Panel title={t("Sessions & lockout")}>
          <div className="fgrid">
            <Field label={t("Idle timeout (minutes)")} htmlFor="ss-idle"><TextInput id="ss-idle" min={5} {...num("sessionIdleMinutes")} /></Field>
            <Field label={t("Absolute lifetime (hours)")} htmlFor="ss-abs"><TextInput id="ss-abs" min={1} {...num("sessionAbsoluteHours")} /></Field>
            <Field label={t("Remember-me idle (days)")} htmlFor="ss-ri"><TextInput id="ss-ri" min={1} {...num("rememberIdleDays")} /></Field>
            <Field label={t("Remember-me lifetime (days)")} htmlFor="ss-ra"><TextInput id="ss-ra" min={1} {...num("rememberAbsoluteDays")} /></Field>
            <Field label={t("Lock after failed attempts")} htmlFor="ss-att"><TextInput id="ss-att" min={3} {...num("lockoutAttempts")} /></Field>
            <Field label={t("Lockout duration (minutes)")} htmlFor="ss-lock"><TextInput id="ss-lock" min={1} {...num("lockoutMinutes")} /></Field>
          </div>
        </Panel>
        <Panel title={t("Audit & email")}>
          <div className="fgrid">
            <Field label={t("Audit retention (days)")} htmlFor="ss-ret"><TextInput id="ss-ret" min={30} {...num("auditRetentionDays")} /></Field>
            <Field label={t("Email provider")} htmlFor="ss-prov"><TextInput id="ss-prov" value={saved.emailProvider} disabled readOnly /></Field>
            <Field label={t("From address")} htmlFor="ss-from"><TextInput id="ss-from" type="email" value={s.emailFrom} onChange={(e) => setS({ ...s, emailFrom: e.target.value })} /></Field>
            <Field label={t("API key (write-only)")} htmlFor="ss-key">
              {saved.emailApiKeySet && !editKey
                ? <div className="ctl"><KeyRound size={14} /><span>{t("•••••••• stored (")}{saved.emailApiKeySource})</span><button type="button" className="link-btn" onClick={() => setEditKey(true)}>{t("Replace")}</button>{saved.emailApiKeySource === "database" && <button type="button" className="link-btn" onClick={() => setKeyConfirm({ title: t("Remove the stored API key?"), confirmLabel: t("Remove key"), danger: true, body: <>{t("The e-mail provider key saved in the database is deleted. E-mail stops sending until a new key is saved, unless one is set in the server environment.")}</>, onConfirm: () => void clearKey() })}>{t("Remove")}</button>}</div>
                : <TextInput id="ss-key" type="password" autoComplete="off" placeholder={t("Paste new key")} value={newKey} onChange={(e) => { setNewKey(e.target.value); setEditKey(true); }} />}
            </Field>
          </div>
          <p className="field-help">{t("Secrets are never displayed after saving and never written to audit logs. The provider itself is chosen by deployment configuration.")}</p>
        </Panel>
      </div>
      <ConfirmOverlay spec={keyConfirm} onClose={() => setKeyConfirm(null)} />
      <Overlay open={confirm} onClose={() => setConfirm(false)} title={t("Save system settings?")} description={t("Policy changes apply to all users.")}
        footer={<><Button onClick={() => setConfirm(false)}>{t("Cancel")}</Button><Button variant="primary" data-autofocus disabled={busy} onClick={save}>{busy ? t("Saving…") : t("Confirm & save")}</Button></>}>
        {risky && <div className="form-alert warn" role="alert">{t("Enabling open registration lets anyone with an email address create an account.")}</div>}
        <ul className="diff-list">{changed.map((k) => <li key={k} className="rem"><b>{k}</b><br />{String(saved[k])} → {String(s[k])}</li>)}{newKey && <li className="rem"><b>{t("emailApiKey")}</b><br />{t("will be replaced (value hidden)")}</li>}</ul>
      </Overlay>
    </>
  );
}

export default function SystemSettingsPage() {
  const q = useQuery(() => get<SystemSettings>("/api/settings"), []);
  return <Require perm="settings.system"><QueryBoundary q={q} rows={6}>{(s) => <Form saved={s} reload={q.reload} />}</QueryBoundary></Require>;
}
