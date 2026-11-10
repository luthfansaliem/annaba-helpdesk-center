"use client";

import { Card } from "@/components/ui/card";
import { Field, SelectInput } from "@/components/ui/field";
import { fmtMinutes } from "@/lib/duration";
import { t } from "@/i18n";

export const PERIODS = [7, 30, 90] as const;

export function PeriodSelect({ days, onChange }: { days: number; onChange: (d: number) => void }) {
  return (
    <Field label={t("Period")} htmlFor="rp-days">
      <SelectInput id="rp-days" value={String(days)} onChange={(e) => onChange(Number(e.target.value))} options={PERIODS.map((d) => ({ value: String(d), label: t("Last {n} days", { n: d }) }))} />
    </Field>
  );
}

export type Tone = "default" | "bad" | "warn" | "good";
const toneColor: Record<Tone, string> = { default: "inherit", bad: "#dc2626", warn: "#d97706", good: "#15803d" };

/** One number with its label (and a short note). The colour is never the only signal: the note says what it means. */
export function Kpi({ label, value, note, tone = "default" }: { label: string; value: string; note?: string; tone?: Tone }) {
  return (
    <Card className="stat">
      <div>
        <small>{label}</small>
        <div className="v" style={{ color: toneColor[tone] }}>{value}</div>
        {note && <div className="lbl">{note}</div>}
      </div>
    </Card>
  );
}

/** Horizontal bars for a short list of counts; each row reads as text too ("Label — 12"). */
export function BarList({ items, color = "#0b62e6", empty }: { items: { label: string; count: number }[]; color?: string; empty?: string }) {
  const max = Math.max(1, ...items.map((i) => i.count));
  if (items.length === 0 || items.every((i) => i.count === 0)) return <p className="tab-placeholder">{empty ?? t("Nothing to show.")}</p>;
  return (
    <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 8 }}>
      {items.map((i) => (
        <li key={i.label}>
          <span className="row" style={{ justifyContent: "space-between" }}><span>{i.label}</span><b>{i.count}</b></span>
          <div className="bar" aria-hidden><i style={{ width: `${(100 * i.count) / max}%`, background: color }} /></div>
        </li>
      ))}
    </ul>
  );
}

export const minutes = (m: number | null) => (m === null ? "—" : fmtMinutes(Math.round(m)));
export const percent = (p: number | null) => (p === null ? "—" : `${p}%`);
