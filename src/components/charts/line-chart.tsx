import { useId } from "react";

export type Series = { name: string; color: string; data: number[] };

/** A smooth curve through the points that never overshoots them: each control point stays between its two neighbours' heights,
 *  so a run of zeros stays flat and the line never dips below the axis (a plain Catmull-Rom curve did). */
function smooth(pts: [number, number][]) {
  let d = `M${pts[0][0]} ${pts[0][1]}`;
  const clamp = (v: number, a: number, b: number) => Math.min(Math.max(v, Math.min(a, b)), Math.max(a, b));
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] ?? pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] ?? p2;
    const c1y = clamp(p1[1] + (p2[1] - p0[1]) / 6, p1[1], p2[1]), c2y = clamp(p2[1] - (p3[1] - p1[1]) / 6, p1[1], p2[1]);
    d += ` C${p1[0] + (p2[0] - p0[0]) / 6} ${c1y}, ${p2[0] - (p3[0] - p1[0]) / 6} ${c2y}, ${p2[0]} ${p2[1]}`;
  }
  return d;
}

export function LineChart({ series, labels, max = 100, ticks = [0, 25, 50, 75, 100], label, width = 560, height = 200 }: {
  series: Series[]; labels: string[]; max?: number; ticks?: number[]; label?: string; width?: number; height?: number;
}) {
  const uid = useId().replace(/:/g, "");
  const W = width, H = height, L = 40, B = 24, T = 6;
  const n = Math.max(...series.map((s) => s.data.length));
  const x = (i: number) => L + (i * (W - L - 6)) / (n - 1);
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  return (
    <svg viewBox={`0 0 ${W} ${H + 4}`} width="100%" role="img" aria-label={label ?? "Line chart"}>
      <defs>
        {series.map((s, i) => (
          <linearGradient key={s.name} id={`${uid}-${i}`} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor={s.color} stopOpacity=".22" /><stop offset="1" stopColor={s.color} stopOpacity="0" />
          </linearGradient>
        ))}
      </defs>
      {ticks.map((v) => (
        <g key={v}><line x1={L} x2={W} y1={y(v)} y2={y(v)} style={{ stroke: "var(--line)" }} /><text x={L - 12} y={y(v) + 4} fontSize="11" textAnchor="end" style={{ fill: "var(--ink-strong)" }}>{v}</text></g>
      ))}
      {labels.map((l, i) => <text key={l + i} x={L + (i * (W - L - 6)) / Math.max(1, labels.length - 1)} y={H + 2} fontSize="11" textAnchor={i === 0 ? "start" : i === labels.length - 1 ? "end" : "middle"} style={{ fill: "var(--ink-strong)" }}>{l}</text>)}
      {series.map((s, i) => ({ s, i })).reverse().map(({ s, i }) => {
        const pts = s.data.map((v, k) => [x(k), y(v)] as [number, number]);
        const line = smooth(pts);
        return (
          <g key={s.name}>
            <path d={`${line} L${x(s.data.length - 1)} ${y(0)} L${x(0)} ${y(0)} Z`} fill={`url(#${uid}-${i})`} />
            <path d={line} fill="none" stroke={s.color} strokeWidth="2.5" strokeLinecap="round" />
          </g>
        );
      })}
    </svg>
  );
}

export function ChartLegend({ items }: { items: { name: string; color: string }[] }) {
  return <div className="legend">{items.map((s) => <span key={s.name}><i style={{ background: s.color }} />{s.name}</span>)}</div>;
}
