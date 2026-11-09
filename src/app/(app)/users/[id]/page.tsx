"use client";

import { Suspense, use, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ArrowLeft, Save } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, avatarUrl } from "@/components/ui/avatar";
import { Tabs } from "@/components/ui/tabs";
import { RowMenu } from "@/components/ui/menu";
import { Field, TextInput } from "@/components/ui/field";
import { UserStatusBadge, OutcomeBadge } from "@/components/ui/status";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { DataTable, type Column } from "@/components/data/data-table";
import { RolePicker } from "@/components/iam/role-picker";
import { QueryBoundary } from "@/components/query-boundary";
import { Require, Can } from "@/components/page-guard";
import { del, get, patch, post, put } from "@/lib/api/client";
import type { ActivityItem, RoleListItem, SessionInfo, UserDetail } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime, rel } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const tabs = [["overview", "Overview"], ["profile", "Profile"], ["roles", "Roles & Permissions"], ["sessions", "Sessions"], ["activity", "Activity History"], ["security", "Security"]] as const;
type Tab = (typeof tabs)[number][0];

function Sessions({ id, canRevoke }: { id: string; canRevoke: boolean }) {
  const run = useRun();
  const q = useQuery(() => get<SessionInfo[]>(`/api/users/${id}/sessions`), [id]);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const cols: Column<SessionInfo>[] = [
    { key: "d", header: t("Device"), cell: (s) => <><b>{s.device}</b><small className="sub">{s.browser}</small></> },
    { key: "l", header: t("IP"), className: "hide-sm", cell: (s) => s.ip ?? "—" },
    { key: "a", header: t("Last active"), cell: (s) => rel(s.lastActiveAt) },
    { key: "x", header: t("Actions"), cell: (s) => canRevoke ? <Button size="sm" variant="danger-outline" aria-label={`Revoke session on ${s.device}`} onClick={() => setConfirm({ title: t("Revoke this session?"), confirmLabel: t("Revoke"), danger: true, body: <>{t("The device (")}{s.device}, {s.browser}{t(") is signed out now. The person can sign in again.")}</>, onConfirm: async () => { if ((await run(() => del(`/api/users/${id}/sessions`, { sessionId: s.id }), t("Session revoked."))).ok) q.reload(); } })}>{t("Revoke")}</Button> : null },
  ];
  return <><QueryBoundary q={q} rows={3}>{(rows) => rows.length ? <DataTable label={t("Sessions")} columns={cols} rows={rows} rowKey={(s) => s.id} /> : <p className="tab-placeholder">{t("No active sessions.")}</p>}</QueryBoundary><ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} /></>;
}

function Activity({ id }: { id: string }) {
  const q = useQuery(() => get<ActivityItem[]>(`/api/users/${id}/activity`), [id]);
  return <QueryBoundary q={q} rows={3}>{(rows) => rows.length ? <ul className="mini-list">{rows.map((a) => <li key={a.id}><div><strong>{a.action}</strong><small>{a.target} · {fmtDateTime(a.ts)}{a.actor ? t(" · by {actor}", { actor: a.actor }) : ""}</small></div><OutcomeBadge outcome={a.outcome} /></li>)}</ul> : <p className="tab-placeholder">{t("No activity recorded.")}</p>}</QueryBoundary>;
}

