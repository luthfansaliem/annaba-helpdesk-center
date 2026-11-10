"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { CalendarHoliday, CalendarItem, CalendarPeriod, SaveCalendar } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const DAYS = [1, 2, 3, 4, 5, 6, 0]; // Monday first
const dayName = (d: number) => [t("Sunday"), t("Monday"), t("Tuesday"), t("Wednesday"), t("Thursday"), t("Friday"), t("Saturday")][d];
const toTime = (m: number) => `${String(Math.floor(m / 60)).padStart(2, "0")}:${String(m % 60).padStart(2, "0")}`;
const fromTime = (s: string) => { const [h, m] = s.split(":").map(Number); return (h || 0) * 60 + (m || 0); };
const toEndTime = (m: number) => (m >= 1440 ? "24:00" : toTime(m));

const blank: SaveCalendar = { name: "", timeZone: "Asia/Jakarta", isAlwaysOpen: false, periods: [1, 2, 3, 4, 5].map((day) => ({ day, startMinute: 480, endMinute: 1020 })), holidays: [] };

function CalendarDrawer({ open, onClose, onDone, editing }: { open: boolean; onClose: () => void; onDone: () => void; editing: CalendarItem | null }) {
  const run = useRun();
  const [f, setF] = useState<SaveCalendar>(blank); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const [hDate, setHDate] = useState(""); const [hName, setHName] = useState("");
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) {
    setKey(current); setErrs({}); setErr(""); setHDate(""); setHName("");
    setF(editing ? { name: editing.name, timeZone: editing.timeZone, isAlwaysOpen: editing.isAlwaysOpen, periods: editing.periods, holidays: editing.holidays, version: editing.version } : blank);
  }
  const setPeriod = (day: number, i: number, p: Partial<CalendarPeriod>) => setF((x) => {
    let n = -1; return { ...x, periods: x.periods.map((q) => (q.day === day && ++n === i ? { ...q, ...p } : q)) };
  });
  const addPeriod = (day: number) => setF((x) => ({ ...x, periods: [...x.periods, { day, startMinute: 480, endMinute: 1020 }] }));
  const removePeriod = (day: number, i: number) => setF((x) => { let n = -1; return { ...x, periods: x.periods.filter((q) => !(q.day === day && ++n === i)) }; });
  const addHoliday = () => { if (!hDate || !hName.trim()) return; setF((x) => ({ ...x, holidays: [...x.holidays.filter((h) => h.date !== hDate), { date: hDate, name: hName.trim() } as CalendarHoliday].sort((a, b) => a.date.localeCompare(b.date)) })); setHDate(""); setHName(""); };

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => (editing ? put(`/api/calendars/${editing.id}`, f) : post("/api/calendars", f)), editing ? t("Calendar saved.") : t("Calendar created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" width={560} title={editing ? t("Edit calendar") : t("New calendar")} description={t("Service-level clocks only run during working windows and stop on holidays.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cal-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="cal-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="cl-name" required error={errs.name}><TextInput id="cl-name" data-autofocus value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
        <Field label={t("Time zone")} htmlFor="cl-tz" required error={errs.timeZone}><TextInput id="cl-tz" value={f.timeZone} onChange={(e) => setF({ ...f, timeZone: e.target.value })} placeholder="Asia/Jakarta" /></Field>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={f.isAlwaysOpen} onChange={(e) => setF({ ...f, isAlwaysOpen: e.target.checked })} /> {t("Open 24 hours, every day")}</label>
        {!f.isAlwaysOpen && (
          <fieldset className="stack" style={{ border: 0, padding: 0 }}>
            <legend><b>{t("Working windows")}</b></legend>
            {errs.periods && <p className="field-error" role="alert">{errs.periods}</p>}
            {DAYS.map((d) => {
              const rows = f.periods.filter((p) => p.day === d);
              return (
                <div key={d} className="row" style={{ gap: 8, alignItems: "center", flexWrap: "wrap" }}>
                  <span style={{ width: 96 }}>{dayName(d)}</span>
                  {rows.length === 0 && <small>{t("Closed")}</small>}
                  {rows.map((p, i) => (
                    <span key={i} className="row" style={{ gap: 4 }}>
                      <input type="time" aria-label={`${dayName(d)} ${t("opens")}`} value={toTime(p.startMinute)} onChange={(e) => setPeriod(d, i, { startMinute: fromTime(e.target.value) })} />
                      –
                      <input type="time" aria-label={`${dayName(d)} ${t("closes")}`} value={toEndTime(p.endMinute)} max="23:59" onChange={(e) => setPeriod(d, i, { endMinute: fromTime(e.target.value) })} />
                      <Button size="sm" variant="outline" type="button" aria-label={`${t("Remove window")} ${dayName(d)}`} onClick={() => removePeriod(d, i)}><Trash2 size={14} /></Button>
                    </span>
                  ))}
                  <Button size="sm" variant="outline" type="button" aria-label={`${t("Add window")} ${dayName(d)}`} onClick={() => addPeriod(d)}><Plus size={14} /></Button>
                </div>
              );
            })}
          </fieldset>
        )}
        <fieldset className="stack" style={{ border: 0, padding: 0 }}>
          <legend><b>{t("Holidays")}</b></legend>
          {errs.holidays && <p className="field-error" role="alert">{errs.holidays}</p>}
          {f.holidays.map((h) => (
            <div key={h.date} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
              <span>{h.date} · {h.name}</span>
              <Button size="sm" variant="outline" type="button" aria-label={`${t("Remove holiday")} ${h.name}`} onClick={() => setF((x) => ({ ...x, holidays: x.holidays.filter((q) => q.date !== h.date) }))}><Trash2 size={14} /></Button>
            </div>
          ))}
          <div className="row" style={{ gap: 8 }}>
            <input type="date" aria-label={t("Holiday date")} value={hDate} onChange={(e) => setHDate(e.target.value)} />
            <TextInput aria-label={t("Holiday name")} value={hName} onChange={(e) => setHName(e.target.value)} placeholder={t("Holiday name")} />
            <Button type="button" onClick={addHoliday} disabled={!hDate || !hName.trim()}>{t("Add")}</Button>
          </div>
        </fieldset>
      </form>
    </Overlay>
  );
}

const summary = (c: CalendarItem) => {
  if (c.isAlwaysOpen) return t("24/7");
  const days = DAYS.filter((d) => c.periods.some((p) => p.day === d));
  if (!days.length) return "—";
  const first = c.periods.filter((p) => p.day === days[0])[0];
  return `${days.map((d) => dayName(d).slice(0, 3)).join(", ")} · ${toTime(first.startMinute)}–${toEndTime(first.endMinute)}`;
};

function Calendars() {
  const run = useRun(); const [archived, setArchived] = useState(false);
  const q = useQuery(() => get<CalendarItem[]>("/api/calendars", { includeArchived: archived }), [archived]);
  const [drawer, setDrawer] = useState<{ open: boolean; row: CalendarItem | null }>({ open: false, row: null });
  const act = async (c: CalendarItem, what: "archive" | "restore" | "default") => {
    const ok = await run(() => post(`/api/calendars/${c.id}/${what}`), what === "default" ? t("Default calendar changed.") : what === "archive" ? t("Calendar archived.") : t("Calendar restored."));
    if (ok.ok) q.reload();
  };
  const cols: Column<CalendarItem>[] = [
    { key: "n", header: t("Calendar"), cell: (c) => <><b>{c.name}</b>{c.isDefault && <> <Badge tone="blue" dot={false}>{t("Default")}</Badge></>}<br /><small>{c.timeZone}</small></> },
    { key: "w", header: t("Working time"), cell: summary },
    { key: "h", header: t("Holidays"), className: "hide-sm", cell: (c) => c.holidays.length },
    { key: "l", header: t("Locations"), className: "hide-sm", cell: (c) => c.locations },
    { key: "s", header: t("Status"), cell: (c) => <Badge tone={c.archived ? "gray" : "green"}>{c.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: (c) => <Can perm="calendars.manage"><span className="row" style={{ gap: 6 }}>
      {!c.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${c.name}`} onClick={() => setDrawer({ open: true, row: c })}>{t("Edit")}</Button>}
      {!c.archived && !c.isDefault && <Button size="sm" variant="outline" aria-label={`${t("Make default")} ${c.name}`} onClick={() => void act(c, "default")}>{t("Make default")}</Button>}
      {!c.isDefault && <Button size="sm" variant="outline" aria-label={`${c.archived ? t("Restore") : t("Archive")} ${c.name}`} onClick={() => void act(c, c.archived ? "restore" : "archive")}>{c.archived ? t("Restore") : t("Archive")}</Button>}</span></Can> },
  ];
  return (
    <>
      <PageHeader title={t("Business calendars")} description={t("When your teams work. Response and resolution targets are counted in working time only.")}
        actions={<Can perm="calendars.manage"><Button variant="primary" onClick={() => setDrawer({ open: true, row: null })}><Plus size={16} /> {t("New calendar")}</Button></Can>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        <DataTable label={t("Business calendars")} columns={cols} rows={q.data ?? []} rowKey={(c) => c.id} emptyMessage={t("No calendars yet.")} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <CalendarDrawer open={drawer.open} onClose={() => setDrawer({ open: false, row: null })} onDone={q.reload} editing={drawer.row} />
    </>
  );
}

export default function CalendarsPage() { return <Require perm="calendars.read"><Calendars /></Require>; }
