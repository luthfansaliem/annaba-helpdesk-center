"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { Badge } from "@/components/ui/badge";
import { get, post, put } from "@/lib/api/client";
import type { DriverOption, TripItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime, fmtMoney } from "@/lib/format";
import { t } from "@/i18n";
import { timeRange } from "./booking-bits";

const CHECKLIST = ["fuel", "clean", "no_new_damage", "keys", "documents"] as const;
const checkLabel = (k: string) => ({ fuel: t("Fuel at the level it was handed over"), clean: t("Clean inside"), no_new_damage: t("No new damage"), keys: t("Keys and card returned"), documents: t("STNK and documents in the car") } as Record<string, string>)[k] ?? k;

/** BKG-004. The trip of a vehicle booking: where to, who drives, picking the car up and bringing it back (odometer, costs, checklist, damage). */
export function TripDrawer({ bookingId, onClose, onDone, currency = "IDR" }: { bookingId: string | null; onClose: () => void; onDone: () => void; currency?: string }) {
  const run = useRun();
  const { can } = useAuth();
  const [tick, setTick] = useState(0);
  const q = useQuery(() => (bookingId ? get<TripItem>(`/api/bookings/${bookingId}/trip`) : Promise.resolve(null)), [bookingId, tick]);
  const drivers = useQuery(() => (bookingId && can("bookings.manage") ? get<DriverOption[]>("/api/drivers").catch(() => [] as DriverOption[]) : Promise.resolve([] as DriverOption[])), [bookingId]);
  const trip = q.data;
  const [seed, setSeed] = useState<string | null>(null);
  const [dest, setDest] = useState(""); const [purpose, setPurpose] = useState(""); const [needsDriver, setNeedsDriver] = useState(false); const [driverId, setDriverId] = useState("");
  const [kmOut, setKmOut] = useState(""); const [kmIn, setKmIn] = useState(""); const [fuel, setFuel] = useState(""); const [toll, setToll] = useState(""); const [parking, setParking] = useState("");
  const [checks, setChecks] = useState<Record<string, boolean>>({}); const [damage, setDamage] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState("");
  const key = trip ? trip.bookingId : null;   // seed when another trip opens, not after each save (typing would be lost)
  if (key !== seed && trip) {
    setSeed(key); setErrs({}); setErr("");
    setDest(trip.destination ?? ""); setPurpose(trip.purpose ?? ""); setNeedsDriver(trip.needsDriver); setDriverId(trip.driverId ?? "");
    setKmOut(trip.lastOdometer ? String(trip.lastOdometer) : ""); setKmIn(""); setFuel(""); setToll(""); setParking(""); setChecks({}); setDamage("");
  }
  const money = (n: number | null) => (n === null ? "—" : fmtMoney(n, currency));
  async function act(fn: () => Promise<TripItem>, ok: string) {
    setErr(""); setErrs({});
    const r = await run(fn, ok, { silent: false });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    setTick((n) => n + 1); onDone();
  }
  const num = (s: string) => (s.trim() === "" ? null : Number(s));
  return (
    <Overlay open={!!bookingId} onClose={onClose} side="right" title={t("Trip")} description={trip ? `${trip.resourceName} · ${timeRange(trip)}` : undefined} footer={<Button onClick={onClose}>{t("Close")}</Button>}>
      {!trip ? <p>{t("Loading…")}</p> : (
        <div className="stack">
          {err && <div className="form-alert err" role="alert">{err}</div>}
          {trip.returnedAt ? (
            <section className="stack" aria-label={t("Trip summary")} data-testid="trip-summary">
              <p style={{ margin: 0 }}><Badge tone="green">{t("Returned")}</Badge> {fmtDateTime(trip.returnedAt)}</p>
              <dl className="props">
                <dt>{t("Driver")}</dt><dd>{trip.driver ?? "—"}</dd>
                <dt>{t("Destination")}</dt><dd>{trip.destination ?? "—"}</dd>
                <dt>{t("Distance")}</dt><dd>{trip.distance === null ? "—" : t("{n} km", { n: trip.distance.toLocaleString() })}</dd>
                <dt>{t("Fuel")}</dt><dd>{money(trip.fuelCost)}</dd><dt>{t("Tolls")}</dt><dd>{money(trip.tollCost)}</dd><dt>{t("Parking")}</dt><dd>{money(trip.parkingCost)}</dd>
                <dt>{t("Total")}</dt><dd><b>{money(trip.cost)}</b></dd>
              </dl>
              <ul className="stack" style={{ listStyle: "none", padding: 0, margin: 0 }}>{trip.checklist.map((c) => <li key={c.item}>{c.ok ? "✓" : "✗"} {checkLabel(c.item)}</li>)}</ul>
              {trip.damageNotes && <div className="form-alert err" role="note">{t("Damage")}: {trip.damageNotes}</div>}
            </section>
          ) : (
            <>
              <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
                <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Where and who drives")}</legend>
                <div className="stack">
                  <Field label={t("Destination")} htmlFor="tr-dest" error={errs.destination}><TextInput id="tr-dest" maxLength={200} value={dest} onChange={(e) => setDest(e.target.value)} disabled={!trip.canEdit} /></Field>
                  <Field label={t("Purpose")} htmlFor="tr-purpose" error={errs.purpose}><TextInput id="tr-purpose" maxLength={300} value={purpose} onChange={(e) => setPurpose(e.target.value)} disabled={!trip.canEdit} /></Field>
                  {trip.canAssignDriver ? (
                    <Field label={t("Driver")} htmlFor="tr-driver" error={errs.driverId}>
                      <SelectInput id="tr-driver" value={driverId} onChange={(e) => setDriverId(e.target.value)}
                        options={[{ value: "", label: trip.needsDriver ? t("Asked for a driver: choose one") : t("The person it is booked for") }, ...(drivers.data ?? []).map((d) => ({ value: d.userId, label: `${d.name}${d.class ? ` · ${d.class}` : ""}${d.state === "expired" ? ` (${t("licence expired")})` : d.state === "soon" ? ` (${t("licence expires soon")})` : ""}` }))]} />
                    </Field>
                  ) : trip.canEdit ? (
                    <label className="row" style={{ gap: 8 }}><Checkbox checked={needsDriver} onChange={(e) => setNeedsDriver(e.target.checked)} /> {t("I need a driver from General Affairs")}</label>
                  ) : null}
                  {!trip.canAssignDriver && trip.driver && <p className="field-help" style={{ margin: 0 }}>{t("Driver")}: <b>{trip.driver}</b>{trip.driverLicence ? ` · ${trip.driverLicence}` : ""}</p>}
                  {trip.canEdit && <div><Button onClick={() => act(() => put<TripItem>(`/api/bookings/${trip.bookingId}/trip`, { destination: dest || null, purpose: purpose || null, needsDriver, driverId: trip.canAssignDriver ? (driverId || null) : undefined, version: trip.version }), t("Trip saved."))}>{t("Save trip")}</Button></div>}
                </div>
              </fieldset>
              {trip.pickedUpAt ? (
                <p style={{ margin: 0 }}><Badge tone="blue">{t("On the road")}</Badge> {t("Picked up {when} at {km} km", { when: fmtDateTime(trip.pickedUpAt), km: (trip.odometerOut ?? 0).toLocaleString() })}</p>
              ) : trip.canPickUp ? (
                <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
                  <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Pick up")}</legend>
                  <div className="row" style={{ gap: 12, alignItems: "flex-end", flexWrap: "wrap" }}>
                    <Field label={t("Odometer (km)")} htmlFor="tr-out" error={errs.odometerOut}><TextInput id="tr-out" type="number" min={trip.lastOdometer ?? 0} value={kmOut} onChange={(e) => setKmOut(e.target.value)} /></Field>
                    <Button variant="primary" onClick={() => act(() => post<TripItem>(`/api/bookings/${trip.bookingId}/trip/pick-up`, { odometerOut: num(kmOut) }), t("Picked up. Drive safely."))}>{t("Pick up the car")}</Button>
                  </div>
                  {trip.lastOdometer !== null && <p className="field-help">{t("It read {km} km when it last came back.", { km: trip.lastOdometer.toLocaleString() })}</p>}
                </fieldset>
              ) : <p className="field-help" style={{ margin: 0 }}>{t("The car can be picked up from 30 minutes before the booking.")}</p>}
              {trip.canReturn && (
                <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
                  <legend style={{ fontWeight: 700, padding: 0, marginBottom: 8 }}>{t("Bring it back")}</legend>
                  <div className="stack">
                    <Field label={t("Odometer (km)")} htmlFor="tr-in" required error={errs.odometerIn}><TextInput id="tr-in" type="number" min={trip.odometerOut ?? 0} value={kmIn} onChange={(e) => setKmIn(e.target.value)} /></Field>
                    <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
                      <Field label={t("Fuel")} htmlFor="tr-fuel" error={errs.fuelCost}><TextInput id="tr-fuel" type="number" min={0} value={fuel} onChange={(e) => setFuel(e.target.value)} /></Field>
                      <Field label={t("Tolls")} htmlFor="tr-toll" error={errs.tollCost}><TextInput id="tr-toll" type="number" min={0} value={toll} onChange={(e) => setToll(e.target.value)} /></Field>
                      <Field label={t("Parking")} htmlFor="tr-park" error={errs.parkingCost}><TextInput id="tr-park" type="number" min={0} value={parking} onChange={(e) => setParking(e.target.value)} /></Field>
                    </div>
                    <div role="group" aria-label={t("Return checklist")} className="stack" style={{ gap: 6 }}>
                      <b>{t("Return checklist")}</b>
                      {errs.checklist && <p className="field-error" role="alert">{errs.checklist}</p>}
                      {CHECKLIST.map((c) => <label key={c} className="row" style={{ gap: 8 }}><Checkbox checked={!!checks[c]} onChange={(e) => setChecks((x) => ({ ...x, [c]: e.target.checked }))} /> {checkLabel(c)}</label>)}
                    </div>
                    <Field label={t("Damage or problems")} htmlFor="tr-damage" error={errs.damageNotes}><textarea id="tr-damage" className="textarea" rows={2} maxLength={2000} value={damage} onChange={(e) => setDamage(e.target.value)} /></Field>
                    <div><Button variant="primary" onClick={() => act(() => post<TripItem>(`/api/bookings/${trip.bookingId}/trip/return`, {
                      odometerIn: num(kmIn), fuelCost: num(fuel), tollCost: num(toll), parkingCost: num(parking), damageNotes: damage || null, checklist: Object.fromEntries(CHECKLIST.map((c) => [c, !!checks[c]])),
                    }), t("Returned. Thank you."))}>{t("Return the car")}</Button></div>
                  </div>
                </fieldset>
              )}
            </>
          )}
        </div>
      )}
    </Overlay>
  );
}
