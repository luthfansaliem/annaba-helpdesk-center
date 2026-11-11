"use client";

import Link from "next/link";
import { ArrowRight, BarChart3, CheckCircle2, Circle, Folder, KeyRound, MailPlus, MonitorSmartphone, Plus, ScrollText, ShieldAlert, ShieldCheck, UserCheck, UserX, Users } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Card, PanelHeader } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, avatarUrl } from "@/components/ui/avatar";
import { UserStatusBadge, SeverityBadge } from "@/components/ui/status";
import { StatCard } from "@/components/charts/stat-card";
import { LineChart, ChartLegend } from "@/components/charts/line-chart";
import { DonutChart, DonutLegend } from "@/components/charts/donut-chart";
import { QueryBoundary } from "@/components/query-boundary";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { AdminDashboard, UserDashboard } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { monthLabels, rel, sparkPath } from "@/lib/format";
import { t } from "@/i18n";

const viewAll = (href: string) => <Link className="va" href={href}>{t("View all")}{" "}<ArrowRight size={13} /></Link>;
const palette = ["#0b62e6", "#f4694f", "#14b8a6", "#f59e0b", "#8b5cf6"];
const statusColor: Record<string, string> = { active: "#16a37f", pending: "#f59e0b", suspended: "#ef4444", deactivated: "#94a3b8" };

