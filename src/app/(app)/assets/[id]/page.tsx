"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { ArrowLeft, MessageSquareWarning, Pencil, QrCode } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { AssetDrawer, AssetStatusBadge, RetireDialog } from "@/components/assets/asset-bits";
import { TicketStatusBadge } from "@/components/tickets/status";
import { get, post } from "@/lib/api/client";
import type { AssetDetail, FormField } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const money = (n: number) => n.toLocaleString(undefined, { maximumFractionDigits: 2 });

/** How an answer reads: the choice's label, yes/no, or the value as given. */
function answer(f: FormField, v: unknown): string {
  if (v === undefined || v === null || v === "") return "—";
  if (f.type === "yes_no") return v === true ? t("Yes") : t("No");
  if (f.type === "select") return f.options?.find((o) => o.value === v)?.label ?? String(v);
  return String(v);
}

function Detail({ id }: { id: string }) {
  const run = useRun();
  const q = useQuery(() => get<AssetDetail>(`/api/assets/${id}`), [id]);
  const [editing, setEditing] = useState(false); const [retiring, setRetiring] = useState<AssetDetail | null>(null);
  const restore = async () => { if ((await run(() => post<AssetDetail>(`/api/assets/${id}/restore`), t("Asset restored."))).ok) q.reload(); };

  return (
    <QueryBoundary q={q}>{(a) => (
      <>
        <div className="crud-top">
          <div>
            <Link href="/assets" className="back-link"><ArrowLeft size={14} /> {t("Assets")}</Link>
            <h1>{a.tag} · {a.name}</h1>
            <p>{a.typeName}{a.serial ? ` · ${t("Serial")} ${a.serial}` : ""}</p>
          </div>
          <span className="row" style={{ gap: 8 }}><AssetStatusBadge status={a.status} /></span>
        </div>
        {a.retiredAt && <div className="form-alert warn" role="status">{t("Retired on {when}: {reason}", { when: fmtDateTime(a.retiredAt), reason: a.retireReason ?? "" })}</div>}
        {a.repairHint && <div className="form-alert warn" role="status" data-testid="repair-hint">{a.repairHint === "frequent_out_of_warranty"
          ? t("{n} requests about this asset in the last 12 months, and its warranty has ended. Consider replacing it rather than repairing it again.", { n: a.requestsLastYear })
          : t("{n} requests about this asset in the last 12 months. If this continues, consider replacing it.", { n: a.requestsLastYear })}</div>}
        <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
          {!a.retiredAt && <Button href={`/requests/new?asset=${a.id}`}><MessageSquareWarning size={14} /> {t("Report a problem")}</Button>}
          <Button href={`/assets/labels?ids=${a.id}`}><QrCode size={14} /> {t("Print label")}</Button>
        </span>
        {a.canManage && <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
          {!a.retiredAt && <Button onClick={() => setEditing(true)}><Pencil size={14} /> {t("Edit")}</Button>}
          {!a.retiredAt && <Button variant="danger-outline" onClick={() => setRetiring(a)}>{t("Retire")}</Button>}
          {a.retiredAt && <Button onClick={() => void restore()}>{t("Restore")}</Button>}
        </span>}

        <div className="grid-2" style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", alignItems: "start" }}>
          <div className="stack">
            <Panel title={t("Details")}>
              <dl className="stack" style={{ margin: 0 }}>
                <div><dt><b>{t("Place")}</b></dt><dd style={{ margin: 0 }}>{a.locationPath ?? "—"}</dd></div>
                <div><dt><b>{t("With")}</b></dt><dd style={{ margin: 0 }}>{a.assignedToName ?? "—"}{a.department ? <small> · {a.department}</small> : null}</dd></div>
                <div><dt><b>{t("Purchased")}</b></dt><dd style={{ margin: 0 }}>{a.purchaseDate ?? "—"}{a.purchaseCost !== null ? ` · ${money(a.purchaseCost)}` : ""}</dd></div>
                <div><dt><b>{t("Warranty ends")}</b></dt><dd style={{ margin: 0 }}>{a.warrantyEnd ?? "—"}</dd></div>
                <div><dt><b>{t("Vendor")}</b></dt><dd style={{ margin: 0 }}>{a.vendorName ?? "—"}</dd></div>
                {a.fields.map((f) => <div key={f.key}><dt><b>{f.label || f.key}</b></dt><dd style={{ margin: 0 }}>{answer(f, a.values[f.key])}</dd></div>)}
                {a.notes && <div><dt><b>{t("Notes")}</b></dt><dd style={{ margin: 0, whiteSpace: "pre-wrap" }}>{a.notes}</dd></div>}
              </dl>
            </Panel>
            <Panel title={t("Requests about this asset")} description={`${t("{n} in the last 12 months.", { n: a.requestsLastYear })}${a.ticketCount > a.tickets.length ? ` ${t("The latest {n} of {total}.", { n: a.tickets.length, total: a.ticketCount })}` : ""}`}>
              {a.tickets.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No requests you can see are linked to this asset.")}</p> : (
                <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
                  {a.tickets.map((x) => <li key={x.id}><Link href={`/tickets/${x.id}`}><b>{x.number}</b></Link> · {x.subject} <TicketStatusBadge status={x.status} /><br /><small>{fmtDateTime(x.createdAt)}</small></li>)}
                </ul>
              )}
            </Panel>
          </div>
          <Panel title={t("History")}>
            <ol className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
              {a.events.map((e) => <li key={e.id} style={{ borderLeft: "3px solid var(--border, #d1d5db)", paddingLeft: 10 }}><b>{e.actor}</b> · <small>{fmtDateTime(e.at)}</small>{e.body && <p style={{ margin: "4px 0 0" }}>{e.body}</p>}</li>)}
            </ol>
          </Panel>
        </div>
        <AssetDrawer open={editing} onClose={() => setEditing(false)} onDone={() => q.reload()} editing={a} />
        <RetireDialog asset={retiring} onClose={() => setRetiring(null)} onDone={() => q.reload()} />
      </>
    )}</QueryBoundary>
  );
}

export default function AssetPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="assets.read"><Suspense><Detail id={id} /></Suspense></Require>;
}
