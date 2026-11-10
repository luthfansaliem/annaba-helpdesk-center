"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { Kpi } from "@/components/reports/parts";
import { get } from "@/lib/api/client";
import type { CostGroup, CostReport, CostRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const fmtMoney = (n: number) => n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const iso = (d: Date) => d.toISOString().slice(0, 10);

function Report() {
  const now = new Date();
  const [from, setFrom] = useState(iso(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 11, 1)))); const [to, setTo] = useState(iso(now)); const [by, setBy] = useState<CostGroup>("month");
  const q = useQuery(() => get<CostReport>("/api/costs/report", { from, to, by }), [from, to, by]);
  const groups: [CostGroup, string][] = [["month", t("Month")], ["site", t("Site")], ["cost_center", t("Cost centre")], ["category", t("Category")], ["service", t("Service")], ["department", t("Department")], ["vendor", t("Vendor")], ["asset", t("Asset")]];
  const label = groups.find((g) => g[0] === by)?.[1] ?? "";
  const cols: Column<CostRow>[] = [
    { key: "l", header: label, cell: (r) => <b>{r.label}</b> },
    { key: "p", header: t("Parts"), className: "hide-sm", cell: (r) => fmtMoney(r.part) },
    { key: "b", header: t("Labour"), className: "hide-sm", cell: (r) => fmtMoney(r.labour + r.time) },
    { key: "s", header: t("Services"), className: "hide-sm", cell: (r) => fmtMoney(r.service) },
    { key: "i", header: t("Vendor invoices"), className: "hide-sm", cell: (r) => fmtMoney(r.invoice) },
    { key: "o", header: t("Other"), className: "hide-sm", cell: (r) => fmtMoney(r.other) },
    { key: "t", header: t("Total"), cell: (r) => <b>{fmtMoney(r.total)}</b> },
    { key: "w", header: t("Work orders"), cell: (r) => r.workOrders },
  ];
  const csv = `/api/costs/report.csv?${new URLSearchParams({ from, to, by }).toString()}`;
  return (
    <>
      <PageHeader title={t("Cost report")} description={t("What the work cost: parts, labour, services and vendor invoices on work orders, by month, place, cost centre, category, department, vendor or asset.")}
        actions={<a className="btn" href={csv} download><Download size={16} /> {t("Export CSV")}</a>} />
      <Panel>
        <div className="row" style={{ gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}>
          <Field label={t("From")} htmlFor="cr-from"><TextInput id="cr-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field label={t("To")} htmlFor="cr-to"><TextInput id="cr-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
          <Field label={t("Group by")} htmlFor="cr-by"><SelectInput id="cr-by" value={by} onChange={(e) => setBy(e.target.value as CostGroup)} options={groups.map(([v, l]) => ({ value: v, label: l }))} /></Field>
        </div>
      </Panel>
      <QueryBoundary q={q}>{(r) => (
        <>
          <section className="stats" aria-label={t("Key metrics")} data-testid="cr-kpis">
            <Kpi label={t("Total cost")} value={`${fmtMoney(r.total.total)} ${r.currency}`} note={t("{n} work orders", { n: r.total.workOrders })} />
            <Kpi label={t("Labour")} value={fmtMoney(r.total.labour + r.total.time)} note={r.labourRatePerHour === null ? t("No labour rate set: logged time has no cost") : t("Logged time at {rate} per hour", { rate: fmtMoney(r.labourRatePerHour) })} />
            <Kpi label={t("Vendor invoices")} value={fmtMoney(r.total.invoice)} />
          </section>
          <Panel title={t("By {group}", { group: label.toLowerCase() })} description={t("Amounts in {currency}, rounded to two decimals per line (half away from zero).", { currency: r.currency })}>
            <DataTable label={t("Cost report")} columns={cols} rows={r.rows} rowKey={(x) => x.key} emptyMessage={t("No costs in this period.")} />
          </Panel>
        </>
      )}</QueryBoundary>
    </>
  );
}

export default function CostReportPage() { return <Require perm="cost.read"><Report /></Require>; }
