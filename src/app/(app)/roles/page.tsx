"use client";

import { useState } from "react";
import { Archive, Lock, Plus, Users } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { RowMenu } from "@/components/ui/menu";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, patch, post } from "@/lib/api/client";
import type { Paged, PermissionItem, RoleListItem, RoleUser } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Draft = { id?: string; name: string; description: string; permissions: string[]; version?: number };

function RoleUsers({ role, onClose }: { role: RoleListItem | null; onClose: () => void }) {
  const q = useQuery(() => (role ? get<Paged<RoleUser>>(`/api/roles/${role.id}/users`, { pageSize: 50 }) : Promise.resolve(undefined as unknown as Paged<RoleUser>)), [role?.id]);
  return (
    <Overlay open={!!role} onClose={onClose} title={t("Users with {v0}", { v0: role?.name ?? "" })}>
      {q.error ? <p className="tab-placeholder">{q.error.message}</p> : !q.data ? <p className="tab-placeholder">{t("Loading…")}</p> : q.data.data.length === 0 ? <p className="tab-placeholder">{t("No users have this role.")}</p>
        : <ul className="mini-list">{q.data.data.map((u) => <li key={u.id}><div><strong>{u.name}</strong><small>{u.email}</small></div></li>)}</ul>}
    </Overlay>
  );
}

export default function RolesPage() {
  const { user, can } = useAuth(); const run = useRun();
  const list = useQuery(() => get<RoleListItem[]>("/api/roles", { includeArchived: true }), []);
  const catalog = useQuery(() => (can("permissions.read") ? get<PermissionItem[]>("/api/permissions") : Promise.resolve([] as PermissionItem[])), []);
  const [draft, setDraft] = useState<Draft | null>(null); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const [viewing, setViewing] = useState<RoleListItem | null>(null);
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const mine = new Set(user?.permissions ?? []);
  const all = list.data ?? [];
  const roles = all.filter((r) => !r.archived); const archived = all.filter((r) => r.archived);
  const perms = catalog.data ?? [];
  const edit = (r?: RoleListItem) => { setErr(""); setDraft(r ? { id: r.id, name: r.name, description: r.description, permissions: r.permissions, version: r.version } : { name: "", description: "", permissions: [] }); };

  const cols: Column<RoleListItem>[] = [
    { key: "n", header: t("Role"), cell: (r) => <><b>{r.name}</b> {r.isProtected && <Badge tone="gray" dot={false}><Lock size={10} /> {t("Protected")}</Badge>}{r.isSystem && !r.isProtected && <Badge tone="blue" dot={false}>{t("System")}</Badge>}<small className="sub">{r.description}</small></> },
    { key: "p", header: t("Permissions"), className: "hide-sm", cell: (r) => `${r.permissionCount}${perms.length ? ` / ${perms.length}` : ""}` },
    { key: "u", header: t("Users"), cell: (r) => <button type="button" className="link-btn" onClick={() => setViewing(r)}><Users size={13} style={{ verticalAlign: -2 }} /> {r.userCount}</button> },
    { key: "a", header: "", width: 44, cell: (r) => (
      <RowMenu label={t("Actions for {name}", { name: r.name })} items={[
        { label: t("Edit"), onSelect: () => edit(r), hidden: !can("roles.update") || r.isProtected },
        { label: t("View assigned users"), onSelect: () => setViewing(r) },
        { label: t("Duplicate"), onSelect: async () => { if ((await run(() => post(`/api/roles/${r.id}/duplicate`), t("{name} duplicated.", { name: r.name }))).ok) list.reload(); }, hidden: !can("roles.create") },
        { label: t("Archive"), danger: true, hidden: !can("roles.update") || r.isSystem, onSelect: () => setConfirm({ title: t("Archive {name}?", { name: r.name }), danger: true, confirmLabel: t("Archive"), body: <>{t("Archived roles stop granting permissions and can no longer be assigned. Roles that still have users cannot be archived.")}</>, onConfirm: async () => { if ((await run(() => post(`/api/roles/${r.id}/archive`), t("{name} archived.", { name: r.name }))).ok) list.reload(); } }) },
      ]} />) },
  ];

  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!draft) return; setErr("");
    if (draft.name.trim().length < 2) return setErr(t("Role name is required."));
    setBusy(true);
    const body = { name: draft.name, description: draft.description, permissions: draft.permissions, version: draft.version };
    const r = await run(() => (draft.id ? patch(`/api/roles/${draft.id}`, body) : post("/api/roles", body)), draft.id ? "Role saved." : "Role created.", { silent: true });
    setBusy(false);
    if (r.ok) { setDraft(null); list.reload(); } else { setErr(r.error); if (r.code === "conflict") list.reload(); }
  }
  const mods = [...new Set(perms.map((p) => p.module))];
  const locked = !can("permissions.assign");

  return (
    <Require perm="roles.read">
      <PageHeader title={t("Roles")} description={t("Bundles of permissions that you assign to users.")} actions={can("roles.create") && <Button variant="primary" onClick={() => edit()}><Plus size={16} /> {t("Create role")}</Button>} />
      <Panel>
        <DataTable label={t("Roles")} columns={cols} rows={roles} rowKey={(r) => r.id} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
        {archived.length > 0 && <p className="field-help" style={{ marginTop: 14 }}><Archive size={12} style={{ verticalAlign: -1 }} /> {t("Archived:")}{" "}{archived.map((r) => r.name).join(", ")}</p>}
      </Panel>
      <Overlay open={!!draft} onClose={() => setDraft(null)} side="right" title={draft?.id ? t("Edit role") : t("Create role")} description={t("Permissions use resource.action identifiers.")}
        footer={<><Button onClick={() => setDraft(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="role-form" disabled={busy}>{busy ? t("Saving…") : t("Save role")}</Button></>}>
        {draft && (
          <form id="role-form" className="stack" noValidate onSubmit={submit}>
            {err && <div className="form-alert err" role="alert">{err}</div>}
            <Field label={t("Name")} htmlFor="r-name" required><TextInput id="r-name" data-autofocus value={draft.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></Field>
            <Field label={t("Description")} htmlFor="r-desc"><TextInput id="r-desc" value={draft.description} onChange={(e) => setDraft({ ...draft, description: e.target.value })} /></Field>
            {locked && <div className="form-alert info">{t("You need")}{" "}<code>permissions.assign</code> {t("to change this role's permissions.")}</div>}
            {mods.map((m) => (
              <fieldset className="fl" key={m}><legend>{m}</legend>
                {perms.filter((p) => p.module === m).map((p) => {
                  const has = draft.permissions.includes(p.key); const ok = mine.has(p.key) || has;
                  return (
                    <label key={p.key} className={"perm-opt" + (ok ? "" : " off")} title={ok ? p.description : t("You can't grant a permission you don't hold")}>
                      <input type="checkbox" className="cb" checked={has} disabled={locked || (!mine.has(p.key) && !has)} onChange={(e) => setDraft({ ...draft, permissions: e.target.checked ? [...draft.permissions, p.key] : draft.permissions.filter((x) => x !== p.key) })} />
                      <code>{p.key}</code>{p.critical && <Badge tone="red" dot={false}>{t("critical")}</Badge>}<small>{p.description}</small>
                    </label>
                  );
                })}
              </fieldset>
            ))}
          </form>
        )}
      </Overlay>
      <RoleUsers role={viewing} onClose={() => setViewing(null)} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Require>
  );
}
