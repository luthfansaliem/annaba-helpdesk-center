"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { QueryBoundary } from "@/components/query-boundary";
import { get, messageOf, patch } from "@/lib/api/client";
import type { Preferences } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useToast } from "@/lib/toast";
import { t } from "@/i18n";

export function ToggleRow({ label, hint, checked, onChange, id }: { label: string; hint?: string; checked: boolean; onChange: (v: boolean) => void; id: string }) {
  return (
    <label className="toggle-row" htmlFor={id}>
      <span><b>{label}</b>{hint && <small>{hint}</small>}</span>
      <input id={id} type="checkbox" role="switch" aria-checked={checked} checked={checked} onChange={(e) => onChange(e.target.checked)} />
    </label>
  );
}

/** Shared by My Profile → Notifications and Settings → Notifications (same stored preferences). */
export function NotificationPrefs() {
  const q = useQuery(() => get<Preferences>("/api/me/preferences"), []);
  return <QueryBoundary q={q} rows={3}>{(saved) => <Form saved={saved} onSaved={q.setData} />}</QueryBoundary>;
}

function Form({ saved, onSaved }: { saved: Preferences; onSaved: (p: Preferences) => void }) {
  const { notify } = useToast();
  const [p, setP] = useState(saved); const [busy, setBusy] = useState(false);
  useEffect(() => setP(saved), [saved]);
  const dirty = p.notifyEmail !== saved.notifyEmail || p.notifySecurity !== saved.notifySecurity || p.notifyProduct !== saved.notifyProduct;
  async function save() {
    setBusy(true);
    try { onSaved(await patch<Preferences>("/api/me/preferences", { notifyEmail: p.notifyEmail, notifySecurity: p.notifySecurity, notifyProduct: p.notifyProduct })); notify("success", t("Notification preferences saved.")); }
    catch (e) { notify("error", messageOf(e)); } finally { setBusy(false); }
  }
  return (
    <div className="stack">
      <ToggleRow id="n-email" label={t("Email notifications")} hint={t("Account and workspace updates")} checked={p.notifyEmail} onChange={(v) => setP({ ...p, notifyEmail: v })} />
      <ToggleRow id="n-sec" label={t("Security alerts")} hint={t("E-mail when someone signs in from a new device, and about password and MFA changes (recommended)")} checked={p.notifySecurity} onChange={(v) => setP({ ...p, notifySecurity: v })} />
      <ToggleRow id="n-prod" label={t("Product news")} hint={t("Occasional feature announcements")} checked={p.notifyProduct} onChange={(v) => setP({ ...p, notifyProduct: v })} />
      <div className="fa"><Button disabled={!dirty} onClick={() => setP(saved)}>{t("Reset")}</Button><Button variant="primary" disabled={!dirty || busy} onClick={save}>{busy ? t("Saving…") : t("Save preferences")}</Button></div>
    </div>
  );
}
