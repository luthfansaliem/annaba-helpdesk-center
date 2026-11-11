"use client";

import Link from "next/link";
import {
  AlarmClock, BookOpen, Boxes, CalendarCheck, CalendarClock, CheckCircle2, CircleDollarSign, Eye, FileClock, FileText, Gauge, Hammer, LifeBuoy, MapPinOff,
  PauseCircle, ShieldAlert, Star, ThumbsUp, Timer, TriangleAlert, Truck, Wrench,
} from "lucide-react";
import { KpiCard, KpiGrid } from "./kpi";
import { Bars, ChartCard, ChartEmpty, ColumnChart, Donut, PALETTE } from "./charts";
import { ModuleEmpty } from "./module-empty";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/states";
import { woStatusLabel } from "@/components/workorders/wo-bits";
import { assetStatusLabel } from "@/components/assets/asset-bits";
import { get } from "@/lib/api/client";
import type { AssetStatus, AssetSummary, KbSummary, MaintenanceSummary, VendorSummary, WorkOrderStatus, WorkOrderSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate, fmtMoney, fmtMoneyShort } from "@/lib/format";
import { formatLocale, t } from "@/i18n";

function Loading() {
  return <div className="kpis">{[0, 1, 2, 3, 4].map((i) => <Skeleton key={i} h={86} />)}</div>;
}

const shortDay = (iso: string) => new Date(`${iso}T00:00:00`).toLocaleDateString(formatLocale(), { day: "2-digit", month: "short" });
const shortMonth = (ym: string) => new Date(`${ym}-01T00:00:00`).toLocaleDateString(formatLocale(), { month: "short" });
const pct = (a: number, b: number) => (b === 0 ? "—" : `${Math.round((100 * a) / b)}%`);
const hours = (h: number | null) => (h === null ? "—" : h < 48 ? t("{n} h", { n: Math.round(h) }) : t("{n} days", { n: Math.round(h / 24) }));

/** The organisation's currency for money tiles (falls back to Rupiah when the setting is not readable). */
function useCurrency() {
  const q = useQuery(() => get<{ currency: string }>("/api/costs/settings").catch(() => ({ currency: "IDR" })), []);
  return q.data?.currency ?? "IDR";
}

// ================================================================ work orders
export function WorkOrderInsights() {
  const { user } = useAuth();
  const q = useQuery(() => get<WorkOrderSummary>("/api/work-orders/summary"), []);
  const s = q.data;
  if (!s) return <Loading />;
  const statusColors: Record<string, string> = { open: "#94a3b8", in_progress: "#14b8a6", on_hold: "#f4694f" };
  const finished = s.onTime30 + s.late30;
  return (
    <>
      <KpiGrid label={t("Work orders at a glance")}>
        <KpiCard icon={<Wrench size={18} />} label={t("Not finished")} value={String(s.open)} note={user?.vendorName ? t("for your company") : t("{a} in-house · {b} by vendors", { a: s.inHouseOpen, b: s.vendorOpen })} />
        <KpiCard icon={<TriangleAlert size={18} />} label={t("Overdue")} value={String(s.overdue)} note={t("past their due time")} tone={s.overdue > 0 ? "bad" : "good"} />
        <KpiCard icon={<AlarmClock size={18} />} label={t("Due this week")} value={String(s.dueThisWeek)} note={t("in the next 7 days")} tone={s.dueThisWeek > 0 ? "warn" : "default"} />
        <KpiCard icon={<CheckCircle2 size={18} />} label={t("Done in 30 days")} value={String(s.done30)} note={finished ? t("{p} on time", { p: pct(s.onTime30, finished) }) : t("none had a due time")} tone="good" />
        <KpiCard icon={<Timer size={18} />} label={t("Average time to complete")} value={hours(s.avgCompletionHours)} note={t("from creation, last 90 days")} />
      </KpiGrid>
      {(s.open > 0 || s.weekly.some((w) => w.created + w.done > 0)) && (
        <div className="chart-grid wide-first">
          <ChartCard title={t("Created and done per week")} sub={t("Last 12 weeks")}>
            <ColumnChart labels={s.weekly.map((w) => shortDay(w.week))} series={[{ name: t("Created"), color: PALETTE[0], data: s.weekly.map((w) => w.created) }, { name: t("Done"), color: PALETTE[5], data: s.weekly.map((w) => w.done) }]} label={t("Work orders created and done per week")} />
          </ChartCard>
          <ChartCard title={t("Open work by status")} sub={user?.vendorName ? undefined : t("And the vendors with the most open work")}>
            <Donut segments={s.byStatus.map((x) => ({ label: woStatusLabel(x.key as WorkOrderStatus), value: x.count, color: statusColors[x.key] ?? "#94a3b8" }))} center={t("Open")} label={t("Open work orders by status")} />
            {!user?.vendorName && s.byVendor.length > 0 && <div style={{ marginTop: 14 }}><Bars items={s.byVendor.map((v) => ({ label: v.label, value: v.count }))} color={PALETTE[3]} /></div>}
          </ChartCard>
        </div>
      )}
    </>
  );
}

