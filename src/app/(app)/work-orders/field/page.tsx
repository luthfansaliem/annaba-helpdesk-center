"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { Camera, CloudOff, RefreshCw, Trash2, Wifi } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { WoStatusBadge } from "@/components/workorders/wo-bits";
import { get } from "@/lib/api/client";
import type { WorkOrderDetail, WorkOrderStatus } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { MAX_FILE_BYTES } from "@/lib/files";
import { fmtDateTime } from "@/lib/format";
import { addOp, fieldSupported, listOps, loadSnapshot, putOp, removeOp, saveSnapshot, sendOp, wipeFieldData, type QueuedOp, type Snapshot } from "@/lib/offline/field-store";
import { t } from "@/i18n";

type Act = "start" | "hold" | "resume" | "complete";
const nextActs: Record<WorkOrderStatus, Act[]> = { open: ["start"], in_progress: ["hold", "complete"], on_hold: ["resume"], done: [], cancelled: [] };
const actLabel = (a: Act) => ({ start: t("Start"), hold: t("Put on hold"), resume: t("Resume"), complete: t("Complete") })[a];

/** What a queued change looks like on the device straight away, before the server has seen it. */
function apply(wo: WorkOrderDetail, op: QueuedOp): WorkOrderDetail {
  const now = new Date().toISOString();
  switch (op.kind) {
    case "action": {
      const b = op.body as { action: Act; reason?: string | null; note?: string | null };
      const status: WorkOrderStatus = b.action === "hold" ? "on_hold" : b.action === "complete" ? "done" : "in_progress";
      return { ...wo, status, holdReason: b.action === "hold" ? b.reason ?? null : null, completionNote: b.action === "complete" ? b.note ?? null : wo.completionNote };
    }
    case "tick": {
      const taskId = op.path.split("/").pop(); const done = (op.body as { done: boolean }).done;
      return { ...wo, tasks: wo.tasks.map((k) => (k.id === taskId ? { ...k, doneAt: done ? now : null, doneBy: done ? t("You (not synced yet)") : null } : k)) };
    }
    case "time": {
      const m = (op.body as { minutes: number }).minutes;
      return { ...wo, totalMinutes: wo.totalMinutes + m, times: [...wo.times, { id: op.id, userId: op.owner, person: t("You (not synced yet)"), minutes: m, date: now.slice(0, 10), note: (op.body as { note?: string }).note ?? null, canDelete: false }] };
    }
    default:
      return { ...wo, files: [...wo.files, { id: op.id, fileName: op.fileName ?? "photo", contentType: op.blob?.type ?? "image/jpeg", stage: "after", inline: false, createdAt: now }] };
  }
}

