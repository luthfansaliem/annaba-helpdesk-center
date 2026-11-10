"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { Button, IconButton } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { get, post, put } from "@/lib/api/client";
import type { PortalLocation, VendorContract, VendorDetail } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Contact = { name: string; role: string; email: string; phone: string };
const blank = { name: "", services: "", email: "", phone: "", website: "", address: "", taxId: "", notes: "" };

/** Add or edit a vendor: details, contacts and the places it covers. */
export function VendorDrawer({ open, onClose, onDone, editing }: { open: boolean; onClose: () => void; onDone: (v: VendorDetail) => void; editing: VendorDetail | null }) {
  const run = useRun();
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const [f, setF] = useState(blank); const [contacts, setContacts] = useState<Contact[]>([]); const [sites, setSites] = useState<string[]>([]); const [placeQ, setPlaceQ] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seed, setSeed] = useState<string | null>(null);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr(""); setPlaceQ("");
    setF(editing ? { name: editing.name, services: editing.services ?? "", email: editing.email ?? "", phone: editing.phone ?? "", website: editing.website ?? "", address: editing.address ?? "", taxId: editing.taxId ?? "", notes: editing.notes ?? "" } : blank);
    setContacts(editing?.contacts.map((c) => ({ name: c.name, role: c.role ?? "", email: c.email ?? "", phone: c.phone ?? "" })) ?? []); setSites(editing?.sites.map((s) => s.locationId) ?? []);
  }
  const set = (k: keyof typeof blank, v: string) => setF((x) => ({ ...x, [k]: v }));
  const setC = (i: number, k: keyof Contact, v: string) => setContacts((cs) => cs.map((c, j) => (j === i ? { ...c, [k]: v } : c)));
  const shown = (places.data ?? []).filter((l) => !placeQ || l.path.toLowerCase().includes(placeQ.toLowerCase())).slice(0, 50);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { ...Object.fromEntries(Object.entries(f).map(([k, v]) => [k, v || null])), name: f.name, contacts: contacts.map((c) => ({ name: c.name, role: c.role || null, email: c.email || null, phone: c.phone || null })), locationIds: sites, version: editing?.version };
    const r = await run(() => (editing ? put<VendorDetail>(`/api/vendors/${editing.id}`, body) : post<VendorDetail>("/api/vendors", body)), editing ? t("Vendor saved.") : t("Vendor added."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit vendor") : t("New vendor")} description={t("A company you buy services from.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="vendor-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="vendor-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="vd-name" required error={errs.name}><TextInput id="vd-name" data-autofocus maxLength={150} value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label={t("What they do")} htmlFor="vd-services" error={errs.services}><TextInput id="vd-services" maxLength={300} placeholder={t("e.g. AC and refrigeration")} value={f.services} onChange={(e) => set("services", e.target.value)} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Phone")} htmlFor="vd-phone" error={errs.phone}><TextInput id="vd-phone" maxLength={40} value={f.phone} onChange={(e) => set("phone", e.target.value)} /></Field>
          <Field label={t("E-mail")} htmlFor="vd-email" error={errs.email}><TextInput id="vd-email" type="email" maxLength={200} value={f.email} onChange={(e) => set("email", e.target.value)} /></Field>
        </div>
        <Field label={t("Website")} htmlFor="vd-web" error={errs.website}><TextInput id="vd-web" maxLength={200} placeholder="https://" value={f.website} onChange={(e) => set("website", e.target.value)} /></Field>
        <Field label={t("Address")} htmlFor="vd-addr" error={errs.address}><TextInput id="vd-addr" maxLength={300} value={f.address} onChange={(e) => set("address", e.target.value)} /></Field>
        <Field label={t("Tax number")} htmlFor="vd-tax" error={errs.taxId}><TextInput id="vd-tax" maxLength={40} value={f.taxId} onChange={(e) => set("taxId", e.target.value)} /></Field>
        <Field label={t("Notes")} htmlFor="vd-notes" error={errs.notes}><textarea id="vd-notes" rows={2} maxLength={2000} className="textarea" value={f.notes} onChange={(e) => set("notes", e.target.value)} /></Field>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Contacts")}</legend>
          <div className="stack" style={{ gap: 8 }}>
            {contacts.map((c, i) => (
              <div key={i} className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }}>
                <Field label={t("Name")} htmlFor={`vc-${i}-n`} error={errs[`contacts[${i}].name`]}><TextInput id={`vc-${i}-n`} maxLength={100} value={c.name} onChange={(e) => setC(i, "name", e.target.value)} /></Field>
                <Field label={t("Role")} htmlFor={`vc-${i}-r`}><TextInput id={`vc-${i}-r`} maxLength={100} value={c.role} onChange={(e) => setC(i, "role", e.target.value)} /></Field>
                <Field label={t("Phone")} htmlFor={`vc-${i}-p`}><TextInput id={`vc-${i}-p`} maxLength={40} value={c.phone} onChange={(e) => setC(i, "phone", e.target.value)} /></Field>
                <Field label={t("E-mail")} htmlFor={`vc-${i}-e`} error={errs[`contacts[${i}].email`]}><TextInput id={`vc-${i}-e`} maxLength={200} value={c.email} onChange={(e) => setC(i, "email", e.target.value)} /></Field>
                <IconButton label={`${t("Remove")} ${c.name || t("contact")}`} onClick={() => setContacts((cs) => cs.filter((_, j) => j !== i))}><Trash2 size={14} /></IconButton>
              </div>
            ))}
            <div><Button size="sm" onClick={() => setContacts((cs) => [...cs, { name: "", role: "", email: "", phone: "" }])} disabled={contacts.length >= 20}><Plus size={14} /> {t("Add contact")}</Button></div>
          </div>
        </fieldset>
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Places they cover")}</legend>
          {errs.locationIds && <p className="field-error" role="alert">{errs.locationIds}</p>}
          <Field label={t("Find a place")} htmlFor="vd-pq"><TextInput id="vd-pq" value={placeQ} onChange={(e) => setPlaceQ(e.target.value)} /></Field>
          <ul className="stack" style={{ listStyle: "none", margin: "8px 0 0", padding: 0, gap: 4, maxHeight: 220, overflow: "auto" }}>
            {shown.map((l) => <li key={l.id}><label className="row" style={{ gap: 8 }}><Checkbox checked={sites.includes(l.id)} onChange={(e) => setSites((s) => (e.target.checked ? [...s, l.id] : s.filter((x) => x !== l.id)))} /> {l.path}</label></li>)}
          </ul>
          <p className="field-help">{t("A place includes everything below it. {n} chosen.", { n: sites.length })}</p>
        </fieldset>
      </form>
    </Overlay>
  );
}

