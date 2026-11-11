"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { Kpi } from "@/components/reports/parts";
import { get } from "@/lib/api/client";
import type { ComplianceReport, ComplianceRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const iso = (d: Date) => d.toISOString().slice(0, 10);

function Report() {
  const today = new Date();
  const [from, setFrom] = useState(iso(new Date(today.getTime() - 89 * 86_400_000))); const [to, setTo] = useState(iso(today)); const [by, setBy] = useState<ComplianceReport["by"]>("plan");
  const q = useQuery(() => get<ComplianceReport>("/api/maintenance/compliance", { from, to, by }), [from, to, by]);
  const pct = (r: ComplianceRow) => (r.percent === null ? "—" : `${r.percent}%`);
  const groupLabel = { plan: t("Plan"), team: t("Team"), vendor: t("Vendor"), site: t("Site") }[by];
  const cols: Column<ComplianceRow>[] = [
    { key: "l", header: groupLabel, cell: (r) => <b>{r.label}</b> },
    { key: "p", header: t("Planned"), cell: (r) => r.planned },
    { key: "o", header: t("Done on time"), cell: (r) => r.onTime },
    { key: "a", header: t("Done late"), cell: (r) => r.late },
    { key: "d", header: t("Overdue"), cell: (r) => r.overdue },
    { key: "m", header: t("Missed or cancelled"), cell: (r) => r.missed + r.cancelled },
    { key: "n", header: t("Not due yet"), className: "hide-sm", cell: (r) => r.open },
    { key: "c", header: t("Compliance"), cell: pct },
  ];
  return (
    <>
      <PageHeader title={t("Maintenance compliance")} description={t("Planned visits against the ones done by their date. Missed, cancelled and overdue visits count against it; visits not due yet do not count.")} />
      <Panel>
        <div className="filters">
          <Field label={t("From")} htmlFor="pc-from"><TextInput id="pc-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field label={t("To")} htmlFor="pc-to"><TextInput id="pc-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
          <Field label={t("Group by")} htmlFor="pc-by"><SelectInput id="pc-by" value={by} onChange={(e) => setBy(e.target.value as ComplianceReport["by"])} options={[{ value: "plan", label: t("Plan") }, { value: "team", label: t("Team") }, { value: "vendor", label: t("Vendor") }, { value: "site", label: t("Site") }]} /></Field>
        </div>
      </Panel>
      <QueryBoundary q={q}>{(r) => (
        <>
          <section className="kpis" aria-label={t("Key metrics")} data-testid="pc-kpis">
            <Kpi label={t("Compliance")} value={pct(r.total)} tone={r.total.percent === null ? "default" : r.total.percent >= 90 ? "good" : r.total.percent >= 70 ? "warn" : "bad"} note={t("{n} of {m} due visits done on time", { n: r.total.onTime, m: r.total.planned - r.total.open })} />
            <Kpi label={t("Planned")} value={String(r.total.planned)} />
            <Kpi label={t("Overdue")} value={String(r.total.overdue)} tone={r.total.overdue > 0 ? "bad" : "default"} note={t("Still open after their date")} />
            <Kpi label={t("Missed or cancelled")} value={String(r.total.missed + r.total.cancelled)} tone={r.total.missed + r.total.cancelled > 0 ? "warn" : "default"} />
          </section>
          <Panel title={t("By {group}", { group: groupLabel.toLowerCase() })}>
            <DataTable label={t("Maintenance compliance")} columns={cols} rows={r.rows} rowKey={(x) => x.key} emptyMessage={t("No planned visits in this period.")} />
          </Panel>
        </>
      )}</QueryBoundary>
    </>
  );
}

export default function CompliancePage() { return <Require perm="pm.read"><Report /></Require>; }
