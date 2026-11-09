"use client";

import { ChevronDown, Filter, Search } from "lucide-react";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

export function SearchBox({ value, onChange, placeholder, label = "Search" }: { value: string; onChange: (v: string) => void; placeholder?: string; label?: string }) {
  return (
    <div className="fld search-f">
      <Search size={16} />
      <input aria-label={label} placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

export function FilterSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (v: string) => void; options: string[] }) {
  return (
    <label className="fld">
      <span className="grow">
        <small>{label}</small>
        <select value={value} onChange={(e) => onChange(e.target.value)}>
          {options.map((o) => <option key={o} value={o}>{t(o)}</option>)}
        </select>
      </span>
      <ChevronDown size={14} />
    </label>
  );
}

export function FilterBar({ children, onApply }: { children: React.ReactNode; onApply?: () => void }) {
  return (
    <div className="toolbar" role="search">
      {children}
      {onApply && <Button variant="primary" style={{ height: "auto" }} onClick={onApply}><Filter size={15} /> {t("Filter")}</Button>}
    </div>
  );
}
