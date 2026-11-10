"use client";

import { useState } from "react";
import { Bookmark } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { del, get, patch, post } from "@/lib/api/client";
import type { TicketViewItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export type ViewFilter = Record<string, string>;

/** Saved filters of the queue: pick one to apply it, save what is on screen as a new view or over the one in use, remove one you no longer need. */
export function ViewsBar({ current, onApply }: { current: ViewFilter; onApply: (f: ViewFilter) => void }) {
  const { can } = useAuth(); const run = useRun();
  const views = useQuery(() => get<TicketViewItem[]>("/api/ticket-views").catch(() => [] as TicketViewItem[]), []);
  const [activeId, setActiveId] = useState(""); const [saving, setSaving] = useState(false); const [confirm, setConfirm] = useState<ConfirmSpec | null>(null);
  const list = views.data ?? []; const active = list.find((v) => v.id === activeId) ?? null;
  const mine = list.filter((v) => v.mine); const shared = list.filter((v) => !v.mine);

  function pick(id: string) { setActiveId(id); const v = list.find((x) => x.id === id); if (v) onApply(v.filter); }
  function remove(v: TicketViewItem) {
    setConfirm({ title: t("Remove this view?"), body: <p>{t("The view “{name}” will be removed. The tickets are not affected.", { name: v.name })}</p>, confirmLabel: t("Remove"), danger: true,
      onConfirm: () => { void run(() => del(`/api/ticket-views/${v.id}`), t("View removed.")).then((r) => { if (r.ok) { setActiveId(""); views.reload(); } }); } });
  }
  async function overwrite(v: TicketViewItem) {
    const r = await run(() => patch<TicketViewItem>(`/api/ticket-views/${v.id}`, { name: v.name, filter: current, shared: v.shared }), t("View updated."));
    if (r.ok) views.reload();
  }

  return (
    <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end", paddingBottom: 12 }}>
      <Field label={t("Saved views")} htmlFor="tq-view">
        <SelectInput id="tq-view" value={activeId} onChange={(e) => pick(e.target.value)} placeholder={t("Choose a view…")}
          options={[...mine.map((v) => ({ value: v.id, label: v.name })), ...shared.map((v) => ({ value: v.id, label: `${v.name} · ${t("shared")}` }))]} />
      </Field>
      <Button onClick={() => setSaving(true)}><Bookmark size={14} /> {t("Save as view")}</Button>
      {active?.canEdit && <Button onClick={() => overwrite(active)} aria-label={t("Update view {name}", { name: active.name })}>{t("Update view")}</Button>}
      {active?.canEdit && <Button variant="danger-outline" onClick={() => remove(active)} aria-label={t("Remove view {name}", { name: active.name })}>{t("Remove view")}</Button>}
      <SaveDialog open={saving} onClose={() => setSaving(false)} filter={current} canShare={can("tickets.assign")} onSaved={(v) => { setSaving(false); views.reload(); setActiveId(v.id); }} />
      <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
    </div>
  );
}

function SaveDialog({ open, onClose, filter, canShare, onSaved }: { open: boolean; onClose: () => void; filter: ViewFilter; canShare: boolean; onSaved: (v: TicketViewItem) => void }) {
  const run = useRun();
  const [name, setName] = useState(""); const [shared, setShared] = useState(false); const [err, setErr] = useState(""); const [errs, setErrs] = useState<Record<string, string>>({}); const [busy, setBusy] = useState(false); const [seen, setSeen] = useState(false);
  if (open !== seen) { setSeen(open); setName(""); setShared(false); setErr(""); setErrs({}); }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => post<TicketViewItem>("/api/ticket-views", { name, filter, shared }), t("View saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.filter ?? ""); setErrs(r.fields ?? {}); if (!r.fields) setErr(r.error); return; }
    onSaved(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} title={t("Save as view")} description={t("Keeps the filters you see now, so you can come back to them in one click.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="view-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="view-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="vw-name" required error={errs.name}><TextInput id="vw-name" data-autofocus maxLength={60} value={name} onChange={(e) => setName(e.target.value)} /></Field>
        {canShare && <label className="row" style={{ gap: 8 }}><Checkbox checked={shared} onChange={(e) => setShared(e.target.checked)} /> {t("Share with everyone who can use the queue")}</label>}
      </form>
    </Overlay>
  );
}