// ================================================================ assets
export function AssetInsights({ onAdd }: { onAdd?: () => void }) {
  const { can } = useAuth();
  const q = useQuery(() => get<AssetSummary>("/api/assets/summary"), []);
  const currency = useCurrency();
  const s = q.data;
  if (!s) return <Loading />;
  if (s.active + s.retired === 0) {
    return (
      <div className="card" style={{ marginBottom: 12 }}>
        <ModuleEmpty icon={<Boxes size={26} />} title={t("Start your asset register")} text={t("Record the things General Affairs looks after (air conditioners, vehicles, generators, furniture) so requests, maintenance and costs can point at them.")}>
          {onAdd && can("assets.manage") && <Button variant="primary" onClick={onAdd}>{t("Add the first asset")}</Button>}
          <Button href="/assets/import">{t("Import from CSV")}</Button>
          <Button href="/setup/asset-types">{t("Set up asset types")}</Button>
        </ModuleEmpty>
      </div>
    );
  }
  const statusColors: Record<string, string> = { in_use: PALETTE[5], in_repair: PALETTE[4], stored: PALETTE[6], retired: "#cbd5e1" };
  return (
    <>
      <KpiGrid label={t("Assets at a glance")}>
        <KpiCard icon={<Boxes size={18} />} label={t("In service")} value={String(s.active)} note={t("{n} retired", { n: s.retired })} />
        <KpiCard icon={<Hammer size={18} />} label={t("In repair")} value={String(s.byStatus.find((x) => x.key === "in_repair")?.count ?? 0)} note={t("not available right now")} tone={(s.byStatus.find((x) => x.key === "in_repair")?.count ?? 0) > 0 ? "warn" : "default"} />
        <KpiCard icon={<ShieldAlert size={18} />} label={t("Warranty ending")} value={String(s.warranty30)} note={t("within 30 days · {n} already ended", { n: s.warrantyExpired })} tone={s.warranty30 > 0 ? "warn" : "default"} href="/assets?warranty=30" />
        <KpiCard icon={<CalendarClock size={18} />} label={t("Maintenance due")} value={String(s.pmDue30)} note={t("plans due in 30 days")} href="/maintenance" />
        <KpiCard icon={<CircleDollarSign size={18} />} label={t("Purchase value")} value={s.value === null ? "—" : fmtMoneyShort(s.value, currency)} note={t("of the assets in service")} />
        {s.unplaced > 0 && <KpiCard icon={<MapPinOff size={18} />} label={t("Without a place")} value={String(s.unplaced)} note={t("give them a location")} tone="warn" />}
      </KpiGrid>
      <div className="chart-grid three">
        <ChartCard title={t("By status")}><Donut segments={s.byStatus.map((x) => ({ label: assetStatusLabel(x.key as AssetStatus), value: x.count, color: statusColors[x.key] ?? "#94a3b8" }))} center={t("Assets")} label={t("Assets by status")} /></ChartCard>
        <ChartCard title={t("By type")} sub={t("In service")}><Bars items={s.byType.map((x) => ({ label: x.label, value: x.count }))} /></ChartCard>
        <ChartCard title={t("Most requests")} sub={t("Assets with the most requests in 12 months")}>
          {s.mostTickets.length === 0 ? <ChartEmpty text={t("No requests about assets yet.")} /> : <Bars items={s.mostTickets.map((a) => ({ label: `${a.tag} · ${a.name}`, value: a.tickets, href: `/assets/${a.id}` }))} color={PALETTE[4]} />}
        </ChartCard>
      </div>
      {s.bySite.length > 1 && <div className="chart-grid"><ChartCard title={t("By site")} sub={t("In service")}><Bars items={s.bySite.map((x) => ({ label: x.label, value: x.count }))} color={PALETTE[1]} /></ChartCard></div>}
    </>
  );
}

