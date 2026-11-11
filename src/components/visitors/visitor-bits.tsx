"use client";

import { useState } from "react";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { get, post, put } from "@/lib/api/client";
import type { PortalLocation, VisitItem, VisitStatus } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";
import { addDays, hm, pad, ymd } from "@/components/bookings/booking-bits";

export const visitStatusLabel = (s: VisitStatus) => ({ expected: t("Expected"), checked_in: t("On site"), checked_out: t("Left"), cancelled: t("Cancelled"), no_show: t("Did not come") })[s];
const tone: Record<VisitStatus, BadgeTone> = { expected: "blue", checked_in: "green", checked_out: "gray", cancelled: "gray", no_show: "orange" };
export function VisitStatusBadge({ s }: { s: VisitStatus }) { return <Badge tone={tone[s]}>{visitStatusLabel(s)}</Badge>; }
export const visitWhen = (v: { expectedFrom: string; expectedUntil: string }) => {
  const a = new Date(v.expectedFrom), b = new Date(v.expectedUntil);
  const day = a.toLocaleDateString(undefined, { weekday: "short", day: "2-digit", month: "short" });
  return ymd(a) === ymd(b) ? `${day} · ${hm(a)}–${hm(b)}` : `${day} ${hm(a)} – ${b.toLocaleDateString(undefined, { day: "2-digit", month: "short" })} ${hm(b)}`;
};

type Draft = { visitorName: string; company: string; email: string; phone: string; vehiclePlate: string; purpose: string; date: string; from: string; to: string; locationId: string; hostEmail: string };

/** Register a visitor (or a walk-in at reception, for any host by e-mail). */
export function VisitDrawer({ open, onClose, onDone, editing, desk }: { open: boolean; onClose: () => void; onDone: () => void; editing: VisitItem | null; desk?: boolean }) {
  const run = useRun();
  const places = useQuery(() => (open ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [open]);
  const [d, setD] = useState<Draft | null>(null); const [seed, setSeed] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr("");
    if (editing) {
      const a = new Date(editing.expectedFrom), b = new Date(editing.expectedUntil);
      setD({ visitorName: editing.visitorName, company: editing.company ?? "", email: editing.email ?? "", phone: editing.phone ?? "", vehiclePlate: editing.vehiclePlate ?? "", purpose: editing.purpose ?? "",
        date: ymd(a), from: hm(a), to: hm(b), locationId: editing.locationId ?? "", hostEmail: "" });
    } else {
      const now = new Date(Math.ceil(Date.now() / 900_000) * 900_000);
      const later = new Date(now.getTime() + 2 * 3600_000);
      setD({ visitorName: "", company: "", email: "", phone: "", vehiclePlate: "", purpose: "", date: ymd(now), from: hm(now), to: ymd(later) === ymd(now) ? hm(later) : `${pad(23)}:45`, locationId: "", hostEmail: "" });
    }
  }
  const set = (k: keyof Draft, v: string) => setD((x) => (x ? { ...x, [k]: v } : x));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!d) return;
    setErr(""); setErrs({}); setBusy(true);
    const from = new Date(`${d.date}T${d.from}:00`); let to = new Date(`${d.date}T${d.to}:00`); if (to <= from) to = addDays(to, 1);
    const body = { visitorName: d.visitorName, company: d.company || null, email: d.email || null, phone: d.phone || null, vehiclePlate: d.vehiclePlate || null, purpose: d.purpose || null,
      locationId: d.locationId || null, expectedFrom: from.toISOString(), expectedUntil: to.toISOString(), hostEmail: desk && !editing && d.hostEmail ? d.hostEmail : undefined, version: editing?.version };
    const r = await run(() => (editing ? put<VisitItem>(`/api/visits/${editing.id}`, body) : post<VisitItem>("/api/visits", body)), editing ? t("Visit changed.") : t("Visitor registered."), { silent: false });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Change visit") : desk ? t("Register a walk-in") : t("Register a visitor")} description={t("Reception is told who to expect; you are told when they arrive.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="visit-form" disabled={busy || !d}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      {d && (
        <form id="visit-form" className="stack" noValidate onSubmit={submit}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <Field label={t("Visitor's name")} htmlFor="vs-name" required error={errs.visitorName}><TextInput id="vs-name" data-autofocus maxLength={150} value={d.visitorName} onChange={(e) => set("visitorName", e.target.value)} /></Field>
          <Field label={t("Company")} htmlFor="vs-company" error={errs.company}><TextInput id="vs-company" maxLength={150} value={d.company} onChange={(e) => set("company", e.target.value)} /></Field>
          <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
            <Field label={t("E-mail")} htmlFor="vs-email" error={errs.email}><TextInput id="vs-email" type="email" maxLength={200} value={d.email} onChange={(e) => set("email", e.target.value)} /></Field>
            <Field label={t("Phone")} htmlFor="vs-phone" error={errs.phone}><TextInput id="vs-phone" maxLength={40} value={d.phone} onChange={(e) => set("phone", e.target.value)} /></Field>
          </div>
          {desk && !editing && <Field label={t("Host's e-mail")} htmlFor="vs-host" error={errs.hostEmail ?? errs.hostId}><TextInput id="vs-host" type="email" placeholder={t("Leave empty if you are the host")} value={d.hostEmail} onChange={(e) => set("hostEmail", e.target.value)} /></Field>}
          <Field label={t("Date")} htmlFor="vs-date" required error={errs.expectedFrom}><TextInput id="vs-date" type="date" value={d.date} onChange={(e) => set("date", e.target.value)} /></Field>
          <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
            <Field label={t("Arrives")} htmlFor="vs-from" required><TextInput id="vs-from" type="time" value={d.from} onChange={(e) => set("from", e.target.value)} /></Field>
            <Field label={t("Until")} htmlFor="vs-to" required error={errs.expectedUntil}><TextInput id="vs-to" type="time" value={d.to} onChange={(e) => set("to", e.target.value)} /></Field>
          </div>
          <Field label={t("Where")} htmlFor="vs-place" error={errs.locationId}><SelectInput id="vs-place" value={d.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Not set") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
          <Field label={t("Purpose")} htmlFor="vs-purpose" error={errs.purpose}><TextInput id="vs-purpose" maxLength={300} value={d.purpose} onChange={(e) => set("purpose", e.target.value)} /></Field>
          <Field label={t("Vehicle plate")} htmlFor="vs-plate" error={errs.vehiclePlate}><TextInput id="vs-plate" maxLength={20} placeholder="B 1234 XYZ" value={d.vehiclePlate} onChange={(e) => set("vehiclePlate", e.target.value)} /></Field>
        </form>
      )}
    </Overlay>
  );
}
