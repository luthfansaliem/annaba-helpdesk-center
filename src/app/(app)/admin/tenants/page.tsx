"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { TenantItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { useAuth } from "@/lib/auth";
import { t } from "@/i18n";

const slugify = (s: string) => s.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40);

function CreateDrawer({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [name, setName] = useState(""); const [slug, setSlug] = useState(""); const [touched, setTouched] = useState(false); const [email, setEmail] = useState("");
  const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const reset = () => { setName(""); setSlug(""); setTouched(false); setEmail(""); setErr(""); };
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => post("/api/tenants", { name, slug, adminEmail: email }), t("Tenant created. The first administrator was invited."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    reset(); onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={() => { reset(); onClose(); }} side="right" title={t("New tenant")} description={t("Creates an organisation with its own users, roles and audit trail, and invites its first Super Admin.")}
      footer={<><Button onClick={() => { reset(); onClose(); }}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="tenant-form" disabled={busy}>{busy ? t("Creating…") : t("Create tenant")}</Button></>}>
      <form id="tenant-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Organisation name")} htmlFor="tn-name" required><TextInput id="tn-name" data-autofocus value={name} onChange={(e) => { setName(e.target.value); if (!touched) setSlug(slugify(e.target.value)); }} /></Field>
        <Field label={t("Identifier")} htmlFor="tn-slug" required><TextInput id="tn-slug" value={slug} onChange={(e) => { setTouched(true); setSlug(e.target.value.toLowerCase()); }} placeholder={t("acme")} /></Field>
        <Field label={t("First administrator's email")} htmlFor="tn-email" required><TextInput id="tn-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
        <p className="field-help">{t("They receive an invitation with the Super Admin role for this tenant only. E-mail addresses are unique across all tenants.")}</p>
      </form>
    </Overlay>
  );
}

function Tenants() {
  const { user, refresh } = useAuth(); const router = useRouter();
  const run = useRun(); const q = useQuery(() => get<TenantItem[]>("/api/tenants"), []); const [open, setOpen] = useState(false); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  async function toggle(tn: TenantItem) {
    const r = await run(() => post(`/api/tenants/${tn.id}/${tn.status === "active" ? "suspend" : "activate"}`), tn.status === "active" ? "Tenant suspended. Its sessions were ended." : "Tenant re-activated.");
    if (r.ok) q.reload();
  }
  const manage = (tn: TenantItem) => setConfirm({ title: t("Manage {name}?", { name: tn.name }), confirmLabel: t("Enter support mode"),
    body: <>{t("You will administer")}{" "}<b>{tn.name}</b> {t("from your own session: its users, roles, invitations, audit trail and reviews. Everything you do is recorded in")}{" "}<b>{t("its")}</b> {t("audit trail as platform support, its administrators get a security notice, and support mode ends by itself after 2 hours. Your own account and the platform pages stay as they are.")}</>,
    onConfirm: async () => { if ((await run(() => post(`/api/tenants/${tn.id}/support`), t("Support mode on: {name}.", { name: tn.name }))).ok) { await refresh(); router.push("/dashboard"); } } });
  const ask = (tn: TenantItem) => tn.status === "active"
    ? setConfirm({ title: t("Suspend {name}?", { name: tn.name }), confirmLabel: t("Suspend tenant"), danger: true, onConfirm: () => void toggle(tn),
        body: <>{t("Everyone in")}{" "}<b>{tn.name}</b> {t("is signed out now and cannot sign in again (including single sign-on) until you re-activate the tenant. Nothing is deleted.")}</> })
    : void toggle(tn);
  const cols: Column<TenantItem>[] = [
    { key: "n", header: t("Organisation"), cell: (tn) => <><b>{tn.name}</b>{tn.isPlatform && <> <Badge tone="blue" dot={false}>{t("Platform")}</Badge></>}<br /><small>{tn.slug}</small></> },
    { key: "s", header: t("Status"), cell: (tn) => <Badge tone={tn.status === "active" ? "green" : "red"}>{tn.status === "active" ? t("Active") : t("Suspended")}</Badge> },
    { key: "u", header: t("Users"), cell: (tn) => tn.users },
    { key: "c", header: t("Created"), className: "hide-sm", cell: (tn) => fmtDateTime(tn.createdAt) },
    { key: "a", header: t("Actions"), cell: (tn) => tn.isPlatform ? <small title={t("The platform organisation runs this console, so it cannot be suspended.")}>{t("Cannot be suspended")}</small>
      : <span className="row" style={{ gap: 6 }}>
        {tn.status === "active" && (user?.actingTenant === tn.name ? <Button size="sm" variant="outline" disabled>{t("Supporting now")}</Button> : <Button size="sm" variant="outline" aria-label={`Manage ${tn.name}`} onClick={() => manage(tn)}>{t("Manage")}</Button>)}
        <Button size="sm" variant={tn.status === "active" ? "danger-outline" : "outline"} aria-label={`${tn.status === "active" ? "Suspend tenant" : "Re-activate tenant"} ${tn.name}`} onClick={() => ask(tn)}>{tn.status === "active" ? t("Suspend tenant") : t("Re-activate tenant")}</Button></span> },
  ];
  return (
    <>
      <PageHeader title={t("Tenants")} description={t("Each tenant is a separate organisation: its people, roles, sessions and audit trail are invisible to the others.")}
        actions={<Button variant="primary" onClick={() => setOpen(true)}><Plus size={16} /> {t("New tenant")}</Button>} />
      <Panel><DataTable label={t("Tenants")} columns={cols} rows={q.data ?? []} rowKey={(tn) => tn.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} /></Panel>
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
      <CreateDrawer open={open} onClose={() => setOpen(false)} onDone={q.reload} />
    </>
  );
}

export default function TenantsPage() { return <Require perm="tenants.manage"><Tenants /></Require>; }
