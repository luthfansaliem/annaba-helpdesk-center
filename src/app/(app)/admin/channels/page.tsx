"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { channelLabel } from "@/components/notifications/my-channels";
import { get, put } from "@/lib/api/client";
import type { ChannelDelivery, ChannelSetting } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime } from "@/lib/format";
import { t } from "@/i18n";

const FIELDS: Record<string, { key: string; label: () => string; secret?: boolean; placeholder?: string }[]> = {
  whatsapp: [
    { key: "phoneNumberId", label: () => t("Phone number ID (Meta)"), placeholder: "1234567890" },
    { key: "accessToken", label: () => t("Access token"), secret: true },
    { key: "notifyTemplate", label: () => t("Notification template (3 variables: title, text, link)"), placeholder: "helpdesk_update" },
    { key: "verifyTemplate", label: () => t("Code template (1 variable: the code)"), placeholder: "helpdesk_code" },
    { key: "language", label: () => t("Template language (empty: the person's)"), placeholder: "id" },
  ],
  slack: [{ key: "botToken", label: () => t("Bot token (scopes chat:write, users:read.email)"), secret: true, placeholder: "xoxb-…" }],
  teams: [],
};

function ChannelCard({ s, onSaved }: { s: ChannelSetting; onSaved: (x: ChannelSetting) => void }) {
  const run = useRun();
  const [values, setValues] = useState<Record<string, string>>({}); const [enabled, setEnabled] = useState(s.enabled); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState("");
  const fields = FIELDS[s.kind] ?? [];
  async function save() {
    setErrs({}); setErr("");
    const settings: Record<string, string> = {}; const secrets: Record<string, string> = {};
    for (const f of fields) { const v = values[f.key]; if (v === undefined) continue; (f.secret ? secrets : settings)[f.key] = v; }
    const r = await run(() => put<ChannelSetting>(`/api/channels/settings/${s.kind}`, { enabled, settings, secrets }), t("Saved."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    setValues({}); onSaved(r.data);
  }
  return (
    <Panel data-testid={`channel-${s.kind}`} title={channelLabel(s.kind)} actions={<Badge tone={s.enabled ? "green" : "gray"}>{s.enabled ? t("On") : t("Off")}</Badge>}
      description={t("{n} people connected · {f} failed in the last 7 days", { n: s.people, f: s.failed7 })}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {s.kind === "teams" && <p className="muted" style={{ margin: 0 }}>{t("Nothing to set up: each person connects their own Teams chat with a Workflows flow.")}</p>}
        {fields.map((f) => (
          <Field key={f.key} label={f.label()} htmlFor={`cs-${s.kind}-${f.key}`} error={errs[`${f.secret ? "secrets" : "settings"}.${f.key}`]}>
            <TextInput id={`cs-${s.kind}-${f.key}`} type={f.secret ? "password" : "text"} autoComplete="off"
              placeholder={f.secret && s.secretsSet.includes(f.key) ? t("Stored — type to replace") : f.placeholder}
              value={values[f.key] ?? (f.secret ? "" : s.settings[f.key] ?? "")} onChange={(e) => setValues((v) => ({ ...v, [f.key]: e.target.value }))} />
          </Field>
        ))}
        <label className="row" style={{ gap: 8 }}><Checkbox checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> {t("Offer this channel to everyone")}</label>
        <span><Button variant="primary" size="sm" onClick={() => void save()}>{t("Save")}</Button></span>
        {s.updatedAt && <small className="muted">{t("Last changed {at}", { at: fmtDateTime(s.updatedAt) })}</small>}
      </div>
    </Panel>
  );
}

/** NTF-003. The organisation's WhatsApp, Slack and Teams set-up; credentials are encrypted and never shown again. */
export default function ChannelsPage() {
  const q = useQuery(() => get<ChannelSetting[]>("/api/channels/settings"), []);
  const log = useQuery(() => get<ChannelDelivery[]>("/api/channels/deliveries"), []);
  const rows = q.data ?? [];
  return (
    <>
      <PageHeader title={t("Notification channels")} description={t("Offer WhatsApp, Slack and Microsoft Teams next to e-mail. People choose them on their own notification settings.")} />
      {rows.map((s) => <ChannelCard key={`${s.kind}-${s.updatedAt ?? ""}`} s={s} onSaved={(x) => q.setData(rows.map((r) => (r.kind === x.kind ? x : r)))} />)}
      <Panel title={t("Recent deliveries")} description={t("What was sent where and whether it arrived. Message text is not kept.")}>
        {(log.data ?? []).length === 0 ? <p className="tab-placeholder">{t("Nothing sent yet.")}</p> : (
          <div className="tbl-wrap"><table className="tbl" aria-label={t("Recent deliveries")}>
            <thead><tr><th scope="col">{t("When")}</th><th scope="col">{t("Channel")}</th><th scope="col">{t("Person")}</th><th scope="col">{t("What")}</th><th scope="col">{t("Result")}</th></tr></thead>
            <tbody>{(log.data ?? []).map((d) => (
              <tr key={d.id}><td>{fmtDateTime(d.createdAt)}</td><td>{channelLabel(d.channel)}</td><td>{d.person ?? "—"}</td><td>{d.notificationKind ?? d.purpose}</td>
                <td>{d.deliveredAt ? <Badge tone="green">{t("Delivered")}</Badge> : d.failedAt ? <Badge tone="red">{d.fellBack ? t("Failed, sent by e-mail") : t("Failed")}</Badge> : <Badge tone="orange">{t("Trying ({n})", { n: d.attempts })}</Badge>}
                  {d.lastError && <><br /><small className="muted">{d.lastError}</small></>}</td></tr>
            ))}</tbody>
          </table></div>
        )}
      </Panel>
    </>
  );
}
