"use client";

import { useState } from "react";
import { Trash2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { del, get, post } from "@/lib/api/client";
import type { LicenceItem, ResourceDocumentItem, ResourceItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDate } from "@/lib/format";
import { t } from "@/i18n";

const DOC_KINDS = ["registration", "insurance", "inspection", "service", "other"] as const;
export const docLabel = (k: string) => ({ registration: t("Registration (STNK)"), insurance: t("Insurance"), inspection: t("Inspection (KIR)"), service: t("Service due"), other: t("Other") } as Record<string, string>)[k] ?? k;
export function ExpiryBadge({ state }: { state: "ok" | "soon" | "expired" }) {
  return state === "expired" ? <Badge tone="red">{t("Expired")}</Badge> : state === "soon" ? <Badge tone="orange">{t("Expires soon")}</Badge> : <Badge tone="green">{t("Valid")}</Badge>;
}

/** BKG-004. Dated documents of a resource (mostly vehicles); people who keep resources are reminded 30 days before and when they expire. */
export function DocumentsDrawer({ resource, onClose }: { resource: ResourceItem | null; onClose: () => void }) {
  const run = useRun();
  const [tick, setTick] = useState(0);
  const q = useQuery(() => (resource ? get<ResourceDocumentItem[]>(`/api/resources/${resource.id}/documents`) : Promise.resolve([] as ResourceDocumentItem[])), [resource?.id, tick]);
  const [kind, setKind] = useState<string>("registration"); const [number, setNumber] = useState(""); const [expires, setExpires] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({});
  async function add() {
    if (!resource) return;
    const r = await run(() => post<ResourceDocumentItem[]>(`/api/resources/${resource.id}/documents`, { kind, number: number || null, expiresOn: expires || null }), t("Document added."), { silent: false });
    if (!r.ok) { setErrs(r.fields ?? {}); return; }
    setErrs({}); setNumber(""); setExpires(""); setTick((n) => n + 1);
  }
  async function remove(d: ResourceDocumentItem) {
    if (!resource) return;
    const r = await run(() => del<ResourceDocumentItem[]>(`/api/resources/${resource.id}/documents/${d.id}`), t("Document removed."), { silent: false });
    if (r.ok) setTick((n) => n + 1);
  }
  return (
    <Overlay open={!!resource} onClose={onClose} side="right" title={t("Documents")} description={resource ? `${resource.name}${resource.code ? ` · ${resource.code}` : ""}` : undefined} footer={<Button onClick={onClose}>{t("Close")}</Button>}>
      <div className="stack">
        {(q.data ?? []).length === 0 ? <p className="field-help" style={{ margin: 0 }}>{t("No documents yet. Add the STNK, insurance and KIR with their expiry dates to be reminded in time.")}</p> : (
          <ul className="req-list" data-testid="resource-documents">
            {q.data!.map((d) => (
              <li key={d.id}>
                <div><b>{docLabel(d.kind)}</b>{d.number ? ` · ${d.number}` : ""}<br /><small className="muted">{t("Expires {date}", { date: fmtDate(d.expiresOn) })}</small></div>
                <span className="row" style={{ gap: 6 }}><ExpiryBadge state={d.state} /><IconButton label={`${t("Remove")} ${docLabel(d.kind)}`} onClick={() => remove(d)}><Trash2 size={14} /></IconButton></span>
              </li>
            ))}
          </ul>
        )}
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Add a document")}</legend>
          <div className="stack">
            <Field label={t("Kind")} htmlFor="rd-kind" error={errs.kind}><SelectInput id="rd-kind" value={kind} onChange={(e) => setKind(e.target.value)} options={DOC_KINDS.map((k) => ({ value: k, label: docLabel(k) }))} /></Field>
            <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
              <Field label={t("Number")} htmlFor="rd-number" error={errs.number}><TextInput id="rd-number" maxLength={60} value={number} onChange={(e) => setNumber(e.target.value)} /></Field>
              <Field label={t("Expires on")} htmlFor="rd-exp" required error={errs.expiresOn}><TextInput id="rd-exp" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} /></Field>
            </div>
            <div><Button variant="primary" onClick={add}>{t("Add document")}</Button></div>
          </div>
        </fieldset>
      </div>
    </Overlay>
  );
}

/** BKG-004. Driving licences (SIM) of the people who drive pool cars: a trip refuses a driver whose licence has expired; they and GA are reminded. */
export function LicencesPanel() {
  const run = useRun();
  const [tick, setTick] = useState(0);
  const q = useQuery(() => get<LicenceItem[]>("/api/drivers/licences"), [tick]);
  const [email, setEmail] = useState(""); const [number, setNumber] = useState(""); const [cls, setCls] = useState(""); const [expires, setExpires] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({});
  async function add() {
    const r = await run(() => post<LicenceItem[]>("/api/drivers/licences", { email, number, class: cls || null, expiresOn: expires || null }), t("Licence saved."), { silent: false });
    if (!r.ok) { setErrs(r.fields ?? {}); return; }
    setErrs({}); setEmail(""); setNumber(""); setCls(""); setExpires(""); setTick((n) => n + 1);
  }
  async function remove(l: LicenceItem) {
    const r = await run(() => del<LicenceItem[]>(`/api/drivers/${l.userId}/licence`), t("Licence removed."), { silent: false });
    if (r.ok) setTick((n) => n + 1);
  }
  return (
    <Panel title={t("Driving licences")} description={t("The people who drive pool cars. A trip cannot be given to someone whose licence has expired.")}>
      {(q.data ?? []).length > 0 && (
        <ul className="req-list" data-testid="licences" style={{ marginBottom: 12 }}>
          {q.data!.map((l) => (
            <li key={l.userId}>
              <div><b>{l.name}</b> <small className="muted">{l.email}</small><br /><small className="muted">{l.number}{l.class ? ` · ${t("class {c}", { c: l.class })}` : ""} · {t("Expires {date}", { date: fmtDate(l.expiresOn) })}</small></div>
              <span className="row" style={{ gap: 6 }}><ExpiryBadge state={l.state} /><IconButton label={`${t("Remove")} ${l.name}`} onClick={() => remove(l)}><Trash2 size={14} /></IconButton></span>
            </li>
          ))}
        </ul>
      )}
      <div className="filters" style={{ alignItems: "flex-end" }}>
        <Field label={t("E-mail of the driver")} htmlFor="dl-email" error={errs.email}><TextInput id="dl-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
        <Field label={t("Licence number")} htmlFor="dl-number" error={errs.number}><TextInput id="dl-number" maxLength={40} value={number} onChange={(e) => setNumber(e.target.value)} /></Field>
        <Field label={t("Class")} htmlFor="dl-class" error={errs.class}><TextInput id="dl-class" maxLength={10} placeholder="A" value={cls} onChange={(e) => setCls(e.target.value)} /></Field>
        <Field label={t("Expires on")} htmlFor="dl-exp" error={errs.expiresOn}><TextInput id="dl-exp" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} /></Field>
        <Button variant="primary" onClick={add}>{t("Save licence")}</Button>
      </div>
    </Panel>
  );
}
