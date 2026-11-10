"use client";

import { Suspense, use, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Ban, Pencil, Plus, Trash2, Upload } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { ConfirmOverlay, type ConfirmSpec } from "@/components/ui/confirm";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { ContractDrawer, VendorDrawer } from "@/components/vendors/vendor-bits";
import { WoStatusBadge } from "@/components/workorders/wo-bits";
import { del, get, post } from "@/lib/api/client";
import type { VendorContract, VendorDetail } from "@/lib/api/types";
import { FILE_ACCEPT, MAX_FILE_BYTES, fmtSize, uploadFile } from "@/lib/files";
import { fmtDateTime } from "@/lib/format";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const money = (n: number) => n.toLocaleString(undefined, { maximumFractionDigits: 2 });
const stateLabel = (s: VendorContract["state"]) => ({ active: t("Active"), ended: t("Ended"), upcoming: t("Not started") })[s];

function BlockDialog({ vendor, open, onClose, onDone }: { vendor: VendorDetail; open: boolean; onClose: () => void; onDone: (v: VendorDetail) => void }) {
  const run = useRun(); const [reason, setReason] = useState(""); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const r = await run(() => post<VendorDetail>(`/api/vendors/${vendor.id}/block`, { reason }), t("Vendor blocked."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.fields?.reason ?? r.error); return; }
    setReason(""); onClose(); onDone(r.data);
  }
  return (
    <Overlay open={open} onClose={onClose} title={t("Block this vendor?")} description={vendor.name}
      footer={<><Button onClick={onClose}>{t("Back")}</Button><Button variant="danger" type="submit" form="block-form" disabled={busy}>{busy ? t("Saving…") : t("Block")}</Button></>}>
      <form id="block-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <p style={{ margin: 0 }}>{t("A blocked vendor gets no new work orders. Work it already has stays as it is.")}</p>
        <Field label={t("Why?")} htmlFor="bl-reason" required><textarea id="bl-reason" data-autofocus rows={3} maxLength={300} className="textarea" value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
      </form>
    </Overlay>
  );
}

/** R2-11: the vendor's people who sign in to work its work orders. */
function PortalUsers({ vendor, onChange }: { vendor: VendorDetail; onChange: (v: VendorDetail) => void }) {
  const run = useRun(); const [email, setEmail] = useState(""); const [err, setErr] = useState("");
  async function add(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    const r = await run(() => post<VendorDetail>(`/api/vendors/${vendor.id}/portal-users`, { email }), t("Portal access given."), { silent: true });
    if (!r.ok) { setErr(r.fields?.email ?? r.error); return; }
    setEmail(""); onChange(r.data);
  }
  const remove = async (userId: string) => { const r = await run(() => del<VendorDetail>(`/api/vendors/${vendor.id}/portal-users/${userId}`), t("Portal access removed.")); if (r.ok && r.data) onChange(r.data); };
  return (
    <Panel title={t("Vendor portal")} description={t("The vendor's people who sign in to see and work only this vendor's work orders. Invite them first with the Vendor role.")}>
      <div className="stack">
        {(vendor.portalUsers ?? []).length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("Nobody yet.")}</p>
          : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }} aria-label={t("Vendor portal")}>{(vendor.portalUsers ?? []).map((u) => (
            <li key={u.userId} className="row" style={{ gap: 8, justifyContent: "space-between" }}><span><b>{u.name}</b><br /><small className="sub">{u.email}</small></span>
              <Button size="sm" onClick={() => void remove(u.userId)}>{t("Remove access")}</Button></li>))}</ul>}
        {vendor.canManage && <form className="row" style={{ gap: 8, alignItems: "flex-end", flexWrap: "wrap" }} noValidate onSubmit={add}>
          <Field label={t("Person's e-mail")} htmlFor="vp-email" error={err || undefined}><TextInput id="vp-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
          <Button type="submit">{t("Give access")}</Button>
        </form>}
      </div>
    </Panel>
  );
}

