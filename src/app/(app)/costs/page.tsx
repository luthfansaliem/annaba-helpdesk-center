"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { Bars, ChartCard, ColumnChart, Donut, PALETTE } from "@/components/insights/charts";
import { CircleDollarSign, Clock, Package, Receipt, Wrench } from "lucide-react";
import { fmtMoney, fmtMoneyShort } from "@/lib/format";
import { get } from "@/lib/api/client";
import type { CostGroup, CostReport, CostRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { formatLocale, t } from "@/i18n";

const iso = (d: Date) => d.toISOString().slice(0, 10);
/** Every month ("2026-03") of the report period, so a month without costs shows as an empty column rather than disappearing. */
const monthsBetween = (from: string, to: string) => {
  const out: string[] = []; let y = +from.slice(0, 4), m = +from.slice(5, 7); const ey = +to.slice(0, 4), em = +to.slice(5, 7);
  while ((y < ey || (y === ey && m <= em)) && out.length < 36) { out.push(`${y}-${String(m).padStart(2, "0")}`); m++; if (m > 12) { m = 1; y++; } }
  return out;
};

function Report() {
  const now = new Date();
  const [from, setFrom] = useState(iso(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 11, 1)))); const [to, setTo] = useState(iso(now)); const [by, setBy] = useState<CostGroup>("month");
  const q = useQuery(() => get<CostReport>("/api/costs/report", { from, to, by }), [from, to, by]);
  const groups: [CostGroup, string][] = [["month", t("Month")], ["site", t("Site")], ["cost_center", t("Cost centre")], ["category", t("Category")], ["service", t("Service")], ["department", t("Department")], ["vendor", t("Vendor")], ["asset", t("Asset")]];
  const label = groups.find((g) => g[0] === by)?.[1] ?? "";
  const currency = q.data?.currency ?? "IDR"; const money = (n: number) => fmtMoney(n, currency);
  const cols: Column<CostRow>[] = [
    { key: "l", header: label, cell: (r) => <b>{r.label}</b> },
    { key: "p", header: t("Parts"), className: "hide-sm", cell: (r) => money(r.part) },
    { key: "b", header: t("Labour"), className: "hide-sm", cell: (r) => money(r.labour + r.time) },
    { key: "s", header: t("Services"), className: "hide-sm", cell: (r) => money(r.service) },
    { key: "i", header: t("Vendor invoices"), className: "hide-sm", cell: (r) => money(r.invoice) },
    { key: "o", header: t("Other"), className: "hide-sm", cell: (r) => money(r.other) },
    { key: "t", header: t("Total"), cell: (r) => <b>{money(r.total)}</b> },
    { key: "w", header: t("Work orders"), cell: (r) => r.workOrders },
  ];
  const csv = `/api/costs/report.csv?${new URLSearchParams({ from, to, by }).toString()}`;
  return (
    <>
      <PageHeader title={t("Cost report")} description={t("What the work cost: parts, labour, services and vendor invoices on work orders, by month, place, cost centre, category, department, vendor or asset.")}
        actions={<a className="btn" href={csv} download><Download size={16} /> {t("Export CSV")}</a>} />
      <Panel>
        <div className="filters">
          <Field label={t("From")} htmlFor="cr-from"><TextInput id="cr-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field label={t("To")} htmlFor="cr-to"><TextInput id="cr-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
          <Field label={t("Group by")} htmlFor="cr-by"><SelectInput id="cr-by" value={by} onChange={(e) => setBy(e.target.value as CostGroup)} options={groups.map(([v, l]) => ({ value: v, label: l }))} /></Field>
        </div>
      </Panel>
      <QueryBoundary q={q}>{(r) => (
        <>
          <KpiGrid label={t("Key metrics")}>
            <KpiCard icon={<CircleDollarSign size={18} />} label={t("Total cost")} value={fmtMoneyShort(r.total.total, r.currency)} note={`${money(r.total.total)} · ${t("{n} work orders", { n: r.total.workOrders })}`} />
            <KpiCard icon={<Package size={18} />} label={t("Parts")} value={fmtMoneyShort(r.total.part, r.currency)} note={money(r.total.part)} />
            <KpiCard icon={<Clock size={18} />} label={t("Labour")} value={fmtMoneyShort(r.total.labour + r.total.time, r.currency)} note={r.labourRatePerHour === null ? t("No labour rate set: logged time has no cost") : t("Logged time at {rate} per hour", { rate: money(r.labourRatePerHour) })} />
            <KpiCard icon={<Wrench size={18} />} label={t("Services")} value={fmtMoneyShort(r.total.service, r.currency)} note={money(r.total.service)} />
            <KpiCard icon={<Receipt size={18} />} label={t("Vendor invoices")} value={fmtMoneyShort(r.total.invoice, r.currency)} note={money(r.total.invoice)} />
          </KpiGrid>
          {r.total.total > 0 && (
            <div className="chart-grid wide-first">
              <ChartCard title={by === "month" ? t("Cost per month") : t("Largest by {group}", { group: label.toLowerCase() })} sub={t("Parts, labour, services, vendor invoices and other costs")}>
                {by === "month"
                  ? (() => {
                      const ms = monthsBetween(r.from, r.to);
                      const v = (k: string, f: (x: CostRow) => number) => { const x = r.rows.find((y) => y.key === k); return x ? f(x) : 0; };
                      return <ColumnChart stacked format={(n) => fmtMoneyShort(n, r.currency)} label={t("Cost per month")}
                        labels={ms.map((k) => new Date(`${k}-01T00:00:00`).toLocaleDateString(formatLocale(), { month: "short", year: "2-digit" }))}
                        series={[{ name: t("Parts"), color: PALETTE[0], data: ms.map((k) => v(k, (x) => x.part)) }, { name: t("Labour"), color: PALETTE[1], data: ms.map((k) => v(k, (x) => x.labour + x.time)) }, { name: t("Services"), color: PALETTE[2], data: ms.map((k) => v(k, (x) => x.service)) }, { name: t("Vendor invoices"), color: PALETTE[3], data: ms.map((k) => v(k, (x) => x.invoice)) }, { name: t("Other"), color: PALETTE[6], data: ms.map((k) => v(k, (x) => x.other)) }]} />;
                    })()
                  : <Bars items={[...r.rows].sort((x, y) => y.total - x.total).slice(0, 8).map((x) => ({ label: x.label, value: x.total }))} format={(n) => fmtMoneyShort(n, r.currency)} />}
              </ChartCard>
              <ChartCard title={t("Where the money went")} sub={t("Share of the total by kind of cost")}>
                <Donut format={(n) => fmtMoneyShort(n, r.currency)} center={t("Total")} value={fmtMoneyShort(r.total.total, r.currency)} label={t("Cost by kind")} segments={[{ label: t("Parts"), value: r.total.part, color: PALETTE[0] }, { label: t("Labour"), value: r.total.labour + r.total.time, color: PALETTE[1] }, { label: t("Services"), value: r.total.service, color: PALETTE[2] }, { label: t("Vendor invoices"), value: r.total.invoice, color: PALETTE[3] }, { label: t("Other"), value: r.total.other, color: PALETTE[6] }]} />
              </ChartCard>
            </div>
          )}
          <Panel title={t("By {group}", { group: label.toLowerCase() })} description={t("Amounts in {currency}.", { currency: r.currency })}>
            <DataTable label={t("Cost report")} columns={cols} rows={r.rows} rowKey={(x) => x.key} emptyMessage={t("No costs in this period.")} />
          </Panel>
        </>
      )}</QueryBoundary>
    </>
  );
}

export default function CostReportPage() { return <Require perm="cost.read"><Report /></Require>; }
