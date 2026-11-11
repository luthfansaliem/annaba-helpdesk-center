"use client";

import { useState } from "react";
import { CalendarClock, CalendarDays, Car, DoorOpen, Gauge, Hourglass, Monitor, ParkingSquare, Plus, UserRound, UserX } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Skeleton } from "@/components/ui/states";
import { del, get, post, put } from "@/lib/api/client";
import type { BookingFeedInfo, BookingItem, BookingSummary, ResourceItem, ResourceKind, SeriesResult } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { formatLocale, t } from "@/i18n";

export const KINDS: ResourceKind[] = ["room", "vehicle", "parking", "equipment"];
export const kindLabel = (k: ResourceKind) => ({ room: t("Meeting room"), vehicle: t("Vehicle"), parking: t("Parking"), equipment: t("Equipment") })[k];
export const kindPlural = (k: ResourceKind) => ({ room: t("Rooms"), vehicle: t("Vehicles"), parking: t("Parking"), equipment: t("Equipment") })[k];
export function KindIcon({ kind, size = 16 }: { kind: ResourceKind; size?: number }) {
  const I = { room: DoorOpen, vehicle: Car, parking: ParkingSquare, equipment: Monitor }[kind];
  return <I size={size} aria-hidden />;
}
export const FEATURES = ["projector", "screen", "video_call", "whiteboard", "phone", "accessible", "air_conditioning", "ev_charger", "four_wheel_drive"];
export const featureLabel = (f: string) => ({
  projector: t("Projector"), screen: t("Screen"), video_call: t("Video call"), whiteboard: t("Whiteboard"), phone: t("Conference phone"), accessible: t("Wheelchair accessible"),
  air_conditioning: t("Air conditioning"), ev_charger: t("EV charger"), four_wheel_drive: t("Four-wheel drive"),
} as Record<string, string>)[f] ?? f.replace(/_/g, " ");

