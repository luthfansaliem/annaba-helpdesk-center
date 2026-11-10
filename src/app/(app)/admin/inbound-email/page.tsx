"use client";

import { useState } from "react";
import Link from "next/link";
import { Copy, KeyRound } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { InboundMessageItem, MailboxDetail, ServiceListItem } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const OUTCOME: Record<string, [BadgeTone, () => string]> = {
  created: ["green", () => t("New request")], replied: ["blue", () => t("Reply added")], ignored: ["gray", () => t("Ignored")], rejected: ["red", () => t("Refused")],
};

function Settings({ m, onChange, url, onToken }: { m: MailboxDetail; onChange: (d: MailboxDetail) => void; url: string | null; onToken: (url: string) => void }) {
  const run = useRun();
  const [f, setF] = useState({ address: m.address ?? "", serviceId: m.defaultServiceId ?? "", enabled: m.enabled }); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState("");
  const svcs = useQuery(() => get<ServiceListItem[]>("/api/catalog/services").catch(() => [] as ServiceListItem[]), []);
  async function save(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({});
    const r = await run(() => put<MailboxDetail>("/api/settings/inbound-email", { address: f.address, defaultServiceId: f.serviceId || null, enabled: f.enabled, version: m.configured ? m.version : undefined }), t("Mailbox saved."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onChange(r.data);
  }
  async function token() {
    const r = await run(() => post<{ path: string; tokenHint: string }>("/api/settings/inbound-email/token"), t("A new webhook address was made. The old one no longer works."));
    if (r.ok) onToken(window.location.origin + r.data.path);
  }
  const copy = async () => { if (url) try { await navigator.clipboard.writeText(url); } catch { /* the address is shown to copy by hand */ } };
  return (
    <div className="stack">
      <Panel title={t("Webhook address")} description={t("Your mail service forwards every e-mail sent to your helpdesk address, as raw MIME, to this secret address (POST).")}>
        <div className="stack">
          {url ? <div className="form-alert" role="status" data-testid="webhook-url">
            <p style={{ margin: "0 0 6px" }}><b>{t("Copy it now — it is shown only once.")}</b></p>
            <code style={{ wordBreak: "break-all" }}>{url}</code> <Button size="sm" onClick={() => void copy()}><Copy size={14} /> {t("Copy")}</Button>
          </div> : <p style={{ margin: 0 }}>{m.tokenHint ? t("A webhook address exists (ends in …{hint}).", { hint: m.tokenHint }) : t("No webhook address yet.")}</p>}
          <div><Button onClick={() => void token()}><KeyRound size={14} /> {m.tokenHint ? t("Make a new webhook address") : t("Make the webhook address")}</Button></div>
          <p className="sub" style={{ margin: 0 }}>{t("Works with services that can post the raw message to a URL, such as Mailgun routes, Postmark inbound, SendGrid Inbound Parse (raw) or Cloudflare Email Workers. Messages up to 10 MB.")}</p>
        </div>
      </Panel>
      <Panel title={t("Mailbox")}>
        <form className="stack" noValidate onSubmit={save}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <Field label={t("Helpdesk e-mail address")} htmlFor="ib-addr" required error={errs.address}><TextInput id="ib-addr" type="email" maxLength={200} placeholder="help@example.com" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} /></Field>
          <Field label={t("New e-mails become requests for")} htmlFor="ib-svc" required error={errs.defaultServiceId}>
            <SelectInput id="ib-svc" value={f.serviceId} onChange={(e) => setF({ ...f, serviceId: e.target.value })} options={[{ value: "", label: t("Choose…") }, ...(svcs.data ?? []).filter((s) => s.isPublished && !s.archived).map((s) => ({ value: s.id, label: `${s.name} (${s.categoryName})` }))]} />
          </Field>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.enabled} onChange={(e) => setF({ ...f, enabled: e.target.checked })} /> {t("Receive e-mail")}</label>
          <ul className="sub" style={{ margin: 0, paddingLeft: 18 }}>
            <li>{t("Only active people of this organisation are heard, by the e-mail address they sign in with.")}</li>
            <li>{t("A subject with a ticket number (as in the helpdesk's own e-mails) adds the reply to that ticket, if the sender can see it; the quoted earlier mail is left out.")}</li>
            <li>{t("Auto-replies, bounces, mailing lists and repeats of the same message are ignored. Attachments get the usual file checks.")}</li>
          </ul>
          <div><Button variant="primary" type="submit">{t("Save")}</Button></div>
        </form>
      </Panel>
    </div>
  );
}

function Inbound() {
  const q = useQuery(() => get<MailboxDetail>("/api/settings/inbound-email"), []);
  const [url, setUrl] = useState<string | null>(null);   // the new webhook address, shown once (kept here so a reload of the settings keeps it on screen)
  const cols: Column<InboundMessageItem>[] = [
    { key: "w", header: t("Received"), cell: (x) => fmtDateTime(x.receivedAt) },
    { key: "f", header: t("From"), cell: (x) => x.fromAddress },
    { key: "s", header: t("Subject"), className: "hide-sm", cell: (x) => x.subject },
    { key: "o", header: t("Result"), cell: (x) => { const [tone, label] = OUTCOME[x.outcome] ?? ["gray", () => x.outcome]; return <><Badge tone={tone}>{label()}</Badge>{x.ticketId && x.ticketNumber && <> <Link href={`/tickets/${x.ticketId}`}>{x.ticketNumber}</Link></>}{x.reason && <><br /><small className="sub">{x.reason}</small></>}</>; } },
  ];
  return (
    <>
      <PageHeader title={t("E-mail to ticket")} description={t("Let people write to the helpdesk by e-mail: new e-mails become requests and replies are added to their tickets.")} />
      <QueryBoundary q={q}>{(m) => (
        <>
          <Settings key={m.version} m={m} onChange={(d) => q.setData(d)} url={url} onToken={(u) => { setUrl(u); q.reload(); }} />
          <Panel title={t("Latest e-mails")} description={t("The last 50 messages and what became of them.")}>
            <DataTable label={t("Latest e-mails")} columns={cols} rows={m.recent} rowKey={(x) => x.id} emptyMessage={t("No e-mail received yet.")} />
          </Panel>
        </>
      )}</QueryBoundary>
    </>
  );
}

export default function InboundEmailPage() { return <Require perm="settings.manage"><Inbound /></Require>; }
