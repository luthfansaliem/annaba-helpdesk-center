"use client";

import { useEffect, useRef, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { RolePicker } from "./role-picker";
import { get, post } from "@/lib/api/client";
import type { ImportUsersResult, RoleListItem } from "@/lib/api/types";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TEMPLATE = "name,email,phone,department,jobTitle\nJane Doe,jane.doe@example.com,+62 811 0000 0000,Finance,Analyst\n";

/** USR-006 — bulk-invite from a CSV. Always checks the file first (nothing is written), then imports on a second, explicit click. */
export function ImportUsersDrawer({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun(); const fileRef = useRef<HTMLInputElement>(null);
  const [csv, setCsv] = useState(""); const [fileName, setFileName] = useState("");
  const [roles, setRoles] = useState<RoleListItem[]>([]); const [roleIds, setRoleIds] = useState<string[]>([]);
  const [check, setCheck] = useState<ImportUsersResult | null>(null); const [done, setDone] = useState<ImportUsersResult | null>(null);
  const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!open) return;
    setCsv(""); setFileName(""); setCheck(null); setDone(null); setErr(""); setRoleIds([]);
    get<RoleListItem[]>("/api/roles").then((r) => { setRoles(r); const u = r.find((x) => x.key === "user"); if (u) setRoleIds([u.id]); }).catch(() => setErr(t("Could not load roles.")));
  }, [open]);

  async function pick(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; setCheck(null); setDone(null); setErr("");
    if (!f) return;
    if (f.size > 200_000) { setErr(t("The file is larger than 200 KB.")); return; }
    setCsv(await f.text()); setFileName(f.name);
  }
  async function send(dryRun: boolean) {
    setErr(""); if (!csv) { setErr(t("Choose a CSV file first.")); return; } if (roleIds.length === 0) { setErr(t("Select at least one role.")); return; }
    setBusy(true);
    const r = await run(() => post<ImportUsersResult>("/api/users/import", { csv, roleIds, dryRun }), dryRun ? "" : "Import finished.", { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    if (dryRun) setCheck(r.data); else { setDone(r.data); onDone(); }
  }
  const shown = done ?? check; const okRows = check ? check.total - check.failed : 0;

  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Import users")} description={t("Invite many people at once from a CSV file. Nobody receives a password.")}
      footer={done ? <Button variant="primary" onClick={onClose}>{t("Close")}</Button> : <><Button onClick={onClose}>{t("Cancel")}</Button>
        {!check ? <Button variant="primary" disabled={busy || !csv} onClick={() => send(true)}>{busy ? t("Checking…") : t("Check file")}</Button>
          : <Button variant="primary" disabled={busy || okRows === 0} onClick={() => send(false)}>{busy ? t("Importing…") : t("Import {okRows} {v1}", { okRows: okRows, v1: okRows === 1 ? "person" : "people" })}</Button>}</>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {!done && <>
          <div>
            <label htmlFor="imp-file" className="fl-label">{t("CSV file")}</label>
            <input id="imp-file" ref={fileRef} type="file" accept=".csv,text/csv" onChange={pick} />
            <p className="field-help">{t("Columns:")}{" "}<code>name</code>, <code>email</code> {t("(required);")}{" "}<code>phone</code>, <code>department</code>, <code>jobTitle</code> {t("(optional). Up to 200 rows.")}{" "}{fileName && <b>{fileName}</b>}{" "}
              <a href={`data:text/csv;charset=utf-8,${encodeURIComponent(TEMPLATE)}`} download="users-template.csv">{t("Download a template")}</a></p>
          </div>
          <fieldset className="fl"><legend>{t("Roles for everyone in this file")}{" "}<em>*</em></legend><RolePicker roles={roles} value={roleIds} onChange={(v) => { setRoleIds(v); setCheck(null); }} idPrefix="imp-role" /></fieldset>
        </>}
        {shown && <>
          <div className={`form-alert ${shown.failed ? "warn" : "ok"}`} role="status">
            {done ? t("{created} invited, {failed} skipped.", { created: done.created, failed: done.failed }) : t("{okRows} of {total} rows are ready{v2}.", { okRows: okRows, total: shown.total, v2: shown.failed ? `; ${shown.failed} will be skipped` : "" })}
          </div>
          {shown.rows.some((r) => r.status === "error") && (
            <table className="tbl" aria-label={t("Rows with problems")}><thead><tr><th>{t("Line")}</th><th>{t("Email")}</th><th>{t("Problem")}</th></tr></thead>
              <tbody>{shown.rows.filter((r) => r.status === "error").map((r) => <tr key={r.line}><td>{r.line}</td><td>{r.email || "—"}</td><td>{r.message}</td></tr>)}</tbody></table>
          )}
        </>}
      </div>
    </Overlay>
  );
}
