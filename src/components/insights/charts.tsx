import { DonutChart, DonutLegend, type DonutSegment } from "@/components/charts/donut-chart";
import { ChartLegend } from "@/components/charts/line-chart";
import { t } from "@/i18n";

export const PALETTE = ["#0b62e6", "#14b8a6", "#f59e0b", "#8b5cf6", "#f4694f", "#16a37f", "#64748b", "#ec4899"];

/** A titled card for one chart. */
export function ChartCard({ title, sub, action, children, className, style }: { title: string; sub?: string; action?: React.ReactNode; children: React.ReactNode; className?: string; style?: React.CSSProperties }) {
  return (
    <section className={"card chart-card" + (className ? " " + className : "")} style={style}>
      <div className="head"><div><h2>{title}</h2>{sub && <p className="sub">{sub}</p>}</div>{action}</div>
      {children}
    </section>
  );
}

export function ChartEmpty({ text }: { text?: string }) {
  return <p className="chart-empty">{text ?? t("Nothing to show yet.")}</p>;
}

/** Horizontal bars for a short ranked list. Every row reads as text too ("Label — 12"); `format` renders the number (money, %). */
export function Bars({ items, color = PALETTE[0], empty, format = String, max }: {
  items: { label: string; value: number; color?: string; href?: string }[]; color?: string; empty?: string; format?: (n: number) => string; max?: number;
}) {
  const top = Math.max(1, max ?? Math.max(0, ...items.map((i) => i.value)));
  if (items.length === 0 || items.every((i) => i.value === 0)) return <ChartEmpty text={empty} />;
  return (
    <ul className="bars">
      {items.map((i) => (
        <li key={i.label}>
          <div className="top"><span title={i.label}>{i.href ? <a href={i.href}>{i.label}</a> : i.label}</span><b>{format(i.value)}</b></div>
          <div className="bar" aria-hidden><i style={{ width: `${Math.min(100, (100 * i.value) / top)}%`, background: i.color ?? color }} /></div>
        </li>
      ))}
    </ul>
  );
}

const niceTop = (v: number) => { if (v <= 5) return 5; const p = Math.pow(10, Math.floor(Math.log10(v))); const m = v / p; return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p; };

/**
 * Vertical columns: one group per label with one bar per series (side by side), or stacked when `stacked`. Plain SVG, scales with its card.
 * A screen reader gets the numbers as a table (visually hidden), not a picture.
 */
export function ColumnChart({ labels, series, stacked, height = 190, format = String, label }: {
  labels: string[]; series: { name: string; color: string; data: number[] }[]; stacked?: boolean; height?: number; format?: (n: number) => string; label: string;
}) {
  const totals = labels.map((_, i) => (stacked ? series.reduce((a, s) => a + (s.data[i] ?? 0), 0) : Math.max(0, ...series.map((s) => s.data[i] ?? 0))));
  const top = niceTop(Math.max(1, ...totals));
  const W = 600, H = height, B = 26, T = 8, R = 6;
  // room on the left for the longest axis label (money labels are wider than counts)
  const L = 16 + 6.4 * Math.max(...[0, 0.5, 1].map((f) => format(Math.round(top * f * 100) / 100).length));
  const y = (v: number) => T + (H - T - B) * (1 - v / top);
  const slot = (W - L - R) / Math.max(1, labels.length);
  const groupW = Math.min(slot * 0.72, stacked ? 34 : 18 * series.length + 6);
  const barW = stacked ? groupW : (groupW - (series.length - 1) * 2) / series.length;
  const every = Math.ceil(labels.length / 8);
  const ticks = [0, 0.5, 1].map((f) => top * f);
  if (totals.every((v) => v === 0)) return <ChartEmpty />;
  return (
    <>
      <svg viewBox={`0 0 ${W} ${H}`} width="100%" role="img" aria-label={label}>
        {ticks.map((v) => (
          <g key={v}>
            <line x1={L} x2={W - R} y1={y(v)} y2={y(v)} style={{ stroke: "var(--line)" }} />
            <text x={L - 8} y={y(v) + 4} fontSize="11" textAnchor="end" style={{ fill: "var(--ink-soft)" }}>{format(Math.round(v * 100) / 100)}</text>
          </g>
        ))}
        {labels.map((l, i) => {
          const x0 = L + slot * i + (slot - groupW) / 2;
          let acc = 0;
          return (
            <g key={l + i}>
              {series.map((s, k) => {
                const v = s.data[i] ?? 0;
                if (v <= 0) return null;
                const x = stacked ? x0 : x0 + k * (barW + 2);
                const yTop = stacked ? y(acc + v) : y(v);
                const yBot = stacked ? y(acc) : y(0);
                acc += stacked ? v : 0;
                return <rect key={s.name} x={x} y={yTop} width={barW} height={Math.max(1, yBot - yTop)} rx={3} fill={s.color}><title>{`${l} · ${s.name}: ${format(v)}`}</title></rect>;
              })}
              {i % every === 0 && <text x={L + slot * i + slot / 2} y={H - 8} fontSize="11" textAnchor="middle" style={{ fill: "var(--ink-soft)" }}>{l}</text>}
            </g>
          );
        })}
      </svg>
      {series.length > 1 && <ChartLegend items={series.map((s) => ({ name: s.name, color: s.color }))} />}
      <table className="sr-only">
        <caption>{label}</caption>
        <thead><tr><th>{t("Period")}</th>{series.map((s) => <th key={s.name}>{s.name}</th>)}</tr></thead>
        <tbody>{labels.map((l, i) => <tr key={l + i}><td>{l}</td>{series.map((s) => <td key={s.name}>{format(s.data[i] ?? 0)}</td>)}</tr>)}</tbody>
      </table>
    </>
  );
}

/** Donut with its legend; nothing drawn when every value is zero. */
export function Donut({ segments, center, label, value, format }: { segments: DonutSegment[]; center: string; label: string; value?: string; format?: (n: number) => string }) {
  const segs = segments.filter((s) => s.value > 0);
  if (segs.length === 0) return <ChartEmpty />;
  return <div className="donut-wrap"><DonutChart segments={segs} centerLabel={center} label={label} centerValue={value} /><DonutLegend segments={segs} format={format} /></div>;
}
