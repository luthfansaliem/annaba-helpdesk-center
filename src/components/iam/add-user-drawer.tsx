"use client";

import { useEffect, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { Field, TextInput } from "@/components/ui/field";
import { RolePicker } from "./role-picker";
import { get, post } from "@/lib/api/client";
import type { RoleListItem } from "@/lib/api/types";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const blank = { name: "", email: "", phone: "", department: "", jobTitle: "", roleIds: [] as string[] };

/** USR-003 — opened from the "+ Add User" button on All Users (never from the sidebar). */
export function AddUserDrawer({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [f, setF] = useState(blank);
  const [roles, setRoles] = useState<RoleListItem[]>([]);
  const [err, setErr] = useState<Record<string, string>>({});
  const [form, setForm] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (!open) return;
    setF(blank); setErr({}); setForm("");
    get<RoleListItem[]>("/api/roles").then((r) => { setRoles(r); const u = r.find((x) => x.key === "user"); if (u) setF((p) => ({ ...p, roleIds: [u.id] })); }).catch(() => setForm(t("Could not load roles.")));
  }, [open]);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const n: Record<string, string> = {};
    if (f.name.trim().length < 2) n.name = "Full name is required.";
    if (!/^\S+@\S+\.\S+$/.test(f.email)) n.email = "Enter a valid email address.";
    if (f.roleIds.length === 0) n.roles = "Select at least one role.";
    setErr(n); if (Object.keys(n).length) return;
    setBusy(true);
    const r = await run(() => post("/api/users", { name: f.name, email: f.email, phone: f.phone || null, department: f.department || null, jobTitle: f.jobTitle || null, roleIds: f.roleIds, mode: "invite" }),
      t("Invitation sent. The user will appear as Pending."), { silent: true });
    setBusy(false);
    if (r.ok) { onClose(); onDone(); return; }
    // Show what the server rejected next to its field; anything this form has no field for goes in the alert so it is never invisible.
    const known = ["name", "email", "roles", "roleIds"]; const fe = r.fields ?? {};
    setErr({ name: fe.name ?? "", email: fe.email ?? "", roles: fe.roles ?? fe.roleIds ?? "" });
    const other = Object.entries(fe).filter(([k]) => !known.includes(k)).map(([, v]) => v);
    setForm(other.length ? `${r.error} ${other.join(" ")}` : r.error);
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Add User")} description={t("Sends an invitation. The person sets their own password; passwords are never emailed.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="add-user-form" disabled={busy}>{busy ? t("Sending…") : t("Send invitation")}</Button></>}>
      <form id="add-user-form" className="stack" noValidate onSubmit={submit}>
        {form && <div className="form-alert err" role="alert">{form}</div>}
        <Field label={t("Full name")} htmlFor="au-name" required error={err.name}><TextInput id="au-name" data-autofocus value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
        <Field label={t("Email")} htmlFor="au-email" required error={err.email}><TextInput id="au-email" type="email" value={f.email} invalid={!!err.email} onChange={(e) => setF({ ...f, email: e.target.value })} /></Field>
        <div className="fgrid">
          <Field label={t("Phone")} htmlFor="au-phone"><TextInput id="au-phone" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} placeholder="+62…" /></Field>
          <Field label={t("Department")} htmlFor="au-dep"><TextInput id="au-dep" value={f.department} onChange={(e) => setF({ ...f, department: e.target.value })} /></Field>
          <Field label={t("Job title")} htmlFor="au-title" full><TextInput id="au-title" value={f.jobTitle} onChange={(e) => setF({ ...f, jobTitle: e.target.value })} /></Field>
        </div>
        <fieldset className="fl"><legend>{t("Roles")}{" "}<em>*</em></legend><RolePicker roles={roles} value={f.roleIds} onChange={(v) => setF({ ...f, roleIds: v })} idPrefix="au-role" />{err.roles && <p className="field-error" role="alert">{err.roles}</p>}</fieldset>
      </form>
    </Overlay>
  );
}
