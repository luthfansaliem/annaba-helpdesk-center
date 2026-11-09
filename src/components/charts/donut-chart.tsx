export type DonutSegment = { label: string; value: number; color: string };

export function DonutChart({ segments, centerLabel, label }: { segments: DonutSegment[]; centerLabel: string; label?: string }) {
  const total = segments.reduce((a, s) => a + s.value, 0);
  const r = 62, c = 2 * Math.PI * r;
  let off = 0;
  return (
    <svg viewBox="0 0 160 160" width="160" height="160" role="img" aria-label={label ?? "Donut chart"}>
      <g transform="rotate(-90 80 80)">
        {segments.map((s) => {
          const len = (s.value / total) * c;
          const el = <circle key={s.label} cx="80" cy="80" r={r} fill="none" stroke={s.color} strokeWidth="18" strokeDasharray={`${len - 2} ${c - len + 2}`} strokeDashoffset={-off} />;
          off += len;
          return el;
        })}
      </g>
      <text x="80" y="82" textAnchor="middle" fontSize="26" fontWeight="800" style={{ fill: "var(--navy)" }}>{total}</text>
      <text x="80" y="100" textAnchor="middle" fontSize="11" style={{ fill: "var(--muted)" }}>{centerLabel}</text>
    </svg>
  );
}

export function DonutLegend({ segments }: { segments: DonutSegment[] }) {
  return <ul className="donut-legend">{segments.map((s) => <li key={s.label}><i className="dot" style={{ background: s.color }} />{s.label}<b>{s.value}</b></li>)}</ul>;
}
