"use client";

import { use } from "react";
import { MessageSquareWarning } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { QueryBoundary } from "@/components/query-boundary";
import { AssetStatusBadge } from "@/components/assets/asset-bits";
import { get } from "@/lib/api/client";
import type { AssetCard } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** Where a scanned asset label leads (AST-002). Anyone signed in to the organisation sees what the asset is and can report a problem about it. */
function Scanned({ tag }: { tag: string }) {
  const q = useQuery(() => get<AssetCard>("/api/assets/card", { tag }), [tag]);
  return (
    <QueryBoundary q={q}>{(a) => (
      <>
        <div className="crud-top">
          <div><h1>{a.name}</h1><p>{a.tag} · {a.typeName}</p></div>
          <AssetStatusBadge status={a.status} />
        </div>
        <Panel>
          <div className="stack">
            {a.locationPath && <p style={{ margin: 0 }}><b>{t("Place")}:</b> {a.locationPath}</p>}
            {a.retired
              ? <div className="form-alert warn" role="status">{t("This asset is no longer in service, so problems can't be reported about it. If it is still in use, tell your helpdesk.")}</div>
              : <div><Button variant="primary" href={`/requests/new?asset=${a.id}`}><MessageSquareWarning size={16} /> {t("Report a problem with this")}</Button></div>}
            {a.canOpen && <div><Button href={`/assets/${a.id}`}>{t("Open in the asset register")}</Button></div>}
          </div>
        </Panel>
      </>
    )}</QueryBoundary>
  );
}

export default function ScanPage({ params }: { params: Promise<{ tag: string }> }) {
  const { tag } = use(params);
  return <Scanned tag={decodeURIComponent(tag)} />;
}
