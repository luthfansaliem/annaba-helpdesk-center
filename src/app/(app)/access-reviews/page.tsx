"use client";

import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { ToggleRow } from "@/components/iam/prefs";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { AccessReviewItem, AccessReviewSummary, Paged, ReviewSchedule } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TONE: Record<AccessReviewItem["decision"], BadgeTone> = { pending: "orange", keep: "green", revoke: "red" };

function StartForm({ onDone }: { onDone: () => void }) {
  const run = useRun(); const [name, setName] = useState(""); const [days, setDays] = useState("14"); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => post("/api/access-reviews", { name, dueInDays: Number(days) }), t("Review started."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    setName(""); onDone();
  }
  return (
    <Panel title={t("Start a review")} description={t("Snapshots every active user's privileged roles so reviewers can keep or revoke each one. Only one review is open at a time.")}>
      <form className="stack" noValidate onSubmit={submit} aria-label={t("Start a review")}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <div className="fgrid">
          <Field label={t("Name")} htmlFor="rv-name" required><TextInput id="rv-name" value={name} onChange={(e) => setName(e.target.value)} placeholder={t("Q4 access review")} /></Field>
          <Field label={t("Due in (days)")} htmlFor="rv-days" required><TextInput id="rv-days" type="number" min={1} max={90} value={days} onChange={(e) => setDays(e.target.value)} /></Field>
        </div>
        <div><Button variant="primary" type="submit" disabled={busy}>{busy ? t("Starting…") : t("Start review")}</Button></div>
      </form>
    </Panel>
  );
}

const EVERY = [{ value: "30", label: "Every month" }, { value: "90", label: "Every 3 months" }, { value: "180", label: "Every 6 months" }, { value: "365", label: "Every year" }];

