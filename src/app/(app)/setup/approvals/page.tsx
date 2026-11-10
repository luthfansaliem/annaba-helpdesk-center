"use client";

import { useCallback, useRef, useState } from "react";
import { ArrowDown, ArrowUp, Plus, X } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { PersonPicker } from "@/components/forms/dynamic-form";
import { Can, Require } from "@/components/page-guard";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { get, post, put } from "@/lib/api/client";
import type { ApprovalPolicyItem, ApprovalStepDef, PortalPerson, RoleListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Draft = { name: string; rule: "any" | "all"; users: { id: string; name: string }[]; roleIds: string[]; manager: boolean };
const blankStep = (): Draft => ({ name: "", rule: "any", users: [], roleIds: [], manager: false });

function summarize(p: ApprovalPolicyItem): string {
  if (!p.hasPolicy) return t("No approval needed");
  return p.steps.map((s) => `${s.name} (${s.rule === "all" ? t("all of") : t("any of")} ${s.users.length + s.roles.length + (s.requesterManager ? 1 : 0)})`).join(" → ");
}

function PolicyDrawer({ open, onClose, onDone, editing, roles }: { open: boolean; onClose: () => void; onDone: () => void; editing: ApprovalPolicyItem | null; roles: RoleListItem[] }) {
  const run = useRun();
  const names = useRef(new Map<string, string>());
  const [steps, setSteps] = useState<Draft[]>([blankStep()]); const [fallback, setFallback] = useState<{ id: string; name: string } | null>(null); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing ? `${editing.serviceId}:${editing.version}` : "none") : null;
  if (current !== key) {
    setKey(current); setErrs({}); setErr("");
    setSteps(editing?.hasPolicy ? editing.steps.map((s) => ({ name: s.name, rule: s.rule, users: s.users.map((u) => ({ id: u.id, name: u.name })), roleIds: s.roles.map((r) => r.id), manager: !!s.requesterManager })) : [blankStep()]);
    setFallback(editing?.fallbackUserId ? { id: editing.fallbackUserId, name: editing.fallbackName ?? "" } : null);
  }
  const search = useCallback(async (q: string) => (await get<PortalPerson[]>("/api/portal/people", { q })).map((p) => { names.current.set(p.id, p.name); return { value: p.id, label: p.department ? `${p.name} · ${p.department}` : p.name }; }), []);
  const patchStep = (i: number, p: Partial<Draft>) => setSteps((s) => s.map((x, k) => (k === i ? { ...x, ...p } : x)));
  const move = (i: number, d: -1 | 1) => setSteps((s) => { const j = i + d; if (j < 0 || j >= s.length) return s; const c = [...s]; [c[i], c[j]] = [c[j], c[i]]; return c; });

  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!editing) return; setErr(""); setErrs({}); setBusy(true);
    const body = { steps: steps.map<ApprovalStepDef>((s) => ({ name: s.name, rule: s.rule, userIds: s.users.map((u) => u.id), roleIds: s.roleIds, requesterManager: s.manager })), fallbackUserId: fallback?.id ?? null, version: editing.hasPolicy ? editing.version : undefined };
    const r = await run(() => put(`/api/approvals/policies/${editing.serviceId}`, body), t("Approval policy saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Approval for {service}", { service: editing.serviceName }) : t("Approval")} description={t("Steps run in order. A step is settled by any one approver, or needs all of them. Nobody can approve their own request.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="apr-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="apr-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {errs.steps && <p className="field-error" role="alert">{errs.steps}</p>}
        {steps.map((s, i) => (
          <fieldset key={i} className="stack" style={{ border: "1px solid var(--border, #d1d5db)", borderRadius: 8, padding: 12, margin: 0 }}>
            <legend style={{ fontWeight: 700, fontSize: 12, padding: "0 4px" }}>{t("Step {n}", { n: i + 1 })}</legend>
            <Field label={t("Name")} htmlFor={`ap-n${i}`} required error={errs[`steps[${i}].name`]}><TextInput id={`ap-n${i}`} value={s.name} onChange={(e) => patchStep(i, { name: e.target.value })} /></Field>
            <Field label={t("Who must approve")} htmlFor={`ap-r${i}`} error={errs[`steps[${i}].rule`]}>
              <SelectInput id={`ap-r${i}`} value={s.rule} onChange={(e) => patchStep(i, { rule: e.target.value as "any" | "all" })} options={[{ value: "any", label: t("Any one of them") }, { value: "all", label: t("All of them") }]} />
            </Field>
            <label className="row" style={{ gap: 8 }}><Checkbox checked={s.manager} onChange={(e) => patchStep(i, { manager: e.target.checked })} /> {t("The requester's manager")}</label>
            <Field label={t("Add a person")} htmlFor={`ap-p${i}`} error={errs[`steps[${i}].userIds`]}>
              <PersonPicker id={`ap-p${i}`} value="" onChange={(id) => { if (id && !s.users.some((u) => u.id === id)) patchStep(i, { users: [...s.users, { id, name: names.current.get(id) ?? "?" }] }); }} search={search} />
            </Field>
            {s.users.length > 0 && <ul className="row" style={{ gap: 6, flexWrap: "wrap", listStyle: "none", margin: 0, padding: 0 }}>
              {s.users.map((u) => <li key={u.id}><Badge tone="blue" dot={false}>{u.name} <button type="button" aria-label={`${t("Remove")} ${u.name}`} onClick={() => patchStep(i, { users: s.users.filter((x) => x.id !== u.id) })} style={{ background: "none", border: 0, cursor: "pointer", padding: 0 }}><X size={12} /></button></Badge></li>)}
            </ul>}
            {roles.length > 0 && <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
              <legend style={{ fontWeight: 700, fontSize: 12, padding: 0 }}>{t("Or anyone with a role")}</legend>
              {roles.filter((r) => !r.archived).map((r) => (
                <label key={r.id} className="row" style={{ gap: 8, padding: "2px 0" }}>
                  <Checkbox checked={s.roleIds.includes(r.id)} onChange={(e) => patchStep(i, { roleIds: e.target.checked ? [...s.roleIds, r.id] : s.roleIds.filter((x) => x !== r.id) })} /> {r.name}
                </label>
              ))}
            </fieldset>}
            <span className="row" style={{ gap: 6 }}>
              <IconButton label={`${t("Move up")} ${t("Step {n}", { n: i + 1 })}`} disabled={i === 0} onClick={() => move(i, -1)}><ArrowUp size={14} /></IconButton>
              <IconButton label={`${t("Move down")} ${t("Step {n}", { n: i + 1 })}`} disabled={i === steps.length - 1} onClick={() => move(i, 1)}><ArrowDown size={14} /></IconButton>
              {steps.length > 1 && <Button size="sm" variant="danger-outline" onClick={() => setSteps((x) => x.filter((_, k) => k !== i))}>{t("Remove step")}</Button>}
            </span>
          </fieldset>
        ))}
        {steps.length < 5 && <Button onClick={() => setSteps((s) => [...s, blankStep()])}><Plus size={14} /> {t("Add a step")}</Button>}
        <Field label={t("Fallback approver")} htmlFor="ap-fb" error={errs.fallbackUserId}>
          <PersonPicker id="ap-fb" value={fallback?.id ?? ""} onChange={(id) => setFallback(id ? { id, name: names.current.get(id) ?? "" } : null)} search={search} />
        </Field>
        <p><small>{fallback ? `${t("Fallback")}: ${fallback.name}. ` : ""}{t("The fallback decides when everyone named in a step is the requester or the person filing it.")}</small></p>
      </form>
    </Overlay>
  );
}

