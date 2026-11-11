"use client";

import { useState } from "react";
import { AlertTriangle, CalendarClock, DoorOpen, LogOut, Plus, UserCheck, UserX } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Bars, ChartCard, ColumnChart, PALETTE } from "@/components/insights/charts";
import { VisitDrawer, VisitStatusBadge, visitWhen } from "@/components/visitors/visitor-bits";
import { startOfDay, ymd } from "@/components/bookings/booking-bits";
import { get, post } from "@/lib/api/client";
import type { PagedVisits, VisitItem, VisitSummary, VisitorSettings } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { formatLocale, t } from "@/i18n";

function CheckIn({ visit, settings, onClose, onDone }: { visit: VisitItem | null; settings: VisitorSettings | undefined; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [badge, setBadge] = useState(""); const [last4, setLast4] = useState(""); const [ack, setAck] = useState(false); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState("");
  const [seed, setSeed] = useState<string | null>(null);
  if ((visit?.id ?? null) !== seed) { setSeed(visit?.id ?? null); setBadge(""); setLast4(""); setAck(false); setErrs({}); setErr(""); }
  async function go() {
    if (!visit) return;
    const r = await run(() => post<VisitItem>(`/api/visits/${visit.id}/check-in`, { badgeNumber: badge, idLast4: last4 || null, acknowledged: ack }), t("Checked in. The host is told."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={!!visit} onClose={onClose} title={t("Check in {name}", { name: visit?.visitorName ?? "" })} description={visit ? `${visit.company ?? ""}${visit.host ? ` · ${t("visiting {host}", { host: visit.host })}` : ""}` : undefined}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" onClick={go}>{t("Check in")}</Button></>}>
      <div className="stack">
        {visit?.flag && <div className="form-alert err" role="alert"><AlertTriangle size={14} /> {visit.flag}</div>}
        {err && !visit?.flag && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Badge number")} htmlFor="ci-badge" required error={errs.badgeNumber}><TextInput id="ci-badge" data-autofocus maxLength={20} value={badge} onChange={(e) => setBadge(e.target.value)} /></Field>
        {settings?.askIdLast4 && <Field label={t("Last 4 characters of their ID")} htmlFor="ci-id" required error={errs.idLast4}><TextInput id="ci-id" maxLength={4} value={last4} onChange={(e) => setLast4(e.target.value)} /></Field>}
        {settings?.acknowledgement && (
          <div className="stack" style={{ gap: 6 }}>
            <div className="card" style={{ padding: 12, whiteSpace: "pre-wrap", maxHeight: 180, overflow: "auto" }}>{settings.acknowledgement}</div>
            <label className="row" style={{ gap: 8 }}><Checkbox checked={ack} onChange={(e) => setAck(e.target.checked)} /> {t("The visitor read and accepted this")}</label>
            {errs.acknowledged && <p className="field-error" role="alert">{errs.acknowledged}</p>}
          </div>
        )}
      </div>
    </Overlay>
  );
}

/** VIS-002. The reception desk: who is expected today, who is on site, check-in with badge (and ID digits and the acknowledgement when the organisation asks), check-out, walk-ins. */
function Desk() {
  const run = useRun();
  const { can } = useAuth();
  const [day, setDay] = useState(() => startOfDay(new Date())); const [q, setQ] = useState(""); const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const [walkIn, setWalkIn] = useState(false); const [checking, setChecking] = useState<VisitItem | null>(null);
  const list = useQuery(() => get<PagedVisits>("/api/visits", { scope: "day", date: ymd(day), q: q || undefined, pageSize: 200 }), [day.getTime(), q, tick]);
  const sum = useQuery(() => get<VisitSummary>("/api/visits/summary"), [tick]);
  const settings = useQuery(() => get<VisitorSettings>("/api/visitors/settings"), []);
  const rows = list.data?.data ?? [];
  const s = sum.data;
  const checkOut = async (v: VisitItem) => { const r = await run(() => post(`/api/visits/${v.id}/check-out`), t("{name} checked out.", { name: v.visitorName }), { silent: false }); if (r.ok) reload(); };
  return (
    <>
      <PageHeader title={t("Reception")} description={t("Who is expected today and who is in the building. Check visitors in with a badge and out when they leave.")}
        actions={can("visitors.checkin") ? <Button variant="primary" onClick={() => setWalkIn(true)}><Plus size={16} /> {t("Walk-in visitor")}</Button> : undefined} />
      {s && (
        <KpiGrid label={t("Visitors at a glance")}>
          <KpiCard icon={<CalendarClock size={18} />} label={t("Expected today")} value={String(s.expectedToday)} note={t("not arrived yet")} />
          <KpiCard icon={<UserCheck size={18} />} label={t("On site")} value={String(s.onSiteNow)} note={t("checked in, not out")} tone="good" />
          <KpiCard icon={<LogOut size={18} />} label={t("Left today")} value={String(s.checkedOutToday)} note={t("checked out")} />
          <KpiCard icon={<UserX size={18} />} label={t("Did not come")} value={String(s.noShows30)} note={t("last 30 days")} />
          {s.flagged > 0 && <KpiCard icon={<AlertTriangle size={18} />} label={t("On the block list")} value={String(s.flagged)} note={t("expected visitors flagged")} tone="bad" />}
        </KpiGrid>
      )}
      <Panel>
        <div className="filters">
          <Field label={t("Date")} htmlFor="rc-day"><TextInput id="rc-day" type="date" value={ymd(day)} onChange={(e) => e.target.value && setDay(startOfDay(new Date(`${e.target.value}T00:00:00`)))} /></Field>
          <Field label={t("Search")} htmlFor="rc-q"><TextInput id="rc-q" placeholder={t("Name, company or plate")} value={q} onChange={(e) => setQ(e.target.value)} /></Field>
        </div>
        {rows.length === 0 ? <p className="tab-placeholder">{t("No visitors for this day.")}</p> : (
          <ul className="req-list" data-testid="desk-visits">
            {rows.map((v) => (
              <li key={v.id}>
                <div>
                  <b>{v.visitorName}</b>{v.company ? ` · ${v.company}` : ""}{v.flag && <> <span className="pill red" title={v.flag}><AlertTriangle size={12} /> {t("Block list")}</span></>}
                  <br /><small className="muted">{visitWhen(v)} · {t("visiting {host}", { host: v.host ?? "—" })}{v.vehiclePlate ? ` · ${v.vehiclePlate}` : ""}{v.badgeNumber ? ` · ${t("badge {n}", { n: v.badgeNumber })}` : ""}</small>
                </div>
                <span className="row" style={{ gap: 6 }}>
                  <VisitStatusBadge s={v.status} />
                  {v.canReceive && v.status === "expected" && <Button size="sm" variant="primary" onClick={() => setChecking(v)}><DoorOpen size={14} /> {t("Check in")}</Button>}
                  {v.canReceive && v.status === "checked_in" && <Button size="sm" onClick={() => checkOut(v)}>{t("Check out")}</Button>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
      {s && s.visitors30 > 0 && (
        <div className="chart-grid wide-first">
          <ChartCard title={t("Visitors per day")} sub={t("Checked in, last 30 days")}>
            <ColumnChart labels={s.perDay.map((d) => new Date(`${d.day}T00:00:00`).toLocaleDateString(formatLocale(), { day: "2-digit", month: "short" }))} series={[{ name: t("Visitors"), color: PALETTE[0], data: s.perDay.map((d) => d.bookings) }]} label={t("Visitors per day")} />
          </ChartCard>
          <ChartCard title={t("By site")} sub={t("Last 30 days")}><Bars items={s.bySite.map((x) => ({ label: x.label, value: x.count }))} color={PALETTE[1]} /></ChartCard>
        </div>
      )}
      <VisitDrawer open={walkIn} onClose={() => setWalkIn(false)} onDone={reload} editing={null} desk />
      <CheckIn visit={checking} settings={settings.data} onClose={() => setChecking(null)} onDone={reload} />
    </>
  );
}

export default function ReceptionPage() { return <Require perm="visitors.read_all"><Desk /></Require>; }
