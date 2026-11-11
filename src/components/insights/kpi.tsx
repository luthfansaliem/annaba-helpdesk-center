import Link from "next/link";
import { cn } from "@/lib/cn";

export type Tone = "default" | "good" | "warn" | "bad";

/** A strip of KPI cards. The grid fits as many columns as there is room for, so a short last row never leaves one stretched card. */
export function KpiGrid({ children, label }: { children: React.ReactNode; label?: string }) {
  return <section className="kpis" aria-label={label}>{children}</section>;
}

/** One number with what it means. Colour is never the only signal: the note says it in words. An `href` makes the card a link to the list behind the number. */
export function KpiCard({ label, value, note, tone = "default", icon, href }: { label: string; value: React.ReactNode; note?: React.ReactNode; tone?: Tone; icon?: React.ReactNode; href?: string }) {
  const body = (
    <>
      {icon && <span className={cn("ki", tone !== "default" && tone)} aria-hidden>{icon}</span>}
      <div>
        <small>{label}</small>
        <div className={cn("v", tone !== "default" && tone)}>{value}</div>
        {note && <div className="n">{note}</div>}
      </div>
    </>
  );
  return href ? <Link href={href} className="card kpi">{body}</Link> : <div className="card kpi">{body}</div>;
}
