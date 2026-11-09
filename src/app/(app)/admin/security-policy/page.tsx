"use client";

import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { ToggleRow } from "@/components/iam/prefs";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get, patch } from "@/lib/api/client";
import type { TenantPolicy, TenantPolicyItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Val = number | boolean | string;
const META: Record<string, { label: string; unit?: string; hint: string; group: string }> = {
  passwordMinLength: { label: "Minimum password length", unit: "characters", hint: "Longer is stricter.", group: "Passwords" },
  requireBreachCheck: { label: "Block passwords found in known breaches", hint: "On is stricter.", group: "Passwords" },
  lockoutAttempts: { label: "Failed sign-ins before lock", unit: "attempts", hint: "Fewer is stricter.", group: "Sign-in" },
  lockoutMinutes: { label: "Lock duration", unit: "minutes", hint: "Longer is stricter.", group: "Sign-in" },
  mfaPolicy: { label: "Two-step verification", hint: "People without it are asked to set it up when you choose “required”.", group: "Sign-in" },
  requireReauthForCritical: { label: "Ask for the password again before critical changes", hint: "On is stricter.", group: "Sign-in" },
  sessionIdleMinutes: { label: "Sign out after inactivity", unit: "minutes", hint: "Shorter is stricter.", group: "Sessions" },
  sessionAbsoluteHours: { label: "Longest session", unit: "hours", hint: "Shorter is stricter.", group: "Sessions" },
  rememberIdleDays: { label: "“Remember me” inactivity", unit: "days", hint: "Shorter is stricter.", group: "Sessions" },
  rememberAbsoluteDays: { label: "“Remember me” longest", unit: "days", hint: "Shorter is stricter.", group: "Sessions" },
  invitationExpiryDays: { label: "Invitations expire after", unit: "days", hint: "Shorter is stricter.", group: "Invitations" },
};
const GROUPS = ["Passwords", "Sign-in", "Sessions", "Invitations"];
const show = (v: Val) => (typeof v === "boolean" ? (v ? t("On") : t("Off")) : String(v));

function Form({ saved, reload }: { saved: TenantPolicy; reload: () => void }) {
  const run = useRun();
  const [draft, setDraft] = useState<Record<string, Val>>({}); const [err, setErr] = useState(""); const [fe, setFe] = useState<Record<string, string>>({}); const [busy, setBusy] = useState(false);
  useEffect(() => setDraft(Object.fromEntries(saved.items.map((i) => [i.key, (i.own ?? i.effective) as Val]))), [saved]);
  const changed = saved.items.filter((i) => draft[i.key] !== undefined && draft[i.key] !== (i.own ?? i.effective));

  async function save() {
    setErr(""); setFe({}); setBusy(true);
    // a value equal to the platform's means "no choice of our own": send null so the platform value keeps applying
    const values = Object.fromEntries(changed.map((i) => [i.key, draft[i.key] === i.platform ? null : draft[i.key]]));
    const r = await run(() => patch<TenantPolicy>("/api/settings/tenant", { values }), t("Security policy saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setFe(r.fields ?? {}); return; }
    reload();
  }

  function control(i: TenantPolicyItem) {
    const v = draft[i.key]; const id = `sp-${i.key}`;
    if (i.kind === "bool") return <ToggleRow id={id} label={t(META[i.key].label)} hint={t("Platform: {v0}. {v1}", { v0: show(i.platform as Val), v1: t(META[i.key].hint) })} checked={Boolean(v)} onChange={(x) => setDraft({ ...draft, [i.key]: x })} />;
    return (
      <Field label={t(META[i.key].label)} htmlFor={id} error={fe[i.key]}>
        {i.kind === "enum"
          ? <SelectInput id={id} value={String(v ?? "")} options={(i.options ?? []).map((o) => ({ value: o, label: t(o[0].toUpperCase() + o.slice(1)) }))} onChange={(e) => setDraft({ ...draft, [i.key]: e.target.value })} />
          : <TextInput id={id} type="number" min={i.min} max={i.max} value={String(v ?? "")} invalid={!!fe[i.key]} onChange={(e) => setDraft({ ...draft, [i.key]: Number(e.target.value) })} />}
        <p className="field-help">{t("Platform:")}{" "}{show(i.platform as Val)}{META[i.key].unit ? ` ${t(META[i.key].unit!)}` : ""}. {t(META[i.key].hint)}{i.own !== null && i.own !== undefined && i.own !== i.platform ? t(" Your organisation is stricter.") : ""}</p>
      </Field>
    );
  }

  return (
    <div className="stack">
      {err && <div className="form-alert err" role="alert">{err}</div>}
      {GROUPS.map((g) => (
        <Panel key={g} title={t(g)}>
          <div className="stack">{saved.items.filter((i) => META[i.key]?.group === g).map((i) => <div key={i.key}>{control(i)}</div>)}</div>
        </Panel>
      ))}
      <div className="row" style={{ gap: 8 }}>
        <Button onClick={() => setDraft(Object.fromEntries(saved.items.map((i) => [i.key, (i.own ?? i.effective) as Val])))} disabled={changed.length === 0}>{t("Reset")}</Button>
        <Button variant="primary" disabled={changed.length === 0 || busy} onClick={save}>{busy ? t("Saving…") : t("Save policy")}</Button>
        <Button disabled={busy || saved.items.every((i) => i.own === null || i.own === undefined)} onClick={async () => {
          setBusy(true); const r = await run(() => patch<TenantPolicy>("/api/settings/tenant", { values: Object.fromEntries(saved.items.filter((i) => i.own !== null && i.own !== undefined).map((i) => [i.key, null])) }), t("Back to the platform's settings."), { silent: false }); setBusy(false); if (r.ok) reload();
        }}>{t("Use all platform settings")}</Button>
      </div>
    </div>
  );
}

function Page() {
  const q = useQuery(() => get<TenantPolicy>("/api/settings/tenant"), []);
  return (
    <>
      <PageHeader title={t("Security Policy")} description={t("Your organisation can be stricter than the platform on passwords, sign-in and sessions, never looser. Anything you leave as is follows the platform.")} />
      <QueryBoundary q={q} rows={6}>{(p) => <Form saved={p} reload={q.reload} />}</QueryBoundary>
    </>
  );
}

export default function SecurityPolicyPage() { return <Require perm="settings.tenant"><Page /></Require>; }
