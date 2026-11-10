"use client";

import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { EmailWording, EmailWordingPreview } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Draft = { senderName: string; subjectPrefix: string; footerEn: string; footerId: string };
const toDraft = (w: EmailWording): Draft => ({ senderName: w.senderName ?? "", subjectPrefix: w.subjectPrefix ?? "", footerEn: w.footerEn ?? "", footerId: w.footerId ?? "" });
const LANG: Record<string, () => string> = { en: () => t("English"), id: () => t("Bahasa Indonesia") };

function Form({ saved, reload }: { saved: EmailWording; reload: () => void }) {
  const run = useRun();
  const [d, setD] = useState<Draft>(toDraft(saved)); const [err, setErr] = useState(""); const [fe, setFe] = useState<Record<string, string>>({}); const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<EmailWordingPreview | null>(null);
  useEffect(() => { setD(toDraft(saved)); setPreview(null); }, [saved]);
  const was = toDraft(saved); const dirty = (Object.keys(was) as (keyof Draft)[]).some((k) => was[k].trim() !== d[k].trim());
  const set = (k: keyof Draft, v: string) => { setD((x) => ({ ...x, [k]: v })); setPreview(null); };
  const custom = Object.values(was).some((v) => v !== "");

  async function call<T>(fn: () => Promise<T>, ok?: string) {
    setErr(""); setFe({}); setBusy(true);
    const r = await run(fn, ok, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setFe(r.fields ?? {}); return null; }
    return r.data;
  }
  const save = async () => { if (await call(() => put<EmailWording>("/api/settings/email-wording", d), t("E-mail wording saved."))) reload(); };
  const show = async () => { const p = await call(() => post<EmailWordingPreview>("/api/settings/email-wording/preview", d)); if (p) setPreview(p); };
  const reset = async () => { if (await call(() => put<EmailWording>("/api/settings/email-wording", { senderName: null, subjectPrefix: null, footerEn: null, footerId: null }), t("Back to the standard wording."))) reload(); };

  return (
    <div className="stack">
      {err && <div className="form-alert err" role="alert">{err}</div>}
      <Panel title={t("Your organisation's wording")} description={t("Added to every e-mail your organisation sends: requests, approvals, invitations and account messages. The rest of each message stays as it is. Leave a box empty to use the standard.")}>
        <div className="stack">
          <Field label={t("Name in the sign-off")} htmlFor="ew-sender" error={fe.senderName}>
            <TextInput id="ew-sender" maxLength={60} placeholder="Annaba Digital" value={d.senderName} invalid={!!fe.senderName} onChange={(e) => set("senderName", e.target.value)} />
            <p className="field-help">{t("Shown at the end of the message, for example “Acme General Affairs”. Up to 60 characters.")}</p>
          </Field>
          <Field label={t("Start of every subject")} htmlFor="ew-prefix" error={fe.subjectPrefix}>
            <TextInput id="ew-prefix" maxLength={30} placeholder={t("e.g. [Acme GA]")} value={d.subjectPrefix} invalid={!!fe.subjectPrefix} onChange={(e) => set("subjectPrefix", e.target.value)} />
            <p className="field-help">{t("Helps people recognise and filter these messages. Up to 30 characters.")}</p>
          </Field>
          <Field label={t("Closing text (English)")} htmlFor="ew-en" error={fe.footerEn}>
            <textarea id="ew-en" rows={3} maxLength={400} className="textarea" value={d.footerEn} onChange={(e) => set("footerEn", e.target.value)} />
            <p className="field-help">{t("For example who to call when it is urgent. Plain text, up to 400 characters on 6 lines.")}</p>
          </Field>
          <Field label={t("Closing text (Bahasa Indonesia)")} htmlFor="ew-id" error={fe.footerId}>
            <textarea id="ew-id" rows={3} maxLength={400} className="textarea" value={d.footerId} onChange={(e) => set("footerId", e.target.value)} />
            <p className="field-help">{t("Each person gets the text in their own language. If you write only one, it is used for both.")}</p>
          </Field>
          {saved.updatedAt && <p className="field-help" style={{ margin: 0 }}>{t("Last changed {when}.", { when: fmtDateTime(saved.updatedAt) })}</p>}
        </div>
      </Panel>
      <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
        <Button onClick={show} disabled={busy}>{t("Preview a message")}</Button>
        <Button variant="primary" onClick={save} disabled={!dirty || busy}>{busy ? t("Saving…") : t("Save wording")}</Button>
        <Button onClick={reset} disabled={busy || !custom}>{t("Use the standard wording")}</Button>
      </div>
      {preview && <Panel title={t("How a message would read")} description={t("An example (a resolved request) with the text above, whether or not it is saved. Nothing is sent.")}>
        <div className="stack" data-testid="wording-preview">
          {preview.versions.map((v) => (
            <section key={v.lang} aria-label={LANG[v.lang]?.() ?? v.lang}>
              <h3 style={{ margin: "0 0 4px" }}>{LANG[v.lang]?.() ?? v.lang}</h3>
              <p style={{ margin: "0 0 4px" }}><b>{t("Subject")}:</b> {v.subject}</p>
              <pre tabIndex={0} aria-label={`${t("Message")} — ${LANG[v.lang]?.() ?? v.lang}`} style={{ whiteSpace: "pre-wrap", margin: 0, font: "inherit", padding: 12, border: "1px solid var(--border, #d1d5db)", borderRadius: 8 }}>{v.text}</pre>
            </section>
          ))}
        </div>
      </Panel>}
    </div>
  );
}

function Page() {
  const q = useQuery(() => get<EmailWording>("/api/settings/email-wording"), []);
  return (
    <>
      <PageHeader title={t("Email Wording")} description={t("Make the e-mails your people receive sound like your organisation.")} />
      <QueryBoundary q={q} rows={5}>{(w) => <Form saved={w} reload={q.reload} />}</QueryBoundary>
    </>
  );
}

export default function EmailWordingPage() { return <Require perm="settings.manage"><Page /></Require>; }