/** Add or edit one contract of a vendor. */
export function ContractDrawer({ vendorId, open, editing, onClose, onDone }: { vendorId: string; open: boolean; editing: VendorContract | null; onClose: () => void; onDone: (v: VendorDetail) => void }) {
  const run = useRun();
  const [f, setF] = useState({ title: "", reference: "", startsOn: "", endsOn: "", value: "", notes: "" }); const [seed, setSeed] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? (editing?.id ?? "new") : null;
  if (key !== seed) { setSeed(key); setErrs({}); setErr(""); setF({ title: editing?.title ?? "", reference: editing?.reference ?? "", startsOn: editing?.startsOn ?? "", endsOn: editing?.endsOn ?? "", value: editing?.value?.toString() ?? "", notes: editing?.notes ?? "" }); }
  const set = (k: keyof typeof f, v: string) => setF((x) => ({ ...x, [k]: v }));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { title: f.title, reference: f.reference || null, startsOn: f.startsOn || null, endsOn: f.endsOn || null, value: f.value === "" ? null : Number(f.value), notes: f.notes || null };
    const r = await run(() => (editing ? put<VendorDetail>(`/api/vendors/${vendorId}/contracts/${editing.id}`, body) : post<VendorDetail>(`/api/vendors/${vendorId}/contracts`, body)), t("Contract saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit contract") : t("New contract")} description={t("People who manage vendors are reminded 30 days before it ends, and when it ends.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="contract-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="contract-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Title")} htmlFor="ct-title" required error={errs.title}><TextInput id="ct-title" data-autofocus maxLength={150} value={f.title} onChange={(e) => set("title", e.target.value)} /></Field>
        <Field label={t("Contract number")} htmlFor="ct-ref" error={errs.reference}><TextInput id="ct-ref" maxLength={60} value={f.reference} onChange={(e) => set("reference", e.target.value)} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Starts")} htmlFor="ct-start" required error={errs.startsOn}><TextInput id="ct-start" type="date" value={f.startsOn} onChange={(e) => set("startsOn", e.target.value)} /></Field>
          <Field label={t("Ends")} htmlFor="ct-end" error={errs.endsOn}><TextInput id="ct-end" type="date" value={f.endsOn} onChange={(e) => set("endsOn", e.target.value)} /></Field>
          <Field label={t("Value")} htmlFor="ct-value" error={errs.value}><TextInput id="ct-value" type="number" min={0} step="0.01" value={f.value} onChange={(e) => set("value", e.target.value)} /></Field>
        </div>
        <Field label={t("Notes")} htmlFor="ct-notes" error={errs.notes}><textarea id="ct-notes" rows={2} maxLength={2000} className="textarea" value={f.notes} onChange={(e) => set("notes", e.target.value)} /></Field>
      </form>
    </Overlay>
  );
}
