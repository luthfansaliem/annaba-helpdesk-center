"use client";

import { useState } from "react";
import { Clock, Inbox, PackageCheck, Plus, Send, Truck } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Bars, ChartCard, ColumnChart, PALETTE } from "@/components/insights/charts";
import { get, post } from "@/lib/api/client";
import type { MailItem, MailSummary, PagedMail, PortalLocation } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime } from "@/lib/format";
import { formatLocale, t } from "@/i18n";

const kindLabel = (k: MailItem["kind"]) => ({ letter: t("Letter"), document: t("Document"), parcel: t("Parcel"), other: t("Other") })[k];
const statusLabel = (s: MailItem["status"]) => ({ waiting: t("Waiting at reception"), collected: t("Collected"), registered: t("To be sent"), sent: t("Sent"), voided: t("Voided") })[s];
const tone: Record<MailItem["status"], BadgeTone> = { waiting: "orange", collected: "green", registered: "blue", sent: "green", voided: "gray" };

function LogDrawer({ direction, onClose, onDone }: { direction: "incoming" | "outgoing" | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const places = useQuery(() => (direction ? get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]) : Promise.resolve([] as PortalLocation[])), [direction]);
  const blank = { kind: "parcel", personEmail: "", otherParty: "", address: "", carrier: "", waybill: "", description: "", locationId: "" };
  const [f, setF] = useState(blank); const [seed, setSeed] = useState<string | null>(null); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState("");
  if ((direction ?? null) !== seed) { setSeed(direction); setF(blank); setErrs({}); setErr(""); }
  const set = (k: keyof typeof blank, v: string) => setF((x) => ({ ...x, [k]: v }));
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!direction) return;
    const r = await run(() => post<MailItem>("/api/mail", { direction, ...Object.fromEntries(Object.entries(f).map(([k, v]) => [k, v || null])) }), direction === "incoming" ? t("Logged. The recipient is told.") : t("Logged."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  const incoming = direction === "incoming";
  return (
    <Overlay open={!!direction} onClose={onClose} side="right" title={incoming ? t("Log incoming mail") : t("Log outgoing mail")} description={incoming ? t("The recipient is told at once and reminded after two days.") : t("Record the courier and waybill when it leaves.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="mail-form">{t("Save")}</Button></>}>
      <form id="mail-form" className="stack" noValidate onSubmit={save}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Kind")} htmlFor="ml-kind" error={errs.kind}><SelectInput id="ml-kind" value={f.kind} onChange={(e) => set("kind", e.target.value)} options={(["parcel", "letter", "document", "other"] as const).map((k) => ({ value: k, label: kindLabel(k) }))} /></Field>
        <Field label={incoming ? t("For (e-mail of the employee)") : t("From (e-mail of the employee)")} htmlFor="ml-person" required error={errs.personEmail}><TextInput id="ml-person" type="email" data-autofocus value={f.personEmail} onChange={(e) => set("personEmail", e.target.value)} /></Field>
        <Field label={incoming ? t("Sender") : t("Addressee")} htmlFor="ml-other" required={!incoming} error={errs.otherParty}><TextInput id="ml-other" maxLength={200} value={f.otherParty} onChange={(e) => set("otherParty", e.target.value)} /></Field>
        {!incoming && <Field label={t("Address")} htmlFor="ml-address" error={errs.address}><textarea id="ml-address" className="textarea" rows={2} maxLength={500} value={f.address} onChange={(e) => set("address", e.target.value)} /></Field>}
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("Courier")} htmlFor="ml-carrier" error={errs.carrier}><TextInput id="ml-carrier" maxLength={60} placeholder="JNE, J&T, SiCepat…" value={f.carrier} onChange={(e) => set("carrier", e.target.value)} /></Field>
          <Field label={t("Waybill")} htmlFor="ml-waybill" error={errs.waybill}><TextInput id="ml-waybill" maxLength={60} value={f.waybill} onChange={(e) => set("waybill", e.target.value)} /></Field>
        </div>
        <Field label={t("Description")} htmlFor="ml-desc" error={errs.description}><TextInput id="ml-desc" maxLength={300} value={f.description} onChange={(e) => set("description", e.target.value)} /></Field>
        <Field label={t("Mailroom")} htmlFor="ml-place" error={errs.locationId}><SelectInput id="ml-place" value={f.locationId} onChange={(e) => set("locationId", e.target.value)} options={[{ value: "", label: t("Not set") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
      </form>
    </Overlay>
  );
}

function Act({ item, onClose, onDone }: { item: { m: MailItem; action: "collect" | "send" | "void" } | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [a, setA] = useState(""); const [b, setB] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({}); const [seed, setSeed] = useState<string | null>(null);
  const key = item ? `${item.m.id}:${item.action}` : null;
  if (key !== seed) { setSeed(key); setA(item?.action === "collect" ? (item.m.person ?? "") : item?.action === "send" ? (item.m.carrier ?? "") : ""); setB(item?.m.waybill ?? ""); setErrs({}); }
  async function go() {
    if (!item) return;
    const body = item.action === "collect" ? { collectedBy: a || null } : item.action === "send" ? { carrier: a || null, waybill: b || null } : { note: a || null };
    const r = await run(() => post<MailItem>(`/api/mail/${item.m.id}/${item.action}`, body), item.action === "collect" ? t("Collected.") : item.action === "send" ? t("Sent.") : t("Voided."), { silent: false });
    if (!r.ok) { setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  const title = item?.action === "collect" ? t("Who collected it?") : item?.action === "send" ? t("Handed to the courier") : t("Void this entry?");
  return (
    <Overlay open={!!item} onClose={onClose} title={title} description={item ? `${kindLabel(item.m.kind)} · ${item.m.person ?? ""}${item.m.otherParty ? ` · ${item.m.otherParty}` : ""}` : undefined}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant={item?.action === "void" ? "danger" : "primary"} onClick={go}>{t("Confirm")}</Button></>}>
      {item?.action === "collect" && <Field label={t("Collected by")} htmlFor="ma-a" error={errs.collectedBy}><TextInput id="ma-a" maxLength={150} value={a} onChange={(e) => setA(e.target.value)} /></Field>}
      {item?.action === "send" && <div className="stack"><Field label={t("Courier")} htmlFor="ma-a" error={errs.carrier}><TextInput id="ma-a" maxLength={60} value={a} onChange={(e) => setA(e.target.value)} /></Field><Field label={t("Waybill")} htmlFor="ma-b" error={errs.waybill}><TextInput id="ma-b" maxLength={60} value={b} onChange={(e) => setB(e.target.value)} /></Field></div>}
      {item?.action === "void" && <Field label={t("Why")} htmlFor="ma-a" error={errs.note}><TextInput id="ma-a" maxLength={300} value={a} onChange={(e) => setA(e.target.value)} /></Field>}
    </Overlay>
  );
}

/** VIS-004. Everyone sees the mail logged for or by them; the mailroom (mail.manage) logs incoming and outgoing items and records collection and dispatch. */
export default function MailPage() {
  const { can } = useAuth();
  const desk = can("mail.manage");
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const [view, setView] = useState<"open" | "">("open"); const [q, setQ] = useState(""); const [direction, setDirection] = useState("");
  const [logging, setLogging] = useState<"incoming" | "outgoing" | null>(null); const [acting, setActing] = useState<{ m: MailItem; action: "collect" | "send" | "void" } | null>(null);
  const list = useQuery(() => get<PagedMail>("/api/mail", { scope: desk ? "desk" : "mine", status: view || undefined, direction: direction || undefined, q: q || undefined, pageSize: 100 }), [desk, view, direction, q, tick]);
  const sum = useQuery(() => (desk ? get<MailSummary>("/api/mail/summary") : Promise.resolve(null)), [desk, tick]);
  const rows = list.data?.data ?? []; const s = sum.data;
  return (
    <>
      <PageHeader title={desk ? t("Mailroom") : t("My mail")} description={desk ? t("Letters and parcels through reception: log what arrives and what leaves, and who took it.") : t("Letters and parcels that reception logged for you or sent for you.")}
        actions={desk ? <><Button onClick={() => setLogging("outgoing")}><Send size={16} /> {t("Outgoing")}</Button><Button variant="primary" onClick={() => setLogging("incoming")}><Plus size={16} /> {t("Incoming")}</Button></> : undefined} />
      {s && (
        <KpiGrid label={t("Mail at a glance")}>
          <KpiCard icon={<Inbox size={18} />} label={t("Waiting")} value={String(s.waiting)} note={s.oldestWaitingDays !== null ? t("oldest {n} days", { n: s.oldestWaitingDays }) : t("nothing waiting")} tone={s.waitingOver2Days > 0 ? "warn" : "default"} />
          <KpiCard icon={<Clock size={18} />} label={t("Waiting over 2 days")} value={String(s.waitingOver2Days)} note={t("recipients were reminded")} tone={s.waitingOver2Days > 0 ? "warn" : "good"} />
          <KpiCard icon={<Inbox size={18} />} label={t("Received today")} value={String(s.receivedToday)} note={t("{n} collected today", { n: s.collectedToday })} />
          <KpiCard icon={<Truck size={18} />} label={t("Outgoing")} value={String(s.outgoingPending)} note={t("{n} sent this week", { n: s.sentThisWeek })} />
        </KpiGrid>
      )}
      <Panel>
        <div className="filters">
          <Field label={t("Show")} htmlFor="ml-view"><SelectInput id="ml-view" value={view} onChange={(e) => setView(e.target.value as "open" | "")} options={[{ value: "open", label: t("Still at reception") }, { value: "", label: t("Everything") }]} /></Field>
          {desk && <Field label={t("Direction")} htmlFor="ml-dir"><SelectInput id="ml-dir" value={direction} onChange={(e) => setDirection(e.target.value)} options={[{ value: "", label: t("Both") }, { value: "incoming", label: t("Incoming") }, { value: "outgoing", label: t("Outgoing") }]} /></Field>}
          {desk && <Field label={t("Search")} htmlFor="ml-q"><TextInput id="ml-q" placeholder={t("Person, sender or waybill")} value={q} onChange={(e) => setQ(e.target.value)} /></Field>}
        </div>
        {rows.length === 0 ? <p className="tab-placeholder">{view === "open" ? t("Nothing at reception.") : t("No mail yet.")}</p> : (
          <ul className="req-list" data-testid="mail-list">
            {rows.map((m) => (
              <li key={m.id}>
                <div>
                  <b>{kindLabel(m.kind)}</b> {m.direction === "incoming" ? <>{t("for")} <b>{m.person ?? "—"}</b>{m.otherParty ? ` · ${t("from {who}", { who: m.otherParty })}` : ""}</> : <>{t("to")} <b>{m.otherParty ?? "—"}</b> · {t("from {who}", { who: m.person ?? "—" })}</>}
                  <br /><small className="muted">{fmtDateTime(m.loggedAt)}{m.carrier ? ` · ${m.carrier}` : ""}{m.waybill ? ` ${m.waybill}` : ""}{m.description ? ` · ${m.description}` : ""}{m.collectedBy ? ` · ${t("collected by {who}", { who: m.collectedBy })}` : ""}{m.waitingDays >= 2 ? ` · ${t("waiting {n} days", { n: m.waitingDays })}` : ""}</small>
                </div>
                <span className="row" style={{ gap: 6 }}>
                  <Badge tone={tone[m.status]}>{statusLabel(m.status)}</Badge>
                  {m.canHandle && m.status === "waiting" && <Button size="sm" variant="primary" onClick={() => setActing({ m, action: "collect" })}><PackageCheck size={14} /> {t("Collected")}</Button>}
                  {m.canHandle && m.status === "registered" && <Button size="sm" variant="primary" onClick={() => setActing({ m, action: "send" })}><Truck size={14} /> {t("Send")}</Button>}
                  {m.canHandle && <Button size="sm" variant="danger-outline" onClick={() => setActing({ m, action: "void" })}>{t("Void")}</Button>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
      {s && s.received30.some((d) => d.bookings > 0) && (
        <div className="chart-grid wide-first">
          <ChartCard title={t("Incoming per day")} sub={t("Last 30 days")}>
            <ColumnChart labels={s.received30.map((d) => new Date(`${d.day}T00:00:00`).toLocaleDateString(formatLocale(), { day: "2-digit", month: "short" }))} series={[{ name: t("Incoming"), color: PALETTE[0], data: s.received30.map((d) => d.bookings) }]} label={t("Incoming mail per day")} />
          </ChartCard>
          <ChartCard title={t("By courier")} sub={t("Last 30 days")}><Bars items={s.byCarrier.map((x) => ({ label: x.label, value: x.count }))} color={PALETTE[2]} /></ChartCard>
        </div>
      )}
      <LogDrawer direction={logging} onClose={() => setLogging(null)} onDone={reload} />
      <Act item={acting} onClose={() => setActing(null)} onDone={reload} />
    </>
  );
}
