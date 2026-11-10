"use client";

import { use } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { ArticleEditor } from "@/components/kb/editor";
import { get } from "@/lib/api/client";
import type { KbArticleDetail } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

function Edit({ id }: { id: string }) {
  const q = useQuery(() => get<KbArticleDetail>(`/api/kb/articles/${id}`, { version: 0 }), [id]);
  return (
    <QueryBoundary q={q}>{(a) => (
      <>
        <div className="crud-top"><div>
          <Link href={`/kb/${a.id}`} className="back-link"><ArrowLeft size={14} /> {a.title}</Link>
          <h1>{t("Edit article")}</h1>
          <p>{t("Saving makes version {n}. Readers keep seeing the published version until it is published.", { n: a.latestVersion + 1 })}</p>
        </div></div>
        <ArticleEditor article={a} />
      </>
    )}</QueryBoundary>
  );
}

export default function EditArticlePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="kb.write"><Edit id={id} /></Require>;
}