// ---------------------------------------------------------------- dates (the device's time zone)
export const pad = (n: number) => String(n).padStart(2, "0");
export const ymd = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
export const hm = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}`;
export const atLocal = (date: string, time: string) => new Date(`${date}T${time}:00`);
export const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
export const addDays = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
export const dayLabel = (d: Date) => d.toLocaleDateString(formatLocale(), { weekday: "short", day: "2-digit", month: "short" });
export const timeRange = (b: { startsAt: string; endsAt: string }) => {
  const s = new Date(b.startsAt), e = new Date(b.endsAt);
  return ymd(s) === ymd(e) ? `${dayLabel(s)} · ${hm(s)}–${hm(e)}` : `${dayLabel(s)} ${hm(s)} – ${dayLabel(e)} ${hm(e)}`;
};

// ---------------------------------------------------------------- timeline
export type TimelineRow = { key: string; label: React.ReactNode; sub?: string; resource: ResourceItem; day: Date; bookings: BookingItem[] };

/** One horizontal track per row (a resource on a day, or a day of one resource) from `fromHour` to `toHour`. Free time is clicked to book; each booking is a
 *  button. Keyboard and screen-reader users book with the row's own "Book" button and read the bookings as buttons with their times. */
export function Timeline({ rows, fromHour, toHour, onPick, onOpen, label }: {
  rows: TimelineRow[]; fromHour: number; toHour: number; onPick: (r: ResourceItem, at: Date) => void; onOpen: (b: BookingItem) => void; label: string;
}) {
  const span = (toHour - fromHour) * 3600_000;
  const hours = Array.from({ length: toHour - fromHour + 1 }, (_, i) => fromHour + i);
  const pct = (day: Date, at: Date) => Math.min(100, Math.max(0, ((at.getTime() - day.getTime() - fromHour * 3600_000) / span) * 100));
  const now = new Date();
  return (
    <div className="tl" role="table" aria-label={label}>
      <div className="tl-head" role="row">
        <div role="columnheader" className="tl-name">{t("Resource")}</div>
        <div role="columnheader" className="tl-scale">{hours.map((h) => <span key={h} style={{ left: `${((h - fromHour) / (toHour - fromHour)) * 100}%` }}>{pad(h)}</span>)}</div>
      </div>
      {rows.map((row) => {
        const r = row.resource;
        const closed: [number, number][] = [];
        if (r.openFrom && r.openUntil) {
          const [fh, fm] = r.openFrom.split(":").map(Number), [uh, um] = r.openUntil.split(":").map(Number);
          closed.push([0, pct(row.day, new Date(row.day.getTime() + (fh * 60 + fm) * 60_000))], [pct(row.day, new Date(row.day.getTime() + (uh * 60 + um) * 60_000)), 100]);
        }
        const dayOpen = (r.openDays & (1 << ((row.day.getDay() + 6) % 7))) !== 0;
        const showNow = ymd(now) === ymd(row.day);
        return (
          <div key={row.key} className="tl-row" role="row">
            <div className="tl-name" role="rowheader">
              <div className="tl-label">{row.label}{row.sub && <small className="muted">{row.sub}</small>}</div>
              {r.canBook && dayOpen && <Button size="sm" variant="soft" onClick={() => onPick(r, new Date(row.day.getTime() + Math.max(fromHour, now.getHours() + 1) * 3600_000))} aria-label={t("Book {name}", { name: typeof row.label === "string" ? row.label : r.name })}><Plus size={14} /></Button>}
            </div>
            <div role="cell" className={"tl-track" + (r.canBook && dayOpen ? " can" : "")}
              onClick={(e) => {
                if (!r.canBook || !dayOpen) return;
                const box = e.currentTarget.getBoundingClientRect();
                const ms = fromHour * 3600_000 + ((e.clientX - box.left) / box.width) * span;
                const step = 30 * 60_000;
                onPick(r, new Date(row.day.getTime() + Math.floor(ms / step) * step));
              }}>
              {!dayOpen && <div className="tl-closed" style={{ left: 0, width: "100%" }}><span>{t("Closed")}</span></div>}
              {dayOpen && closed.map(([a, b], i) => b > a && <div key={i} className="tl-closed" style={{ left: `${a}%`, width: `${b - a}%` }} />)}
              {hours.slice(1, -1).map((h) => <i key={h} className="tl-grid" style={{ left: `${((h - fromHour) / (toHour - fromHour)) * 100}%` }} />)}
              {row.bookings.map((b) => {
                const s = new Date(b.startsAt), en = new Date(b.endsAt), bl = new Date(b.blockedUntil);
                const left = pct(row.day, s), width = Math.max(0.8, pct(row.day, en) - left), buffer = Math.max(0, pct(row.day, bl) - pct(row.day, en));
                if (pct(row.day, bl) <= 0 || left >= 100) return null;
                const what = (b.title ?? t("Booked")) + (b.status === "pending" ? ` (${t("waiting for approval")})` : "");
                return (
                  <span key={b.id}>
                    <button type="button" className={"tl-bar" + (b.mine ? " mine" : "") + (b.status === "pending" ? " pending" : "")} style={{ left: `${left}%`, width: `${width}%` }}
                      onClick={(e) => { e.stopPropagation(); onOpen(b); }} aria-label={`${hm(s)} ${what} – ${hm(en)}${b.bookedFor ? `, ${b.bookedFor}` : ""}`} title={`${hm(s)}–${hm(en)} · ${what}${b.bookedFor ? ` · ${b.bookedFor}` : ""}`}>
                      <b>{hm(s)}</b> {what}
                    </button>
                    {buffer > 0 && <i className="tl-buffer" style={{ left: `${left + width}%`, width: `${buffer}%` }} aria-hidden />}
                  </span>
                );
              })}
              {showNow && pct(row.day, now) > 0 && pct(row.day, now) < 100 && <i className="tl-now" style={{ left: `${pct(row.day, now)}%` }} aria-hidden />}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ---------------------------------------------------------------- the booking form
type Draft = { resourceId: string; date: string; from: string; to: string; title: string; attendees: string; note: string; every: "" | "day" | "weekday" | "week"; until: string };

/** New booking or a change to one. Times are entered in the device's time zone; the server checks opening hours in the resource's own time zone. */
export function BookingDrawer({ open, onClose, onDone, resources, start, editing, onCancelBooking, onTrip }: {
  open: boolean; onClose: () => void; onDone: () => void; resources: ResourceItem[]; start: { resourceId: string; at: Date } | null; editing: BookingItem | null; onCancelBooking?: (b: BookingItem) => void; onTrip?: (b: BookingItem) => void;
}) {
  const run = useRun();
  const [d, setD] = useState<Draft | null>(null); const [seed, setSeed] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [result, setResult] = useState<SeriesResult | null>(null);
  const key = open ? `${editing?.id ?? "new"}:${editing?.version ?? 0}:${start?.resourceId ?? ""}:${start?.at.getTime() ?? 0}` : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr(""); setResult(null);
    if (editing) {
      const s = new Date(editing.startsAt), e = new Date(editing.endsAt);
      setD({ resourceId: editing.resourceId, date: ymd(s), from: hm(s), to: hm(e), title: editing.title ?? "", attendees: editing.attendees ? String(editing.attendees) : "", note: editing.note ?? "", every: "", until: "" });
    } else {
      const at = start?.at ?? new Date(Math.ceil(Date.now() / 1800_000) * 1800_000);
      const end = new Date(at.getTime() + 3600_000);
      setD({ resourceId: start?.resourceId ?? resources.find((r) => r.canBook)?.id ?? "", date: ymd(at), from: hm(at), to: ymd(end) === ymd(at) ? hm(end) : "23:55", title: "", attendees: "", note: "", every: "", until: ymd(addDays(at, 28)) });
    }
  }
  const set = (k: keyof Draft, v: string) => setD((x) => (x ? { ...x, [k]: v } : x));
  const res = resources.find((r) => r.id === d?.resourceId);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!d) return;
    setErr(""); setErrs({}); setBusy(true);
    const startsAt = atLocal(d.date, d.from), endsAt = atLocal(d.date, d.to);
    if (endsAt <= startsAt) endsAt.setDate(endsAt.getDate() + 1);   // ends after midnight
    const body = { resourceId: d.resourceId || null, startsAt: startsAt.toISOString(), endsAt: endsAt.toISOString(), title: d.title, attendees: d.attendees ? Number(d.attendees) : null, note: d.note || null, version: editing?.version };
    if (!editing && d.every) {
      const s = await run(() => post<SeriesResult>("/api/bookings/series", { ...body, every: d.every, until: d.until || null }), "", { silent: true });
      setBusy(false);
      if (!s.ok) { setErr(s.error); setErrs(s.fields ?? {}); return; }
      onDone();
      if (s.data.skipped.length === 0) { onClose(); return; }
      setResult(s.data); return;
    }
    const r = await run(() => (editing ? put<BookingItem>(`/api/bookings/${editing.id}`, body) : post<BookingItem>("/api/bookings", body)),
      editing ? t("Booking changed.") : res?.needsApproval ? t("Booked. It waits for approval.") : t("Booked."), { silent: false });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Change booking") : t("New booking")} description={res ? `${kindLabel(res.kind)}${res.place ? ` · ${res.place}` : ""}` : t("Book a room, a vehicle, a parking slot or equipment.")}
      footer={result ? <Button variant="primary" onClick={onClose}>{t("Done")}</Button> : <>{editing && onCancelBooking && <Button variant="danger-outline" onClick={() => onCancelBooking(editing)}>{t("Cancel booking")}</Button>}{editing && onTrip && editing.resourceKind === "vehicle" && <Button onClick={() => onTrip(editing)}>{t("Trip")}</Button>}<Button onClick={onClose}>{t("Close")}</Button><Button variant="primary" type="submit" form="booking-form" disabled={busy || !d}>{busy ? t("Saving…") : editing ? t("Save") : t("Book")}</Button></>}>
      {result && (
        <div className="stack" role="status" data-testid="series-result">
          <div className="form-alert">{t("Booked {n} times. These could not be booked:", { n: result.made.length })}</div>
          <ul className="req-list">{result.skipped.map((x) => <li key={x.startsAt}><div><b>{dayLabel(new Date(x.startsAt))} {hm(new Date(x.startsAt))}</b><br /><small className="muted">{x.reason}</small></div></li>)}</ul>
        </div>
      )}
      {d && !result && (
        <form id="booking-form" className="stack" noValidate onSubmit={submit}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <Field label={t("What to book")} htmlFor="bk-res" required error={errs.resourceId}>
            <SelectInput id="bk-res" value={d.resourceId} onChange={(e) => set("resourceId", e.target.value)}
              options={[{ value: "", label: t("Choose…") }, ...KINDS.flatMap((k) => resources.filter((r) => r.kind === k && (r.canBook || r.id === d.resourceId)).map((r) => ({ value: r.id, label: `${kindLabel(k)} · ${r.name}${r.capacity ? ` (${r.capacity})` : ""}` })))]} />
          </Field>
          {res && (res.openFrom || res.bufferMinutes > 0 || res.capacity) && (
            <p className="field-help" style={{ margin: 0 }}>
              {[res.openFrom && res.openUntil ? t("Open {from}–{until}", { from: res.openFrom, until: res.openUntil }) : null, res.capacity ? t("Holds {n}", { n: res.capacity }) : null,
                res.bufferMinutes ? t("{n} min kept free after each booking", { n: res.bufferMinutes }) : null].filter(Boolean).join(" · ")}
            </p>
          )}
          {res?.needsApproval && <div className="form-alert" role="note">{t("General Affairs approves bookings of this resource. The time is held for you until they decide.")}</div>}
          {res?.checkInMinutes && <p className="field-help" style={{ margin: 0 }}>{t("Check in within {n} minutes of the start, or the booking is released for others.", { n: res.checkInMinutes })}</p>}
          <Field label={t("Date")} htmlFor="bk-date" required error={errs.startsAt}><TextInput id="bk-date" type="date" value={d.date} onChange={(e) => set("date", e.target.value)} /></Field>
          <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
            <Field label={t("From")} htmlFor="bk-from" required><TextInput id="bk-from" type="time" step={300} value={d.from} onChange={(e) => set("from", e.target.value)} /></Field>
            <Field label={t("Until")} htmlFor="bk-to" required error={errs.endsAt}><TextInput id="bk-to" type="time" step={300} value={d.to} onChange={(e) => set("to", e.target.value)} /></Field>
          </div>
          {!editing && (
            <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
              <Field label={t("Repeat")} htmlFor="bk-every" error={errs.every}>
                <SelectInput id="bk-every" value={d.every} onChange={(e) => set("every", e.target.value)} options={[{ value: "", label: t("Does not repeat") }, { value: "day", label: t("Every day") }, { value: "weekday", label: t("Every weekday") }, { value: "week", label: t("Every week") }]} />
              </Field>
              {d.every && <Field label={t("Until")} htmlFor="bk-until" error={errs.until ?? errs.count}><TextInput id="bk-until" type="date" value={d.until} onChange={(e) => set("until", e.target.value)} /></Field>}
            </div>
          )}
          {editing?.seriesId && <p className="field-help" style={{ margin: 0 }}>{t("Part of a repeating booking: this changes only this one.")}</p>}
          <Field label={t("What is it for?")} htmlFor="bk-title" required error={errs.title}><TextInput id="bk-title" data-autofocus maxLength={150} placeholder={t("e.g. Weekly team meeting")} value={d.title} onChange={(e) => set("title", e.target.value)} /></Field>
          {res && (res.kind === "room" || res.kind === "vehicle") && (
            <Field label={res.kind === "vehicle" ? t("Passengers") : t("People")} htmlFor="bk-att" error={errs.attendees}><TextInput id="bk-att" type="number" min={1} max={res.capacity ?? 1000} value={d.attendees} onChange={(e) => set("attendees", e.target.value)} /></Field>
          )}
          <Field label={t("Note")} htmlFor="bk-note" error={errs.note}><textarea id="bk-note" className="textarea" rows={2} maxLength={1000} value={d.note} onChange={(e) => set("note", e.target.value)} /></Field>
        </form>
      )}
    </Overlay>
  );
}

/** Cancel a booking; someone else's booking needs a reason, which the person is told. */
export function CancelBooking({ booking, onClose, onDone }: { booking: BookingItem | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [reason, setReason] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [seed, setSeed] = useState<string | null>(null); const [scope, setScope] = useState<"this" | "following">("this");
  if ((booking?.id ?? null) !== seed) { setSeed(booking?.id ?? null); setReason(""); setErr(""); setScope("this"); }
  async function go() {
    if (!booking) return;
    setBusy(true); setErr("");
    const r = await run(() => post<BookingItem>(`/api/bookings/${booking.id}/cancel`, { reason: reason || null, scope }), t("Booking cancelled."), { silent: false });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.reason ?? r.error); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={!!booking} onClose={onClose} title={t("Cancel this booking?")} description={booking ? `${booking.resourceName} · ${timeRange(booking)}` : undefined}
      footer={<><Button onClick={onClose}>{t("Keep it")}</Button><Button variant="danger" onClick={go} disabled={busy}>{busy ? t("Cancelling…") : t("Cancel booking")}</Button></>}>
      {err && <div className="form-alert err" role="alert">{err}</div>}
      {booking?.seriesId && (
        <fieldset className="fl" style={{ border: 0, padding: 0, margin: "0 0 12px" }}>
          <legend style={{ fontWeight: 700, padding: 0, marginBottom: 6 }}>{t("This is a repeating booking")}</legend>
          <label className="row" style={{ gap: 8 }}><input type="radio" name="bk-scope" checked={scope === "this"} onChange={() => setScope("this")} /> {t("Only this one")}</label>
          <label className="row" style={{ gap: 8 }}><input type="radio" name="bk-scope" checked={scope === "following"} onChange={() => setScope("following")} /> {t("This one and all after it")}</label>
        </fieldset>
      )}
      <Field label={booking?.mine ? t("Reason (optional)") : t("Reason (the person is told)")} htmlFor="bk-why"><TextInput id="bk-why" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
    </Overlay>
  );
}

// ---------------------------------------------------------------- KPI strip
export function BookingInsights({ reloadKey }: { reloadKey: number }) {
  const q = useQuery(() => get<BookingSummary>("/api/bookings/summary"), [reloadKey]);
  const s = q.data;
  if (!s) return <div className="kpis">{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} h={86} />)}</div>;
  if (s.resources === 0) return null;
  return (
    <KpiGrid label={t("Bookings at a glance")}>
      <KpiCard icon={<DoorOpen size={18} />} label={t("Free now")} value={String(s.freeNow)} note={t("of {n} resources", { n: s.resources })} tone="good" />
      <KpiCard icon={<CalendarClock size={18} />} label={t("In use now")} value={String(s.inUseNow)} note={t("booked at this moment")} />
      <KpiCard icon={<CalendarDays size={18} />} label={t("Bookings today")} value={String(s.bookingsToday)} note={t("{n} in the next 7 days", { n: s.next7Days.reduce((a, d) => a + d.bookings, 0) })} />
      <KpiCard icon={<UserRound size={18} />} label={t("My upcoming")} value={String(s.myUpcoming)} note={t("bookings for you")} />
      {s.pendingApprovals > 0 && <KpiCard icon={<Hourglass size={18} />} label={t("Waiting for approval")} value={String(s.pendingApprovals)} note={t("bookings you can decide")} tone="warn" />}
      {s.noShows7 > 0 && <KpiCard icon={<UserX size={18} />} label={t("No-shows")} value={String(s.noShows7)} note={t("released, last 7 days")} tone="warn" />}
      <KpiCard icon={<Gauge size={18} />} label={t("Room and vehicle use")} value={s.utilisationPercent === null ? "—" : `${s.utilisationPercent}%`} note={t("of opening hours, last 7 days")} />
    </KpiGrid>
  );
}

// ---------------------------------------------------------------- approvals (people with bookings.manage)
export function PendingApprovals({ reloadKey, onChanged }: { reloadKey: number; onChanged: () => void }) {
  const run = useRun();
  const q = useQuery(() => get<{ data: BookingItem[] }>("/api/bookings", { scope: "pending", pageSize: 50 }), [reloadKey]);
  const [rejecting, setRejecting] = useState<BookingItem | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState("");
  const rows = q.data?.data ?? [];
  if (rows.length === 0) return null;
  const approve = async (b: BookingItem) => { const r = await run(() => post<BookingItem>(`/api/bookings/${b.id}/approve`, {}), t("Approved."), { silent: false }); if (r.ok) onChanged(); };
  const reject = async () => {
    if (!rejecting) return;
    const r = await run(() => post<BookingItem>(`/api/bookings/${rejecting.id}/reject`, { note: note || null }), t("Not approved."), { silent: true });
    if (!r.ok) { setErr(r.fields?.note ?? r.error); return; }
    setRejecting(null); setNote(""); setErr(""); onChanged();
  };
  return (
    <section className="card" style={{ padding: 16, marginBottom: 12 }} aria-labelledby="bk-pending-h">
      <h2 id="bk-pending-h" style={{ margin: "0 0 8px", fontSize: 16 }}>{t("Waiting for your approval")}</h2>
      <ul className="req-list" data-testid="pending-bookings">
        {rows.map((b) => (
          <li key={b.id}>
            <div><b>{b.title}</b><br /><small className="muted"><KindIcon kind={b.resourceKind} size={13} /> {b.resourceName} · {timeRange(b)}{b.bookedFor ? ` · ${b.bookedFor}` : ""}</small></div>
            <span className="row" style={{ gap: 6 }}>
              <Button size="sm" variant="primary" onClick={() => approve(b)}>{t("Approve")}</Button>
              <Button size="sm" variant="danger-outline" onClick={() => { setRejecting(b); setNote(""); setErr(""); }}>{t("Reject")}</Button>
            </span>
          </li>
        ))}
      </ul>
      <Overlay open={!!rejecting} onClose={() => setRejecting(null)} title={t("Not approve this booking?")} description={rejecting ? `${rejecting.resourceName} · ${timeRange(rejecting)}` : undefined}
        footer={<><Button onClick={() => setRejecting(null)}>{t("Back")}</Button><Button variant="danger" onClick={reject}>{t("Reject")}</Button></>}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Reason (the person is told)")} htmlFor="bk-rej"><TextInput id="bk-rej" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
      </Overlay>
    </section>
  );
}

// ---------------------------------------------------------------- private calendar feed (ICS)
export function CalendarFeed({ open, onClose }: { open: boolean; onClose: () => void }) {
  const run = useRun();
  const [tick, setTick] = useState(0); const [url, setUrl] = useState<string | null>(null);
  const info = useQuery(() => (open ? get<BookingFeedInfo>("/api/bookings/feed") : Promise.resolve(null)), [open, tick]);
  const make = async () => { const r = await run(() => post<BookingFeedInfo>("/api/bookings/feed"), t("Calendar link made."), { silent: false }); if (r.ok) { setUrl(r.data.url); setTick((n) => n + 1); } };
  const revoke = async () => { const r = await run(() => del<BookingFeedInfo>("/api/bookings/feed"), t("Calendar link turned off."), { silent: false }); if (r.ok) { setUrl(null); setTick((n) => n + 1); } };
  return (
    <Overlay open={open} onClose={() => { setUrl(null); onClose(); }} title={t("Your bookings in your calendar")} description={t("Subscribe to a private link in Outlook, Google Calendar or Apple Calendar. It shows your bookings from 30 days ago to 6 months ahead and is read-only.")}
      footer={<Button onClick={() => { setUrl(null); onClose(); }}>{t("Close")}</Button>}>
      <div className="stack">
        {url ? (
          <>
            <Field label={t("Your calendar link (shown once)")} htmlFor="bk-feed"><TextInput id="bk-feed" readOnly value={url} onFocus={(e) => e.currentTarget.select()} /></Field>
            <div className="row" style={{ gap: 8 }}><Button onClick={() => navigator.clipboard?.writeText(url)}>{t("Copy link")}</Button></div>
            <p className="field-help" style={{ margin: 0 }}>{t("Anyone with this link can see your bookings. Keep it to yourself; make a new one if it leaks.")}</p>
          </>
        ) : info.data?.active ? (
          <p style={{ margin: 0 }}>{t("You have a calendar link.")} {info.data.lastReadAt ? t("Your calendar last read it on {when}.", { when: new Date(info.data.lastReadAt).toLocaleString() }) : t("No calendar has read it yet.")}</p>
        ) : <p style={{ margin: 0 }}>{t("You have no calendar link yet.")}</p>}
        <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
          <Button variant="primary" onClick={make}>{info.data?.active || url ? t("Make a new link") : t("Make a link")}</Button>
          {(info.data?.active || url) && <Button variant="danger-outline" onClick={revoke}>{t("Turn the link off")}</Button>}
        </div>
        {(info.data?.active || url) && <p className="field-help" style={{ margin: 0 }}>{t("A new link stops the old one from working.")}</p>}
      </div>
    </Overlay>
  );
}