function Admin({ d }: { d: AdminDashboard }) {
  const k = d.kpis; const v = (id: string) => d.visible.includes(id);
  const growth = d.userGrowth ?? [];
  const gMax = Math.max(10, Math.ceil(Math.max(...growth, 1) / 10) * 10);
  const login = d.loginActivity; const lMax = Math.max(10, Math.ceil(Math.max(...(login?.success ?? [0]), ...(login?.failed ?? [0])) / 10) * 10);
  const labels = (n: number) => { const all = monthLabels(n); return all.filter((_, i) => i % 3 === 0 || i === n - 1).slice(0, 5); };
  const cards: React.ReactNode[] = [];
  if (k.totalUsers !== undefined) cards.push(<StatCard key="t" label={t("Total Users")} value={String(k.totalUsers)} delta={t("all accounts")} up icon={<Users size={22} />} color="#0b62e6" bg="#e5efff" spark={sparkPath(growth)} caption="" />);
  if (k.activeUsers !== undefined) cards.push(<StatCard key="a" label={t("Active Users")} value={String(k.activeUsers)} delta={`${Math.round(((k.activeUsers ?? 0) / Math.max(1, k.totalUsers ?? 1)) * 100)}%`} up icon={<UserCheck size={22} />} color="#16a37f" bg="#d9f5ef" spark={sparkPath(growth.map((g) => g * 0.8))} caption="of all users" />);
  if (k.suspendedUsers !== undefined) cards.push(<StatCard key="s" label={t("Suspended Users")} value={String(k.suspendedUsers)} delta={t("review")} up={false} icon={<UserX size={22} />} color="#ef4444" bg="#ffe4e4" spark={sparkPath([0, 1, 1, 1, k.suspendedUsers])} caption="accounts" />);
  if (k.pendingInvitations !== undefined) cards.push(<StatCard key="i" label={t("Pending Invitations")} value={String(k.pendingInvitations)} delta={t("open")} up icon={<MailPlus size={22} />} color="#f59e0b" bg="#fff1d6" spark={sparkPath([0, 1, 1, 2, k.pendingInvitations])} caption="awaiting acceptance" />);
  if (k.totalRoles !== undefined) cards.push(<StatCard key="r" label={t("Total Roles")} value={String(k.totalRoles)} delta={t("active")} up icon={<ShieldCheck size={22} />} color="#8b5cf6" bg="#eadfff" spark={sparkPath([3, 3, 4, 4, k.totalRoles])} caption="not archived" />);
  if (k.activeSessions !== undefined) cards.push(<StatCard key="x" label={t("Active Sessions")} value={String(k.activeSessions)} delta={t("live")} up icon={<MonitorSmartphone size={22} />} color="#14b8a6" bg="#d9f5ef" spark={sparkPath([6, 8, 7, 9, k.activeSessions])} caption="across all users" />);
  const statusSeg = (d.userStatus ?? []).map((s) => ({ label: t(s.key[0].toUpperCase() + s.key.slice(1)), value: s.count, color: statusColor[s.key] ?? "#94a3b8" }));
  const roleSeg = (d.roleDistribution ?? []).map((s, i) => ({ label: s.key, value: s.count, color: palette[i % palette.length] }));

  return (
    <>
      {cards.length > 0 && <section className="stats six" aria-label={t("Key metrics")}>{cards}</section>}
      {v("chart.growth") && (
        <section className="g3 charts">
          <Card className="panel"><PanelHeader icon={<Users size={18} />} title={t("User Growth")} subtitle={t("Total users, last 12 months")} />
            <LineChart series={[{ name: "Users", color: "#0b62e6", data: growth }]} labels={labels(12)} max={gMax} ticks={[0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(gMax * f))} label={t("User growth line chart")} /></Card>
          <Card className="panel"><PanelHeader icon={<UserCheck size={18} />} title={t("User Status")} subtitle={t("Distribution by account state")} />
            <div className="donut-wrap" style={{ marginTop: 18 }}><DonutChart segments={statusSeg} centerLabel={t("Users")} label={t("User status distribution")} /><DonutLegend segments={statusSeg} /></div></Card>
          <Card className="panel"><PanelHeader icon={<ShieldCheck size={18} />} title={t("Role Distribution")} subtitle={t("Users per role")} />
            <div className="donut-wrap" style={{ marginTop: 18 }}><DonutChart segments={roleSeg} centerLabel={t("Assignments")} label={t("Role distribution")} /><DonutLegend segments={roleSeg} /></div></Card>
        </section>
      )}
      {login && (
        <Card className="panel" style={{ marginBottom: 12 }}>
          <PanelHeader icon={<ScrollText size={18} />} title={t("Login Activity")} subtitle={t("Successful vs failed sign-ins, last 14 days")} />
          <LineChart series={[{ name: "Successful", color: "#16a37f", data: login.success }, { name: "Failed", color: "#ef4444", data: login.failed }]} labels={[login.days[0], login.days[3], login.days[7], login.days[10], login.days[13]].map((x) => x.slice(5))} max={lMax} ticks={[0, 0.25, 0.5, 0.75, 1].map((f) => Math.round(lMax * f))} label={t("Login activity chart")} width={1100} height={220} />
          <ChartLegend items={[{ name: "Successful", color: "#16a37f" }, { name: "Failed", color: "#ef4444" }]} />
        </Card>
      )}
      <section className="g2 panels">
        {d.recentRegistrations && <Card className="panel"><PanelHeader icon={<Users size={18} />} title={t("Recent Registrations")} trailing={viewAll("/users/all")} />
          <ul className="mini-list">{d.recentRegistrations.map((x) => <li key={x.id}><Avatar name={x.name} size={30} src={avatarUrl(x.id, x.avatarVersion)} /><div><Link href={`/users/${x.id}`}><strong>{x.name}</strong></Link><small>{x.email} · {rel(x.createdAt)}</small></div><UserStatusBadge status={x.status} locked={x.locked} /></li>)}</ul></Card>}
        {d.recentRoleChanges && <Card className="panel"><PanelHeader icon={<KeyRound size={18} />} title={t("Recent Role Changes")} trailing={viewAll("/audit?category=role")} />
          {d.recentRoleChanges.length ? <ul className="mini-list">{d.recentRoleChanges.map((a, i) => <li key={i}><div><strong>{a.action}</strong><small>{a.target} · {rel(a.ts)} {t("· by")}{" "}{a.actor}</small></div></li>)}</ul> : <p className="tab-placeholder">{t("No role changes yet.")}</p>}</Card>}
        {d.recentSecurityEvents && <Card className="panel"><PanelHeader icon={<ShieldAlert size={18} />} title={t("Recent Security Events")} trailing={viewAll("/security-events")} />
          <ul className="mini-list">{d.recentSecurityEvents.map((e) => <li key={e.id}><div><strong>{e.type}</strong><small>{e.detail} · {rel(e.ts)}</small></div><SeverityBadge severity={e.severity} /></li>)}</ul></Card>}
        {d.recentLogins && <Card className="panel"><PanelHeader icon={<Folder size={18} />} title={t("Recent Sign-ins")} trailing={viewAll("/audit?category=auth")} />
          <ul className="mini-list">{d.recentLogins.map((l, i) => <li key={i}><div><strong>{l.actor}</strong><small>{rel(l.ts)}</small></div><Badge tone="green">{t("Success")}</Badge></li>)}</ul></Card>}
        {d.pendingInvitations && <Card className="panel"><PanelHeader icon={<MailPlus size={18} />} title={t("Pending Invitations")} trailing={viewAll("/users/invitations")} />
          {d.pendingInvitations.length ? <ul className="mini-list">{d.pendingInvitations.map((i) => <li key={i.id}><div><strong>{i.email}</strong><small>{i.roles.join(", ")} {t("· expires")}{" "}{rel(i.expiresAt)}</small></div><Badge tone="orange">{t("Pending")}</Badge></li>)}</ul> : <p className="tab-placeholder">{t("No pending invitations.")}</p>}</Card>}
        <Card className="panel"><PanelHeader icon={<ArrowRight size={18} />} title={t("Quick Actions")} />
          <div className="stack">
            {d.quickActions.includes("add_user") && <Button variant="primary" href="/users/all?add=1"><Plus size={15} /> {t("Add User")}</Button>}
            {d.quickActions.includes("invite_user") && <Button href="/users/invitations?new=1"><MailPlus size={15} /> {t("Invite someone")}</Button>}
            {d.quickActions.includes("review_access_matrix") && <Button href="/access-matrix"><KeyRound size={15} /> {t("Review access matrix")}</Button>}
            {d.quickActions.includes("open_audit_log") && <Button href="/audit"><ScrollText size={15} /> {t("Open audit log")}</Button>}
            {d.quickActions.includes("review_security_events") && <Button href="/security-events"><ShieldAlert size={15} /> {t("Security events")}</Button>}
          </div></Card>
      </section>
    </>
  );
}