/** Opens a review by itself every period, so access is re-checked even when nobody remembers to start one. */
function Schedule() {
  const run = useRun(); const q = useQuery(() => get<ReviewSchedule>("/api/access-reviews/schedule"), []);
  const [on, setOn] = useState(false); const [every, setEvery] = useState("90"); const [due, setDue] = useState("14"); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const saved = q.data;
  useEffect(() => { if (saved) { setOn(saved.enabled); setEvery(String(saved.everyDays)); setDue(String(saved.dueInDays)); } }, [saved]);
  const dirty = !!saved && (on !== saved.enabled || every !== String(saved.everyDays) || due !== String(saved.dueInDays));
  async function save() {
    setErr(""); setBusy(true);
    const r = await run(() => put<ReviewSchedule>("/api/access-reviews/schedule", { enabled: on, everyDays: Number(every), dueInDays: Number(due) }), on ? "Schedule saved." : "Scheduled reviews turned off.", { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    q.reload();
  }
  // Controls appear only once the saved schedule is known; otherwise a quick click could be overwritten when it arrives.
  if (!saved) return <Panel title={t("Schedule")} description={t("Open a review automatically so access is re-checked on a regular rhythm.")}><p className="field-help" role="status">{t("Loading…")}</p></Panel>;
  return (
    <Panel title={t("Schedule")} description={t("Open a review automatically so access is re-checked on a regular rhythm. Reviewers are e-mailed when it opens; a review that is still open is never doubled.")}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <ToggleRow id="rv-sched-on" label={t("Open reviews automatically")} hint={saved?.enabled && saved.nextRunAt ? t("Next review opens on {v0}.", { v0: fmtDateTime(saved.nextRunAt) }) : t("Off — reviews start only when you start them.")} checked={on} onChange={setOn} />
        <div className="fgrid">
          <Field label={t("How often")} htmlFor="rv-every"><SelectInput id="rv-every" options={EVERY.map((o) => ({ ...o, label: t(o.label) }))} value={every} disabled={!on} onChange={(e) => setEvery(e.target.value)} /></Field>
          <Field label={t("Reviewers have (days)")} htmlFor="rv-sdue"><TextInput id="rv-sdue" type="number" min={1} max={90} value={due} disabled={!on} onChange={(e) => setDue(e.target.value)} /></Field>
        </div>
        <div><Button variant="primary" disabled={!dirty || busy} onClick={save}>{busy ? t("Saving…") : t("Save schedule")}</Button></div>
      </div>
    </Panel>
  );
}

function Items({ review, onChange }: { review: AccessReviewSummary; onChange: () => void }) {
  const run = useRun(); const open = review.status === "open";
  const [decision, setDecision] = useState<"pending" | "all">("pending"); const [page, setPage] = useState(1); const [size, setSize] = useState(25);
  const q = useQuery(() => get<Paged<AccessReviewItem>>(`/api/access-reviews/${review.id}/items`, { decision, page, pageSize: size }), [review.id, decision, page, size, review.pending]);
  useEffect(() => setPage(1), [decision, size, review.id]);
  const [revoke, setRevoke] = useState<AccessReviewItem | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);

  async function decide(item: AccessReviewItem, d: "keep" | "revoke", n?: string) {
    setBusy(true); setErr("");
    const r = await run(() => post(`/api/access-reviews/${review.id}/items/${item.id}/decision`, { decision: d, note: n?.trim() || null }), d === "keep" ? "Kept." : "Role revoked.", { silent: d === "revoke" });
    setBusy(false);
    if (!r.ok) { if (d === "revoke") setErr(r.error); return false; }
    onChange(); return true;
  }
  async function confirmRevoke() { if (revoke && (await decide(revoke, "revoke", note))) setRevoke(null); }
  async function close() { const r = await run(() => post(`/api/access-reviews/${review.id}/close`), t("Review closed.")); if (r.ok) onChange(); }

  const cols: Column<AccessReviewItem>[] = [
    { key: "p", header: t("Person"), cell: (r) => <><b>{r.userName}</b><br /><small>{r.userEmail}</small></> },
    { key: "r", header: t("Role"), cell: (r) => r.role.name },
    { key: "d", header: t("Decision"), cell: (r) => <Badge tone={TONE[r.decision]}>{r.decision[0].toUpperCase() + r.decision.slice(1)}</Badge> },
    { key: "w", header: t("Reviewer"), className: "hide-sm", cell: (r) => (r.reviewerName ? `${r.reviewerName}${r.note ? `: ${r.note}` : ""}` : "—") },
    { key: "a", header: t("Actions"), cell: (r) => open && r.decision === "pending" ? <span className="row" style={{ gap: 6 }}><Button size="sm" disabled={busy} onClick={() => decide(r, "keep")}>{t("Keep")}</Button><Button size="sm" variant="danger-outline" onClick={() => { setRevoke(r); setNote(""); setErr(""); }}>{t("Revoke")}</Button></span> : null },
  ];
  const rows = q.data?.data ?? []; const total = q.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));
  return (
    <Panel title={review.name} description={t("{kept} kept · {revoked} revoked · {pending} pending of {total}. Due {v4}{v5}.", { kept: review.kept, revoked: review.revoked, pending: review.pending, total: review.total, v4: fmtDateTime(review.dueAt), v5: review.overdue ? " (overdue)" : "" })}
      actions={open && <Button size="sm" onClick={close}>{t("Close review")}</Button>}>
      <Tabs variant="segmented" label={t("Items")} value={decision} onChange={setDecision} items={[{ key: "pending", label: t("Pending") }, { key: "all", label: t("All") }]} />
      {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={decision === "pending" ? t("Nothing left to review") : t("No items")} description={decision === "pending" ? t("Every assignment has a decision.") : t("This review has no items.")} />
        : <DataTable label={t("Review items")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      {rows.length > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={setSize} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total}`} />}
      <Overlay open={!!revoke} onClose={() => setRevoke(null)} side="right" title={t("Revoke role")} description={revoke ? `${revoke.userName} · ${revoke.role.name}` : undefined}
        footer={<><Button onClick={() => setRevoke(null)}>{t("Cancel")}</Button><Button variant="danger" disabled={busy} onClick={confirmRevoke}>{busy ? t("Revoking…") : t("Revoke role")}</Button></>}>
        <div className="stack">
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <p>{t("The role is removed right away. A person left with no role keeps the basic User role.")}</p>
          <Field label={t("Note (optional)")} htmlFor="rv-note"><textarea id="rv-note" rows={3} maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} className="textarea" /></Field>
        </div>
      </Overlay>
    </Panel>
  );
}

function Reviews() {
  const list = useQuery(() => get<AccessReviewSummary[]>("/api/access-reviews"), []);
  const [selected, setSelected] = useState<string | null>(null);
  const reviews = list.data ?? []; const open = reviews.find((r) => r.status === "open");
  const current = reviews.find((r) => r.id === selected) ?? open ?? reviews[0];
  const cols: Column<AccessReviewSummary>[] = [
    { key: "n", header: t("Review"), cell: (r) => <button type="button" className="link-btn" onClick={() => setSelected(r.id)}>{r.name}</button> },
    { key: "s", header: t("Status"), cell: (r) => <Badge tone={r.status === "open" ? (r.overdue ? "red" : "blue") : "gray"}>{r.status === "open" ? (r.overdue ? t("Overdue") : t("Open")) : t("Closed")}</Badge> },
    { key: "p", header: t("Progress"), cell: (r) => `${r.total - r.pending} / ${r.total}` },
    { key: "d", header: t("Due"), className: "hide-sm", cell: (r) => fmtDateTime(r.dueAt) },
    { key: "c", header: t("Started by"), className: "hide-sm", cell: (r) => r.createdByName ?? "—" },
  ];
  return (
    <div className="stack">
      <Schedule />
      {list.data && !open && <StartForm onDone={() => { setSelected(null); list.reload(); }} />}
      {reviews.length > 0 && <Panel title={t("Reviews")}><DataTable label={t("Access reviews")} columns={cols} rows={reviews} rowKey={(r) => r.id} loading={false} error={null} /></Panel>}
      {list.error && <div className="form-alert err" role="alert">{list.error.message}</div>}
      {current && <Items key={current.id} review={current} onChange={list.reload} />}
    </div>
  );
}

export default function AccessReviewsPage() {
  return (
    <Require perm="users.assign_role">
      <PageHeader title={t("Access Reviews")} description={t("Periodically confirm that everyone who holds a privileged role still needs it.")} />
      <Reviews />
    </Require>
  );
}
