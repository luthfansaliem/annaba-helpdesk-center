"use client";

import { ArrowDown, ArrowUp, ArrowUpDown } from "lucide-react";
import { ErrorState, SkeletonRows } from "@/components/ui/states";
import { Checkbox } from "@/components/ui/field";
import { cn } from "@/lib/cn";
import { t } from "@/i18n";

export type Column<T> = {
  key: string;
  header: React.ReactNode;
  cell: (row: T, index: number) => React.ReactNode;
  sortable?: boolean;
  width?: number | string;
  /** extra class for the cell (e.g. hide on mobile) */
  className?: string;
};
export type Sort = { key: string; dir: "asc" | "desc" };

export function DataTable<T>({
  columns, rows, rowKey, selectable, selected = [], onSelectedChange, emptyMessage = "No data.", variant = "grid", label, sort, onSortChange, loading, error, onRetry,
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  selectable?: boolean;
  selected?: string[];
  onSelectedChange?: (keys: string[]) => void;
  emptyMessage?: string;
  /** `grid` = tinted header (CRUD lists); `plain` = compact dashboard table */
  variant?: "grid" | "plain";
  label?: string;
  /** controlled sorting: sortable columns become buttons */
  sort?: Sort | null;
  onSortChange?: (s: Sort) => void;
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
}) {
  if (loading) return <SkeletonRows rows={6} />;
  if (error) return <ErrorState message={error} onRetry={onRetry} />;
  const keys = rows.map(rowKey);
  const allSelected = keys.length > 0 && keys.every((k) => selected.includes(k));
  const toggle = (k: string) => onSelectedChange?.(selected.includes(k) ? selected.filter((x) => x !== k) : [...selected, k]);
  const span = columns.length + (selectable ? 1 : 0);

  return (
    <div className="tbl-wrap">
      <table className={cn(variant === "grid" && "tbl")} aria-label={label}>
        <thead>
          <tr>
            {selectable && (
              <th style={{ width: 30 }}>
                <Checkbox aria-label={t("Select all rows")} checked={allSelected} onChange={() => onSelectedChange?.(allSelected ? [] : keys)} />
              </th>
            )}
            {columns.map((c) => (
              <th key={c.key} style={{ width: c.width }} className={c.className} aria-sort={sort?.key === c.key ? (sort.dir === "asc" ? "ascending" : "descending") : undefined}>
                {c.sortable && onSortChange ? (
                  <button type="button" className="th-sort" onClick={() => onSortChange({ key: c.key, dir: sort?.key === c.key && sort.dir === "asc" ? "desc" : "asc" })}>
                    {c.header}{sort?.key === c.key ? (sort.dir === "asc" ? <ArrowUp size={11} /> : <ArrowDown size={11} />) : <ArrowUpDown size={11} />}
                  </button>
                ) : (<>{c.header}{c.sortable && <ArrowUpDown size={11} style={{ verticalAlign: -1, marginLeft: 4 }} />}</>)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => {
            const k = rowKey(row);
            return (
              <tr key={k} aria-selected={selected.includes(k) || undefined}>
                {selectable && <td><Checkbox aria-label={`Select row ${k}`} checked={selected.includes(k)} onChange={() => toggle(k)} /></td>}
                {columns.map((c) => <td key={c.key} className={c.className}>{c.cell(row, i)}</td>)}
              </tr>
            );
          })}
          {rows.length === 0 && <tr><td colSpan={span} className="tbl-empty">{emptyMessage}</td></tr>}
        </tbody>
      </table>
    </div>
  );
}