function FieldMode() {
  const { user } = useAuth();
  const owner = user?.id ?? "";
  const [online, setOnline] = useState(true);
  const [snap, setSnap] = useState<Snapshot | null>(null); const [ops, setOps] = useState<QueuedOp[]>([]);
  const [selected, setSelected] = useState<string | null>(null); const [msg, setMsg] = useState(""); const [busy, setBusy] = useState(false);
  const [minutes, setMinutes] = useState(""); const [note, setNote] = useState(""); const [actText, setActText] = useState(""); const [pending, setPending] = useState<Act | null>(null);
  const syncing = useRef(false);

  const reloadOps = useCallback(async () => setOps(await listOps(owner)), [owner]);
  const store = useCallback(async (s: Snapshot) => { await saveSnapshot(s); setSnap(s); }, []);

  /** Replays the queue in order; stops at the first network failure; a refusal marks that change (and later ones for the same work order wait behind it). */
  const sync = useCallback(async () => {
    if (syncing.current || !owner) return;
    syncing.current = true; setBusy(true);
    try {
      let current = await loadSnapshot(owner);
      const blocked = new Set<string>();
      for (const op of await listOps(owner)) {
        if (op.state === "conflict") { blocked.add(op.woId); continue; }
        if (blocked.has(op.woId)) continue;
        const r = await sendOp(op);
        if (r.ok) {
          await removeOp(op.id);
          if (r.wo && current) { const wo = r.wo; current = { ...current, items: current.items.map((x) => (x.id === wo.id ? wo : x)) }; await saveSnapshot(current); }
        } else if (r.offline) { setOnline(navigator.onLine); setMsg(t("Still offline. Your changes are kept on this device.")); break; }
        else { await putOp({ ...op, state: "conflict", error: r.message }); blocked.add(op.woId); }
      }
      if (current) setSnap(current);
      const left = await listOps(owner); setOps(left);
      if (left.length === 0) setMsg(t("All changes are synced."));
      else if (left.some((o) => o.state === "conflict")) setMsg(t("Some changes were refused. Check them below."));
    } finally { syncing.current = false; setBusy(false); }
  }, [owner]);

  /** Fresh snapshot from the server (only when nothing is waiting, so local changes are never overwritten). */
  const refresh = useCallback(async () => {
    if (!owner) return;
    if ((await listOps(owner)).length > 0) { await sync(); if ((await listOps(owner)).length > 0) return; }
    try {
      const r = await get<{ takenAt: string; items: WorkOrderDetail[] }>("/api/work-orders/offline");
      await store({ owner, takenAt: r.takenAt, items: r.items }); setOnline(true);
    } catch { setOnline(false); }
  }, [owner, store, sync]);

  useEffect(() => {
    if (!owner || !fieldSupported()) return;
    let alive = true;
    void (async () => {
      const s = await loadSnapshot(owner); if (alive && s) setSnap(s);
      await reloadOps();
      setOnline(navigator.onLine);
      if (navigator.onLine) await refresh();
    })();
    const up = () => { setOnline(true); void sync().then(refresh); };
    const down = () => setOnline(false);
    window.addEventListener("online", up); window.addEventListener("offline", down);
    return () => { alive = false; window.removeEventListener("online", up); window.removeEventListener("offline", down); };
  }, [owner, refresh, reloadOps, sync]);

  async function queue(wo: WorkOrderDetail, op: Omit<QueuedOp, "id" | "seq" | "createdAt" | "state" | "owner" | "woId">) {
    const full = await addOp({ ...op, owner, woId: wo.id });
    if (snap) await store({ ...snap, items: snap.items.map((x) => (x.id === wo.id ? apply(x, full) : x)) });
    await reloadOps();
    if (navigator.onLine) await sync(); else setMsg(t("Saved on this device. It will sync when you are back online."));
  }

  if (!fieldSupported()) return <Panel><p className="tab-placeholder">{t("This browser cannot keep data offline.")}</p></Panel>;
  const items = snap?.items ?? []; const wo = items.find((x) => x.id === selected) ?? null;
  const waiting = ops.filter((o) => o.state === "waiting").length; const conflicts = ops.filter((o) => o.state === "conflict");

  return (
    <>
      <PageHeader title={t("Field mode")} description={t("Your own open work orders, kept on this device so you can keep working without a signal. Open this page while online and keep it open.")}
        actions={<><Button onClick={() => void refresh()} disabled={busy || !online}><RefreshCw size={16} /> {t("Refresh")}</Button>
          <Button onClick={() => void wipeFieldData().then(() => { setSnap(null); setOps([]); setSelected(null); setMsg(t("Field mode data removed from this device.")); })}><Trash2 size={16} /> {t("Forget this device's data")}</Button></>} />
      <div className={`form-alert ${online ? "" : "warn"}`} role="status" data-testid="field-status">
        {online ? <><Wifi size={14} aria-hidden /> {t("Online")}</> : <><CloudOff size={14} aria-hidden /> {t("Offline")}</>}
        {" · "}{waiting === 0 ? t("No changes waiting") : t("{n} changes waiting", { n: waiting })}
        {snap && <> · {t("Saved {at}", { at: fmtDateTime(snap.takenAt) })}</>}
        {waiting > 0 && online && <> · <Button size="sm" onClick={() => void sync()} disabled={busy}>{t("Sync now")}</Button></>}
        {msg && <><br /><span data-testid="field-msg">{msg}</span></>}
      </div>
      {conflicts.length > 0 && (
        <Panel title={t("Changes the server refused")} description={t("Nothing was overwritten. Try again after checking the work order, or discard the change.")}>
          <ul className="req-list" data-testid="field-conflicts">
            {conflicts.map((o) => (
              <li key={o.id}><div><b>{o.label}</b><br /><small className="field-error">{o.error}</small></div>
                <span className="row" style={{ gap: 6 }}>
                  <Button size="sm" onClick={() => void putOp({ ...o, state: "waiting", error: undefined }).then(sync)}>{t("Try again")}</Button>
                  <Button size="sm" variant="danger-outline" onClick={() => void removeOp(o.id).then(reloadOps).then(refresh)}>{t("Discard")}</Button>
                </span></li>
            ))}
          </ul>
        </Panel>
      )}
      <div className="grid-2" style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(300px, 1fr))", alignItems: "start" }}>
        <Panel title={t("My open work orders")}>
          {items.length === 0 ? <p className="tab-placeholder">{snap ? t("Nothing open is given to you.") : t("Open this page while online to keep your work orders here.")}</p> : (
            <ul className="req-list" data-testid="field-list">
              {items.map((x) => (
                <li key={x.id}>
                  <button type="button" className="link-btn" onClick={() => { setSelected(x.id); setPending(null); setActText(""); }} aria-pressed={x.id === selected}>
                    <b>{x.number}</b> · {x.title}
                  </button>
                  <WoStatusBadge status={x.status} />
                </li>
              ))}
            </ul>
          )}
        </Panel>
        {wo && (
          <Panel title={`${wo.number} · ${wo.title}`} description={`${wo.ticketNumber} · ${wo.ticketSubject}${wo.dueAt ? ` · ${t("due")} ${fmtDateTime(wo.dueAt)}` : ""}`} data-testid="field-detail">
            <div className="stack">
              {wo.instructions && <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{wo.instructions}</p>}
              <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                <WoStatusBadge status={wo.status} />
                {nextActs[wo.status].map((a) => <Button key={a} size="sm" variant={a === "start" || a === "complete" ? "primary" : "outline"}
                  onClick={() => (a === "start" || a === "resume" ? void queue(wo, { kind: "action", path: `/api/work-orders/${wo.id}/action`, body: { action: a, version: wo.version }, label: `${wo.number}: ${actLabel(a)}` }) : setPending(a))}>{actLabel(a)}</Button>)}
              </span>
              {pending && (
                <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }}>
                  <Field label={pending === "complete" ? t("What was done?") : t("What are you waiting for?")} htmlFor="fm-act"><TextInput id="fm-act" value={actText} onChange={(e) => setActText(e.target.value)} /></Field>
                  <Button size="sm" variant="primary" disabled={actText.trim().length < 3} onClick={() => { const a = pending; setPending(null); void queue(wo, { kind: "action", path: `/api/work-orders/${wo.id}/action`,
                    body: { action: a, reason: a === "hold" ? actText : null, note: a === "complete" ? actText : null, version: wo.version }, label: `${wo.number}: ${actLabel(a)}` }); setActText(""); }}>{t("Save")}</Button>
                </div>
              )}
              {wo.tasks.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("Checklist")}>
                {wo.tasks.map((k) => (
                  <li key={k.id}><label className="row" style={{ gap: 8 }}>
                    <Checkbox checked={!!k.doneAt} disabled={wo.status !== "in_progress"} onChange={(e) => void queue(wo, { kind: "tick", path: `/api/work-orders/${wo.id}/tasks/${k.id}`, body: { done: e.target.checked, note: null }, label: `${wo.number}: ${k.text}` })} />
                    <span>{k.text}</span></label></li>
                ))}
              </ul>}
              {wo.status === "in_progress" && <>
                <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "flex-end" }}>
                  <Field label={t("Minutes")} htmlFor="fm-min"><TextInput id="fm-min" type="number" min={1} max={1440} value={minutes} onChange={(e) => setMinutes(e.target.value)} /></Field>
                  <Field label={t("Note")} htmlFor="fm-note"><TextInput id="fm-note" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} /></Field>
                  <Button size="sm" disabled={!minutes} onClick={() => { void queue(wo, { kind: "time", path: `/api/work-orders/${wo.id}/time`, body: { minutes: Number(minutes), note: note || null }, label: `${wo.number}: ${minutes} min` }); setMinutes(""); setNote(""); }}>{t("Log time")}</Button>
                </div>
                <label className="btn sm" style={{ alignSelf: "flex-start" }}><Camera size={14} /> {t("Add an after photo")}
                  <input type="file" accept="image/*" capture="environment" className="sr-only" onChange={(e) => { const f = e.target.files?.[0]; e.target.value = "";
                    if (!f) return; if (f.size > MAX_FILE_BYTES) { setMsg(t("The file is larger than 8 MB.")); return; }
                    void queue(wo, { kind: "photo", path: `/api/work-orders/${wo.id}/files?stage=after`, blob: f, fileName: f.name, label: `${wo.number}: ${t("photo")} ${f.name}` }); }} /></label>
              </>}
              <p className="muted" style={{ margin: 0 }}>{t("{n} min logged", { n: wo.totalMinutes })} · {t("{n} photos", { n: wo.files.length })}</p>
            </div>
          </Panel>
        )}
      </div>
    </>
  );
}

/** ADR-GA-07. Field mode: works from the device when the network drops; changes sync when it is back. */
export default function FieldModePage() {
  return <Require perm="workorders.complete"><FieldMode /></Require>;
}
