"use client";

import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { NotificationPrefs } from "@/components/iam/prefs";
import { QueryBoundary } from "@/components/query-boundary";
import { get, patch } from "@/lib/api/client";
import type { Preferences } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { useAuth } from "@/lib/auth";
import { setLang, t } from "@/i18n";

const zones = ["Asia/Jakarta", "Asia/Singapore", "Europe/London", "America/New_York", "UTC"];

function Prefs({ saved, onSaved }: { saved: Preferences; onSaved: (p: Preferences) => void }) {
  const run = useRun(); const { refresh } = useAuth();
  const [p, setP] = useState(saved); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => setP(saved), [saved]);
  const dirty = p.language !== saved.language || p.timezone !== saved.timezone || p.autoLockMinutes !== saved.autoLockMinutes;
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => patch<Preferences>("/api/me/preferences", { language: p.language, timezone: p.timezone, autoLockMinutes: p.autoLockMinutes }), t("Settings saved."), { silent: true });
    setBusy(false); if (r.ok) { onSaved(r.data); setLang(r.data.language); void refresh(); } else setErr(r.error);
  }
  return (
    <form className="fgrid" noValidate onSubmit={submit}>
      {err && <div className="form-alert err" style={{ gridColumn: "1/-1" }} role="alert">{err}</div>}
      <Field label={t("Language")} htmlFor="s-lang"><SelectInput id="s-lang" options={[{ value: "en", label: t("English") }, { value: "id", label: t("Bahasa Indonesia") }]} value={p.language} onChange={(e) => setP({ ...p, language: e.target.value as Preferences["language"] })} /></Field>
      <Field label={t("Timezone")} htmlFor="s-tz"><SelectInput id="s-tz" options={zones.includes(p.timezone) ? zones : [p.timezone, ...zones]} value={p.timezone} onChange={(e) => setP({ ...p, timezone: e.target.value })} /></Field>
      <Field label={t("Auto-lock after inactivity (minutes)")} htmlFor="s-lock"><TextInput id="s-lock" type="number" min={1} value={p.autoLockMinutes} onChange={(e) => setP({ ...p, autoLockMinutes: Number(e.target.value) })} /></Field>
      <p className="field-help" style={{ alignSelf: "end" }}>{t("The server rejects values above the organisation's idle limit.")}</p>
      <div style={{ gridColumn: "1/-1", display: "flex", justifyContent: "flex-end", gap: 10 }}><Button type="button" disabled={!dirty} onClick={() => setP(saved)}>{t("Reset")}</Button><Button variant="primary" type="submit" disabled={!dirty || busy}>{busy ? t("Saving…") : t("Save settings")}</Button></div>
    </form>
  );
}

export default function SettingsPage() {
  const q = useQuery(() => get<Preferences>("/api/me/preferences"), []);
  return (
    <>
      <PageHeader title={t("Settings")} description={t("Personal preferences. They only affect your account.")} />
      <div className="section-gap">
        <Panel title={t("Preferences")}><QueryBoundary q={q} rows={3}>{(p) => <Prefs saved={p} onSaved={q.setData} />}</QueryBoundary></Panel>
        <Panel title={t("Notifications")}><NotificationPrefs /></Panel>
      </div>
    </>
  );
}