function Detail({ id }: { id: string }) {
  const run = useRun();
  const q = useQuery(() => get<VendorDetail>(`/api/vendors/${id}`), [id]);
  const [editing, setEditing] = useState(false); const [blocking, setBlocking] = useState(false); const [contract, setContract] = useState<{ open: boolean; row: VendorContract | null }>({ open: false, row: null });
  const [confirm, setConfirm] = useState<ConfirmSpec | null>(null); const [upload, setUpload] = useState(""); const [busy, setBusy] = useState(false);
  return (
    <QueryBoundary q={q}>{(v) => {
      const set = (d: VendorDetail) => q.setData(d);
      async function addDoc(file: File | undefined) {
        if (!file) return; setUpload("");
        if (file.size > MAX_FILE_BYTES) { setUpload(t("The file is larger than 8 MB.")); return; }
        setBusy(true); const r = await run(() => uploadFile<VendorDetail>(`/api/vendors/${v.id}/documents`, file), t("Document added."), { silent: true }); setBusy(false);
        if (!r.ok) { setUpload(r.fields?.file ?? r.error); return; }
        set(r.data);
      }
      const remove = (label: string, path: string) => setConfirm({ title: t("Remove {what}?", { what: label }), confirmLabel: t("Remove"), danger: true, body: t("This can't be undone."),
        onConfirm: async () => { const r = await run(() => del<VendorDetail>(path), t("Removed.")); if (r.ok && r.data) set(r.data); } });
      const unblock = async () => { const r = await run(() => post<VendorDetail>(`/api/vendors/${v.id}/unblock`), t("Vendor unblocked.")); if (r.ok) set(r.data); };
      return (
        <>
          <div className="crud-top">
            <div>
              <Link href="/vendors" className="back-link"><ArrowLeft size={14} /> {t("Vendors")}</Link>
              <h1>{v.name}</h1>
              <p>{v.services ?? t("No description.")}</p>
            </div>
            {v.blocked ? <Badge tone="red">{t("Blocked")}</Badge> : <Badge tone="green">{t("Active")}</Badge>}
          </div>
          {v.blocked && <div className="form-alert warn" role="status">{t("Blocked {when}: {reason}", { when: v.blockedAt ? fmtDateTime(v.blockedAt) : "", reason: v.blockReason ?? "" })}</div>}
          {v.canManage && <span className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            <Button onClick={() => setEditing(true)}><Pencil size={14} /> {t("Edit")}</Button>
            {v.blocked ? <Button onClick={() => void unblock()}>{t("Unblock")}</Button> : <Button variant="danger-outline" onClick={() => setBlocking(true)}><Ban size={14} /> {t("Block")}</Button>}
          </span>}

          <div className="grid-2" style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(320px, 1fr))", alignItems: "start" }}>
            <div className="stack">
              <Panel title={t("Details")}>
                <dl className="stack" style={{ margin: 0 }}>
                  <div><dt><b>{t("Phone")}</b></dt><dd style={{ margin: 0 }}>{v.phone ?? "—"}</dd></div>
                  <div><dt><b>{t("E-mail")}</b></dt><dd style={{ margin: 0 }}>{v.email ?? "—"}</dd></div>
                  <div><dt><b>{t("Website")}</b></dt><dd style={{ margin: 0 }}>{v.website ? <a href={v.website} target="_blank" rel="noreferrer noopener">{v.website}</a> : "—"}</dd></div>
                  <div><dt><b>{t("Address")}</b></dt><dd style={{ margin: 0 }}>{v.address ?? "—"}</dd></div>
                  <div><dt><b>{t("Tax number")}</b></dt><dd style={{ margin: 0 }}>{v.taxId ?? "—"}</dd></div>
                  {v.notes && <div><dt><b>{t("Notes")}</b></dt><dd style={{ margin: 0, whiteSpace: "pre-wrap" }}>{v.notes}</dd></div>}
                </dl>
              </Panel>
              <Panel title={t("Contacts")}>
                {v.contacts.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No contacts yet.")}</p>
                  : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{v.contacts.map((c) => <li key={c.id}><b>{c.name}</b>{c.role ? ` · ${c.role}` : ""}<br /><small>{[c.phone, c.email].filter(Boolean).join(" · ") || "—"}</small></li>)}</ul>}
              </Panel>
              <Panel title={t("Places they cover")}>
                {v.sites.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("Not limited to any place.")}</p>
                  : <ul style={{ margin: 0, paddingLeft: 18 }}>{v.sites.map((s) => <li key={s.locationId}>{s.path}</li>)}</ul>}
              </Panel>
            </div>
            <div className="stack">
              <Panel title={t("How their work goes")} description={t("From the work orders given to this vendor.")}>
                <dl className="row" style={{ gap: 24, flexWrap: "wrap", margin: 0 }} data-testid="vendor-score">
                  <div><dt><small>{t("Work orders")}</small></dt><dd style={{ margin: 0 }}><b>{v.score.workOrders}</b></dd></div>
                  <div><dt><small>{t("Done")}</small></dt><dd style={{ margin: 0 }}><b>{v.score.done}</b></dd></div>
                  <div><dt><small>{t("Open")}</small></dt><dd style={{ margin: 0 }}><b>{v.score.open}</b></dd></div>
                  <div><dt><small>{t("Done on time")}</small></dt><dd style={{ margin: 0 }}><b>{v.score.onTime}</b> / {v.score.onTime + v.score.late}</dd></div>
                  <div><dt><small>{t("Requester rating")}</small></dt><dd style={{ margin: 0 }}><b>{v.score.rating === null ? "—" : `${v.score.rating.toFixed(1)} / 5`}</b>{v.score.ratings > 0 && <small> ({v.score.ratings})</small>}</dd></div>
                  {v.score.cost !== null && <div><dt><small>{t("Cost")}</small></dt><dd style={{ margin: 0 }}><b>{money(v.score.cost)}</b></dd></div>}
                </dl>
              </Panel>
              <Panel title={t("Contracts")} actions={v.canManage ? <Button size="sm" onClick={() => setContract({ open: true, row: null })}><Plus size={14} /> {t("New contract")}</Button> : undefined}>
                {v.contracts.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No contracts yet.")}</p>
                  : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{v.contracts.map((c) => (
                    <li key={c.id} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
                      <span><b>{c.title}</b> <Badge tone={c.state === "active" ? "green" : c.state === "ended" ? "gray" : "blue"}>{stateLabel(c.state)}</Badge>
                        <br /><small>{c.reference ? `${c.reference} · ` : ""}{c.startsOn} – {c.endsOn ?? t("open-ended")}{c.value !== null ? ` · ${money(c.value)}` : ""}</small></span>
                      {v.canManage && <span className="row" style={{ gap: 4 }}>
                        <IconButton label={`${t("Edit")} ${c.title}`} onClick={() => setContract({ open: true, row: c })}><Pencil size={14} /></IconButton>
                        <IconButton label={`${t("Remove")} ${c.title}`} onClick={() => remove(c.title, `/api/vendors/${v.id}/contracts/${c.id}`)}><Trash2 size={14} /></IconButton>
                      </span>}
                    </li>))}</ul>}
              </Panel>
              <Panel title={t("Documents")}>
                <div className="stack">
                  {v.documents.length === 0 ? <p className="tab-placeholder" style={{ margin: 0 }}>{t("No documents yet.")}</p>
                    : <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{v.documents.map((d) => (
                      <li key={d.id} className="row" style={{ gap: 8, justifyContent: "space-between" }}>
                        <span><a href={`/api/vendors/${v.id}/documents/${d.id}`}>{d.fileName}</a><br /><small>{fmtSize(d.size)} · {fmtDateTime(d.createdAt)}{d.uploadedBy ? ` · ${d.uploadedBy}` : ""}</small></span>
                        {v.canManage && <IconButton label={`${t("Remove")} ${d.fileName}`} onClick={() => remove(d.fileName, `/api/vendors/${v.id}/documents/${d.id}`)}><Trash2 size={14} /></IconButton>}
                      </li>))}</ul>}
                  {upload && <div className="form-alert err" role="alert">{upload}</div>}
                  {v.canManage && <div><label className="btn sm"><Upload size={14} /> {t("Add a document")}<input type="file" accept={FILE_ACCEPT} className="sr-only" disabled={busy} onChange={(e) => void addDoc(e.target.files?.[0])} /></label></div>}
                </div>
              </Panel>
              {v.portalUsers && <PortalUsers vendor={v} onChange={set} />}
              {v.workOrders.length > 0 && <Panel title={t("Work orders")}>
                <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{v.workOrders.map((w) => <li key={w.id}><Link href={`/work-orders/${w.id}`}><b>{w.number}</b> · {w.title}</Link> <WoStatusBadge status={w.status} /></li>)}</ul>
              </Panel>}
            </div>
          </div>
          <VendorDrawer open={editing} onClose={() => setEditing(false)} onDone={set} editing={v} />
          <BlockDialog vendor={v} open={blocking} onClose={() => setBlocking(false)} onDone={set} />
          <ContractDrawer vendorId={v.id} open={contract.open} editing={contract.row} onClose={() => setContract({ open: false, row: null })} onDone={set} />
          <ConfirmOverlay spec={confirm} onClose={() => setConfirm(null)} />
        </>
      );
    }}</QueryBoundary>
  );
}

export default function VendorPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="vendors.read"><Suspense><Detail id={id} /></Suspense></Require>;
}
