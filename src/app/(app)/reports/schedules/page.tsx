"use client";

import { useState } from "react";
import { CalendarClock, Download, Pencil, Plus, Send, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { del, get, post, put } from "@/lib/api/client";
import type { ReportKind, ReportSchedule } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { fmtDateTime } from "@/lib/format";
import { t } from "@/i18n";

const reportLabel = (k: string) => ({ tickets: t("Tickets"), sla: t("SLA attainment"), csat: t("Satisfaction"), costs: t("Costs"), compliance: t("Maintenance compliance"), utilisation: t("Room and vehicle utilisation"), inventory: t("Stock on hand") })[k] ?? k;
const DAYS = () => [1, 7, 30, 90, 365].map((d) => ({ value: String(d), label: d === 1 ? t("Today") : t("Last {n} days", { n: d }) }));
const WEEKDAYS = () => [t("Monday"), t("Tuesday"), t("Wednesday"), t("Thursday"), t("Friday"), t("Saturday"), t("Sunday")];
const when = (s: ReportSchedule) => {
  const h = `${String(s.hour).padStart(2, "0")}:00`;
  return s.frequency === "daily" ? t("Every day at {h}", { h }) : s.frequency === "weekly" ? t("Every {day} at {h}", { day: WEEKDAYS()[s.weekday - 1], h }) : t("Day {d} of every month at {h}", { d: s.monthDay, h });
};

function ScheduleDrawer({ open, editing, kinds, onClose, onDone }: { open: boolean; editing: ReportSchedule | null; kinds: ReportKind[]; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const blank = { name: "", report: "", format: "xlsx", frequency: "weekly", weekday: "1", monthDay: "1", hour: "7", days: "7", recipients: "", active: true };
  const [f, setF] = useState(blank); const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [seed, setSeed] = useState<string | null>(null);
  const key = open ? (editing ? `${editing.id}:${editing.version}` : "new") : null;
  if (key !== seed) {
    setSeed(key); setErrs({}); setErr("");
    setF(editing ? { name: editing.name, report: editing.report, format: editing.format, frequency: editing.frequency, weekday: String(editing.weekday), monthDay: String(editing.monthDay), hour: String(editing.hour), days: String(editing.days), recipients: editing.recipients.map((r) => r.email).join(", "), active: editing.active } : { ...blank, report: kinds[0]?.key ?? "" });
  }
  const set = (k: keyof typeof blank, v: string | boolean) => setF((x) => ({ ...x, [k]: v }));
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const body = { name: f.name, report: f.report, format: f.format, frequency: f.frequency, weekday: Number(f.weekday), monthDay: Number(f.monthDay), hour: Number(f.hour), days: Number(f.days),
      recipients: f.recipients.split(/[\s,;]+/).filter(Boolean), active: f.active, version: editing?.version };
    const r = await run(() => (editing ? put(`/api/report-schedules/${editing.id}`, body) : post("/api/report-schedules", body)), t("Schedule saved."), { silent: true });
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit schedule") : t("New scheduled report")} description={t("The file is e-mailed to each person as they would see the report themselves.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rs-form">{t("Save")}</Button></>}>
      <form id="rs-form" className="stack" noValidate onSubmit={save}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="rs-name" required error={errs.name}><TextInput id="rs-name" data-autofocus maxLength={120} value={f.name} onChange={(e) => set("name", e.target.value)} placeholder={t("Weekly stock for the GA team")} /></Field>
        <Field label={t("Report")} htmlFor="rs-report" required error={errs.report}><SelectInput id="rs-report" value={f.report} onChange={(e) => set("report", e.target.value)} options={kinds.map((k) => ({ value: k.key, label: reportLabel(k.key) }))} /></Field>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("File")} htmlFor="rs-format" error={errs.format}><SelectInput id="rs-format" value={f.format} onChange={(e) => set("format", e.target.value)} options={[{ value: "xlsx", label: t("Excel (XLSX)") }, { value: "csv", label: "CSV" }]} /></Field>
          <Field label={t("Covering")} htmlFor="rs-days" error={errs.days}><SelectInput id="rs-days" value={f.days} onChange={(e) => set("days", e.target.value)} options={DAYS()} /></Field>
        </div>
        <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
          <Field label={t("How often")} htmlFor="rs-freq" error={errs.frequency}><SelectInput id="rs-freq" value={f.frequency} onChange={(e) => set("frequency", e.target.value)} options={[{ value: "daily", label: t("Every day") }, { value: "weekly", label: t("Every week") }, { value: "monthly", label: t("Every month") }]} /></Field>
          {f.frequency === "weekly" && <Field label={t("On")} htmlFor="rs-weekday" error={errs.weekday}><SelectInput id="rs-weekday" value={f.weekday} onChange={(e) => set("weekday", e.target.value)} options={WEEKDAYS().map((d, i) => ({ value: String(i + 1), label: d }))} /></Field>}
          {f.frequency === "monthly" && <Field label={t("Day of the month")} htmlFor="rs-monthday" error={errs.monthDay}><SelectInput id="rs-monthday" value={f.monthDay} onChange={(e) => set("monthDay", e.target.value)} options={Array.from({ length: 28 }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }))} /></Field>}
          <Field label={t("At")} htmlFor="rs-hour" error={errs.hour}><SelectInput id="rs-hour" value={f.hour} onChange={(e) => set("hour", e.target.value)} options={Array.from({ length: 24 }, (_, i) => ({ value: String(i), label: `${String(i).padStart(2, "0")}:00` }))} /></Field>
        </div>
        <Field label={t("Send to (e-mail addresses)")} htmlFor="rs-to" required error={errs.recipients}>
          <textarea id="rs-to" className="textarea" rows={3} value={f.recipients} onChange={(e) => set("recipients", e.target.value)} placeholder="ani@company.com, budi@company.com" />
        </Field>
        <p className="muted">{t("Only people who can open this report in the app can receive it.")}</p>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={f.active} onChange={(e) => set("active", e.target.checked)} /> {t("Active")}</label>
      </form>
    </Overlay>
  );
}