function Policies() {
  const run = useRun();
  const q = useQuery(() => get<ApprovalPolicyItem[]>("/api/approvals/policies"), []);
  const roles = useQuery(() => get<RoleListItem[]>("/api/roles").catch(() => [] as RoleListItem[]), []);
  const [drawer, setDrawer] = useState<{ open: boolean; row: ApprovalPolicyItem | null }>({ open: false, row: null });
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const remove = (p: ApprovalPolicyItem) => setConfirm({ title: t("Remove approval?"), confirmLabel: t("Remove"), danger: true, body: <>{t("New requests for")} <b>{p.serviceName}</b> {t("will go straight to the team. Requests already waiting keep their approvers.")}</>,
    onConfirm: async () => { if ((await run(() => post(`/api/approvals/policies/${p.serviceId}/remove`), t("Approval removed."))).ok) q.reload(); } });
  const cols: Column<ApprovalPolicyItem>[] = [
    { key: "s", header: t("Service"), cell: (p) => <><b>{p.serviceName}</b><br /><small>{p.categoryName}</small></> },
    { key: "p", header: t("Published"), className: "hide-sm", cell: (p) => <Badge tone={p.published ? "green" : "gray"}>{p.published ? t("Published") : t("Draft")}</Badge> },
    { key: "a", header: t("Approval"), cell: (p) => <span>{summarize(p)}{p.hasPolicy && <><br /><small>{p.steps.map((s) => [...(s.requesterManager ? [t("The requester's manager")] : []), ...s.users.map((u) => u.name), ...s.roles.map((r) => r.name)].join(", ")).join(" → ")}</small></>}</span> },
    { key: "x", header: t("Actions"), cell: (p) => <Can perm="approvals.manage"><span className="row" style={{ gap: 6 }}>
      <Button size="sm" variant="outline" aria-label={`${p.hasPolicy ? t("Edit") : t("Set up")} ${p.serviceName}`} onClick={() => setDrawer({ open: true, row: p })}>{p.hasPolicy ? t("Edit") : t("Set up")}</Button>
      {p.hasPolicy && <Button size="sm" variant="outline" aria-label={`${t("Remove")} ${p.serviceName}`} onClick={() => remove(p)}>{t("Remove")}</Button>}</span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Approval Policies")} description={t("Which services need someone's approval before work starts, and who decides.")} />
      <Panel>
        <DataTable label={t("Approval policies")} columns={cols} rows={q.data ?? []} rowKey={(p) => p.serviceId} emptyMessage={t("No services yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <PolicyDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} roles={roles.data ?? []} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </>
  );
}

export default function ApprovalPoliciesPage() { return <Require perm="approvals.read"><Policies /></Require>; }