// ================================================================ vendors
export function VendorInsights({ onAdd }: { onAdd?: () => void }) {
  const { can } = useAuth();
  const q = useQuery(() => get<VendorSummary>("/api/vendors/summary"), []);
  const currency = useCurrency();
  const s = q.data;
  if (!s) return <Loading />;
  if (s.active + s.blocked === 0) {
    return (
      <div className="card" style={{ marginBottom: 12 }}>
        <ModuleEmpty icon={<Truck size={26} />} title={t("Add the vendors you work with")} text={t("Contractors and suppliers, their contracts and documents. Give them work orders and see who delivers on time.")}>
          {onAdd && can("vendors.manage") && <Button variant="primary" onClick={onAdd}>{t("Add a vendor")}</Button>}
        </ModuleEmpty>
      </div>
    );
  }
  return (
    <>
      <KpiGrid label={t("Vendors at a glance")}>
        <KpiCard icon={<Truck size={18} />} label={t("Active vendors")} value={String(s.active)} note={s.blocked ? t("{n} blocked", { n: s.blocked }) : t("none blocked")} />
        <KpiCard icon={<FileText size={18} />} label={t("Contracts in force")} value={String(s.contractsActive)} note={t("{n} end within 60 days", { n: s.contractsExpiring60 })} tone={s.contractsExpiring60 > 0 ? "warn" : "default"} />
        <KpiCard icon={<Wrench size={18} />} label={t("Open vendor work")} value={String(s.openWorkOrders)} note={t("work orders not finished")} href="/work-orders" />
        <KpiCard icon={<TriangleAlert size={18} />} label={t("Late vendor work")} value={String(s.overdueWorkOrders)} note={t("past their due time")} tone={s.overdueWorkOrders > 0 ? "bad" : "good"} />
        {s.spend12Months !== null && <KpiCard icon={<CircleDollarSign size={18} />} label={t("Spend, 12 months")} value={fmtMoneyShort(s.spend12Months, currency)} note={t("costs on vendor work orders")} />}
      </KpiGrid>
      <div className="chart-grid">
        <ChartCard title={t("Vendor performance")} sub={t("The vendors with the most work: on-time record and rating")}>
          {s.top.length === 0 ? <ChartEmpty text={t("No vendor has had a work order yet.")} /> : (
            <div className="tbl-wrap">
              <table className="tbl" aria-label={t("Vendor performance")}>
                <thead><tr><th>{t("Vendor")}</th><th>{t("Work orders")}</th><th>{t("Open")}</th><th>{t("On time")}</th><th>{t("Rating")}</th>{s.spend12Months !== null && <th>{t("Spend, 12 months")}</th>}</tr></thead>
                <tbody>{s.top.map((v) => (
                  <tr key={v.id}>
                    <td><Link href={`/vendors/${v.id}`}><b>{v.name}</b></Link></td>
                    <td>{v.workOrders}</td><td>{v.open}</td>
                    <td>{v.onTime + v.late === 0 ? "—" : <span className="row" style={{ gap: 8, flexWrap: "nowrap" }}><span className="bar" style={{ width: 70, flex: "none" }}><i style={{ width: `${(100 * v.onTime) / (v.onTime + v.late)}%`, background: PALETTE[5] }} /></span>{pct(v.onTime, v.onTime + v.late)}</span>}</td>
                    <td>{v.rating === null ? "—" : <span className="nowrap"><Star size={13} style={{ verticalAlign: -2, color: "#f59e0b" }} aria-hidden /> {v.rating.toFixed(1)} <small className="muted">({v.ratings})</small></span>}</td>
                    {s.spend12Months !== null && <td className="nowrap">{fmtMoney(v.spend ?? 0, currency)}</td>}
                  </tr>
                ))}</tbody>
              </table>
            </div>
          )}
        </ChartCard>
      </div>
    </>
  );
}

