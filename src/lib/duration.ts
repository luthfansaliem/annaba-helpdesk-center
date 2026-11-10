import { t } from "@/i18n";

/** Minutes as people say them: 15 min, 4 h, 1 d 2 h. */
export function fmtMinutes(minutes: number): string {
  if (minutes < 60) return `${minutes} ${t("min")}`;
  if (minutes < 1440) { const h = Math.floor(minutes / 60); const m = minutes % 60; return m ? `${h} ${t("h")} ${m} ${t("min")}` : `${h} ${t("h")}`; }
  const d = Math.floor(minutes / 1440); const h = Math.floor((minutes % 1440) / 60);
  return h ? `${d} ${t("d")} ${h} ${t("h")}` : `${d} ${t("d")}`;
}
