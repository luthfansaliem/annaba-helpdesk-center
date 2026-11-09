"use client";

import { useState } from "react";
import { Laptop, Smartphone } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { EmptyState } from "@/components/data/empty-state";
import { QueryBoundary } from "@/components/query-boundary";
import { del, get } from "@/lib/api/client";
import type { AllSession, MySession, Paged } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime, rel } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const icon = (d: string) => (/phone|iphone|android/i.test(d) ? <Smartphone size={18} /> : <Laptop size={18} />);

function Mine() {
  const { can, signOut } = useAuth(); const run = useRun();
  const q = useQuery(() => get<MySession[]>("/api/sessions/me"), []);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  void can;
  const revoke = (s: MySession) => {
    const go = async () => {
      const r = await run(() => del<{ signedOut: boolean }>(`/api/sessions/me/${s.id}`), s.current ? undefined : "Session revoked.");
      if (r.ok) { if (r.data.signedOut) await signOut(); else q.reload(); }
    };
    if (s.current) setConfirm({ title: t("Sign out of this session?"), confirmLabel: t("Sign out"), danger: true, body: <>{t("You will be signed out of this device.")}</>, onConfirm: go }); else go();
  };
  const cols: Column<MySession>[] = [
    { key: "d", header: t("Device"), cell: (s) => <div className="who">{icon(s.device)}<span><b>{s.device} {s.current && <Badge tone="green" dot={false}>{t("This session")}</Badge>}</b><small className="sub">{s.browser}{s.remember ? t(" · remembered") : ""}</small></span></div> },
    { key: "l", header: t("IP"), className: "hide-sm", cell: (s) => s.ip ?? "—" },
    { key: "c", header: t("Created"), className: "hide-md", cell: (s) => fmtDateTime(s.createdAt) },
    { key: "a", header: t("Last active"), cell: (s) => rel(s.lastActiveAt) },
    { key: "e", header: t("Expires"), className: "hide-md", cell: (s) => rel(s.expiresAt) },
    { key: "x", header: t("Actions"), cell: (s) => <Button size="sm" variant="danger-outline" onClick={() => revoke(s)}>{s.current ? t("Sign out") : t("Revoke")}</Button> },
  ];
  return (
    <QueryBoundary q={q} rows={4}>{(rows) => (
      <>
        <div className="sess-actions" style={{ display: "flex", gap: 8, flexWrap: "wrap", margin: "0 0 12px" }}>
          <Button disabled={rows.length < 2} onClick={() => setConfirm({ title: t("Revoke all other sessions?"), confirmLabel: t("Revoke others"), danger: true, body: <>{rows.length - 1} {t("other device(s) will be signed out.")}</>, onConfirm: async () => { if ((await run(() => del("/api/sessions/me", { except: "current" }), t("Other sessions revoked."))).ok) q.reload(); } })}>{t("Revoke all other sessions")}</Button>
          <Button variant="danger-outline" onClick={() => setConfirm({ title: t("Sign out everywhere?"), confirmLabel: t("Sign out everywhere"), danger: true, body: <>{t("All")}{" "}{rows.length} {t("session(s), including this one, will end.")}</>, onConfirm: async () => { if ((await run(() => del("/api/sessions/me"))).ok) await signOut(); } })}>{t("Sign out everywhere")}</Button>
        </div>
        {rows.length === 0 ? <EmptyState title={t("No active sessions")} description={t("Sessions appear here when you sign in.")} /> : <DataTable label={t("My sessions")} columns={cols} rows={rows} rowKey={(s) => s.id} />}
        <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
      </>
    )}</QueryBoundary>
  );
}

function Everyone() {
  const { can } = useAuth(); const run = useRun();
  const [page, setPage] = useState(1); const [size, setSize] = useState(25);
  const q = useQuery(() => get<Paged<AllSession>>("/api/sessions", { page, pageSize: size }), [page, size]);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const cols: Column<AllSession>[] = [
    { key: "d", header: t("Device"), cell: (s) => <div className="who">{icon(s.device)}<span><b>{s.device}</b><small className="sub">{s.browser}</small></span></div> },
    { key: "u", header: t("User"), cell: (s) => <><b>{s.userName}</b><small className="sub">{s.userEmail}</small></> },
    { key: "l", header: t("IP"), className: "hide-sm", cell: (s) => s.ip ?? "—" },
    { key: "a", header: t("Last active"), cell: (s) => rel(s.lastActiveAt) },
    { key: "x", header: t("Actions"), cell: (s) => can("sessions.revoke") ? <Button size="sm" variant="danger-outline" onClick={() => setConfirm({ title: t("Revoke {userName}'s session?", { userName: s.userName }), confirmLabel: t("Revoke"), danger: true, body: <>{t("That device will be signed out.")}</>, onConfirm: async () => { if ((await run(() => del(`/api/users/${s.userId}/sessions`, { sessionId: s.id }), t("Session revoked."))).ok) q.reload(); } })}>{t("Revoke")}</Button> : null },
  ];
  const total = q.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  return (
    <QueryBoundary q={q} rows={4}>{(r) => (
      <>
        {r.data.length === 0 ? <EmptyState title={t("No active sessions")} description={t("Nobody is signed in.")} /> : <DataTable label={t("All sessions")} columns={cols} rows={r.data} rowKey={(s) => s.id} />}
        {total > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={(n) => { setSize(n); setPage(1); }} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total} sessions`} />}
        <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
      </>
    )}</QueryBoundary>
  );
}

export default function SessionsPage() {
  const { can } = useAuth();
  const [scope, setScope] = useState<"mine" | "all">("mine");
  return (
    <>
      <PageHeader title={t("Sessions & Devices")} description={t("Where your account is signed in. Raw session tokens are never shown.")} />
      <Panel>
        {can("sessions.read_all") && <Tabs label={t("Session scope")} items={[{ key: "mine", label: t("My sessions") }, { key: "all", label: t("All users") }]} value={scope} onChange={setScope} />}
        {scope === "mine" ? <Mine /> : <Everyone />}
      </Panel>
    </>
  );
}