/** RPT-007. Download any report you can see as CSV or Excel, or have it e-mailed on a schedule. */
export default function ReportSchedulesPage() {
  const run = useRun();
  const [tick, setTick] = useState(0); const reload = () => setTick((n) => n + 1);
  const kinds = useQuery(() => get<ReportKind[]>("/api/reports/catalog"), []);
  const list = useQuery(() => get<ReportSchedule[]>("/api/report-schedules"), [tick]);
  const [report, setReport] = useState(""); const [days, setDays] = useState("30"); const [format, setFormat] = useState("xlsx");
  const [drawer, setDrawer] = useState<{ editing: ReportSchedule | null } | null>(null);
  const ks = kinds.data ?? []; const chosen = report || ks[0]?.key || "";
  const href = `/api/reports/export?${new URLSearchParams({ report: chosen, days, format })}`;
  const rows = list.data ?? [];
  async function sendNow(s: ReportSchedule) { const r = await run(() => post(`/api/report-schedules/${s.id}/send`), t("Sent.")); if (r.ok) reload(); }
  async function remove(s: ReportSchedule) { const r = await run(() => del(`/api/report-schedules/${s.id}`), t("Schedule removed.")); if (r.ok) reload(); }
  return (
    <>
      <PageHeader title={t("Report exports")} description={t("Download a report as CSV or Excel, or have it e-mailed on a schedule.")}
        actions={ks.length > 0 ? <Button variant="primary" onClick={() => setDrawer({ editing: null })}><Plus size={16} /> {t("New schedule")}</Button> : undefined} />
      {ks.length === 0 && !kinds.loading ? <Panel><p className="tab-placeholder">{t("You have no reports to export.")}</p></Panel> : (
        <Panel title={t("Download now")}>
          <div className="filters" style={{ alignItems: "flex-end" }}>
            <Field label={t("Report")} htmlFor="rx-report"><SelectInput id="rx-report" value={chosen} onChange={(e) => setReport(e.target.value)} options={ks.map((k) => ({ value: k.key, label: reportLabel(k.key) }))} /></Field>
            <Field label={t("Covering")} htmlFor="rx-days"><SelectInput id="rx-days" value={days} onChange={(e) => setDays(e.target.value)} options={DAYS()} /></Field>
            <Field label={t("File")} htmlFor="rx-format"><SelectInput id="rx-format" value={format} onChange={(e) => setFormat(e.target.value)} options={[{ value: "xlsx", label: t("Excel (XLSX)") }, { value: "csv", label: "CSV" }]} /></Field>
            <a className="btn primary" href={href} download data-testid="rx-download"><Download size={16} /> {t("Download")}</a>
          </div>
        </Panel>
      )}
      <Panel title={t("Scheduled reports")}>
        {rows.length === 0 ? <p className="tab-placeholder">{t("No scheduled reports yet.")}</p> : (
          <ul className="req-list" data-testid="rs-list">
            {rows.map((s) => (
              <li key={s.id}>
                <div>
                  <b>{s.name}</b> · {reportLabel(s.report)} · {s.format.toUpperCase()}
                  <br /><small className="muted"><CalendarClock size={12} aria-hidden /> {when(s)} · {t("next {at}", { at: fmtDateTime(s.nextRunAt) })}{s.owner && !s.mine ? ` · ${t("by {who}", { who: s.owner })}` : ""}</small>
                  <br /><small className="muted">{t("To")}: {s.recipients.map((r) => r.canSee ? r.name : `${r.name} (${t("no access")})`).join(", ")}</small>
                  {s.lastResult && <><br /><small className="muted" data-testid="rs-last">{t("Last run {at}", { at: fmtDateTime(s.lastRunAt) })}: {s.lastResult}</small></>}
                </div>
                <span className="row" style={{ gap: 6 }}>
                  {!s.active && <Badge tone="gray">{t("Paused")}</Badge>}
                  {s.mine && <Button size="sm" onClick={() => void sendNow(s)}><Send size={14} /> {t("Send now")}</Button>}
                  {s.mine && <IconButton label={`${t("Edit")} ${s.name}`} onClick={() => setDrawer({ editing: s })}><Pencil size={14} /></IconButton>}
                  {s.mine && <IconButton label={`${t("Remove")} ${s.name}`} onClick={() => void remove(s)}><Trash2 size={14} /></IconButton>}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Panel>
      <ScheduleDrawer open={!!drawer} editing={drawer?.editing ?? null} kinds={ks} onClose={() => setDrawer(null)} onDone={reload} />
    </>
  );
}