function Mine({ d }: { d: UserDashboard }) {
  const c = d.profileCompletion;
  return (
    <section className="g2 panels">
      <Card className="panel"><PanelHeader icon={<UserCheck size={18} />} title={t("Account")} subtitle={t("Your account status")} />
        <div className="kv"><span>{t("Status")}</span><UserStatusBadge status={d.status} /><span>{t("Email")}</span><b>{d.email}</b><span>{t("Verification")}</span><Badge tone={d.emailVerified ? "green" : "orange"}>{d.emailVerified ? t("Verified") : t("Pending")}</Badge><span>{t("Roles")}</span><b>{d.roles.map((r) => r.name).join(", ")}</b><span>{t("Last sign-in")}</span><b>{rel(d.lastLoginAt)}</b></div></Card>
      <Card className="panel"><PanelHeader icon={<KeyRound size={18} />} title={t("Effective permissions")} subtitle={t("{n} granted", { n: d.permissionCount })} />
        {d.permissions.length ? <div className="chips">{d.permissions.map((p) => <code key={p}>{p}</code>)}</div> : <p className="tab-placeholder">{t("Your role grants self-service access only (profile, sessions, security).")}</p>}</Card>
      <Card className="panel"><PanelHeader icon={<ShieldCheck size={18} />} title={t("Security status")} />
        <div className="kv"><span>{t("MFA")}</span><Badge tone={d.mfaEnabled ? "green" : "orange"}>{d.mfaEnabled ? t("Enabled") : t("Not enabled")}</Badge><span>{t("Active sessions")}</span><b>{d.activeSessions}</b><span>{t("Policy")}</span><b>{t("MFA")}{" "}{d.mfaPolicy}</b></div>
        {!d.mfaEnabled && <Button href="/profile?tab=security" size="sm" style={{ marginTop: 10 }}>{t("Set up MFA")}</Button>}</Card>
      <Card className="panel"><PanelHeader icon={<Users size={18} />} title={t("Profile completion")} subtitle={t("{n}% complete", { n: c.percent })} />
        <div className="bar" role="progressbar" aria-valuenow={c.percent} aria-valuemin={0} aria-valuemax={100}><i style={{ width: c.percent + "%", background: "#0b62e6" }} /></div>
        <ul className="checklist">{["name", "phone", "department", "jobTitle", "avatar", "emailVerified", "mfa"].map((l) => { const ok = !c.missing.includes(l); return <li key={l} className={ok ? "ok" : ""}>{ok ? <CheckCircle2 size={14} /> : <Circle size={14} />} {t(({ name: "Name", phone: "Phone", department: "Department", jobTitle: "Job title", avatar: "Photo", emailVerified: "E-mail verified", mfa: "Two-step verification" } as Record<string, string>)[l] ?? l)}</li>; })}</ul></Card>
      <Card className="panel"><PanelHeader icon={<ScrollText size={18} />} title={t("My recent activity")} />
        {d.recentActivity.length ? <ul className="mini-list">{d.recentActivity.map((a, i) => <li key={i}><div><strong>{a.action}</strong><small>{rel(a.ts)}</small></div></li>)}</ul> : <p className="tab-placeholder">{t("No recent activity.")}</p>}</Card>
      <Card className="panel"><PanelHeader icon={<BarChart3 size={18} />} title={t("Quick links")} />
        <div className="stack">{d.quickLinks.map((l) => <Button key={l} href={l}>{l.replace("/", "").replace(/-/g, " ") || "Home"}</Button>)}</div></Card>
    </section>
  );
}

