"use client";

import { Suspense, useDeferredValue, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AboutAssetBanner, useAboutAsset } from "@/components/assets/about-asset";
import Link from "next/link";
import { Search } from "lucide-react";
import { CatalogIcon, TONES } from "@/components/catalog-icon";
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
  for (const sv of catalog.services) byCat.set(sv.categoryId, [...(byCat.get(sv.categoryId) ?? []), sv]);
  const all = ordered(catalog.categories);
  const tone = new Map(all.map(({ cat }, i) => [cat.id, TONES[i % TONES.length]]));
  const groups = all.filter(({ cat }) => byCat.has(cat.id));
  if (groups.length === 0) return <p className="tab-placeholder">{t("Nothing matches. Try other words.")}</p>;
  return (
    <div>
      {groups.map(({ cat, depth }) => {
        const c = tone.get(cat.id) ?? TONES[0];
        return (
          <section key={cat.id} aria-labelledby={`cat-${cat.id}`}>
            <div className="cat-h">
              <span className="ti" style={{ background: c.bg, color: c.fg }}><CatalogIcon name={cat.icon} size={16} /></span>
              <div><h2 id={`cat-${cat.id}`} style={depth ? { fontSize: 14 } : undefined}>{cat.name}</h2>{cat.description && <p>{cat.description}</p>}</div>
            </div>
            <div className="tiles">
              {(byCat.get(cat.id) ?? []).map((sv) => (
                <Link key={sv.id} href={`/requests/new/${sv.id}${asset ? `?asset=${asset}` : ""}`} className="tile">
                  <span className="ti" style={{ background: c.bg, color: c.fg }}><CatalogIcon name={sv.icon ?? cat.icon} /></span>
                  <div><b>{sv.name}</b>{sv.description && <small>{sv.description}</small>}</div>
                </Link>
              ))}
            </div>
          </section>
        );
      })}
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
      <section className="card ask-card">
        <label htmlFor="nr-q" style={{ display: "block", fontWeight: 700, color: "var(--navy)", marginBottom: 8 }}>{t("Search services")}</label>
        <div className="ask"><Search size={18} aria-hidden /><input id="nr-q" data-autofocus placeholder={t("e.g. air conditioning, meeting room, visitor")} value={q} onChange={(e) => setQ(e.target.value)} /></div>
      </section>
      <QueryBoundary q={catalog} rows={4}>{(c) => <Services catalog={c} asset={about?.id ?? null} />}</QueryBoundary>
    </>
  );
}

export default function NewRequestPage() { return <Suspense><Catalog /></Suspense>; }
