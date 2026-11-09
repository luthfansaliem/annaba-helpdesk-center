"use client";

import { cn } from "@/lib/cn";

export type TabItem<K extends string = string> = { key: K; label: string; icon?: React.ReactNode };

/** Controlled tab switcher. `underline` = in-card tabs, `segmented` = page-level switch, `pill` = small range picker. */
export function Tabs<K extends string>({ items, value, onChange, variant = "underline", size, label }: {
  items: TabItem<K>[]; value: K; onChange: (key: K) => void; variant?: "underline" | "segmented" | "pill"; size?: "lg"; label?: string;
}) {
  const wrap = variant === "segmented" ? "seg" : variant === "pill" ? "range" : cn("tabs", size === "lg" && "big");
  return (
    <div className={wrap} role="tablist" aria-label={label}>
      {items.map((t) => (
        <button key={t.key} type="button" role="tab" aria-selected={value === t.key} className={value === t.key ? "on" : ""} onClick={() => onChange(t.key)}>
          {t.icon}{t.label}
        </button>
      ))}
    </div>
  );
}