/** People & access → Access overview: the organisation-wide account and security picture that used to be the home page (docs/52).
 *  The home page is now about the helpdesk; this stays one click away for the people who look after accounts. */
export default function AccessOverviewPage() {
  const q = useQuery(async () => {
    const admin = await get<AdminDashboard>("/api/dashboard/admin");
    if (admin.visible.length > 0) return { kind: "admin" as const, admin };
    return { kind: "me" as const, me: await get<UserDashboard>("/api/dashboard/me") };
  }, []);
  const data = q.data;
  const chips: { label: string; href?: string; tone: "warn" | "info" | "ok" }[] = [];
  if (data?.kind === "admin") {
    const unack = data.admin.recentSecurityEvents?.filter((e) => !e.acknowledged).length ?? 0; const inv = data.admin.kpis.pendingInvitations ?? 0;
    if (unack) chips.push({ label: t("{unack}+ security event{v1} to review", { unack: unack, v1: unack > 1 ? "s" : "" }), href: "/security-events", tone: "warn" });
    if (inv) chips.push({ label: t("{inv} pending invitation{v1}", { inv: inv, v1: inv > 1 ? "s" : "" }), href: "/users/invitations", tone: "info" });
    if (!chips.length) chips.push({ label: t("All clear — nothing needs attention"), tone: "ok" });
  }
  return (
    <>
      <PageHeader title={t("Access overview")} description={t("Accounts, roles, sign-ins and security events across the organisation.")} />
      {chips.length > 0 && <div className="hero-chips" style={{ margin: "-4px 0 12px" }}>{chips.map((c) => c.href ? <Link key={c.label} href={c.href} className={"hero-chip " + c.tone}>{c.label}</Link> : <span key={c.label} className={"hero-chip " + c.tone}>{c.label}</span>)}</div>}
      <QueryBoundary q={q} rows={6}>{(r) => r.kind === "admin" ? <Admin d={r.admin} /> : <Mine d={r.me} />}</QueryBoundary>
      {q.data && q.data.kind === "admin" && q.data.admin.visible.length === 0 && <EmptyState title={t("Nothing to show")} description={t("Your role has no organisation-wide data.")} />}
    </>
  );
}
