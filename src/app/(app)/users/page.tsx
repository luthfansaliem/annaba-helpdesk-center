"use client";

import Link from "next/link";
import { ArrowRight, MailWarning, Plus, UserCheck, UserX, Users } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Card, PanelHeader } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Avatar, avatarUrl } from "@/components/ui/avatar";
import { UserStatusBadge } from "@/components/ui/status";
import { StatCard } from "@/components/charts/stat-card";
import { LineChart } from "@/components/charts/line-chart";
import { DonutChart, DonutLegend } from "@/components/charts/donut-chart";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { UserStats } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { monthLabels, rel, sparkPath } from "@/lib/format";
import { t } from "@/i18n";

const colors: Record<string, string> = { active: "#16a37f", pending: "#f59e0b", suspended: "#ef4444", locked: "#8b5cf6", deactivated: "#94a3b8" };

export default function UsersOverviewPage() {
  const { can } = useAuth();
  const q = useQuery(() => get<UserStats>("/api/users/stats"), []);
  return (
    <Require perm="users.read">
      <PageHeader title={t("Users Overview")} description={t("Account health and registration trends.")} actions={can("users.create") && <Button variant="primary" href="/users/all?add=1"><Plus size={16} /> {t("Add User")}</Button>} />
      <QueryBoundary q={q}>{(s) => {
        const g = s.growthLast12Months; const max = Math.max(10, Math.ceil(Math.max(...g, 1) / 10) * 10);
        const seg = s.byStatus.filter((x) => x.count).map((x) => ({ label: t(x.key[0].toUpperCase() + x.key.slice(1)), value: x.count, color: colors[x.key] ?? "#94a3b8" }));
        return (
          <>
            <section className="stats" aria-label={t("User metrics")}>
              <StatCard label={t("Total Users")} value={String(s.total)} delta="all accounts" up icon={<Users size={24} />} color="#0b62e6" bg="#e5efff" spark={sparkPath(g)} caption="" />
              <StatCard label={t("Active Users")} value={String(s.active)} delta="can sign in" up icon={<UserCheck size={24} />} color="#16a37f" bg="#d9f5ef" spark={sparkPath(g.map((x) => x * 0.8))} caption="" />
              <StatCard label={t("Pending Verification")} value={String(s.pendingVerification)} delta="needs action" up={false} icon={<MailWarning size={24} />} color="#f59e0b" bg="#fff1d6" spark={sparkPath([2, 1, 3, 2, 2, 3, s.pendingVerification])} caption="" />
              <StatCard label={t("Suspended Users")} value={String(s.suspended)} delta="review" up={false} icon={<UserX size={24} />} color="#ef4444" bg="#ffe4e4" spark={sparkPath([0, 1, 1, 1, 2, 1, s.suspended])} caption="" />
            </section>
            <section className="g3 charts">
              <Card className="panel"><PanelHeader icon={<Users size={18} />} title={t("User Growth")} subtitle={t("Total users over 12 months")} />
                <LineChart series={[{ name: "Users", color: "#0b62e6", data: g }]} labels={monthLabels(12).filter((_, i) => i % 3 === 0 || i === 11).slice(0, 5)} max={max} ticks={[0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(max * f))} label={t("User growth")} /></Card>
              <Card className="panel"><PanelHeader icon={<UserCheck size={18} />} title={t("Status distribution")} />
                <div className="donut-wrap" style={{ marginTop: 18 }}><DonutChart segments={seg} centerLabel={t("Users")} label={t("Status distribution")} /><DonutLegend segments={seg} /></div></Card>
              <Card className="panel"><PanelHeader icon={<Users size={18} />} title={t("Recent registrations")} trailing={<Link className="va" href="/users/all">{t("View all")}{" "}<ArrowRight size={13} /></Link>} />
                <ul className="mini-list">{s.recentRegistrations.map((x) => <li key={x.id}><Avatar name={x.name} size={28} src={avatarUrl(x.id, x.avatarVersion)} /><div><Link href={`/users/${x.id}`}><strong>{x.name}</strong></Link><small>{rel(x.createdAt)}</small></div><UserStatusBadge status={x.status} locked={x.locked} /></li>)}</ul></Card>
            </section>
          </>
        );
      }}</QueryBoundary>
    </Require>
  );
}
