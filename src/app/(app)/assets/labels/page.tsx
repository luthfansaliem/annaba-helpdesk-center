"use client";

import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import QRCode from "qrcode";
import { ArrowLeft, Printer } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, SelectInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { AssetLabel, AssetTypeItem, PortalLocation } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** Where a scanned label leads: the scan page of this installation, which works for anyone signed in to the organisation. */
const scanUrl = (tag: string) => `${window.location.origin}/a/${encodeURIComponent(tag)}`;

function Label({ a }: { a: AssetLabel }) {
  const [src, setSrc] = useState<string | null>(null);
  useEffect(() => { let live = true; QRCode.toDataURL(scanUrl(a.tag), { margin: 1, width: 192, errorCorrectionLevel: "M" }).then((d) => { if (live) setSrc(d); }); return () => { live = false; }; }, [a.tag]);
  return (
    <div className="asset-label" data-testid="asset-label">
      {/* eslint-disable-next-line @next/next/no-img-element -- a QR code generated in the browser as a data: URL; there is nothing to optimise */}
      {src ? <img src={src} alt={t("QR code for {tag}", { tag: a.tag })} /> : <span style={{ width: 96, height: 96 }} />}
      <span><b>{a.tag}</b><small>{a.name}</small><small>{a.typeName}{a.locationPath ? ` · ${a.locationPath}` : ""}</small><small>{t("Scan to report a problem")}</small></span>
    </div>
  );
}

function Labels() {
  const params = useSearchParams(); const ids = params.get("ids") ?? "";
  const [typeId, setTypeId] = useState(params.get("typeId") ?? ""); const [locationId, setLocationId] = useState(params.get("locationId") ?? "");
  const types = useQuery(() => get<AssetTypeItem[]>("/api/asset-types"), []);
  const places = useQuery(() => get<PortalLocation[]>("/api/portal/locations").catch(() => [] as PortalLocation[]), []);
  const labels = useQuery(() => (ids || typeId || locationId ? get<AssetLabel[]>("/api/assets/labels", { ids: ids || undefined, typeId: typeId || undefined, locationId: locationId || undefined }) : Promise.resolve([] as AssetLabel[])), [ids, typeId, locationId]);
  return (
    <>
      <div className="no-print">
        <PageHeader title={t("Asset labels")} description={t("Print these and stick them on the assets. Scanning one with a phone opens the asset, where anyone can report a problem about it.")}
          actions={<span className="row" style={{ gap: 8 }}><Button href="/assets"><ArrowLeft size={14} /> {t("Assets")}</Button><Button variant="primary" onClick={() => window.print()} disabled={!labels.data?.length}><Printer size={14} /> {t("Print")}</Button></span>} />
        {!ids && <Panel>
          <div className="filters">
            <Field label={t("Type")} htmlFor="lb-type"><SelectInput id="lb-type" value={typeId} onChange={(e) => setTypeId(e.target.value)} options={[{ value: "", label: t("Choose…") }, ...(types.data ?? []).map((x) => ({ value: x.id, label: x.name }))]} /></Field>
            <Field label={t("Place")} htmlFor="lb-place"><SelectInput id="lb-place" value={locationId} onChange={(e) => setLocationId(e.target.value)} options={[{ value: "", label: t("Anywhere") }, ...(places.data ?? []).map((l) => ({ value: l.id, label: l.path }))]} /></Field>
          </div>
        </Panel>}
      </div>
      <QueryBoundary q={labels} rows={3}>{(rows) => rows.length === 0
        ? <p className="tab-placeholder no-print">{ids ? t("These assets were not found.") : t("Choose a type or a place to see its labels.")}</p>
        : <>
          <p className="no-print" role="status" style={{ margin: "8px 0" }}>{t("{n} labels.", { n: rows.length })}{rows.length >= 500 ? ` ${t("At most 500 at a time: narrow the choice for the rest.")}` : ""}</p>
          <div className="label-sheet">{rows.map((a) => <Label key={a.id} a={a} />)}</div>
        </>}</QueryBoundary>
    </>
  );
}

export default function AssetLabelsPage() { return <Require perm="assets.read"><Suspense><Labels /></Suspense></Require>; }
