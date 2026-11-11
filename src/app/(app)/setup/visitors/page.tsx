"use client";

import { useState } from "react";
import { Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { del, get, post, put } from "@/lib/api/client";
import type { VisitorBlockItem, VisitorSettings } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDate } from "@/lib/format";
import { t } from "@/i18n";

function Rules() {
  const run = useRun();
  const q = useQuery(() => get<VisitorSettings>("/api/visitors/settings"), []);
  const [s, setS] = useState<VisitorSettings | null>(null); const [errs, setErrs] = useState<Record<string, string>>({});
  if (q.data && !s) setS(q.data);
  async function save() {
    if (!s) return;
    const r = await run(() => put<VisitorSettings>("/api/visitors/settings", s), t("Visitor rules saved."), { silent: false });
    setErrs(r.ok ? {} : r.fields ?? {});
  }
  return (
    <Panel title={t("Rules at reception")}>
      {s && (
        <div className="stack">
          <Field label={t("Acknowledgement visitors accept at check-in")} htmlFor="vr-ack" error={errs.acknowledgement}>
            <textarea id="vr-ack" className="textarea" rows={5} maxLength={4000} placeholder={t("e.g. house rules, safety instructions or a short confidentiality statement. Leave empty for none.")} value={s.acknowledgement ?? ""} onChange={(e) => setS({ ...s, acknowledgement: e.target.value || null })} />
          </Field>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={s.askIdLast4} onChange={(e) => setS({ ...s, askIdLast4: e.target.checked })} /> {t("Reception records the last 4 characters of the visitor's ID (never the whole number)")}</label>
          <Field label={t("Keep visitors' details for (days)")} htmlFor="vr-days" error={errs.retentionDays}><TextInput id="vr-days" type="number" min={7} max={3650} value={String(s.retentionDays)} onChange={(e) => setS({ ...s, retentionDays: Number(e.target.value) })} /></Field>
          <p className="field-help" style={{ margin: 0 }}>{t("After this many days, names, companies, contact details, plates and badges are removed from past visits. The visit counts stay.")}</p>
          <div><Button variant="primary" onClick={save}>{t("Save rules")}</Button></div>
        </div>
      )}
    </Panel>
  );
}

function Blocks() {
  const run = useRun();
  const [tick, setTick] = useState(0);
  const q = useQuery(() => get<VisitorBlockItem[]>("/api/visitors/blocks"), [tick]);
  const [name, setName] = useState(""); const [company, setCompany] = useState(""); const [reason, setReason] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({});
  async function add() {
    const r = await run(() => post<VisitorBlockItem[]>("/api/visitors/blocks", { name, company: company || null, reason }), t("Added to the block list."), { silent: false });
    if (!r.ok) { setErrs(r.fields ?? {}); return; }
    setErrs({}); setName(""); setCompany(""); setReason(""); setTick((n) => n + 1);
  }
  async function remove(b: VisitorBlockItem) { const r = await run(() => del(`/api/visitors/blocks/${b.id}`), t("Removed from the block list."), { silent: false }); if (r.ok) setTick((n) => n + 1); }
  return (
    <Panel title={t("Block list")} description={t("People reception must not let in. A visitor with the same name (and company, when given) is flagged and cannot be checked in.")}>
      {(q.data ?? []).length > 0 && (
        <ul className="req-list" data-testid="blocks" style={{ marginBottom: 12 }}>
          {q.data!.map((b) => (
            <li key={b.id}>
              <div><b>{b.name}</b>{b.company ? ` · ${b.company}` : ""}<br /><small className="muted">{b.reason} · {b.by ?? "—"}, {fmtDate(b.createdAt)}</small></div>
              <IconButton label={`${t("Remove")} ${b.name}`} onClick={() => remove(b)}><Trash2 size={14} /></IconButton>
            </li>
          ))}
        </ul>
      )}
      <div className="filters" style={{ alignItems: "flex-end" }}>
        <Field label={t("Name")} htmlFor="vb-name" error={errs.name}><TextInput id="vb-name" maxLength={150} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label={t("Company")} htmlFor="vb-company" error={errs.company}><TextInput id="vb-company" maxLength={150} value={company} onChange={(e) => setCompany(e.target.value)} /></Field>
        <Field label={t("Reason")} htmlFor="vb-reason" error={errs.reason}><TextInput id="vb-reason" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
        <Button variant="primary" onClick={add}>{t("Add to block list")}</Button>
      </div>
    </Panel>
  );
}

export default function VisitorRulesPage() {
  return (
    <Require perm="visitors.manage">
      <PageHeader title={t("Visitor rules")} description={t("What reception asks of visitors, how long their details are kept, and who is not let in.")} />
      <Rules />
      <Blocks />
    </Require>
  );
}
