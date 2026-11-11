"use client";

import { useState } from "react";
import { CalendarPlus, CalendarRange, ChevronLeft, ChevronRight, Plus, Repeat } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { ModuleEmpty } from "@/components/insights/module-empty";
import { BookingDrawer, BookingInsights, CalendarFeed, CancelBooking, PendingApprovals, FEATURES, KINDS, KindIcon, Timeline, addDays, dayLabel, featureLabel, kindPlural, startOfDay, timeRange, ymd, type TimelineRow } from "@/components/bookings/booking-bits";
import { get, post } from "@/lib/api/client";
import { useRun } from "@/lib/use-run";
import { TripDrawer } from "@/components/bookings/trip";
import type { AvailabilityView, BookingItem, PagedBookings, PortalLocation, ResourceItem, ResourceKind } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** The hours to draw: the widest opening hours of the resources shown, 07–20 by default, whole day for around-the-clock resources. */
function hourRange(resources: ResourceItem[]): [number, number] {
  if (resources.length === 0) return [7, 20];
  if (resources.some((r) => !r.openFrom)) return resources.every((r) => !r.openFrom) && resources.every((r) => r.kind !== "room") ? [0, 24] : [7, 20];
  const from = Math.min(...resources.map((r) => Number(r.openFrom!.slice(0, 2))));
  const until = Math.max(...resources.map((r) => Math.ceil(Number(r.openUntil!.slice(0, 2)) + Number(r.openUntil!.slice(3)) / 60)));
  return [Math.max(0, from), Math.min(24, Math.max(from + 1, until))];
}

