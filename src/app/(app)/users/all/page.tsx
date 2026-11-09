"use client";

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Plus, Upload } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Avatar, avatarUrl } from "@/components/ui/avatar";
import { RowMenu } from "@/components/ui/menu";
import { UserStatusBadge } from "@/components/ui/status";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { DataTable, type Column, type Sort } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect, SearchBox } from "@/components/data/filter-bar";
import { EmptyState } from "@/components/data/empty-state";
import { AddUserDrawer } from "@/components/iam/add-user-drawer";
import { ImportUsersDrawer } from "@/components/iam/import-users-drawer";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { BulkResult, Paged, PublicSettings, RoleListItem, UserListItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useDebounced, useQuery } from "@/lib/hooks";
import { rel } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { useToast } from "@/lib/toast";
import { t } from "@/i18n";

const ALL = { status: "All statuses", role: "All roles", ver: "Any verification" };

function AllUsers() {
  const { can } = useAuth(); const run = useRun(); const { notify } = useToast();
  const router = useRouter(); const params = useSearchParams();
  const [q, setQ] = useState(""); const dq = useDebounced(q, 250);
  const [status, setStatusF] = useState(ALL.status); const [role, setRole] = useState(ALL.role); const [ver, setVer] = useState(ALL.ver);
  const [sort, setSort] = useState<Sort>({ key: "name", dir: "asc" });
  const [page, setPage] = useState(1); const [size, setSize] = useState(10);
  const [sel, setSel] = useState<string[]>([]);
  const [add, setAdd] = useState(false); const [imp, setImp] = useState(false);
  const pub = useQuery(() => get<PublicSettings>("/api/settings/public"), []); const emailOff = pub.data?.emailEnabled === false;
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  useEffect(() => { const st = params.get("status"); if (st) setStatusF(st[0].toUpperCase() + st.slice(1).toLowerCase()); }, [params]);
  useEffect(() => { if (params.get("add") === "1" && can("users.create")) setAdd(true); }, [params, can]);
  useEffect(() => setPage(1), [dq, status, role, ver, size]);

  const roles = useQuery(() => (can("roles.read") ? get<RoleListItem[]>("/api/roles") : Promise.resolve([] as RoleListItem[])), []);
  const roleKey = role === ALL.role ? undefined : roles.data?.find((r) => r.name === role)?.key;
  const list = useQuery(() => get<Paged<UserListItem>>("/api/users", {
    q: dq.trim() || undefined, status: status === ALL.status ? undefined : status.toLowerCase(), role: roleKey,
    verified: ver === ALL.ver ? undefined : ver === "Verified", sort: sort.key, dir: sort.dir, page, pageSize: size }), [dq, status, roleKey, ver, sort.key, sort.dir, page, size]);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0;
  const pageCount = Math.max(1, Math.ceil(total / size));

  const act = (u: UserListItem, kind: "suspend" | "activate" | "unlock" | "resend") => {
    if (kind === "suspend") return setConfirm({ title: t("Suspend {name}?", { name: u.name }), danger: true, confirmLabel: t("Suspend"), reasonLabel: t("Reason"), body: <>{t("They will be signed out of all sessions and cannot sign in until reactivated.")}</>, onConfirm: async (reason) => { if ((await run(() => post(`/api/users/${u.id}/suspend`, { reason }), t("{name} suspended.", { name: u.name }))).ok) list.reload(); } });
    const path = kind === "activate" ? "activate" : kind === "unlock" ? "unlock" : "resend-verification";
    const msg = kind === "activate" ? `${u.name} activated.` : kind === "unlock" ? `${u.name} unlocked.` : "Verification email sent.";
    run(() => post(`/api/users/${u.id}/${path}`), msg).then((r) => r.ok && list.reload());
  };

  const columns: Column<UserListItem>[] = [
    { key: "name", header: t("User"), sortable: true, cell: (u) => <Link href={`/users/${u.id}`} className="who"><Avatar name={u.name} size={30} src={avatarUrl(u.id, u.avatarVersion)} /><span><b>{u.name}</b><small className="sub">{u.email}</small></span></Link> },
    { key: "roles", header: t("Roles"), className: "hide-sm", cell: (u) => <div className="chips">{u.roles.map((r) => <Badge key={r.id} tone="blue" dot={false}>{r.name}</Badge>)}</div> },
    { key: "status", header: t("Status"), sortable: true, cell: (u) => <UserStatusBadge status={u.status} locked={u.locked} /> },
    { key: "ver", header: t("Email"), className: "hide-md", cell: (u) => <Badge tone={u.verified ? "green" : "orange"}>{u.verified ? t("Verified") : t("Unverified")}</Badge> },
    { key: "lastLogin", header: t("Last login"), sortable: true, className: "hide-md", cell: (u) => rel(u.lastLoginAt) },
    { key: "actions", header: "", width: 44, cell: (u) => (
      <RowMenu label={t("Actions for {name}", { name: u.name })} items={[
        { label: t("View details"), onSelect: () => router.push(`/users/${u.id}`) },
        // Hierarchy rule: someone who holds permissions you don't can be looked at but not changed (the server refuses it anyway).
        { label: t("View only: holds higher permissions"), onSelect: () => router.push(`/users/${u.id}`), disabled: true, hidden: u.manageable !== false },
        { label: t("Edit"), onSelect: () => router.push(`/users/${u.id}?tab=profile`), hidden: !can("users.update") || u.manageable === false },
        { label: t("Suspend"), danger: true, onSelect: () => act(u, "suspend"), hidden: !can("users.suspend") || u.manageable === false || ["suspended", "deactivated"].includes(u.status) },
        { label: t("Activate"), onSelect: () => act(u, "activate"), hidden: !can("users.suspend") || u.manageable === false || !["suspended", "deactivated"].includes(u.status) },
        { label: t("Unlock"), onSelect: () => act(u, "unlock"), hidden: !can("users.suspend") || u.manageable === false || !u.locked },
        { label: t("Resend verification"), onSelect: () => act(u, "resend"), hidden: !can("users.update") || u.manageable === false || u.verified },
      ]} />) },
  ];

  const bulk = (kind: "suspend" | "resend") => {
    const picked = rows.filter((u) => sel.includes(u.id));
    const targets = picked.filter((u) => u.manageable !== false); const skipped = picked.length - targets.length;
    if (targets.length === 0) { notify("error", t("None of the selected people can be changed by you: they hold permissions you don't.")); return; }
    setConfirm({
      title: kind === "suspend" ? t("Suspend {length} users?", { length: targets.length }) : t("Resend verification to {length} users?", { length: targets.length }), danger: kind === "suspend",
      confirmLabel: kind === "suspend" ? t("Suspend") : t("Send"), body: <>{targets.map((t) => t.name).join(", ")}{skipped > 0 && <><br /><small>{skipped} {t("selected")}{" "}{skipped === 1 ? t("person is") : t("people are")} {t("skipped: they hold permissions you don't.")}</small></>}</>,
      onConfirm: async () => {
        const r = await run(() => post<BulkResult>("/api/users/bulk", { ids: targets.map((t) => t.id), action: kind === "suspend" ? "suspend" : "resend_verification" }), undefined);
        if (r.ok) {
          const ok = r.data.results.filter((x) => x.ok).length; const bad = r.data.results.filter((x) => !x.ok);
          if (ok) notify("success", t("{v0} {ok} user(s).", { v0: kind === "suspend" ? "Suspended" : "Verification sent to", ok: ok }));
          bad.slice(0, 2).forEach((f) => notify("error", `${rows.find((u) => u.id === f.id)?.name ?? f.id}: ${f.error}`));
        }
        setSel([]); list.reload();
      },
    });
  };

  const empty = !list.loading && !list.error && rows.length === 0;
  return (
    <Require perm="users.read">
      <PageHeader title={t("All Users")} description={t("Search, filter and manage every account.")}
        actions={can("users.create") && <div className="row" style={{ gap: 8 }}>{can("users.assign_role") && <Button disabled={emailOff} title={emailOff ? t("E-mail delivery is not configured") : undefined} onClick={() => setImp(true)}><Upload size={16} /> {t("Import CSV")}</Button>}<Button variant="primary" onClick={() => setAdd(true)}><Plus size={16} /> {t("Add User")}</Button></div>} />
      <Panel>
        <FilterBar>
          <SearchBox label={t("Search users")} placeholder={t("Search by name, email or department…")} value={q} onChange={setQ} />
          <FilterSelect label={t("Status")} value={status} onChange={setStatusF} options={[ALL.status, "Active", "Pending", "Suspended", "Deactivated", "Locked"]} />
          <FilterSelect label={t("Role")} value={role} onChange={setRole} options={[ALL.role, ...(roles.data ?? []).filter((r) => !r.archived).map((r) => r.name)]} />
          <FilterSelect label={t("Email")} value={ver} onChange={setVer} options={[ALL.ver, "Verified", "Unverified"]} />
        </FilterBar>
        {sel.length > 0 && (
          <div className="bulk-bar" role="region" aria-label={t("Bulk actions")}><b>{sel.length} {t("selected")}</b>
            {can("users.suspend") && <Button size="sm" variant="danger-outline" onClick={() => bulk("suspend")}>{t("Suspend")}</Button>}
            {can("users.update") && <Button size="sm" onClick={() => bulk("resend")}>{t("Resend verification")}</Button>}
            <button type="button" className="link-btn" onClick={() => setSel([])}>{t("Clear")}</button></div>
        )}
        {empty
          ? <EmptyState title={t("No users found")} description={t("Try adjusting your search or filters.")} action={can("users.create") ? <Button variant="primary" onClick={() => setAdd(true)}><Plus size={15} /> {t("Add User")}</Button> : undefined} />
          : <DataTable label={t("Users")} columns={columns} rows={rows} rowKey={(u) => u.id} selectable={can("users.suspend") || can("users.update")} selected={sel} onSelectedChange={setSel}
              sort={sort} onSortChange={setSort} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />}
        {rows.length > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={setSize} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total} users`} />}
      </Panel>
      <ImportUsersDrawer open={imp} onClose={() => setImp(false)} onDone={() => list.reload()} />
      <AddUserDrawer open={add} onClose={() => setAdd(false)} onDone={() => { list.reload(); }} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </Require>
  );
}
export default function AllUsersPage() { return <Suspense><AllUsers /></Suspense>; }
