"use client";

import { useState } from "react";
import Link from "next/link";
import { FolderTree, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { Pagination } from "@/components/data/pagination";
import { Can } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { KbCategoryItem, KbListItem, Paged } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

export default function KnowledgeBasePage() {
  const { can } = useAuth(); const writer = can("kb.write") || can("kb.publish");
  const [q, setQ] = useState(""); const [cat, setCat] = useState(""); const [status, setStatus] = useState("published"); const [page, setPage] = useState(1); const size = 20;
  const cats = useQuery(() => get<KbCategoryItem[]>("/api/kb/categories"), []);
  const list = useQuery(() => get<Paged<KbListItem>>("/api/kb/articles", { q: q || undefined, categoryId: cat || undefined, status: writer ? status : undefined, page, pageSize: size }), [q, cat, status, page, writer]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  return (
    <>
      <PageHeader title={t("Knowledge base")} description={t("Answers to common questions, how-tos and the rules of the building. Look here first — it is often quicker than a request.")}
        actions={<span className="row" style={{ gap: 8 }}>
          <Can perm="kb.publish"><Button href="/kb/categories"><FolderTree size={16} /> {t("Categories")}</Button></Can>
          <Can perm="kb.write"><Button variant="primary" href="/kb/new"><Plus size={16} /> {t("New article")}</Button></Can>
        </span>} />
      <Panel>
        <div className="row" style={{ gap: 12, flexWrap: "wrap", alignItems: "flex-end" }}>
          <Field label={t("Search the knowledge base")} htmlFor="kb-q"><TextInput id="kb-q" type="search" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} /></Field>
          <Field label={t("Category")} htmlFor="kb-cat-f"><SelectInput id="kb-cat-f" value={cat} onChange={(e) => { setCat(e.target.value); setPage(1); }} options={[{ value: "", label: t("All") }, ...(cats.data ?? []).filter((c) => !c.archived).map((c) => ({ value: c.id, label: c.name }))]} /></Field>
          {writer && <Field label={t("Show")} htmlFor="kb-st"><SelectInput id="kb-st" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} options={[{ value: "published", label: t("Published") }, { value: "draft", label: t("Drafts and unpublished changes") }, { value: "review", label: t("Review due") }, { value: "archived", label: t("Archived") }, { value: "all", label: t("All") }]} /></Field>}
        </div>
      </Panel>
      <Panel>
        {list.error ? <div className="form-alert err" role="alert">{list.error.message} <Button size="sm" onClick={list.reload}>{t("Try again")}</Button></div>
          : rows.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{list.data === undefined ? t("Loading…") : t("No articles found.")}</p>
            : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("Articles")}>{rows.map((a) => (
              <li key={a.id} style={{ borderBottom: "1px solid var(--border, #e5e7eb)", paddingBottom: 10 }}>
                <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                  <Link href={`/kb/${a.id}${writer && a.status !== "published" ? "?draft=1" : ""}`}><b>{a.title}</b></Link>
                  {a.categoryName && <Badge tone="gray" dot={false}>{a.categoryName}</Badge>}
                  {a.visibility === "staff" && <Badge tone="blue" dot={false}>{t("Staff only")}</Badge>}
                  {writer && a.status !== "published" && <Badge tone={a.status === "archived" ? "gray" : "blue"}>{a.status === "archived" ? t("Archived") : t("Draft")}</Badge>}
                  {writer && a.status === "published" && a.hasDraft && <Badge tone="blue">{t("Unpublished changes")}</Badge>}
                  {a.reviewDue && <Badge tone="orange">{t("Review due")}</Badge>}
                </span>
                {a.summary && <p style={{ margin: "4px 0 0" }}>{a.summary}</p>}
                <small className="sub">{t("Updated {when}", { when: fmtDateTime(a.updatedAt) })}{writer && ` · ${t("{v} views · {y} helpful · {n} not helpful · {d} solved without a request", { v: a.views, y: a.helpful, n: a.notHelpful, d: a.deflections })}`}</small>
              </li>))}</ul>}
        {total > size && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} summary={t("Showing {from}–{to} of {total}", { from: (page - 1) * size + 1, to: Math.min(page * size, total), total })} />}
      </Panel>
    </>
  );
}
