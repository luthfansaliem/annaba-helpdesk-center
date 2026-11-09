"use client";

import Link from "next/link";
import { Suspense, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import { MailPlus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Tabs } from "@/components/ui/tabs";
import { RowMenu } from "@/components/ui/menu";
import { InviteBadge } from "@/components/ui/status";
import { Field, TextInput } from "@/components/ui/field";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { RolePicker } from "@/components/iam/role-picker";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { InvitationItem, PublicSettings, RoleListItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { rel } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Tab = "all" | "pending" | "accepted" | "expired" | "revoked";
const TABS: Tab[] = ["all", "pending", "accepted", "expired", "revoked"];

function Invitations() {
  const { can } = useAuth(); const run = useRun();
  const params = useSearchParams();
  const [tab, setTab] = useState<Tab>("all");
  const [open, setOpen] = useState(false);
  const [f, setF] = useState({ email: "", roleIds: [] as string[], message: "" }); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  useEffect(() => { if (params.get("new") === "1" && can("invitations.manage")) setOpen(true); }, [params, can]);

  const q = useQuery(() => get<InvitationItem[]>("/api/invitations"), []);
  const pub = useQuery(() => get<PublicSettings>("/api/settings/public"), []);
  const emailOff = pub.data?.emailEnabled === false;
  const roles = useQuery(() => (can("roles.read") ? get<RoleListItem[]>("/api/roles") : Promise.resolve([] as RoleListItem[])), []);
  useEffect(() => { if (open) { const u = roles.data?.find((r) => r.key === "user"); setF({ email: "", roleIds: u ? [u.id] : [], message: "" }); setErr(""); } }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  const all = q.data ?? [];
  const list = all.filter((i) => tab === "all" || i.status === tab);
  const count = (t: Tab) => all.filter((i) => t === "all" || i.status === t).length;
  const columns: Column<InvitationItem>[] = [
    { key: "email", header: t("Email"), cell: (i) => <b>{i.email}</b> },
    { key: "roles", header: t("Roles"), className: "hide-sm", cell: (i) => i.roles.map((r) => r.name).join(", ") },
    { key: "status", header: t("Status"), cell: (i) => <InviteBadge status={i.status} /> },
    { key: "by", header: t("Invited by"), className: "hide-md", cell: (i) => i.invitedBy ?? "—" },
    { key: "exp", header: t("Expires"), className: "hide-md", cell: (i) => (i.status === "pending" ? rel(i.expiresAt) : "—") },
    { key: "a", header: "", width: 44, cell: (i) => can("invitations.manage") ? (
      <RowMenu label={t("Actions for {email}", { email: i.email })} items={[
        { label: t("Resend (new link)"), hidden: i.status === "accepted", onSelect: () => run(() => post(`/api/invitations/${i.id}/resend`), t("Invitation resent. The previous link no longer works.")).then((r) => r.ok && q.reload()) },
        { label: t("Revoke"), danger: true, hidden: i.status !== "pending", onSelect: () => setConfirm({ title: t("Revoke invitation?"), danger: true, confirmLabel: t("Revoke"), body: <>{i.email} {t("will no longer be able to accept this invitation.")}</>, onConfirm: async () => { if ((await run(() => post(`/api/invitations/${i.id}/revoke`), t("Invitation revoked."))).ok) q.reload(); } }) },
      ]} />) : null },
  ];

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    if (!/^\S+@\S+\.\S+$/.test(f.email)) return setErr(t("Enter a valid email address."));
    if (!f.roleIds.length) return setErr(t("Select at least one role."));
    setBusy(true);
    const r = await run(() => post("/api/invitations", { email: f.email, roleIds: f.roleIds, message: f.message || null }), t("Invitation sent."), { silent: true });
    setBusy(false);
    if (r.ok) { setOpen(false); q.reload(); } else setErr(r.error);
  }
  return (
    <Require perm="invitations.read">
      <PageHeader title={t("Invitations")} description={t("Invite people with the roles they should start with.")} actions={can("invitations.manage") && <Button variant="primary" disabled={emailOff} title={emailOff ? t("E-mail delivery is not configured") : undefined} onClick={() => setOpen(true)}><MailPlus size={16} /> {t("Invite user")}</Button>} />
      <Panel>
        {emailOff && <div className="form-alert warn" role="status">{t("E-mail delivery is not configured, so invitations cannot be delivered.")}{" "}{can("settings.manage") ? <Link href="/admin/email">{t("Check e-mail delivery")}</Link> : t("Ask a system administrator to configure it.")}</div>}
        <Tabs label={t("Invitation status")} items={TABS.map((t) => ({ key: t, label: `${t[0].toUpperCase()}${t.slice(1)} (${count(t)})` }))} value={tab} onChange={setTab} />
        {!q.loading && !q.error && list.length === 0
          ? <EmptyState title={t("No invitations")} description={t("Invitations you send will appear here.")} action={can("invitations.manage") ? <Button variant="primary" onClick={() => setOpen(true)}><MailPlus size={14} /> {t("Invite user")}</Button> : undefined} />
          : <DataTable label={t("Invitations")} columns={columns} rows={list} rowKey={(i) => i.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Invite user")} description={t("Invitation links expire and work once.")}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="inv-form" disabled={busy}>{busy ? t("Sending…") : t("Send invitation")}</Button></>}>
        <form id="inv-form" className="stack" noValidate onSubmit={submit}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <Field label={t("Email")} htmlFor="inv-email" required><TextInput id="inv-email" type="email" data-autofocus value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} /></Field>
          <fieldset className="fl"><legend>{t("Roles")}{" "}<em>*</em></legend><RolePicker roles={roles.data ?? []} idPrefix="inv-role" value={f.roleIds} onChange={(v) => setF({ ...f, roleIds: v })} /></fieldset>
          <Field label={t("Message (optional)")} htmlFor="inv-msg"><TextInput id="inv-msg" value={f.message} onChange={(e) => setF({ ...f, message: e.target.value })} placeholder={t("Welcome to the team!")} /></Field>
        </form>
      </Overlay>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Require>
  );
}
export default function InvitationsPage() { return <Suspense><Invitations /></Suspense>; }
