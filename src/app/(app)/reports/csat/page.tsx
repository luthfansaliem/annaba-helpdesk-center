"use client";

import { useState } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Field, SelectInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { Kpi, PeriodSelect } from "@/components/reports/parts";
import { Stars } from "@/components/tickets/rating";
import { get } from "@/lib/api/client";
import type { CsatReport, CsatRow } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const avg = (n: number | null) => (n === null ? "—" : n.toFixed(2));

function Report() {
  const [days, setDays] = useState(30); const [by, setBy] = useState<CsatReport["by"]>("team");
  const q = useQuery(() => get<CsatReport>("/api/reports/csat", { days, by }), [days, by]);
  const label = { agent: t("Agent"), team: t("Team"), site: t("Site"), vendor: t("Vendor") }[by];
  const cols: Column<CsatRow>[] = [
    { key: "l", header: label, cell: (r) => <b>{r.label}</b> },
    { key: "c", header: t("Ratings"), cell: (r) => r.count },
    { key: "a", header: t("Average"), cell: (r) => avg(r.average) },
    { key: "s", header: t("Satisfied (4–5)"), cell: (r) => (r.satisfiedPercent === null ? "—" : `${r.satisfiedPercent}%`) },
    { key: "w", header: t("Low (1–2)"), cell: (r) => r.low },
  ];
  return (
    <>
      <PageHeader title={t("Satisfaction")} description={t("How requesters rated their resolved requests (1–5), by the person, team, site or vendor that worked them.")} />
      <Panel>
        <div className="row" style={{ gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}>
          <PeriodSelect days={days} onChange={setDays} />
          <Field label={t("Group by")} htmlFor="cs-by"><SelectInput id="cs-by" value={by} onChange={(e) => setBy(e.target.value as CsatReport["by"])} options={[{ value: "team", label: t("Team") }, { value: "agent", label: t("Agent") }, { value: "site", label: t("Site") }, { value: "vendor", label: t("Vendor") }]} /></Field>
        </div>
      </Panel>
      <QueryBoundary q={q}>{(r) => {
        const max = Math.max(1, ...r.distribution);
        return (
          <>
            {r.ownOnly && <div className="form-alert" role="status">{t("You see the ratings of requests you worked on.")}</div>}
            <section className="stats" aria-label={t("Key metrics")} data-testid="cs-kpis">
              <Kpi label={t("Average rating")} value={avg(r.average)} note={t("out of 5, from {n} ratings", { n: r.count })} tone={r.average === null ? "default" : r.average >= 4.2 ? "good" : r.average >= 3.5 ? "warn" : "bad"} />
              <Kpi label={t("Satisfied (4–5)")} value={r.satisfiedPercent === null ? "—" : `${r.satisfiedPercent}%`} />
              <Kpi label={t("Low (1–2)")} value={String(r.low)} tone={r.low > 0 ? "bad" : "default"} note={t("Team leads are alerted")} />
            </section>
            <div style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", alignItems: "start" }}>
              <Panel title={t("Spread of ratings")}>
                <table className="tbl" aria-label={t("Spread of ratings")}>
                  <thead><tr><th scope="col">{t("Rating")}</th><th scope="col">{t("Ratings")}</th><th scope="col"><span className="sr-only">{t("Share")}</span></th></tr></thead>
                  <tbody>{[5, 4, 3, 2, 1].map((s) => (
                    <tr key={s}><td><Stars score={s} /></td><td>{r.distribution[s - 1]}</td>
                      <td style={{ width: "50%" }}><div aria-hidden style={{ height: 8, borderRadius: 4, background: s >= 4 ? "#16a34a" : s === 3 ? "#d97706" : "#dc2626", width: `${(r.distribution[s - 1] / max) * 100}%` }} /></td></tr>
                  ))}</tbody>
                </table>
              </Panel>
              <Panel title={t("By {group}", { group: label.toLowerCase() })}>
                <DataTable label={t("Satisfaction by group")} columns={cols} rows={r.rows} rowKey={(x) => x.key} emptyMessage={t("No ratings in this period.")} />
              </Panel>
            </div>
            <Panel title={t("Latest comments")}>
              {r.comments.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No comments in this period.")}</p>
                : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{r.comments.map((c) => (
                  <li key={c.ticketId}><span className="row" style={{ gap: 8, flexWrap: "wrap" }}><Stars score={c.score} /> <Link href={`/tickets/${c.ticketId}`}>{c.number}</Link> <small className="sub">{c.agent ?? "—"} · {fmtDateTime(c.at)}</small></span>
                    <p style={{ margin: "4px 0 0", whiteSpace: "pre-wrap" }}>{c.comment}</p></li>))}</ul>}
            </Panel>
          </>
        );
      }}</QueryBoundary>
    </>
  );
}

export default function CsatPage() { return <Require perm="tickets.read_team"><Report /></Require>; }
