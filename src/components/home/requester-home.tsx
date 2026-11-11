"use client";

import { useDeferredValue, useMemo, useState } from "react";
import Link from "next/link";
import { ArrowRight, BookOpen, FilePlus2, Inbox, Search } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { CatalogIcon, TONES } from "@/components/catalog-icon";
import { ModuleEmpty } from "@/components/insights/module-empty";
import { TicketStatusBadge } from "@/components/tickets/status";
import { Skeleton } from "@/components/ui/states";
import { get } from "@/lib/api/client";
import type { KbSuggestion, Paged, PortalCatalog, TicketListItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { rel } from "@/lib/format";
import { t } from "@/i18n";

/** "What do you need?": one box that finds services (to ask for) and articles (to help yourself) as you type. */
function AskBox({ catalog }: { catalog: PortalCatalog | undefined }) {
  const [q, setQ] = useState(""); const term = useDeferredValue(q.trim());
  const kb = useQuery(() => (term.length >= 3 ? get<KbSuggestion[]>("/api/kb/suggest", { q: term }).catch(() => [] as KbSuggestion[]) : Promise.resolve([] as KbSuggestion[])), [term]);
  const services = useMemo(() => {
    if (!catalog || term.length < 2) return [];
    const words = term.toLowerCase().split(/\s+/);
    return catalog.services.filter((s) => words.every((w) => `${s.name} ${s.description ?? ""}`.toLowerCase().includes(w))).slice(0, 5);
  }, [catalog, term]);
  const articles = (kb.data ?? []).slice(0, 4);
  return (
    <section className="card ask-card" aria-labelledby="ask-h">
      <h2 id="ask-h">{t("What do you need?")}</h2>
      <p>{t("Search the services you can ask for and the answers in the knowledge base.")}</p>
      <label className="ask"><Search size={18} aria-hidden /><input aria-label={t("What do you need?")} placeholder={t("e.g. air conditioning, meeting room, visitor badge")} value={q} onChange={(e) => setQ(e.target.value)} /></label>
      {term.length >= 2 && (
        <ul className="ask-results" aria-live="polite">
          {services.map((s) => <li key={s.id}><Link href={`/requests/new/${s.id}`}><FilePlus2 size={16} aria-hidden /> <span><b>{s.name}</b>{s.description && <small> · {s.description}</small>}</span></Link></li>)}
          {articles.map((a) => <li key={a.id}><Link href={`/kb/${a.id}`}><BookOpen size={16} aria-hidden /> <span><b>{a.title}</b><small> · {t("Article")}</small></span></Link></li>)}
          {services.length === 0 && articles.length === 0 && !kb.loading && <li><Link href="/requests/new"><ArrowRight size={16} aria-hidden /> <span>{t("Nothing matches. Browse all services")}</span></Link></li>}
        </ul>
      )}
    </section>
  );
}

function Popular({ catalog }: { catalog: PortalCatalog }) {
  const tone = new Map(catalog.categories.map((c, i) => [c.id, TONES[i % TONES.length]]));
  const icon = new Map(catalog.categories.map((c) => [c.id, c.icon]));
  const list = catalog.services.slice(0, 8);
  if (list.length === 0) return <ModuleEmpty icon={<FilePlus2 size={24} />} title={t("No services yet")} text={t("Your helpdesk has not published any services yet. Ask your General Affairs team.")} />;
  return (
    <div className="tiles">
      {list.map((s) => {
        const c = tone.get(s.categoryId) ?? TONES[0];
        return (
          <Link key={s.id} href={`/requests/new/${s.id}`} className="tile">
            <span className="ti" style={{ background: c.bg, color: c.fg }}><CatalogIcon name={s.icon ?? icon.get(s.categoryId)} /></span>
            <div><b>{s.name}</b>{s.description && <small>{s.description}</small>}</div>
          </Link>
        );
      })}
    </div>
  );
}

function MyOpen() {
  const mine = useQuery(() => get<Paged<TicketListItem>>("/api/tickets/mine", { status: "open", pageSize: 6 }), []);
  if (mine.data === undefined) return <div className="stack"><Skeleton h={38} /><Skeleton h={38} /><Skeleton h={38} /></div>;
  const rows = mine.data.data;
  if (rows.length === 0) return <ModuleEmpty icon={<Inbox size={24} />} title={t("No open requests")} text={t("When you ask for something, you can follow it here until it is done.")} />;
  return (
    <>
      <ul className="req-list">
        {rows.map((r) => (
          <li key={r.id}>
            <div><Link href={`/tickets/${r.id}`}><b>{r.subject}</b></Link><small>{r.number} · {r.serviceName} · {t("updated")} {rel(r.updatedAt)}</small></div>
            <TicketStatusBadge status={r.status} />
          </li>
        ))}
      </ul>
      {mine.data.total > rows.length && <p style={{ marginTop: 8 }}><Link className="link-btn" href="/requests">{t("See all {n} open requests", { n: mine.data.total })}</Link></p>}
    </>
  );
}

export function RequesterHome() {
  const { user } = useAuth();
  const catalog = useQuery(() => get<PortalCatalog>("/api/portal/catalog"), []);
  const first = user?.name.split(" ")[0] ?? "";
  return (
    <>
      <div className="home-head">
        <div><h1>{t("Hello, {name}", { name: first })}</h1><p>{t("Ask General Affairs for help and follow your requests in one place.")}</p></div>
        <Button variant="primary" href="/requests/new"><FilePlus2 size={16} /> {t("New request")}</Button>
      </div>
      <AskBox catalog={catalog.data} />
      <div className="home-grid">
        <Panel title={t("Popular services")} actions={<Link className="link-btn" href="/requests/new">{t("All services")}</Link>}>
          {catalog.data ? <Popular catalog={catalog.data} /> : <div className="tiles">{[0, 1, 2, 3].map((i) => <Skeleton key={i} h={64} />)}</div>}
        </Panel>
        <Panel title={t("My open requests")} actions={<Link className="link-btn" href="/requests">{t("All my requests")}</Link>}><MyOpen /></Panel>
      </div>
      <Panel title={t("Help yourself first")} description={t("Answers to common questions, how-tos and the building rules are often quicker than a request.")} actions={<Button size="sm" href="/kb"><BookOpen size={14} /> {t("Open the knowledge base")}</Button>}>{null}</Panel>
    </>
  );
}
