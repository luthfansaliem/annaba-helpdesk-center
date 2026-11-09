"use client";

import { useEffect, useState } from "react";
import { Lock, RotateCcw, Save } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get, put } from "@/lib/api/client";
import type { MatrixView } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** PERM-002 — roles as rows, permissions grouped by module, unsaved-change tracking, confirm-before-save. */
function Matrix({ m, reload }: { m: MatrixView; reload: () => void }) {
  const { can } = useAuth(); const run = useRun();
  const [draft, setDraft] = useState<Record<string, string[]>>({});
  const [confirm, setConfirm] = useState(false); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => setDraft({}), [m.version]);
  const base = Object.fromEntries(m.roles.map((r) => [r.id, r.permissions])) as Record<string, string[]>;
  const cur = (id: string) => draft[id] ?? base[id];
  const editable = can("permissions.assign");
  const grantable = new Set(m.grantable);
  const diffs = m.roles.flatMap((r) => { const a = cur(r.id), b = base[r.id]; return [...a.filter((p) => !b.includes(p)).map((p) => ({ id: r.id, role: r.name, p, add: true })), ...b.filter((p) => !a.includes(p)).map((p) => ({ id: r.id, role: r.name, p, add: false }))]; });
  const mods = [...new Set(m.permissions.map((p) => p.module))];
  const toggle = (rid: string, p: string) => setDraft((d) => { const c = d[rid] ?? base[rid]; return { ...d, [rid]: c.includes(p) ? c.filter((x) => x !== p) : [...c, p] }; });

  async function save() {
    setErr(""); setBusy(true);
    const byRole = new Map<string, { roleId: string; add: string[]; remove: string[] }>();
    for (const d of diffs) { const e = byRole.get(d.id) ?? { roleId: d.id, add: [], remove: [] }; (d.add ? e.add : e.remove).push(d.p); byRole.set(d.id, e); }
    const r = await run(() => put("/api/access-matrix", { changes: [...byRole.values()], expectedVersion: m.version }), t("Access matrix saved."), { silent: true });
    setBusy(false);
    if (r.ok) { setConfirm(false); setDraft({}); reload(); }
    else { setErr(r.code === "conflict" ? "The matrix changed since you opened it. It was reloaded — review and try again." : r.error); if (r.code === "conflict") { setConfirm(false); setDraft({}); reload(); } }
  }
  return (
    <>
      <PageHeader title={t("Access Matrix")} description={t("Review and change which permissions each role grants.")}
        actions={editable ? <div style={{ display: "flex", gap: 8 }}><Button disabled={!diffs.length} onClick={() => { setDraft({}); setErr(""); }}><RotateCcw size={14} /> {t("Discard")}</Button><Button variant="primary" disabled={!diffs.length} onClick={() => setConfirm(true)}><Save size={15} /> {t("Save changes")}</Button></div> : undefined} />
      {err && !confirm && <div className="form-alert err" role="alert">{err}</div>}
      {diffs.length > 0 && <div className="form-alert warn" role="status">{t("You have")}{" "}{diffs.length} {t("unsaved change(s). Nothing is applied until you confirm.")}</div>}
      <Panel>
        <div className="matrix-wrap" tabIndex={0} aria-label={t("Access matrix, scrollable")}>
          <table className="matrix">
            <thead>
              <tr><th rowSpan={2} className="sticky-col">{t("Role")}</th>{mods.map((x) => <th key={x} colSpan={m.permissions.filter((p) => p.module === x).length} className="mod">{x}</th>)}</tr>
              <tr>{m.permissions.map((p) => <th key={p.key} className="perm" title={p.description}><span>{p.key.split(".")[1]}</span></th>)}</tr>
            </thead>
            <tbody>
              {m.roles.map((r) => (
                <tr key={r.id}>
                  <th scope="row" className="sticky-col">{r.name}{r.isProtected && <Lock size={11} style={{ marginLeft: 6 }} aria-label={t("Protected")} />}</th>
                  {m.permissions.map((p) => {
                    const on = cur(r.id).includes(p.key); const changed = on !== base[r.id].includes(p.key);
                    const blocked = !editable || r.isProtected || (!on && !grantable.has(p.key));
                    return (
                      <td key={p.key} className={changed ? "changed" : ""}>
                        <input type="checkbox" className="cb" checked={on} disabled={blocked} aria-label={`${r.name}: ${p.key}`} title={r.isProtected ? t("Protected role") : !grantable.has(p.key) && !on ? t("You can't grant a permission you don't hold") : p.description} onChange={() => toggle(r.id, p.key)} />
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p className="field-help" style={{ margin: "12px 0 0" }}>{t("Changed cells are highlighted. Protected roles and permissions you don't hold are locked.")}</p>
      </Panel>
      <Overlay open={confirm} onClose={() => setConfirm(false)} title={t("Save access changes?")} description={t("These changes affect users on their next request.")}
        footer={<><Button onClick={() => setConfirm(false)}>{t("Cancel")}</Button><Button variant="primary" data-autofocus disabled={busy} onClick={save}>{busy ? t("Saving…") : t("Confirm & save")}</Button></>}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <ul className="diff-list">{diffs.map((d) => <li key={d.id + d.p} className={d.add ? "add" : "rem"}><b>{d.add ? "+" : "−"}</b> <code>{d.p}</code> <span>{d.add ? t("granted to") : t("removed from")} {d.role}</span></li>)}</ul>
      </Overlay>
    </>
  );
}

export default function AccessMatrixPage() {
  const q = useQuery(() => get<MatrixView>("/api/access-matrix"), []);
  return <Require perm="permissions.read"><QueryBoundary q={q} rows={6}>{(m) => <Matrix m={m} reload={q.reload} />}</QueryBoundary></Require>;
}
