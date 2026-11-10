"use client";

import { Suspense, useDeferredValue, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AboutAssetBanner, useAboutAsset } from "@/components/assets/about-asset";
import Link from "next/link";
import { Panel } from "@/components/ui/card";
import { Field, TextInput } from "@/components/ui/field";
import { PageHeader } from "@/components/page-header";
import { QueryBoundary } from "@/components/query-boundary";
import { get } from "@/lib/api/client";
import type { PortalCatalog, PortalCategory, PortalServiceItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** Roots first, each followed by its sub-categories, so the page reads like the catalog admin sees it. */
function ordered(cats: PortalCategory[]): { cat: PortalCategory; depth: number }[] {
  const ids = new Set(cats.map((c) => c.id));
  const roots = cats.filter((c) => !c.parentId || !ids.has(c.parentId));
  return roots.flatMap((r) => [{ cat: r, depth: 0 }, ...cats.filter((c) => c.parentId === r.id).map((c) => ({ cat: c, depth: 1 }))]);
}

function Services({ catalog, asset }: { catalog: PortalCatalog; asset: string | null }) {
  const byCat = new Map<string, PortalServiceItem[]>();
  for (const s of catalog.services) byCat.set(s.categoryId, [...(byCat.get(s.categoryId) ?? []), s]);
  const groups = ordered(catalog.categories).filter(({ cat }) => byCat.has(cat.id));
  if (groups.length === 0) return <p className="tab-placeholder">{t("Nothing matches. Try other words.")}</p>;
  return (
    <div className="stack">
      {groups.map(({ cat, depth }) => (
        <Panel key={cat.id} title={depth ? `${cat.name}` : cat.name} description={cat.description ?? undefined}>
          <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
            {(byCat.get(cat.id) ?? []).map((s) => (
              <li key={s.id}>
                <Link href={`/requests/new/${s.id}${asset ? `?asset=${asset}` : ""}`} style={{ display: "block" }}><b>{s.name}</b>{s.description && <><br /><small>{s.description}</small></>}</Link>
              </li>
            ))}
          </ul>
        </Panel>
      ))}
    </div>
  );
}

function Catalog() {
  const [q, setQ] = useState(""); const term = useDeferredValue(q.trim());
  const about = useAboutAsset(useSearchParams().get("asset"));
  const catalog = useQuery(() => get<PortalCatalog>("/api/portal/catalog", { q: term || undefined }), [term]);
  return (
    <>
      <PageHeader help="make-a-request" title={t("New request")} description={t("What do you need? Choose a service and tell us the details.")} />
      {about && <AboutAssetBanner asset={about} clearHref="/requests/new" />}
      <Panel>
        <Field label={t("Search services")} htmlFor="nr-q"><TextInput id="nr-q" data-autofocus placeholder={t("e.g. air conditioning, meeting room, visitor")} value={q} onChange={(e) => setQ(e.target.value)} /></Field>
      </Panel>
      <QueryBoundary q={catalog} rows={4}>{(c) => <Services catalog={c} asset={about?.id ?? null} />}</QueryBoundary>
    </>
  );
}

export default function NewRequestPage() { return <Suspense><Catalog /></Suspense>; }
