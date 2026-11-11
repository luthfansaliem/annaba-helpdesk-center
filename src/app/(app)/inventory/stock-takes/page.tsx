"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { PortalLocation, StockTake } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime } from "@/lib/format";
import { t } from "@/i18n";

function Counting({ take, onDone }: { take: StockTake; onDone: () => void }) {
  const run = useRun();
  const [counts, setCounts] = useState<Record<string, string>>(() => Object.fromEntries(take.lines.map((l) => [l.itemId, l.counted === null ? "" : String(l.counted)])));
  const save = async (close: boolean) => {
    const body = { counts: Object.fromEntries(Object.entries(counts).filter(([, v]) => v !== "").map(([k, v]) => [k, Number(v)])) };
    const r = await run(() => put<StockTake>(`/api/inventory/stock-takes/${take.id}/counts`, body), close ? "" : t("Counts saved."), { silent: close });
    if (!r.ok) return;
    if (close) { const c = await run(() => post<StockTake>(`/api/inventory/stock-takes/${take.id}/close`), t("Count closed. Differences were posted."), { silent: false }); if (!c.ok) return; }
    onDone();
  };
  return (
    <div className="stack">
      <div className="tbl-wrap">
        <table className="tbl" aria-label={t("Count of {place}", { place: take.place })}>
          <thead><tr><th scope="col">{t("Item")}</th><th scope="col">{t("Expected")}</th><th scope="col">{t("Counted")}</th><th scope="col">{t("Difference")}</th></tr></thead>
          <tbody>{take.lines.map((l) => {
            const v = counts[l.itemId]; const diff = v === "" || v === undefined ? null : Number(v) - l.expected;
            return (
              <tr key={l.itemId}>
                <td><b>{l.name}</b> <small className="muted">{l.sku}</small></td>
                <td>{l.expected} {l.unit}</td>
                <td><input aria-label={t("Counted {name}", { name: l.name })} className="input" style={{ width: 100 }} type="number" min={0} value={v ?? ""} onChange={(e) => setCounts((c) => ({ ...c, [l.itemId]: e.target.value }))} /></td>
                <td>{diff === null ? "—" : diff === 0 ? "0" : <b style={{ color: diff < 0 ? "var(--red-ink)" : "var(--green-ink)" }}>{diff > 0 ? `+${diff}` : diff}</b>}</td>
              </tr>
            );
          })}</tbody>
        </table>
      </div>
      <div className="row" style={{ gap: 8 }}>
        <Button onClick={() => save(false)}>{t("Save counts")}</Button>
        <Button variant="primary" onClick={() => save(true)}>{t("Close and post differences")}</Button>
      </div>
    </div>
  );
}

/** INV-004. Cycle counts: start one for a place, count what is on the shelves, and close it to post every difference as a stock-take movement. */
function Takes() {
  const run = useRun();
  const { can } = useAuth();
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const list = useQuery(() => get<StockTake[]>("/api/inventory/stock-takes"), [tick]);
  const [place, setPlace] = useState(""); const [note, setNote] = useState(""); const [err, setErr] = useState("");
  const start = async () => {
    setErr("");
    const r = await run(() => post<StockTake>("/api/inventory/stock-takes", { locationId: place || null, note: note || null }), t("Count started."), { silent: true });
    if (!r.ok) { setErr(r.fields?.locationId ?? r.error); return; }
    setNote(""); reload();
  };
  const takes = list.data ?? [];
  return (
    <>
      <PageHeader title={t("Stock-takes")} description={t("Count what is really on the shelves. Closing a count posts every difference to the stock ledger.")} />
      {can("inventory.adjust") && (
        <Panel title={t("Start a count")}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <div className="filters" style={{ alignItems: "flex-end" }}>
            <Field label={t("Place")} htmlFor="st-place"><SelectInput id="st-place" value={place} onChange={(e) => setPlace(e.target.value)} options={[{ value: "", label: t("Choose…") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
            <Field label={t("Note")} htmlFor="st-note"><TextInput id="st-note" maxLength={300} placeholder={t("e.g. Month end")} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
            <Button variant="primary" onClick={start}>{t("Start count")}</Button>
          </div>
        </Panel>
      )}
      {takes.length === 0 ? <Panel><p className="tab-placeholder" style={{ margin: 0 }}>{t("No counts yet.")}</p></Panel> : takes.map((take) => (
        <Panel key={take.id} title={`${take.place} · ${fmtDateTime(take.startedAt)}`} description={[take.note, take.startedBy].filter(Boolean).join(" · ") || undefined}>
          <p style={{ margin: "0 0 10px" }}>{take.closedAt ? <Badge tone="gray">{t("Closed {when}", { when: fmtDateTime(take.closedAt) })}</Badge> : <Badge tone="blue">{t("Open")}</Badge>} {t("{n} of {m} counted, {v} differences", { n: take.counted, m: take.lines.length, v: take.variances })}</p>
          {!take.closedAt && can("inventory.adjust") ? <Counting key={take.id} take={take} onDone={reload} /> : (
            <ul className="req-list">{take.lines.filter((l) => l.variance).map((l) => <li key={l.itemId}><div>{l.name}</div><span>{l.expected} → {l.counted} ({(l.variance ?? 0) > 0 ? `+${l.variance}` : l.variance})</span></li>)}</ul>
          )}
        </Panel>
      ))}
    </>
  );
}

export default function StockTakesPage() { return <Require perm="inventory.read"><Takes /></Require>; }
