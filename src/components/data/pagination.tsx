"use client";

import { ChevronDown, ChevronLeft, ChevronRight } from "lucide-react";
import { cn } from "@/lib/cn";
import { t } from "@/i18n";

/** "Showing 1–10 of 42 users" is built by the pages in English; translate its fixed parts and the noun here. */
function tSummary(summary: string) {
  const m = summary.match(/^Showing (\d+)[–-](\d+) of (\d+)(?: (.+))?$/);
  if (m) return t("Showing {from}–{to} of {total}", { from: m[1], to: m[2], total: m[3] }) + (m[4] ? " " + t(m[4]) : "");
  const z = summary.match(/^Showing 0 (.+)$/);
  return z ? t("Showing 0") + " " + t(z[1]) : t(summary);
}

export function Pagination({ page, pageCount, onPageChange, summary, pageSize, pageSizeOptions = [10, 25, 50], onPageSizeChange }: {
  page: number; pageCount: number; onPageChange: (p: number) => void; summary: string;
  pageSize: number; pageSizeOptions?: number[]; onPageSizeChange?: (n: number) => void;
}) {
  // 1 2 3 4 5 … last
  const head = Array.from({ length: Math.min(5, pageCount) }, (_, i) => i + 1);
  const showTail = pageCount > 5;
  const go = (p: number) => onPageChange(Math.min(pageCount, Math.max(1, p)));
  return (
    <div className="pager">
      <span>{tSummary(summary)}</span>
      <nav className="pg" aria-label={t("Pagination")}>
        <button type="button" aria-label={t("Previous page")} disabled={page === 1} onClick={() => go(page - 1)}><ChevronLeft size={13} /></button>
        {head.map((n) => <button type="button" key={n} className={cn(page === n && "on")} aria-current={page === n ? "page" : undefined} onClick={() => go(n)}>{n}</button>)}
        {showTail && <><span>...</span><button type="button" className={cn(page === pageCount && "on")} onClick={() => go(pageCount)}>{pageCount}</button></>}
        <button type="button" aria-label={t("Next page")} disabled={page === pageCount} onClick={() => go(page + 1)}><ChevronRight size={13} /></button>
      </nav>
      <label className="rpp">{t("Rows per page")}<span className="sel">
          <select aria-label={t("Rows per page")} value={pageSize} onChange={(e) => onPageSizeChange?.(Number(e.target.value))}>
            {pageSizeOptions.map((n) => <option key={n}>{n}</option>)}
          </select>
          <ChevronDown size={13} />
        </span>
      </label>
    </div>
  );
}
