import { useId } from "react";

export function Sparkline({ path, color }: { path: string; color: string }) {
  const id = useId().replace(/:/g, "");
  return (
    <svg className="spark" viewBox="0 0 84 44" preserveAspectRatio="none" aria-hidden>
      <defs><linearGradient id={id} x1="0" x2="0" y1="0" y2="1"><stop offset="0" stopColor={color} stopOpacity=".2" /><stop offset="1" style={{ stopColor: "var(--surface)" }} stopOpacity="0" /></linearGradient></defs>
      <path d={`${path} L84 44 L0 44 Z`} fill={`url(#${id})`} />
      <path d={path} fill="none" stroke={color} strokeWidth="2" strokeLinejoin="round" />
    </svg>
  );
}
