"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Calendar } from "lucide-react";
import { Building } from "@/components/logo";
import { formatLocale, t } from "@/i18n";

export type HeroHighlight = { label: string; href?: string; tone?: "warn" | "info" | "ok" };

function greeting(h: number) { return h < 12 ? t("Good morning") : h < 18 ? t("Good afternoon") : t("Good evening"); }

/** Personal greeting banner: time-aware hello, today's date and "what needs attention" chips. */
export function GreetingHero({ name, subtitle, highlights = [] }: {
  name: string; subtitle: string; highlights?: HeroHighlight[];
}) {
  const [now, setNow] = useState<Date | null>(null);
  useEffect(() => { setNow(new Date()); const t = setInterval(() => setNow(new Date()), 60_000); return () => clearInterval(t); }, []);
  const h = now?.getHours() ?? 9;
  const date = now?.toLocaleDateString(formatLocale(), { weekday: "short", day: "2-digit", month: "short", year: "numeric" }) ?? "";
  return (
    <section className="hero" aria-label={t("Greeting")}>
      <div style={{ position: "relative", zIndex: 1 }}>
        <h1>{greeting(h)}, {name} 👋</h1>
        <p>{subtitle}</p>
        {highlights.length > 0 && (
          <div className="hero-chips">
            {highlights.map((c) => {
              const cls = "hero-chip " + (c.tone ?? "info");
              return c.href ? <Link key={c.label} href={c.href} className={cls}>{c.label}</Link> : <span key={c.label} className={cls}>{c.label}</span>;
            })}
          </div>
        )}
      </div>
      <Building className="bld" />
      <div className="meta">
        <div className="pillbox"><Calendar size={18} /> {date || "—"}</div>
      </div>
    </section>
  );
}