function Detail({ id }: { id: string }) {
  const { can: canRaw, user: me } = useAuth(); const run = useRun();
  const router = useRouter(); const params = useSearchParams();
  const [tab, setTab] = useState<Tab>("overview");
  const q = useQuery(() => get<UserDetail>(`/api/users/${id}`), [id]);
  // Hierarchy rule (the server enforces it too): you cannot act on someone who holds permissions you don't. Show that instead of offering actions that are always refused.
  const outranked = !!q.data && q.data.id !== me?.id && q.data.effectivePermissions.some((p) => !(me?.permissions ?? []).includes(p));
  const can = (p: string) => canRaw(p) && !(outranked && ["users.update", "users.suspend", "users.assign_role", "sessions.revoke"].includes(p));
  const roles = useQuery(() => (can("roles.read") || can("users.assign_role") ? get<RoleListItem[]>("/api/roles") : Promise.resolve([] as RoleListItem[])), []);
  const [form, setForm] = useState({ name: "", phone: "", department: "", jobTitle: "" }); const [ferr, setFerr] = useState("");
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const u = q.data;
  useEffect(() => { const t = params.get("tab") as Tab | null; if (t && tabs.some((x) => x[0] === t)) setTab(t); }, [params]);
  useEffect(() => { if (u) { setForm({ name: u.name, phone: u.phone ?? "", department: u.department ?? "", jobTitle: u.jobTitle ?? "" }); setRoleIds(u.roles.map((r) => r.id)); } }, [u]);

  const setTabUrl = (t: Tab) => { setTab(t); router.replace(`/users/${id}?tab=${t}`, { scroll: false }); };
  const doStatus = (action: "suspend" | "deactivate" | "activate", title: string, body: string, label: string, danger = true) =>
    setConfirm({ title, body, confirmLabel: label, danger, reasonLabel: danger ? t("Reason") : undefined, onConfirm: async (reason) => { if ((await run(() => post(`/api/users/${id}/${action}`, { reason }), `${u?.name} ${action === "activate" ? "activated" : action === "suspend" ? "suspended" : "deactivated"}.`)).ok) q.reload(); } });

  async function saveProfile(e: React.FormEvent) {
    e.preventDefault(); setFerr("");
    if (form.name.trim().length < 2) return setFerr("Full name is required.");
    const r = await run(() => patch<UserDetail>(`/api/users/${id}`, { ...form, version: u!.version }), t("Profile saved."), { silent: true });
    if (r.ok) q.setData(r.data); else { setFerr(r.error); if (r.code === "conflict") q.reload(); }
  }

  return (
    <Require perm="users.read">
      <Link href="/users/all" className="back-link"><ArrowLeft size={15} /> {t("All Users")}</Link>
      <QueryBoundary q={q}>{(u) => (
        <>
          {outranked && <div className="form-alert warn" role="note" style={{ marginBottom: 12 }}><b>{t("View only.")}</b> {u.name} {t("holds permissions you don't have, so you can't change their profile, roles, status or sessions. Ask a Super Admin.")}</div>}
          <div className="crud-top">
            <div className="profile" style={{ margin: 0 }}>
              <Avatar name={u.name} size={56} src={avatarUrl(u.id, u.avatarVersion)} />
              <div><h1 style={{ fontSize: 24 }}>{u.name}</h1><div className="badges"><UserStatusBadge status={u.status} locked={u.locked} /><Badge tone={u.verified ? "green" : "orange"}>{u.verified ? t("Email verified") : t("Unverified")}</Badge>{u.mfaEnabled && <Badge tone="blue">{t("MFA")}</Badge>}</div><small style={{ color: "var(--muted)" }}>{u.email}</small></div>
            </div>
            <RowMenu label={t("User actions")} items={[
              { label: t("Suspend"), danger: true, hidden: !can("users.suspend") || ["suspended", "deactivated"].includes(u.status), onSelect: () => doStatus("suspend", `Suspend ${u.name}?`, "They are signed out of all sessions and cannot sign in until reactivated.", "Suspend") },
              { label: t("Deactivate"), danger: true, hidden: !can("users.suspend") || u.status === "deactivated", onSelect: () => doStatus("deactivate", `Deactivate ${u.name}?`, "Deactivated accounts cannot sign in. Roles and history are kept.", "Deactivate") },
              { label: t("Activate"), hidden: !can("users.suspend") || !["suspended", "deactivated"].includes(u.status), onSelect: () => doStatus("activate", `Activate ${u.name}?`, "The user will be able to sign in again.", "Activate", false) },
              { label: t("Unlock"), hidden: !can("users.suspend") || !u.locked, onSelect: async () => { if ((await run(() => post(`/api/users/${id}/unlock`), t("Account unlocked."))).ok) q.reload(); } },
              { label: t("Resend verification"), hidden: !can("users.update") || u.verified, onSelect: () => run(() => post(`/api/users/${id}/resend-verification`), t("Verification email sent.")) },
              { label: t("Revoke all sessions"), danger: true, hidden: !can("sessions.revoke") || u.activeSessions === 0, onSelect: () => setConfirm({ title: t("Revoke all sessions?"), danger: true, confirmLabel: t("Revoke all"), body: <>{u.name} {t("will be signed out on")}{" "}{u.activeSessions} {t("device(s).")}</>, onConfirm: async () => { if ((await run(() => del(`/api/users/${id}/sessions`), t("All sessions revoked."))).ok) q.reload(); } }) },
            ]} />
          </div>
          <Panel>
            <Tabs items={tabs.map(([k, l]) => ({ key: k, label: t(l) }))} value={tab} onChange={setTabUrl} label={t("User sections")} />
            {tab === "overview" && (
              <div className="kv wide">
                <span>{t("Status")}</span><UserStatusBadge status={u.status} locked={u.locked} /><span>{t("Email")}</span><b>{u.email}</b><span>{t("Roles")}</span><b>{u.roles.map((r) => r.name).join(", ")}</b>
                <span>{t("Department")}</span><b>{u.department ?? "—"}</b><span>{t("Job title")}</span><b>{u.jobTitle ?? "—"}</b><span>{t("Created")}</span><b>{fmtDateTime(u.createdAt)}</b>
                <span>{t("Last login")}</span><b>{rel(u.lastLoginAt)}</b><span>{t("Active sessions")}</span><b>{u.activeSessions}</b><span>{t("MFA")}</span><b>{u.mfaEnabled ? t("Enabled") : t("Not enabled")}</b>
              </div>
            )}
            {tab === "profile" && (
              <form className="fgrid" noValidate onSubmit={saveProfile}>
                {ferr && <div className="form-alert err full" role="alert" style={{ gridColumn: "1/-1" }}>{ferr}</div>}
                <Field label={t("Full name")} htmlFor="p-name" required><TextInput id="p-name" disabled={!can("users.update")} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
                <Field label={t("Email")} htmlFor="p-email"><TextInput id="p-email" value={u.email} disabled readOnly /></Field>
                <Field label={t("Phone")} htmlFor="p-phone"><TextInput id="p-phone" disabled={!can("users.update")} value={form.phone} onChange={(e) => setForm({ ...form, phone: e.target.value })} /></Field>
                <Field label={t("Department")} htmlFor="p-dep"><TextInput id="p-dep" disabled={!can("users.update")} value={form.department} onChange={(e) => setForm({ ...form, department: e.target.value })} /></Field>
                <Field label={t("Job title")} htmlFor="p-title" full><TextInput id="p-title" disabled={!can("users.update")} value={form.jobTitle} onChange={(e) => setForm({ ...form, jobTitle: e.target.value })} /></Field>
                <Can perm="users.update"><div className="full" style={{ gridColumn: "1/-1", display: "flex", justifyContent: "flex-end" }}><Button variant="primary" type="submit"><Save size={15} /> {t("Save changes")}</Button></div></Can>
              </form>
            )}
            {tab === "roles" && (
              <div className="stack">
                <h3 className="sec-t">{t("Assigned roles")}</h3>
                {can("users.assign_role") ? <><RolePicker roles={roles.data ?? []} value={roleIds} onChange={setRoleIds} idPrefix="ur" />
                  <div style={{ display: "flex", justifyContent: "flex-end" }}><Button variant="primary" disabled={[...roleIds].sort().join() === u.roles.map((r) => r.id).sort().join() || roleIds.length === 0}
                    onClick={() => setConfirm({ title: t("Change roles?"), confirmLabel: t("Save roles"), danger: false, body: <>{t("Roles for")}{" "}{u.name} {t("become:")}{" "}<b>{(roles.data ?? []).filter((r) => roleIds.includes(r.id)).map((r) => r.name).join(", ")}</b>{t(". Their permissions change immediately.")}</>, onConfirm: async () => { if ((await run(() => put(`/api/users/${id}/roles`, { roleIds }), t("Roles updated."))).ok) q.reload(); } })}>{t("Save roles")}</Button></div></>
                  : <div className="chips">{u.roles.map((r) => <Badge key={r.id} tone="blue" dot={false}>{r.name}</Badge>)}</div>}
                <h3 className="sec-t" style={{ marginTop: 8 }}>{t("Effective permissions (")}{u.effectivePermissions.length})</h3>
                {u.effectivePermissions.length ? <div className="chips">{u.effectivePermissions.map((p) => <code key={p}>{p}</code>)}</div> : <p className="tab-placeholder">{t("No administrative permissions.")}</p>}
              </div>
            )}
            {tab === "sessions" && (can("sessions.read_all") ? <Sessions id={id} canRevoke={can("sessions.revoke")} /> : <p className="tab-placeholder">{t("You don't have permission to view sessions.")}</p>)}
            {tab === "activity" && (can("audit.read") ? <Activity id={id} /> : <p className="tab-placeholder">{t("You don't have permission to view the audit log.")}</p>)}
            {tab === "security" && (
              <div className="stack">
                <div className="kv wide"><span>{t("MFA")}</span><Badge tone={u.mfaEnabled ? "green" : "gray"}>{u.mfaEnabled ? t("Enabled") : t("Not enabled")}</Badge><span>{t("Email verification")}</span><Badge tone={u.verified ? "green" : "orange"}>{u.verified ? t("Verified") : t("Pending")}</Badge><span>{t("Account lock")}</span><Badge tone={u.locked ? "orange" : "green"}>{u.locked ? t("Locked") : t("Not locked")}</Badge></div>
                <p className="field-help" style={{ margin: 0 }}>{t("MFA secrets and recovery codes are never visible to administrators.")}</p>
                {can("users.reset_credentials") && u.status === "active" && u.id !== me?.id && !outranked && (
                  <div className="stack" style={{ gap: 8 }}>
                    <h3 className="sec-t" style={{ marginTop: 8 }}>{t("Account recovery")}</h3>
                    <p className="field-help" style={{ margin: 0 }}>{t("For someone locked out. Both actions sign them out everywhere, are recorded in the audit log, and they are e-mailed. You never see or set a password.")}</p>
                    <div className="row" style={{ gap: 8 }}>
                      <Button variant="danger-outline" disabled={!u.mfaEnabled} title={u.mfaEnabled ? undefined : t("No two-step verification is set up")} onClick={() => setConfirm({ title: t("Reset two-step verification for {name}?", { name: u.name }), danger: true, confirmLabel: t("Reset two-step verification"), reasonLabel: t("Reason"),
                        body: <>{t("Their authenticator app and recovery codes are removed and they are signed out everywhere. They can sign in with their password and set it up again")}{" "}{t("(or must, if your policy requires it).")}</>,
                        onConfirm: async (reason) => { if ((await run(() => post(`/api/users/${id}/reset-mfa`, { reason }), t("Two-step verification reset."))).ok) q.reload(); } })}>{t("Reset two-step verification")}</Button>
                      <Button variant="danger-outline" onClick={() => setConfirm({ title: t("Require a new password for {name}?", { name: u.name }), danger: true, confirmLabel: t("Require new password"), reasonLabel: t("Reason"),
                        body: <>{t("They are signed out everywhere and their current password stops working for sign-in. They receive a one-time link (valid 30 minutes) to choose a new one.")}</>,
                        onConfirm: async (reason) => { if ((await run(() => post(`/api/users/${id}/require-password-reset`, { reason }), t("They were e-mailed a link to choose a new password."))).ok) q.reload(); } })}>{t("Require new password")}</Button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </Panel>
        </>
      )}</QueryBoundary>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Require>
  );
}
export default function UserDetailPage({ params }: { params: Promise<{ id: string }> }) { const { id } = use(params); return <Suspense><Detail id={id} /></Suspense>; }
