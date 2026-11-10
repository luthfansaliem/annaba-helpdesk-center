"use client";

import { useState } from "react";
import { KeyRound, Plus, Trash2, Webhook } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { del, get, post, put } from "@/lib/api/client";
import type { ApiKeyItem, DeliveryItem, NewApiKey, NewWebhook, WebhookItem } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const SCOPES: [string, () => string][] = [["tickets.read", () => t("Read tickets")], ["tickets.write", () => t("Create tickets and comment")], ["catalog.read", () => t("Read the service catalog")]];
const EVENTS: [string, () => string][] = [["ticket.created", () => t("A ticket is created")], ["ticket.status_changed", () => t("A ticket changes status")], ["ticket.assigned", () => t("A ticket is assigned")], ["ticket.commented", () => t("A public comment is added")], ["workorder.completed", () => t("A work order is completed")]];

function Secret({ label, value }: { label: string; value: string }) {
  return (
    <div className="form-alert" role="status" data-testid="one-time-secret">
      <p style={{ margin: "0 0 6px" }}><b>{label}</b> {t("Copy it now — it is shown only once.")}</p>
      <code style={{ wordBreak: "break-all" }}>{value}</code>
    </div>
  );
}

function Picks({ legend, options, value, onChange, error }: { legend: string; options: [string, () => string][]; value: string[]; onChange: (v: string[]) => void; error?: string }) {
  return (
    <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
      <legend style={{ fontWeight: 700, padding: 0, marginBottom: 6 }}>{legend}</legend>
      {error && <p className="field-error" role="alert">{error}</p>}
      <div className="stack" style={{ gap: 4 }}>{options.map(([k, l]) => (
        <label key={k} className="row" style={{ gap: 8 }}><Checkbox checked={value.includes(k)} onChange={(e) => onChange(e.target.checked ? [...value, k] : value.filter((x) => x !== k))} /> {l()} <code style={{ fontSize: 12 }}>{k}</code></label>
      ))}</div>
    </fieldset>
  );
}

function Keys() {
  const run = useRun();
  const q = useQuery(() => get<ApiKeyItem[]>("/api/integrations/keys"), []);
  const [open, setOpen] = useState(false); const [name, setName] = useState(""); const [scopes, setScopes] = useState<string[]>(["tickets.read"]); const [expires, setExpires] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [token, setToken] = useState<string | null>(null); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  async function create(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({});
    const r = await run(() => post<NewApiKey>("/api/integrations/keys", { name, scopes, expiresOn: expires || null }), t("API key created."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    setToken(r.data.token); setOpen(false); setName(""); q.reload();
  }
  const cols: Column<ApiKeyItem>[] = [
    { key: "n", header: t("Name"), cell: (k) => <><b>{k.name}</b> {k.active ? <Badge tone="green">{t("Active")}</Badge> : <Badge tone="gray">{t("Stopped")}</Badge>}<br /><small className="sub"><code>adk_{k.prefix}…</code> · {k.owner ?? "—"}</small></> },
    { key: "s", header: t("Scopes"), cell: (k) => k.scopes.join(", ") },
    { key: "u", header: t("Last used"), className: "hide-sm", cell: (k) => (k.lastUsedAt ? fmtDateTime(k.lastUsedAt) : t("Never")) },
    { key: "e", header: t("Expires"), className: "hide-sm", cell: (k) => (k.expiresAt ? fmtDateTime(k.expiresAt) : t("Never")) },
    { key: "x", header: <span className="sr-only">{t("Actions")}</span>, cell: (k) => k.active && <Button size="sm" variant="danger-outline" onClick={() => setConfirm({ title: t("Revoke {what}?", { what: k.name }), confirmLabel: t("Revoke"), danger: true, body: t("Systems using this key stop working at once."),
      onConfirm: async () => { const r = await run(() => post(`/api/integrations/keys/${k.id}/revoke`), t("Key revoked.")); if (r.ok) q.reload(); } })}>{t("Revoke")}</Button> },
  ];
  return (
    <Panel title={t("API keys")} description={t("For other systems to use the public API (/api/v1). A key acts as the person who made it, only within its scopes, and stops when that person leaves.")}
      actions={<Button variant="primary" size="sm" onClick={() => { setOpen(true); setToken(null); }}><Plus size={14} /> {t("New API key")}</Button>}>
      <div className="stack">
        {token && <Secret label={t("The new key:")} value={token} />}
        <DataTable label={t("API keys")} columns={cols} rows={q.data ?? []} rowKey={(k) => k.id} emptyMessage={t("No API keys yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </div>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New API key")} description={t("You may be asked for your password first.")}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="key-form"><KeyRound size={14} /> {t("Create key")}</Button></>}>
        <form id="key-form" className="stack" noValidate onSubmit={create}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <Field label={t("Name")} htmlFor="ak-name" required error={errs.name}><TextInput id="ak-name" data-autofocus maxLength={80} placeholder={t("e.g. HR system")} value={name} onChange={(e) => setName(e.target.value)} /></Field>
          <Picks legend={t("Scopes")} options={SCOPES} value={scopes} onChange={setScopes} error={errs.scopes} />
          <Field label={t("Expires on (optional)")} htmlFor="ak-exp" error={errs.expiresOn}><TextInput id="ak-exp" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} /></Field>
        </form>
      </Overlay>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Panel>
  );
}

