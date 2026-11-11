"use client";

import { useState } from "react";
import { CalendarClock, Car, CircleDollarSign, Gauge, UserX } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Field, SelectInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { LONG_PERIODS, PeriodSelect } from "@/components/reports/parts";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Bars, ChartCard, PALETTE } from "@/components/insights/charts";
import { KindIcon, kindLabel } from "@/components/bookings/booking-bits";
import { get } from "@/lib/api/client";
import type { ResourceKind, UtilisationReport, UtilisationRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtMoney, fmtMoneyShort } from "@/lib/format";
import { t } from "@/i18n";

const pct = (p: number | null) => (p === null ? "—" : `${p}%`);
const hours = (h: number) => t("{n} h", { n: Math.round(h * 10) / 10 });

/** RPT-005 (rooms and vehicles). How much of their opening hours the resources were booked, the no-shows and cancellations, and for vehicles the kilometres and trip costs. */
function Report() {
  const [days, setDays] = useState(30); const [kind, setKind] = useState("");
  const q = useQuery(() => get<UtilisationReport>("/api/reports/utilisation", { days, kind: kind || undefined }), [days, kind]);
  const currency = useQuery(() => get<{ currency: string }>("/api/costs/settings").catch(() => ({ currency: "IDR" })), []).data?.currency ?? "IDR";
  const cols: Column<UtilisationRow>[] = [
    { key: "n", header: t("Resource"), cell: (r) => <><b>{r.name}</b><br /><small className="muted"><KindIcon kind={r.kind as ResourceKind} size={12} /> {kindLabel(r.kind as ResourceKind)}{r.place ? ` · ${r.place}` : ""}</small></> },
    { key: "u", header: t("Used"), cell: (r) => <span className="nowrap"><b>{pct(r.percent)}</b> <small className="muted">{hours(r.bookedHours)} / {hours(r.openHours)}</small></span> },
    { key: "b", header: t("Bookings"), cell: (r) => r.bookings },
    { key: "x", header: t("No-shows"), className: "hide-sm", cell: (r) => r.noShows },
    { key: "c", header: t("Cancelled"), className: "hide-sm", cell: (r) => r.cancelled },
    { key: "k", header: t("Km"), className: "hide-sm", cell: (r) => (r.kind === "vehicle" ? r.km.toLocaleString() : "—") },
    { key: "m", header: t("Trip costs"), className: "hide-sm", cell: (r) => (r.kind === "vehicle" ? fmtMoney(r.tripCost, currency) : "—") },
  ];
  return (
    <>
      <PageHeader title={t("Utilisation")} description={t("How much of their opening hours rooms, vehicles, parking and equipment were booked, who did not show up, and what the trips cost.")}
        actions={<PeriodSelect days={days} onChange={setDays} options={LONG_PERIODS} />} />
      <Panel>
        <div className="filters">
          <Field label={t("Kind")} htmlFor="ut-kind"><SelectInput id="ut-kind" value={kind} onChange={(e) => setKind(e.target.value)} options={[{ value: "", label: t("All kinds") }, { value: "room", label: t("Meeting room") }, { value: "vehicle", label: t("Vehicle") }, { value: "parking", label: t("Parking") }, { value: "equipment", label: t("Equipment") }]} /></Field>
        </div>
      </Panel>
      <QueryBoundary q={q}>{(r) => (
        <>
          <KpiGrid label={t("Key metrics")}>
            <KpiCard icon={<Gauge size={18} />} label={t("Used")} value={pct(r.total.percent)} note={t("{a} of {b} opening hours", { a: hours(r.total.bookedHours), b: hours(r.total.openHours) })} />
            <KpiCard icon={<CalendarClock size={18} />} label={t("Bookings")} value={String(r.total.bookings)} note={t("{n} cancelled", { n: r.total.cancelled })} />
            <KpiCard icon={<UserX size={18} />} label={t("No-shows")} value={String(r.total.noShows)} note={t("released, nobody checked in")} tone={r.total.noShows > 0 ? "warn" : "default"} />
            {(kind === "" || kind === "vehicle") && <KpiCard icon={<Car size={18} />} label={t("Kilometres driven")} value={r.total.km.toLocaleString()} note={t("{n} trips", { n: r.total.trips })} />}
            {(kind === "" || kind === "vehicle") && <KpiCard icon={<CircleDollarSign size={18} />} label={t("Trip costs")} value={fmtMoneyShort(r.total.tripCost, currency)} note={t("fuel, tolls and parking")} />}
          </KpiGrid>
          {r.rows.length > 0 && (
            <div className="chart-grid">
              <ChartCard title={t("Most used")} sub={t("Share of opening hours booked")}>
                <Bars max={100} format={(n) => `${n}%`} items={[...r.rows].filter((x) => x.percent !== null).sort((a, b) => (b.percent ?? 0) - (a.percent ?? 0)).slice(0, 10).map((x) => ({ label: x.name, value: x.percent ?? 0 }))} empty={t("Nothing booked in this period.")} />
              </ChartCard>
              <ChartCard title={t("Least used")} sub={t("Candidates to give up or share")}>
                <Bars max={100} format={(n) => `${n}%`} color={PALETTE[4]} items={[...r.rows].filter((x) => x.percent !== null).sort((a, b) => (a.percent ?? 0) - (b.percent ?? 0)).slice(0, 5).map((x) => ({ label: x.name, value: x.percent ?? 0 }))} empty={t("Nothing was booked in this period, so everything is unused.")} />
              </ChartCard>
            </div>
          )}
          <Panel title={t("By resource")}>
            <DataTable label={t("Utilisation by resource")} columns={cols} rows={r.rows} rowKey={(x) => x.resourceId} emptyMessage={t("No resources yet.")} />
          </Panel>
        </>
      )}</QueryBoundary>
    </>
  );
}

export default function UtilisationPage() { return <Require perm="bookings.read_all"><Report /></Require>; }
