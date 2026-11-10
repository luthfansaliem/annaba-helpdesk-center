"use client";

import { useState } from "react";
import { ArrowLeft } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { post } from "@/lib/api/client";
import type { ImportReport } from "@/lib/api/types";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const COLUMNS = "tag, name, type, serial, status, location_code, assigned_to_email, department, purchase_date, purchase_cost, warranty_end, vendor, notes";

function Import() {
  const run = useRun();
  const [csv, setCsv] = useState(""); const [file, setFile] = useState(""); const [report, setReport] = useState<ImportReport | null>(null); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  async function pick(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; setReport(null); setErr("");
    if (!f) return;
    if (f.size > 2_000_000) { setErr(t("The file is too large (at most 2 MB).")); return; }
    setCsv(await f.text()); setFile(f.name);
  }
  async function send(dryRun: boolean) {
    setBusy(true); setErr("");
    const r = await run(() => post<ImportReport>("/api/assets/import", { csv, dryRun }), undefined, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.csv ?? r.error); setReport(null); return; }
    setReport(r.data);
  }
  const ok = report && report.problems.length === 0;
  return (
    <>
      <PageHeader title={t("Import assets")} description={t("Add and update many assets from a CSV file. Every line is checked first; nothing is written unless all lines are correct.")}
        actions={<Button href="/assets"><ArrowLeft size={14} /> {t("Assets")}</Button>} />
      <div className="stack">
        <Panel title={t("The file")} description={t("First line: the column names. “name” and “type” are required. A line whose tag already exists updates that asset, and an empty cell keeps its current value; other lines create assets (no tag: the next automatic one). Dates as yyyy-mm-dd, amounts with a dot for decimals. A type's own questions go in x_<question key> columns. Tip: export first, edit, then import.")}>
          <div className="stack">
            <p style={{ margin: 0 }}><small>{t("Columns:")} <code>{COLUMNS}</code>, <code>x_…</code></small></p>
            <Field label={t("CSV file")} htmlFor="im-file"><input id="im-file" type="file" accept=".csv,text/csv" onChange={(e) => void pick(e)} /></Field>
            {file && <p style={{ margin: 0 }}>{t("{name} is ready to check.", { name: file })}</p>}
            {err && <div className="form-alert err" role="alert">{err}</div>}
            <span className="row" style={{ gap: 8 }}>
              <Button onClick={() => void send(true)} disabled={!csv || busy}>{busy ? t("Checking…") : t("Check the file")}</Button>
              <Button variant="primary" onClick={() => void send(false)} disabled={!csv || busy || !ok || report?.applied}>{t("Import")}</Button>
            </span>
          </div>
        </Panel>
        {report && <Panel title={report.applied ? t("Imported") : ok ? t("Ready to import") : t("Problems to fix")}>
          <div className="stack" data-testid="import-report">
            <p role="status" style={{ margin: 0 }}>{report.applied
              ? t("{c} assets created and {u} updated.", { c: report.creates, u: report.updates })
              : ok ? t("{rows} lines are correct: {c} would be created and {u} updated. Nothing has been written yet.", { rows: report.rows, c: report.creates, u: report.updates })
                : t("{n} of {rows} lines have problems. Fix them in the file and check again; nothing has been written.", { n: report.problems.length, rows: report.rows })}</p>
            {report.problems.length > 0 && <div className="tbl-wrap"><table className="tbl" aria-label={t("Problems by line")}>
              <thead><tr><th scope="col">{t("Line")}</th><th scope="col">{t("Tag")}</th><th scope="col">{t("Problem")}</th></tr></thead>
              <tbody>{report.problems.map((p) => <tr key={p.line}><td>{p.line}</td><td>{p.tag ?? "—"}</td><td><ul style={{ margin: 0, paddingLeft: 16 }}>{Object.entries(p.errors).map(([k, v]) => <li key={k}><code>{k}</code>: {v}</li>)}</ul></td></tr>)}</tbody>
            </table></div>}
          </div>
        </Panel>}
      </div>
    </>
  );
}

export default function AssetImportPage() { return <Require perm="assets.import"><Import /></Require>; }