function Deliveries({ hook, onClose }: { hook: WebhookItem | null; onClose: () => void }) {
  const q = useQuery(() => (hook ? get<DeliveryItem[]>(`/api/integrations/webhooks/${hook.id}/deliveries`) : Promise.resolve([] as DeliveryItem[])), [hook?.id]);
  const cols: Column<DeliveryItem>[] = [
    { key: "w", header: t("When"), cell: (d) => fmtDateTime(d.createdAt) },
    { key: "e", header: t("Event"), cell: (d) => <code>{d.event}</code> },
    { key: "s", header: t("Result"), cell: (d) => d.deliveredAt ? <Badge tone="green">{t("Delivered")}</Badge> : d.failedAt ? <Badge tone="red">{t("Gave up")}</Badge> : <Badge tone="orange">{t("Retrying")}</Badge> },
    { key: "a", header: t("Attempts"), cell: (d) => <>{d.attempts}{d.lastError && <><br /><small className="sub">{d.lastError}</small></>}</> },
  ];
  return (
    <Overlay open={hook !== null} onClose={onClose} side="right" title={t("Deliveries")} description={hook?.url}>
      <DataTable label={t("Deliveries")} columns={cols} rows={q.data ?? []} rowKey={(d) => d.id} emptyMessage={t("Nothing sent yet.")} />
    </Overlay>
  );
}

function Webhooks() {
  const run = useRun();
  const q = useQuery(() => get<WebhookItem[]>("/api/integrations/webhooks"), []);
  const [open, setOpen] = useState(false); const [url, setUrl] = useState(""); const [desc, setDesc] = useState(""); const [events, setEvents] = useState<string[]>(["ticket.created"]);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [secret, setSecret] = useState<string | null>(null);
  const [log, setLog] = useState<WebhookItem | null>(null); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  async function create(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({});
    const r = await run(() => post<NewWebhook>("/api/integrations/webhooks", { url, description: desc || null, events, enabled: true }), t("Webhook created."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    setSecret(r.data.secret); setOpen(false); setUrl(""); setDesc(""); q.reload();
  }
  const toggle = async (h: WebhookItem) => { const r = await run(() => put(`/api/integrations/webhooks/${h.id}`, { url: h.url, description: h.description, events: h.events, enabled: !h.enabled, version: h.version }), h.enabled ? t("Webhook switched off.") : t("Webhook switched on.")); if (r.ok) q.reload(); };
  const cols: Column<WebhookItem>[] = [
    { key: "u", header: t("Address"), cell: (h) => <><b style={{ wordBreak: "break-all" }}>{h.url}</b> {h.enabled ? <Badge tone="green">{t("On")}</Badge> : <Badge tone="gray">{t("Off")}</Badge>}{h.description && <><br /><small className="sub">{h.description}</small></>}</> },
    { key: "e", header: t("Events"), className: "hide-sm", cell: (h) => h.events.join(", ") },
    { key: "d", header: t("Deliveries"), cell: (h) => <Button size="sm" onClick={() => setLog(h)}>{t("{p} waiting · {f} failed", { p: h.pending, f: h.failed })}</Button> },
    { key: "x", header: <span className="sr-only">{t("Actions")}</span>, cell: (h) => <span className="row" style={{ gap: 6 }}>
      <Button size="sm" onClick={() => void toggle(h)}>{h.enabled ? t("Switch off") : t("Switch on")}</Button>
      <Button size="sm" variant="danger-outline" aria-label={`${t("Remove")} ${h.url}`} onClick={() => setConfirm({ title: t("Remove this webhook?"), confirmLabel: t("Remove"), danger: true, body: h.url,
        onConfirm: async () => { const r = await run(() => del(`/api/integrations/webhooks/${h.id}`), t("Removed.")); if (r.ok) q.reload(); } })}><Trash2 size={14} /></Button></span> },
  ];
  return (
    <Panel title={t("Webhooks")} description={t("Events are sent as signed JSON (X-Annaba-Signature: HMAC-SHA256 of timestamp.body) and retried for about a day. Only https addresses on the public internet.")}
      actions={<Button variant="primary" size="sm" onClick={() => { setOpen(true); setSecret(null); }}><Webhook size={14} /> {t("New webhook")}</Button>}>
      <div className="stack">
        {secret && <Secret label={t("The signing secret:")} value={secret} />}
        <DataTable label={t("Webhooks")} columns={cols} rows={q.data ?? []} rowKey={(h) => h.id} emptyMessage={t("No webhooks yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </div>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New webhook")} description={t("You may be asked for your password first.")}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="hook-form">{t("Create webhook")}</Button></>}>
        <form id="hook-form" className="stack" noValidate onSubmit={create}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <Field label={t("Address")} htmlFor="wh-url" required error={errs.url}><TextInput id="wh-url" data-autofocus maxLength={500} placeholder="https://example.com/hooks/helpdesk" value={url} onChange={(e) => setUrl(e.target.value)} /></Field>
          <Field label={t("Description")} htmlFor="wh-desc" error={errs.description}><TextInput id="wh-desc" maxLength={200} value={desc} onChange={(e) => setDesc(e.target.value)} /></Field>
          <Picks legend={t("Events")} options={EVENTS} value={events} onChange={setEvents} error={errs.events} />
        </form>
      </Overlay>
      <Deliveries hook={log} onClose={() => setLog(null)} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Panel>
  );
}

export default function IntegrationsPage() {
  return (
    <Require perm="integrations.manage">
      <PageHeader title={t("Integrations")} description={t("Connect other systems: API keys for the public API, and webhooks that tell them when something happens.")} />
      <Keys />
      <Webhooks />
    </Require>
  );
}
