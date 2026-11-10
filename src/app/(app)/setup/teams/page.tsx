"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { get, patch, post, put } from "@/lib/api/client";
import type { Paged, TeamDetail, TeamItem, UserListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function TeamDrawer({ open, onClose, onDone, editing }: { open: boolean; onClose: () => void; onDone: () => void; editing: TeamItem | null }) {
  const run = useRun();
  const [name, setName] = useState(""); const [description, setDescription] = useState(""); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) { setKey(current); setName(editing?.name ?? ""); setDescription(editing?.description ?? ""); setErrs({}); setErr(""); }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { name, description: description || null, version: editing?.version };
    const r = await run(() => (editing ? patch(`/api/teams/${editing.id}`, body) : post("/api/teams", body)), editing ? t("Team saved.") : t("Team created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit team") : t("New team")} description={t("Teams group the agents who work requests together.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="team-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="team-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="tm-name" required error={errs.name}><TextInput id="tm-name" data-autofocus value={name} onChange={(e) => setName(e.target.value)} /></Field>
        <Field label={t("Description")} htmlFor="tm-desc" error={errs.description}><TextInput id="tm-desc" value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
      </form>
    </Overlay>
  );
}

function MembersDrawer({ team, onClose, onDone }: { team: TeamItem | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const detail = useQuery(() => (team ? get<TeamDetail>(`/api/teams/${team.id}`) : Promise.resolve(null)), [team?.id]);
  const users = useQuery(() => (team ? get<Paged<UserListItem>>("/api/users", { status: "active", pageSize: 100 }) : Promise.resolve(null)), [team?.id]);
  const [picked, setPicked] = useState<Record<string, boolean | undefined>>({}); const [seed, setSeed] = useState<string | null>(null);
  const [filter, setFilter] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const d = detail.data;
  if (d && seed !== `${d.id}:${d.version}`) { // pick up the saved membership (lead = true, member = false, absent = undefined)
    setSeed(`${d.id}:${d.version}`); setPicked(Object.fromEntries(d.members.map((m) => [m.userId, m.isLead])));
  }
  const list = (users.data?.data ?? []).filter((u) => !filter || `${u.name} ${u.email}`.toLowerCase().includes(filter.toLowerCase()));
  async function save() {
    if (!team || !d) return; setErr(""); setBusy(true);
    const members = Object.entries(picked).filter(([, v]) => v !== undefined).map(([userId, isLead]) => ({ userId, isLead: !!isLead }));
    const r = await run(() => put(`/api/teams/${team.id}/members`, { members, version: d.version }), t("Members saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={!!team} onClose={onClose} side="right" title={t("Members of {name}", { name: team?.name ?? "" })} description={t("Only active people can join. A lead can manage the team's queue.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" onClick={() => void save()} disabled={busy || !d}>{busy ? t("Saving…") : t("Save members")}</Button></>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {users.error && <div className="form-alert err" role="alert">{t("People could not be loaded. You need permission to view users.")}</div>}
        <Field label={t("Search people")} htmlFor="tm-filter"><TextInput id="tm-filter" value={filter} onChange={(e) => setFilter(e.target.value)} /></Field>
        <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>
          {list.map((u) => (
            <li key={u.id} className="row" style={{ gap: 10, justifyContent: "space-between" }}>
              <label className="row" style={{ gap: 8 }}>
                <Checkbox checked={picked[u.id] !== undefined} aria-label={u.name} onChange={(e) => setPicked((p) => ({ ...p, [u.id]: e.target.checked ? false : undefined }))} />
                <span><b>{u.name}</b><br /><small>{u.email}</small></span>
              </label>
              {picked[u.id] !== undefined && <label className="row" style={{ gap: 6 }}><Checkbox checked={!!picked[u.id]} aria-label={`${t("Lead")} ${u.name}`} onChange={(e) => setPicked((p) => ({ ...p, [u.id]: e.target.checked }))} /> {t("Lead")}</label>}
            </li>
          ))}
        </ul>
      </div>
    </Overlay>
  );
}

function Teams() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const q = useQuery(() => get<TeamItem[]>("/api/teams", { includeArchived: archived }), [archived]);
  const [drawer, setDrawer] = useState<{ open: boolean; row: TeamItem | null }>({ open: false, row: null });
  const [members, setMembers] = useState<TeamItem | null>(null);
  const act = async (tm: TeamItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/teams/${tm.id}/${what}`), what === "archive" ? t("Team archived.") : t("Team restored."))).ok) q.reload(); };
  const cols: Column<TeamItem>[] = [
    { key: "n", header: t("Team"), cell: (tm) => <><b>{tm.name}</b>{tm.description && <><br /><small>{tm.description}</small></>}</> },
    { key: "m", header: t("Members"), cell: (tm) => tm.members },
    { key: "s", header: t("Status"), cell: (tm) => <Badge tone={tm.archived ? "gray" : "green"}>{tm.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: (tm) => <Can perm="teams.manage"><span className="row" style={{ gap: 6 }}>
      {!tm.archived && <Button size="sm" variant="outline" aria-label={`${t("Members")} ${tm.name}`} onClick={() => setMembers(tm)}>{t("Members")}</Button>}
      {!tm.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${tm.name}`} onClick={() => setDrawer({ open: true, row: tm })}>{t("Edit")}</Button>}
      <Button size="sm" variant="outline" aria-label={`${tm.archived ? t("Restore") : t("Archive")} ${tm.name}`} onClick={() => void act(tm, tm.archived ? "restore" : "archive")}>{tm.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Teams")} description={t("Group agents into teams such as Facilities, Transport or Reception.")}
        actions={<Can perm="teams.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New team")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("Teams")} columns={cols} rows={q.data ?? []} rowKey={(tm) => tm.id} emptyMessage={t("No teams yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <TeamDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} />
      <MembersDrawer team={members} onClose={() => setMembers(null)} onDone={q.reload} />
    </>
  );
}

export default function TeamsPage() { return <Require perm="teams.read"><Teams /></Require>; }
