import { formatLocale, getLang, t } from "@/i18n";

export const fmtDate = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleDateString(formatLocale(), { day: "2-digit", month: "short", year: "numeric" }) : "—");
export const fmtDateTime = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleString(formatLocale(), { day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false }) : "—");

export function rel(iso: string | null | undefined, now = Date.now()): string {
  if (!iso) return t("Never");
  const m = Math.round((now - new Date(iso).getTime()) / 60000);
  const id = getLang() === "id";
  const f = (n: number, u: "min" | "hour" | "day") => (id ? `${n} ${{ min: "menit", hour: "jam", day: "hari" }[u]}` : `${n} ${u === "min" ? "min" : u}${n === 1 ? "" : "s"}`);
  if (m < 0) { const a = -m; const w = a < 60 ? f(a, "min") : a < 1440 ? f(Math.round(a / 60), "hour") : f(Math.round(a / 1440), "day"); return id ? `dalam ${w}` : `in ${w}`; }
  if (m < 1) return t("Just now");
  const w = m < 60 ? f(m, "min") : m < 1440 ? f(Math.round(m / 60), "hour") : m < 43200 ? f(Math.round(m / 1440), "day") : null;
  return w === null ? fmtDate(iso) : id ? `${w} lalu` : `${w} ago`;
}

export function sparkPath(vals: number[]) {
  if (vals.length < 2) return "M0 40 L84 40";
  const min = Math.min(...vals), max = Math.max(...vals), span = max - min || 1;
  return vals.map((v, i) => `${i ? "L" : "M"}${(i * 84) / (vals.length - 1)} ${40 - ((v - min) / span) * 34}`).join(" ");
}

export function monthLabels(n = 12, now = new Date()) {
  return Array.from({ length: n }, (_, i) => new Date(now.getTime() - (n - 1 - i) * 30 * 864e5).toLocaleDateString(formatLocale(), { month: "short" }));
}

/** Only same-origin relative paths are honoured (mirrors the server rule). */
export function safeReturnTo(v: string | null | undefined, fallback = "/dashboard") {
  if (!v || !v.startsWith("/") || v.startsWith("//") || v.startsWith("/\\") || /[\r\n\\]/.test(v) || v.startsWith("/login")) return fallback;
  return v;
}
