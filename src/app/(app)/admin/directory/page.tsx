"use client";

import { useState } from "react";
import { Network, Plus, Trash2, Upload } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { KpiCard, KpiGrid } from "@/components/insights/kpi";
import { del, get, post } from "@/lib/api/client";
import type { DirectoryOverview, DirectorySyncResult, GroupMapping, RoleListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime } from "@/lib/format";
import { t } from "@/i18n";

const actionLabel = (a: DirectorySyncResult["details"][number]["action"]) => ({ updated: t("Updated"), unchanged: t("Unchanged"), not_found: t("Not in the app"), error: t("Not applied"), suspended: t("Suspended") })[a];
const actionTone: Record<DirectorySyncResult["details"][number]["action"], BadgeTone> = { updated: "green", unchanged: "gray", not_found: "orange", error: "red", suspended: "orange" };

/** INT-003 / ADM-004. Connect the company directory: groups that give roles (SSO and SCIM), the SCIM address, and the HR CSV sync. */
export default function DirectoryPage() {
  const run = useRun();
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const overview = useQuery(() => get<DirectoryOverview>("/api/directory/overview"), [tick]);
  const mappings = useQuery(() => get<GroupMapping[]>("/api/directory/mappings"), [tick]);
  const roles = useQuery(() => get<RoleListItem[]>("/api/roles").catch(() => [] as RoleListItem[]), []);
  const [group, setGroup] = useState(""); const [roleId, setRoleId] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({});
  const [csv, setCsv] = useState(""); const [missing, setMissing] = useState(false); const [result, setResult] = useState<DirectorySyncResult | null>(null); const [csvErr, setCsvErr] = useState("");
  const o = overview.data;
  const mappable = (roles.data ?? []).filter((r) => !r.archived && r.key !== "super_admin");
  const scimUrl = typeof window === "undefined" ? "/api/v1/scim/v2" : `${window.location.origin}/api/v1/scim/v2`;

  async function addMapping(e: React.FormEvent) {
    e.preventDefault(); setErrs({});
    const r = await run(() => post("/api/directory/mappings", { group, roleId: roleId || null }), t("Mapping added."), { silent: true });
    if (!r.ok) { setErrs(r.fields ?? { group: r.error }); return; }
    setGroup(""); setRoleId(""); reload();
  }
  async function removeMapping(m: GroupMapping) { const r = await run(() => del(`/api/directory/mappings/${m.id}`), t("Mapping removed.")); if (r.ok) reload(); }
  async function sync(dryRun: boolean) {
    setCsvErr("");
    const r = await run(() => post<DirectorySyncResult>("/api/directory/sync", { csv, dryRun, suspendMissing: missing }), dryRun ? t("Preview ready.") : t("Directory synced."), { silent: true });
    if (!r.ok) { setCsvErr(r.fields?.csv ?? r.error); return; }
    setResult(r.data); if (!dryRun) reload();
  }
  async function readFile(f: File | undefined) { if (f) { setCsv(await f.text()); setResult(null); } }

  return (
    <>
      <PageHeader title={t("Directory")} description={t("Connect the company directory: groups that give roles, SCIM from the identity provider, and the HR file that keeps employee data and suspends leavers.")} />
      {o && (
        <KpiGrid label={t("Directory at a glance")}>
          <KpiCard icon={<Network size={18} />} label={t("Group mappings")} value={String(o.mappings)} note={o.ssoGroupsClaim ? t("SSO groups apply at sign-in") : t("SSO is not configured")} />
          <KpiCard icon={<Network size={18} />} label={t("SCIM groups")} value={String(o.scimGroups)} note={t("{n} people from SCIM", { n: o.scimUsers })} />
          <KpiCard icon={<Upload size={18} />} label={t("Last SCIM change")} value={o.lastScimAt ? fmtDateTime(o.lastScimAt) : "—"} note={t("pushed by the identity provider")} />
          <KpiCard icon={<Upload size={18} />} label={t("Last HR sync")} value={o.lastSyncAt ? fmtDateTime(o.lastSyncAt) : "—"} note={t("CSV, here or from a nightly job")} />
        </KpiGrid>
      )}

      <Panel title={t("Groups that give roles")} description={t("People in a group (from the SSO groups claim or a SCIM group) get its role; leaving the group takes it away. Roles given by hand are not touched. Super Admin is never given by a group.")}>
        <div className="stack">
          {(mappings.data ?? []).length === 0 ? <p className="tab-placeholder">{t("No mappings yet.")}</p> : (
            <ul className="req-list" data-testid="dir-mappings">
              {(mappings.data ?? []).map((m) => (
                <li key={m.id}>
                  <div><b>{m.group}</b> → {m.roleName}<br /><small className="muted">{t("{n} people hold this role", { n: m.people })} · {fmtDateTime(m.createdAt)}</small></div>
                  <IconButton label={`${t("Remove")} ${m.group} → ${m.roleName}`} onClick={() => void removeMapping(m)}><Trash2 size={14} /></IconButton>
                </li>
              ))}
            </ul>
          )}
          <form className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }} noValidate onSubmit={addMapping} aria-label={t("Add a mapping")}>
            <Field label={t("Directory group")} htmlFor="dm-group" error={errs.group}><TextInput id="dm-group" maxLength={200} value={group} onChange={(e) => setGroup(e.target.value)} placeholder="GA-Agents" /></Field>
            <Field label={t("Gives the role")} htmlFor="dm-role" error={errs.roleId}><SelectInput id="dm-role" value={roleId} onChange={(e) => setRoleId(e.target.value)} options={[{ value: "", label: t("Choose…") }, ...mappable.map((r) => ({ value: r.id, label: r.name }))]} /></Field>
            <Button type="submit" size="sm" disabled={!group || !roleId}><Plus size={14} /> {t("Add")}</Button>
          </form>
        </div>
      </Panel>

      <Panel title={t("SCIM from the identity provider")}>
        <div className="stack">
          <p style={{ margin: 0 }}>{t("Base URL")}: <code data-testid="scim-url">{scimUrl}</code></p>
          <p className="muted" style={{ margin: 0 }}>{t("Create an API key with the scope “Directory: SCIM and HR sync” under Integrations and give it to the identity provider as the bearer token. People it creates sign in with SSO; turning them off suspends them, and removing them deactivates them.")}</p>
        </div>
      </Panel>

      <Panel title={t("HR sync (CSV)")} description={t("Columns: email (required), employee_id, department, job_title, manager_email, site_code, status (active or inactive). An empty cell leaves the value as it is. Nobody is created here.")}>
        <div className="stack">
          {csvErr && <div className="form-alert err" role="alert">{csvErr}</div>}
          <label className="btn sm" style={{ alignSelf: "flex-start" }}><Upload size={14} /> {t("Choose a CSV file")}<input type="file" accept=".csv,text/csv" className="sr-only" onChange={(e) => void readFile(e.target.files?.[0])} /></label>
          <Field label={t("Or paste the file")} htmlFor="dir-csv" full><textarea id="dir-csv" className="textarea" rows={6} value={csv} onChange={(e) => { setCsv(e.target.value); setResult(null); }} placeholder={"email,employee_id,department,job_title,manager_email,site_code,status\nani@company.com,E-001,Finance,Analyst,budi@company.com,JKT1,active"} /></Field>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={missing} onChange={(e) => { setMissing(e.target.checked); setResult(null); }} /> {t("Suspend people who are not in the file (never Super Admins or you)")}</label>
          <span className="row" style={{ gap: 8 }}>
            <Button onClick={() => void sync(true)} disabled={!csv.trim()}>{t("Preview")}</Button>
            <Button variant="primary" onClick={() => void sync(false)} disabled={!result || !result.dryRun}>{t("Apply")}</Button>
          </span>
          {result && (
            <div className="stack" data-testid="dir-result">
              <p style={{ margin: 0 }}><b>{result.dryRun ? t("Preview") : t("Applied")}:</b> {t("{u} updated, {s} suspended, {c} unchanged, {n} not in the app, {f} not applied", { u: result.updated, s: result.suspended, c: result.unchanged, n: result.notFound, f: result.failed })}</p>
              <ul className="req-list">
                {result.details.filter((d) => d.action !== "unchanged").slice(0, 200).map((d, i) => (
                  <li key={`${d.email}-${i}`}><div>{d.line > 0 ? `${t("Line {n}", { n: d.line })} · ` : ""}<b>{d.email || "—"}</b>{d.detail ? <><br /><small className="muted">{d.detail}</small></> : null}</div><Badge tone={actionTone[d.action]}>{actionLabel(d.action)}</Badge></li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </Panel>
    </>
  );
}
