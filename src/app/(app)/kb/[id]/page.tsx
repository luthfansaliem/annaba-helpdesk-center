"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Archive, ArrowLeft, Pencil, RotateCcw, Send } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { QueryBoundary } from "@/components/query-boundary";
import { ArticleBody, KbStatusBadge } from "@/components/kb/kb-bits";
import { get, post } from "@/lib/api/client";
import type { KbArticleDetail } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function Article({ id }: { id: string }) {
  const run = useRun(); const params = useSearchParams();
  const [version, setVersion] = useState<number | undefined>(params.get("draft") ? 0 : undefined);
  const q = useQuery(() => get<KbArticleDetail>(`/api/kb/articles/${id}`, { version }), [id, version]);
  return (
    <QueryBoundary q={q}>{(a) => {
      const act = async (path: string, done: string) => { const r = await run(() => post<KbArticleDetail>(`/api/kb/articles/${a.id}/${path}`), done); if (r.ok) { setVersion(undefined); q.setData(r.data); } };
      const viewingOther = a.canWrite && a.shownVersion !== a.publishedVersion;
      return (
        <>
          <div className="crud-top">
            <div>
              <Link href="/kb" className="back-link"><ArrowLeft size={14} /> {t("Knowledge base")}</Link>
              <h1>{a.title}</h1>
              <p className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                {a.categoryName && <Badge tone="gray" dot={false}>{a.categoryName}</Badge>}
                {a.visibility === "staff" && <Badge tone="blue" dot={false}>{t("Staff only")}</Badge>}
                {a.publishedAt && <small className="sub">{t("Published {when}", { when: fmtDateTime(a.publishedAt) })}</small>}
              </p>
            </div>
            {a.canWrite && <KbStatusBadge a={a} />}
          </div>
          {a.canWrite && <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            <Button href={`/kb/${a.id}/edit`}><Pencil size={14} /> {t("Edit")}</Button>
            {a.canPublish && (a.hasDraft || a.status === "draft") && a.status !== "archived" && <Button variant="primary" onClick={() => void act("publish", t("Article published."))}><Send size={14} /> {t("Publish version {n}", { n: a.latestVersion })}</Button>}
            {a.canPublish && a.status !== "archived" && <Button variant="danger-outline" onClick={() => void act("archive", t("Article archived."))}><Archive size={14} /> {t("Archive")}</Button>}
            {a.canPublish && a.status === "archived" && <Button onClick={() => void act("restore", t("Article restored."))}><RotateCcw size={14} /> {t("Restore")}</Button>}
          </span>}
          {viewingOther && <div className="form-alert" role="status">{a.shownVersion > a.publishedVersion ? t("You are looking at version {n}, which readers do not see yet.", { n: a.shownVersion }) : t("You are looking at an older version ({n}).", { n: a.shownVersion })}</div>}

          <div className="kb-grid">
            <Panel><ArticleBody a={a} onChange={(d) => q.setData(d)} /></Panel>
            <div className="stack">
              {a.services.length > 0 && <Panel title={t("Still need help?")}>
                <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{a.services.map((s) => <li key={s.id}><Link href={`/requests/new/${s.id}`}>{t("Ask for {service}", { service: s.name })}</Link></li>)}</ul>
              </Panel>}
              {a.canWrite && <Panel title={t("How it is used")}>
                <dl className="stack" style={{ margin: 0 }} data-testid="kb-stats">
                  <div><dt><small>{t("Views")}</small></dt><dd style={{ margin: 0 }}><b>{a.views}</b></dd></div>
                  <div><dt><small>{t("Helpful / not helpful")}</small></dt><dd style={{ margin: 0 }}><b>{a.helpful}</b> / <b>{a.notHelpful}</b></dd></div>
                  <div><dt><small>{t("Solved without a request")}</small></dt><dd style={{ margin: 0 }}><b>{a.deflections}</b></dd></div>
                  {a.reviewBy && <div><dt><small>{t("Review by")}</small></dt><dd style={{ margin: 0 }}>{a.reviewBy}</dd></div>}
                </dl>
              </Panel>}
              {a.versions && <Panel title={t("Versions")}>
                <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{a.versions.map((v) => (
                  <li key={v.number}>
                    <Button size="sm" onClick={() => setVersion(v.number)} aria-current={a.shownVersion === v.number ? "true" : undefined}>{t("Version {n}", { n: v.number })}</Button>
                    {v.published && <> <Badge tone="green">{t("Published")}</Badge></>}
                    <br /><small className="sub">{v.by ?? "—"} · {fmtDateTime(v.at)}{v.note ? ` · ${v.note}` : ""}</small>
                  </li>))}</ul>
              </Panel>}
            </div>
          </div>
        </>
      );
    }}</QueryBoundary>
  );
}

export default function ArticlePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Suspense><Article id={id} /></Suspense>;
}
