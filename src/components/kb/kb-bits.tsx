"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { BookOpen, ThumbsDown, ThumbsUp } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Markdown } from "@/components/kb/markdown";
import { get, post } from "@/lib/api/client";
import type { KbArticleDetail, KbSuggestion } from "@/lib/api/types";
import { fmtSize } from "@/lib/files";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const kbFileUrl = (articleId: string) => (fileId: string) => `/api/kb/articles/${articleId}/files/${fileId}`;

export function KbStatusBadge({ a }: { a: Pick<KbArticleDetail, "status" | "hasDraft" | "reviewDue"> }) {
  return (
    <span className="row" style={{ gap: 4, flexWrap: "wrap" }}>
      {a.status === "published" ? <Badge tone="green">{t("Published")}</Badge> : a.status === "archived" ? <Badge tone="gray">{t("Archived")}</Badge> : <Badge tone="blue">{t("Draft")}</Badge>}
      {a.status === "published" && a.hasDraft && <Badge tone="blue">{t("Unpublished changes")}</Badge>}
      {a.reviewDue && <Badge tone="orange">{t("Review due")}</Badge>}
    </span>
  );
}

/** The article's text, its documents and "was this helpful?". */
export function ArticleBody({ a, onChange, compact }: { a: KbArticleDetail; onChange?: (d: KbArticleDetail) => void; compact?: boolean }) {
  const run = useRun();
  const docs = a.files.filter((f) => !f.contentType.startsWith("image/"));
  const vote = async (helpful: boolean) => { const r = await run(() => post<KbArticleDetail>(`/api/kb/articles/${a.id}/feedback`, { helpful }), t("Thank you.")); if (r.ok) onChange?.(r.data); };
  return (
    <div className="stack">
      {a.summary && <p style={{ margin: 0 }}><b>{a.summary}</b></p>}
      <Markdown text={a.body} fileUrl={kbFileUrl(a.id)} />
      {docs.length > 0 && <div>
        <b>{t("Documents")}</b>
        <ul style={{ margin: "4px 0 0", paddingLeft: 18 }}>{docs.map((f) => <li key={f.id}><a href={kbFileUrl(a.id)(f.id)}>{f.fileName}</a> <small>({fmtSize(f.size)})</small></li>)}</ul>
      </div>}
      {!compact && a.status === "published" && a.shownVersion === a.publishedVersion && <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "center" }} data-testid="kb-helpful">
        <span>{a.myFeedback === null ? t("Was this helpful?") : t("Thanks for telling us.")}</span>
        <Button size="sm" variant={a.myFeedback === true ? "primary" : undefined} aria-pressed={a.myFeedback === true} onClick={() => void vote(true)}><ThumbsUp size={14} /> {t("Yes")}</Button>
        <Button size="sm" variant={a.myFeedback === false ? "primary" : undefined} aria-pressed={a.myFeedback === false} onClick={() => void vote(false)}><ThumbsDown size={14} /> {t("No")}</Button>
      </div>}
    </div>
  );
}

/** KB-002. Articles that may answer the request while it is being written; "this solved it" counts as a deflection and leaves the form. */
export function KbSuggestions({ serviceId, text }: { serviceId: string; text: string }) {
  const router = useRouter(); const run = useRun();
  const [term, setTerm] = useState(text.trim()); const [open, setOpen] = useState<string | null>(null);
  useEffect(() => { const h = setTimeout(() => setTerm(text.trim()), 400); return () => clearTimeout(h); }, [text]);
  const list = useQuery(() => get<KbSuggestion[]>("/api/kb/suggest", { serviceId, q: term.length >= 3 ? term : undefined }).catch(() => [] as KbSuggestion[]), [serviceId, term]);
  const article = useQuery(() => (open ? get<KbArticleDetail>(`/api/kb/articles/${open}`) : Promise.resolve(null)), [open]);
  const items = list.data ?? [];
  if (items.length === 0) return null;
  const solved = async () => { if (!open) return; const r = await run(() => post(`/api/kb/articles/${open}/solved`), t("Glad it helped. No request was sent.")); if (r.ok) router.push("/requests"); };
  return (
    <Panel title={t("Maybe this already answers it")} description={t("Articles from the knowledge base that match your request.")}>
      <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} data-testid="kb-suggestions">
        {items.map((s) => (
          <li key={s.id}><Button size="sm" onClick={() => setOpen(s.id)}><BookOpen size={14} /> {s.title}</Button>{s.summary && <small className="sub"> {s.summary}</small>}</li>
        ))}
      </ul>
      <Overlay open={open !== null} onClose={() => setOpen(null)} side="right" title={article.data?.title ?? t("Article")} description={t("From the knowledge base")}
        footer={<><Button onClick={() => setOpen(null)}>{t("No, continue with my request")}</Button><Button variant="primary" onClick={() => void solved()}>{t("Yes, this solved it")}</Button></>}>
        {article.data && <ArticleBody a={article.data} compact />}
      </Overlay>
    </Panel>
  );
}
