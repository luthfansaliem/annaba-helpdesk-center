"use client";

import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { Require } from "@/components/page-guard";
import { ArticleEditor } from "@/components/kb/editor";
import { t } from "@/i18n";

export default function NewArticlePage() {
  return (
    <Require perm="kb.write">
      <div className="crud-top"><div>
        <Link href="/kb" className="back-link"><ArrowLeft size={14} /> {t("Knowledge base")}</Link>
        <h1>{t("New article")}</h1>
        <p>{t("It stays a draft until someone with publishing rights publishes it.")}</p>
      </div></div>
      <ArticleEditor article={null} />
    </Require>
  );
}