function Bookings() {
  const { can } = useAuth();
  const run = useRun();
  const [feed, setFeed] = useState(false); const [trip, setTrip] = useState<string | null>(null);
  const [view, setView] = useState<"day" | "week">("day");
  const [day, setDay] = useState(() => startOfDay(new Date()));
  const [kind, setKind] = useState<ResourceKind>("room");
  const [place, setPlace] = useState(""); const [capacity, setCapacity] = useState(""); const [feature, setFeature] = useState(""); const [one, setOne] = useState("");
  const [drawer, setDrawer] = useState<{ start: { resourceId: string; at: Date } | null; editing: BookingItem | null } | null>(null);
  const [cancelling, setCancelling] = useState<BookingItem | null>(null);
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);

  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const all = useQuery(() => get<ResourceItem[]>("/api/resources"), [tick]);
  const resources = all.data ?? [];
  const ofKind = resources.filter((r) => r.kind === kind);
  const weekResource = view === "week" ? (ofKind.find((r) => r.id === one) ?? ofKind[0]) : undefined;
  const from = view === "week" ? day : day, to = addDays(day, view === "week" ? 7 : 1);
  const avail = useQuery(() => get<AvailabilityView>("/api/bookings/availability", view === "week"
    ? { from: from.toISOString(), to: to.toISOString(), resourceId: weekResource?.id }
    : { from: from.toISOString(), to: to.toISOString(), kind, locationId: place || undefined, capacity: capacity || undefined, feature: feature || undefined }), [view, from.getTime(), kind, place, capacity, feature, weekResource?.id, tick]);
  const mine = useQuery(() => get<PagedBookings>("/api/bookings", { scope: "mine", from: new Date().toISOString(), pageSize: 20 }), [tick]);

  const rowsShown = avail.data?.rows ?? [];
  const [fromHour, toHour] = hourRange(rowsShown.map((r) => r.resource));
  const rows: TimelineRow[] = view === "week" && weekResource && avail.data
    ? Array.from({ length: 7 }, (_, i) => addDays(day, i)).map((d) => ({ key: ymd(d), label: dayLabel(d), resource: avail.data!.rows[0]?.resource ?? weekResource, day: d,
        bookings: (avail.data!.rows[0]?.bookings ?? []).filter((b) => new Date(b.startsAt) < addDays(d, 1) && new Date(b.blockedUntil) > d) }))
    : rowsShown.map((r) => ({ key: r.resource.id, label: r.resource.name, sub: [r.resource.capacity ? t("{n} people", { n: r.resource.capacity }) : null, r.resource.place].filter(Boolean).join(" · "), resource: r.resource, day, bookings: r.bookings }));

  const open = (b: BookingItem) => { if (b.canChange) setDrawer({ start: null, editing: b }); };
  const loaded = all.data !== undefined;

  return (
    <>
      <PageHeader title={t("Bookings")} description={t("Book meeting rooms, pool vehicles, parking and shared equipment. Click a free time to book it.")}
        actions={<><Button onClick={() => setFeed(true)}><CalendarPlus size={16} /> {t("Calendar feed")}</Button>{resources.some((r) => r.canBook) && <Button variant="primary" onClick={() => { const first = ofKind.find((r) => r.canBook); setDrawer({ start: first ? { resourceId: first.id, at: new Date(Math.ceil(Date.now() / 1800_000) * 1800_000) } : null, editing: null }); }}><Plus size={16} /> {t("New booking")}</Button>}</>} />
      <BookingInsights reloadKey={tick} />
      {can("bookings.manage") && <PendingApprovals reloadKey={tick} onChanged={reload} />}
      {loaded && resources.length === 0 ? (
        <div className="card" style={{ marginBottom: 12 }}>
          <ModuleEmpty icon={<CalendarRange size={26} />} title={t("Nothing to book yet")} text={t("Meeting rooms, pool vehicles, parking slots and shared equipment appear here once General Affairs adds them.")}>
            {can("resources.manage") && <Button variant="primary" href="/setup/resources">{t("Add what people can book")}</Button>}
          </ModuleEmpty>
        </div>
      ) : (
        <Panel>
          <div className="qtabs" role="group" aria-label={t("What to book")}>
            {KINDS.filter((k) => resources.some((r) => r.kind === k)).map((k) => (
              <button key={k} type="button" aria-pressed={kind === k} onClick={() => { setKind(k); setOne(""); }}><KindIcon kind={k} size={15} /> {kindPlural(k)} <b>{resources.filter((r) => r.kind === k).length}</b></button>
            ))}
          </div>
          <div className="filters">
            <div className="row" style={{ gap: 6, alignItems: "flex-end" }}>
              <Button size="sm" onClick={() => setDay(addDays(day, view === "week" ? -7 : -1))} aria-label={view === "week" ? t("Previous week") : t("Previous day")}><ChevronLeft size={16} /></Button>
              <Field label={t("Date")} htmlFor="bk-day"><TextInput id="bk-day" type="date" value={ymd(day)} onChange={(e) => e.target.value && setDay(startOfDay(new Date(`${e.target.value}T00:00:00`)))} /></Field>
              <Button size="sm" onClick={() => setDay(addDays(day, view === "week" ? 7 : 1))} aria-label={view === "week" ? t("Next week") : t("Next day")}><ChevronRight size={16} /></Button>
              <Button size="sm" onClick={() => setDay(startOfDay(new Date()))}>{t("Today")}</Button>
            </div>
            <Field label={t("View")} htmlFor="bk-view"><SelectInput id="bk-view" value={view} onChange={(e) => setView(e.target.value as "day" | "week")} options={[{ value: "day", label: t("Day: all resources") }, { value: "week", label: t("Week: one resource") }]} /></Field>
            {view === "week" ? (
              <Field label={t("Resource")} htmlFor="bk-one"><SelectInput id="bk-one" value={weekResource?.id ?? ""} onChange={(e) => setOne(e.target.value)} options={ofKind.map((r) => ({ value: r.id, label: r.name }))} /></Field>
            ) : (
              <>
                <Field label={t("Place")} htmlFor="bk-place"><SelectInput id="bk-place" value={place} onChange={(e) => setPlace(e.target.value)} options={[{ value: "", label: t("Anywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
                {(kind === "room" || kind === "vehicle") && <Field label={t("At least … people")} htmlFor="bk-cap"><TextInput id="bk-cap" type="number" min={1} value={capacity} onChange={(e) => setCapacity(e.target.value)} /></Field>}
                <Field label={t("With")} htmlFor="bk-feat"><SelectInput id="bk-feat" value={feature} onChange={(e) => setFeature(e.target.value)} options={[{ value: "", label: t("Anything") }, ...FEATURES.map((f) => ({ value: f, label: featureLabel(f) }))]} /></Field>
              </>
            )}
          </div>
          {avail.error ? <div className="form-alert err" role="alert">{avail.error.message}</div>
            : rows.length === 0 && avail.data ? <p className="tab-placeholder">{t("Nothing matches. Try another place, size or feature.")}</p>
            : <Timeline rows={rows} fromHour={fromHour} toHour={toHour} label={view === "week" ? t("{name}, week of {day}", { name: weekResource?.name ?? "", day: dayLabel(day) }) : t("{kind} on {day}", { kind: kindPlural(kind), day: dayLabel(day) })}
                onPick={(r, at) => setDrawer({ start: { resourceId: r.id, at }, editing: null })} onOpen={open} />}
          <p className="field-help" style={{ marginTop: 8 }}>{t("Times are shown in this device's time zone. Your own bookings are filled in blue; a striped strip after a booking is kept free for cleaning or handover.")}</p>
        </Panel>
      )}

      <Panel title={t("My upcoming bookings")}>
        {(mine.data?.data ?? []).length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("You have no upcoming bookings.")}</p> : (
          <ul className="req-list" data-testid="my-bookings">
            {mine.data!.data.map((b) => (
              <li key={b.id}>
                <div><b>{b.title}</b>{b.repeat && <> <Repeat size={13} aria-label={t("Repeating")} /></>}<br /><small className="muted"><KindIcon kind={b.resourceKind} size={13} /> {b.resourceName} · {timeRange(b)}{b.bookedFor && !b.mine ? ` · ${b.bookedFor}` : ""}</small></div>
                <span className="row" style={{ gap: 6 }}>
                  {b.status === "pending" && <Badge tone="orange">{t("Waiting for approval")}</Badge>}
                  {b.checkedInAt && <Badge tone="green">{t("Checked in")}</Badge>}
                  {!b.checkedInAt && b.status === "confirmed" && new Date(b.startsAt) <= new Date() && <Badge tone="green">{t("Now")}</Badge>}
                  {b.canCheckIn && <Button size="sm" variant="primary" onClick={async () => { const r = await run(() => post(`/api/bookings/${b.id}/check-in`), t("Checked in."), { silent: false }); if (r.ok) reload(); }}>{t("Check in")}</Button>}
                  {b.resourceKind === "vehicle" && b.status === "confirmed" && <Button size="sm" onClick={() => setTrip(b.id)}>{t("Trip")}</Button>}
                  {b.canChange && <Button size="sm" onClick={() => setDrawer({ start: null, editing: b })}>{t("Change")}</Button>}
                  {b.canChange && <Button size="sm" variant="danger-outline" onClick={() => setCancelling(b)}>{t("Cancel")}</Button>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>

      <BookingDrawer open={!!drawer} onClose={() => setDrawer(null)} onDone={reload} resources={resources} start={drawer?.start ?? null} editing={drawer?.editing ?? null} onCancelBooking={(b) => { setDrawer(null); setCancelling(b); }} onTrip={(b) => { setDrawer(null); setTrip(b.id); }} />
      <TripDrawer bookingId={trip} onClose={() => setTrip(null)} onDone={reload} />
      <CancelBooking booking={cancelling} onClose={() => setCancelling(null)} onDone={reload} />
      <CalendarFeed open={feed} onClose={() => setFeed(false)} />
    </>
  );
}

export default function BookingsPage() { return <Bookings />; }