// ================================================================ maintenance
export function MaintenanceInsights({ onAdd }: { onAdd?: () => void }) {
  const { can } = useAuth();
  const q = useQuery(() => get<MaintenanceSummary>("/api/maintenance/summary"), []);
  const s = q.data;
  if (!s) return <Loading />;
  if (s.active + s.paused === 0) {
    return (
      <div className="card" style={{ marginBottom: 12 }}>
        <ModuleEmpty icon={<CalendarCheck size={26} />} title={t("Plan the routine work")} text={t("Air-conditioner servicing, generator checks, pest control: a plan creates the request and work order on time, every time, and tracks whether it was done.")}>
          {onAdd && can("pm.manage") && <Button variant="primary" onClick={onAdd}>{t("Create the first plan")}</Button>}
        </ModuleEmpty>
      </div>
    );
  }
  const L = s.last90;
  return (
    <>
      <KpiGrid label={t("Maintenance at a glance")}>
        <KpiCard icon={<CalendarCheck size={18} />} label={t("Active plans")} value={String(s.active)} note={s.paused ? t("{n} paused", { n: s.paused }) : t("none paused")} />
        <KpiCard icon={<AlarmClock size={18} />} label={t("Due in 7 days")} value={String(s.due7)} note={t("{n} in 30 days", { n: s.due30 })} tone={s.due7 > 0 ? "warn" : "default"} />
        <KpiCard icon={<TriangleAlert size={18} />} label={t("Overdue visits")} value={String(s.overdueVisits)} note={t("work not finished after its date")} tone={s.overdueVisits > 0 ? "bad" : "good"} />
        <KpiCard icon={<Gauge size={18} />} label={t("Compliance")} value={L.percent === null ? "—" : `${L.percent}%`} note={t("done on time, last 90 days")} tone={L.percent === null ? "default" : L.percent >= 90 ? "good" : L.percent >= 70 ? "warn" : "bad"} href="/maintenance/compliance" />
        <KpiCard icon={<PauseCircle size={18} />} label={t("Missed")} value={String(L.missed)} note={t("visits skipped, last 90 days")} tone={L.missed > 0 ? "warn" : "default"} />
      </KpiGrid>
      <div className="chart-grid wide-first">
        <ChartCard title={t("Visits per month")} sub={t("On time, late, still open and missed (last 6 months)")} action={<Link className="link-btn" href="/maintenance/compliance">{t("Compliance report")}</Link>}>
          <ColumnChart stacked labels={s.monthly.map((m) => shortMonth(m.key))} label={t("Maintenance visits per month")}
            series={[{ name: t("On time"), color: PALETTE[5], data: s.monthly.map((m) => m.onTime) }, { name: t("Late"), color: PALETTE[2], data: s.monthly.map((m) => m.late) }, { name: t("Open"), color: PALETTE[0], data: s.monthly.map((m) => m.open + m.overdue) }, { name: t("Missed"), color: PALETTE[4], data: s.monthly.map((m) => m.missed) }]} />
        </ChartCard>
        <ChartCard title={t("Coming up")} sub={t("The next plans due")}>
          {s.upcoming.length === 0 ? <ChartEmpty text={t("Nothing scheduled.")} /> : (
            <ul className="req-list">{s.upcoming.map((p) => <li key={p.id}><div><Link href={`/maintenance/${p.id}`}><b>{p.name}</b></Link></div><span className="pill blue">{fmtDate(p.nextDueOn)}</span></li>)}</ul>
          )}
        </ChartCard>
      </div>
    </>
  );
}

// ================================================================ knowledge base (for the people who write it)
export function KbInsights() {
  const q = useQuery(() => get<KbSummary>("/api/kb/summary"), []);
  const s = q.data;
  if (!s) return <Loading />;
  const votes = s.helpful + s.notHelpful;
  return (
    <>
      <KpiGrid label={t("Knowledge base at a glance")}>
        <KpiCard icon={<BookOpen size={18} />} label={t("Published")} value={String(s.published)} note={t("{n} drafts", { n: s.drafts })} />
        <KpiCard icon={<Eye size={18} />} label={t("Reads")} value={s.views.toLocaleString(formatLocale())} note={t("all published articles")} />
        <KpiCard icon={<LifeBuoy size={18} />} label={t("Requests avoided")} value={String(s.deflections)} note={t("“this solved my problem”")} tone={s.deflections > 0 ? "good" : "default"} />
        <KpiCard icon={<ThumbsUp size={18} />} label={t("Found it helpful")} value={votes ? pct(s.helpful, votes) : "—"} note={t("{n} votes", { n: votes })} />
        <KpiCard icon={<FileClock size={18} />} label={t("Due for review")} value={String(s.reviewDue)} note={t("past their review date")} tone={s.reviewDue > 0 ? "warn" : "default"} />
      </KpiGrid>
      {s.published > 0 && (
        <div className="chart-grid">
          <ChartCard title={t("Most read")}>
            <Bars items={s.top.map((a) => ({ label: a.title, value: a.views, href: `/kb/${a.id}` }))} empty={t("No reads yet.")} />
          </ChartCard>
          <ChartCard title={t("Articles per category")}>
            <Bars items={s.byCategory.map((c) => ({ label: c.label || t("No category"), value: c.count }))} color={PALETTE[1]} />
          </ChartCard>
        </div>
      )}
    </>
  );
}

