import { ArrowDown, ArrowUp } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Sparkline } from "./sparkline";
import { t } from "@/i18n";

export function StatCard({ label, value, delta, up, icon, color, bg, spark, caption = "vs last month" }: {
  label: string; value: string; delta: string; up: boolean; icon: React.ReactNode; color: string; bg: string; spark: string; caption?: string;
}) {
  return (
    <Card className="stat">
      <span className="sic" style={{ background: bg, color }}>{icon}</span>
      <div>
        <small>{label}</small>
        <div className="v">{value}</div>
        <div className={"delta " + (up ? "up" : "down")}>{up ? <ArrowUp size={15} /> : <ArrowDown size={15} />}{t(delta)}</div>
        <div className="lbl">{caption ? t(caption) : caption}</div>
      </div>
      <Sparkline path={spark} color={up ? color : "#14b8a6"} />
    </Card>
  );
}
