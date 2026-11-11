"use client";

import { ChevronDown } from "lucide-react";
import { KpiCard, type Tone } from "@/components/insights/kpi";
import { Bars } from "@/components/insights/charts";
import { fmtMinutes } from "@/lib/duration";
import { t } from "@/i18n";

export const PERIODS = [7, 30, 90] as const;
/** Periods for module summaries and the satisfaction report (the API accepts 7 to 365 days). */
export const LONG_PERIODS = [30, 90, 180, 365] as const;
export type { Tone };

export const periodLabel = (d: number) => (d === 365 ? t("Last 12 months") : d === 180 ? t("Last 6 months") : t("Last {n} days", { n: d }));

/** The report period as a compact control for the page header (its label is for screen readers; the options say what they are). */
export function PeriodSelect({ days, onChange, options = PERIODS, id = "rp-days" }: { days: number; onChange: (d: number) => void; options?: readonly number[]; id?: string }) {
  return (
    <div className="ctl" style={{ height: 38, minWidth: 150 }}>
      <select id={id} aria-label={t("Period")} value={String(days)} onChange={(e) => onChange(Number(e.target.value))}>
        {options.map((d) => <option key={d} value={String(d)}>{periodLabel(d)}</option>)}
      </select>
      <ChevronDown size={14} />
    </div>
  );
}

/** One number with its label (and a short note). The colour is never the only signal: the note says what it means. */
export function Kpi({ label, value, note, tone = "default", icon, href }: { label: string; value: string; note?: string; tone?: Tone; icon?: React.ReactNode; href?: string }) {
  return <KpiCard label={label} value={value} note={note} tone={tone} icon={icon} href={href} />;
}

/** Horizontal bars for a short list of counts; each row reads as text too ("Label — 12"). */
export function BarList({ items, color = "#0b62e6", empty }: { items: { label: string; count: number }[]; color?: string; empty?: string }) {
  return <Bars items={items.map((i) => ({ label: i.label, value: i.count }))} color={color} empty={empty ?? t("Nothing to show.")} />;
}

export const minutes = (m: number | null) => (m === null ? "—" : fmtMinutes(Math.round(m)));
export const percent = (p: number | null) => (p === null ? "—" : `${p}%`);
