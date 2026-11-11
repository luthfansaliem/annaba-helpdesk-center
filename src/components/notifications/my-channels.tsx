"use client";

import { useState } from "react";
import { MessageCircle, Send, Trash2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { del, get, post, put } from "@/lib/api/client";
import type { MyChannels } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const channelLabel = (k: string) => ({ whatsapp: "WhatsApp", slack: "Slack", teams: "Microsoft Teams" })[k] ?? k;

/** NTF-003. The person's own channels: connect WhatsApp (with a code), Slack (by e-mail) or Teams (a Workflows URL); choose "instead of e-mail"; send a test. */
export function MyChannelsPanel() {
  const run = useRun();
  const q = useQuery(() => get<MyChannels>("/api/me/channels"), []);
  const [phone, setPhone] = useState(""); const [code, setCode] = useState(""); const [url, setUrl] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({});
  const data = q.data;
  if (!data || (data.available.length === 0 && data.mine.length === 0)) return null;
  const mine = (k: string) => data.mine.find((c) => c.kind === k);
  const set = (d: MyChannels) => q.setData(d);
  async function act(fn: () => Promise<MyChannels>, ok: string, field: string) {
    setErrs({});
    const r = await run(fn, ok, { silent: true });
    if (!r.ok) { setErrs(r.fields ?? { [field]: r.error }); return false; }
    set(r.data); return true;
  }
  return (
    <Panel title={t("Other channels")} description={t("Get what you receive by e-mail on WhatsApp, Slack or Teams as well, or instead of e-mail. If a channel fails, the e-mail is sent after all.")}>
      <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} data-testid="my-channels">
        {data.available.map((k) => {
          const c = mine(k);
          return (
            <li key={k} className="stack" style={{ gap: 6, borderTop: "1px solid var(--line)", paddingTop: 10 }}>
              <div className="row" style={{ gap: 8, justifyContent: "space-between", flexWrap: "wrap" }}>
                <span><MessageCircle size={14} aria-hidden /> <b>{channelLabel(k)}</b>{c ? ` · ${c.hint}` : ""} {c?.verified ? <Badge tone={c.enabled ? "green" : "gray"}>{c.enabled ? t("On") : t("Paused")}</Badge> : c?.codePending ? <Badge tone="orange">{t("Waiting for the code")}</Badge> : null}</span>
                {c?.verified && <span className="row" style={{ gap: 6 }}>
                  <Button size="sm" onClick={() => void run(() => post(`/api/me/channels/${k}/test`), t("Test message on its way."))}><Send size={14} /> {t("Send a test")}</Button>
                  <IconButton label={`${t("Disconnect")} ${channelLabel(k)}`} onClick={() => void act(() => del<MyChannels>(`/api/me/channels/${k}`), t("Disconnected."), k)}><Trash2 size={14} /></IconButton>
                </span>}
              </div>
              {c?.lastError && c.verified && <small className="field-error">{t("Last message could not be delivered: {e}", { e: c.lastError })}</small>}
              {c?.verified ? (
                <div className="row" style={{ gap: 16, flexWrap: "wrap" }}>
                  <label className="row" style={{ gap: 6 }}><Checkbox checked={c.enabled} onChange={(e) => void act(() => put<MyChannels>(`/api/me/channels/${k}`, { enabled: e.target.checked }), t("Saved."), k)} /> {t("Send notifications here")}</label>
                  <label className="row" style={{ gap: 6 }}><Checkbox checked={c.replaceEmail} onChange={(e) => void act(() => put<MyChannels>(`/api/me/channels/${k}`, { replaceEmail: e.target.checked }), t("Saved."), k)} /> {t("Instead of e-mail")}</label>
                </div>
              ) : k === "whatsapp" ? (
                <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }}>
                  <Field label={t("Mobile number")} htmlFor="ch-phone" error={errs.address}><TextInput id="ch-phone" type="tel" inputMode="tel" placeholder="0812 3456 7890" value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
                  <Button size="sm" disabled={!phone} onClick={() => void act(() => post<MyChannels>("/api/me/channels/whatsapp", { address: phone }), t("A code is on its way to WhatsApp."), "address")}>{c?.codePending ? t("Send a new code") : t("Send a code")}</Button>
                  {c?.codePending && <>
                    <Field label={t("Code from WhatsApp")} htmlFor="ch-code" error={errs.code}><TextInput id="ch-code" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value)} /></Field>
                    <Button size="sm" variant="primary" disabled={code.length < 6} onClick={() => void act(() => post<MyChannels>("/api/me/channels/whatsapp/verify", { code }), t("WhatsApp connected."), "code").then((ok) => ok && setCode(""))}>{t("Confirm")}</Button>
                  </>}
                </div>
              ) : k === "slack" ? (
                <div className="row" style={{ gap: 8, alignItems: "center", flexWrap: "wrap" }}>
                  <Button size="sm" onClick={() => void act(() => post<MyChannels>("/api/me/channels/slack", {}), t("Slack connected."), "slack")}>{t("Connect with my e-mail")}</Button>
                  {errs.slack && <small className="field-error" role="alert">{errs.slack}</small>}
                </div>
              ) : (
                <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }}>
                  <Field label={t("Teams Workflows URL")} htmlFor="ch-teams" error={errs.address}><TextInput id="ch-teams" type="url" style={{ minWidth: 320 }} placeholder="https://….logic.azure.com/workflows/…" value={url} onChange={(e) => setUrl(e.target.value)} /></Field>
                  <Button size="sm" disabled={!url} onClick={() => void act(() => post<MyChannels>("/api/me/channels/teams", { address: url }), t("Teams connected."), "address")}>{t("Connect")}</Button>
                  <small className="muted" style={{ flexBasis: "100%" }}>{t("In Teams, add the Workflows template “Post to a chat when a webhook request is received” and paste its URL here.")}</small>
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}
