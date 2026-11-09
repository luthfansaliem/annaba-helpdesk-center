"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import {
  List, Plus, Pencil, Trash2, Save, SaveAll, ChevronDown, Download, Eye, MoreHorizontal, Mail, User, Briefcase, KeyRound, FileText, ArrowLeft,
} from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { FlowSteps } from "@/components/flow-steps";
import { ProfileHeader, InfoGrid } from "@/components/detail-view";
import { Button, IconButton } from "@/components/ui/button";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Toast, type ToastTone } from "@/components/ui/toast";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { Avatar } from "@/components/ui/avatar";
import { DataTable, type Column } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect, SearchBox } from "@/components/data/filter-bar";
import { ConfirmDialog } from "@/components/data/confirm-dialog";
import { EmptyState } from "@/components/data/empty-state";
import { employees as seed, departments, positions, type Employee } from "@/data/dummy";

const empTone = { Active: "green", "On Leave": "orange", Inactive: "red" } as const;
type Msg = { id: number; tone: ToastTone; text: string };
type FormState = { name: string; idNo: string; email: string; dept: string; position: string; employment: Employee["employment"]; account: Employee["account"] };
const blank: FormState = { name: "", idNo: "", email: "", dept: "", position: "", employment: "Active", account: "Active" };
const ALL = { dept: "All Departments", emp: "All Status", acc: "All Account Status" };

const topTabs = ["CRUD", "Components", "Patterns", "Guidelines"].map((k) => ({ key: k, label: k }));
const formTabs = [
  { key: "General", label: "General", icon: <User size={14} /> }, { key: "Employment", label: "Employment", icon: <Briefcase size={14} /> },
  { key: "Access", label: "Access", icon: <KeyRound size={14} /> }, { key: "Documents", label: "Documents", icon: <FileText size={14} /> },
];
const detailTabs = ["Overview", "Employment", "Access", "Documents", "Activity"].map((k) => ({ key: k, label: k }));

export default function EmployeesPage() {
  const router = useRouter();
  const [topTab, setTopTab] = useState("CRUD");
  const [formTab, setFormTab] = useState("General");
  const [detailTab, setDetailTab] = useState("Overview");

  const [data, setData] = useState<Employee[]>(seed);
  const [query, setQuery] = useState("");
  const [draft, setDraft] = useState(ALL);
  const [applied, setApplied] = useState(ALL);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [selected, setSelected] = useState<string[]>([]);

  const [form, setForm] = useState<FormState>(blank);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({});

  const [detailId, setDetailId] = useState<string>(seed[0].id);
  const [deleteId, setDeleteId] = useState<string | null>(seed[0].id);
  const [msgs, setMsgs] = useState<Msg[]>([
    { id: 1, tone: "success", text: "Employee created successfully." },
    { id: 2, tone: "info", text: "Employee updated successfully." },
    { id: 3, tone: "warning", text: "This employee is currently on leave." },
    { id: 4, tone: "error", text: "Failed to delete employee. Please try again." },
  ]);
  const notify = (tone: ToastTone, text: string) => setMsgs((m) => [{ id: Date.now() + Math.random(), tone, text }, ...m].slice(0, 5));

  const filtered = useMemo(() => data.filter((e) => {
    const q = query.trim().toLowerCase();
    return (!q || [e.name, e.id, e.email].some((v) => v.toLowerCase().includes(q)))
      && (applied.dept === ALL.dept || e.dept === applied.dept)
      && (applied.emp === ALL.emp || e.employment === applied.emp)
      && (applied.acc === ALL.acc || e.account === applied.acc);
  }), [data, query, applied]);

  const detail = data.find((e) => e.id === detailId) ?? data[0];
  const toDelete = data.find((e) => e.id === deleteId);
  const hasFilters = query || applied.dept !== ALL.dept || applied.emp !== ALL.emp || applied.acc !== ALL.acc;
  const total = hasFilters ? filtered.length : 248;
  const pageCount = Math.max(1, Math.ceil(total / pageSize));
  const shown = filtered.slice(0, pageSize);

  function edit(e: Employee) {
    setEditingId(e.id);
    setForm({ name: e.name, idNo: e.id.replace("EMP-", ""), email: e.email, dept: e.dept, position: e.position, employment: e.employment, account: e.account });
    setErrors({}); setFormTab("General");
    document.getElementById("employee-form")?.scrollIntoView({ behavior: "smooth", block: "center" });
  }

  function validate() {
    const er: typeof errors = {};
    if (!form.name.trim()) er.name = "Full name is required";
    if (!/^\d{3,}$/.test(form.idNo)) er.idNo = "Use 3+ digits";
    else if (data.some((d) => d.id === "EMP-" + form.idNo && d.id !== editingId)) er.idNo = "ID already exists";
    if (!/^\S+@\S+\.\S+$/.test(form.email)) er.email = "Enter a valid email";
    if (!form.dept) er.dept = "Select a department";
    if (!form.position) er.position = "Select a position";
    setErrors(er);
    return Object.keys(er).length === 0;
  }

  function save() {
    if (!validate()) return;
    const rec: Employee = {
      id: "EMP-" + form.idNo, name: form.name.trim(), dept: form.dept, position: form.position, employment: form.employment, account: form.account,
      email: form.email, phone: data.find((d) => d.id === editingId)?.phone ?? "+62 800-0000-0000", joined: data.find((d) => d.id === editingId)?.joined ?? "22 Apr 2025",
    };
    setData((d) => (editingId ? d.map((x) => (x.id === editingId ? rec : x)) : [rec, ...d]));
    notify(rec.employment === "On Leave" ? "warning" : "success", editingId ? "Employee updated successfully." : "Employee created successfully.");
    setDetailId(rec.id); setEditingId(null); setForm(blank); setErrors({});
  }

  function cancel() { setEditingId(null); setForm(blank); setErrors({}); }

  function confirmDelete() {
    if (!toDelete) return;
    setData((d) => d.filter((x) => x.id !== toDelete.id));
    setSelected((s) => s.filter((k) => k !== toDelete.id));
    if (detailId === toDelete.id) setDetailId(data.find((x) => x.id !== toDelete.id)?.id ?? "");
    notify("success", `${toDelete.name} deleted.`);
    setDeleteId(null);
  }

  const columns: Column<Employee>[] = [
    { key: "id", header: "Employee ID", sortable: true, cell: (e) => e.id },
    { key: "name", header: "Name", sortable: true, cell: (e) => <div className="who"><Avatar name={e.name} size={26} />{e.name}</div> },
    { key: "dept", header: "Department", cell: (e) => e.dept },
    { key: "position", header: "Position", cell: (e) => e.position },
    { key: "employment", header: "Employment", cell: (e) => <Badge tone={empTone[e.employment]}>{e.employment}</Badge> },
    { key: "account", header: "Account", cell: (e) => <Badge tone={e.account === "Active" ? "green" : "gray"}>{e.account}</Badge> },
    { key: "actions", header: "Actions", cell: (e) => (
      <div className="ra">
        <IconButton label={`View ${e.name}`} onClick={() => { setDetailId(e.id); setDetailTab("Overview"); }}><Eye size={14} /></IconButton>
        <IconButton label={`Edit ${e.name}`} onClick={() => edit(e)}><Pencil size={13} /></IconButton>
        <IconButton label={`Delete ${e.name}`} onClick={() => setDeleteId(e.id)}><MoreHorizontal size={14} /></IconButton>
      </div>) },
  ];

  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => ({ ...f, [k]: v }));

  return (
    <>
      <PageHeader
        title="Employee Management – CRUD Components"
        description="Complete CRUD interface components for enterprise applications • Azure Executive 2.0"
        actions={<Tabs variant="segmented" label="Sections" items={topTabs} value={topTab} onChange={(k) => { if (k === "Components") router.push("/design/components"); else setTopTab(k); }} />}
      />

      <div className="cg r1">
        <Panel title="1. CRUD Flow Overview">
          <FlowSteps steps={[
            { icon: <List size={26} />, title: "List / Read", description: "View & manage data", color: "#0b62e6", bg: "#e5efff" },
            { icon: <Plus size={26} />, title: "Create", description: "Add new data", color: "#0b62e6", bg: "#e5efff" },
            { icon: <Pencil size={26} />, title: "Update", description: "Edit existing data", color: "#f4694f", bg: "#ffe9e4" },
            { icon: <Trash2 size={26} />, title: "Delete", description: "Remove data (with confirmation)", color: "#f4694f", bg: "#ffe9e4" },
          ]} />
        </Panel>
        <Panel title={<>2. Action Buttons <span style={{ fontWeight: 500 }}>(Examples)</span></>}>
          <div className="abtn">
            <Button variant="primary" onClick={() => { cancel(); document.getElementById("employee-form")?.scrollIntoView({ behavior: "smooth", block: "center" }); }}><Plus size={16} /> Add Employee</Button>
            <Button onClick={() => edit(detail)}><Pencil size={15} /> Edit</Button>
            <Button variant="danger-outline" onClick={() => setDeleteId(detail.id)}><Trash2 size={15} /> Delete</Button>
            <Button>More <ChevronDown size={15} /></Button>
            <Button variant="primary" onClick={save}><Save size={15} /> Save</Button>
            <Button variant="soft" onClick={() => { save(); }}><SaveAll size={15} /> Save &amp; Close</Button>
            <Button onClick={cancel}>Cancel</Button>
          </div>
        </Panel>
      </div>

      <div className="cg r2">
        <Panel title="3. Data List (Read) – Employees" description="Manage employee information, roles, and status."
          actions={<>
            <Button onClick={() => { notify("info", `Exported ${filtered.length} employees (demo).`); }}><Download size={15} /> Export <ChevronDown size={14} /></Button>
            <Button variant="primary" onClick={() => { cancel(); document.getElementById("employee-form")?.scrollIntoView({ behavior: "smooth", block: "center" }); }}><Plus size={15} /> Add Employee</Button>
          </>}>
          <FilterBar onApply={() => { setApplied(draft); setPage(1); }}>
            <SearchBox label="Search employees" placeholder="Search by name, employee ID, or email..." value={query} onChange={(v) => { setQuery(v); setPage(1); }} />
            <FilterSelect label="Department" value={draft.dept} onChange={(v) => setDraft((d) => ({ ...d, dept: v }))} options={[ALL.dept, ...departments]} />
            <FilterSelect label="Employment Status" value={draft.emp} onChange={(v) => setDraft((d) => ({ ...d, emp: v }))} options={[ALL.emp, "Active", "On Leave", "Inactive"]} />
            <FilterSelect label="Account Status" value={draft.acc} onChange={(v) => setDraft((d) => ({ ...d, acc: v }))} options={[ALL.acc, "Active", "Disabled"]} />
          </FilterBar>
          <DataTable label="Employees" selectable columns={columns} rows={shown} rowKey={(e) => e.id} selected={selected} onSelectedChange={setSelected} emptyMessage="No employees match your filters." />
          <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={pageSize} onPageSizeChange={(n) => { setPageSize(n); setPage(1); }}
            summary={shown.length ? `Showing 1–${shown.length} of ${total} results` : "Showing 0 results"} />
        </Panel>

        <Panel title={editingId ? "4. Create / Update (Form) – Editing" : "4. Create / Update (Form)"} id="employee-form">
          <Tabs items={formTabs} value={formTab} onChange={setFormTab} label="Form sections" />
          {formTab === "General" ? (
            <form className="fgrid" noValidate onSubmit={(e) => { e.preventDefault(); save(); }}>
              <Field label="Full Name" htmlFor="f-name" required full error={errors.name}>
                <TextInput id="f-name" placeholder="Enter full name" value={form.name} invalid={!!errors.name} onChange={(e) => set("name", e.target.value)} />
              </Field>
              <Field label="Employee ID" htmlFor="f-id" required error={errors.idNo}>
                <TextInput id="f-id" prefix="EMP-" inputMode="numeric" value={form.idNo} invalid={!!errors.idNo} onChange={(e) => set("idNo", e.target.value.replace(/\D/g, ""))} />
              </Field>
              <Field label="Email" htmlFor="f-email" required error={errors.email}>
                <TextInput id="f-email" type="email" icon={<Mail size={14} />} placeholder="name@company.com" value={form.email} invalid={!!errors.email} onChange={(e) => set("email", e.target.value)} />
              </Field>
              <Field label="Department" htmlFor="f-dept" required error={errors.dept}>
                <SelectInput id="f-dept" placeholder="Select department" options={departments} value={form.dept} invalid={!!errors.dept} onChange={(e) => set("dept", e.target.value)} />
              </Field>
              <Field label="Position" htmlFor="f-pos" required error={errors.position}>
                <SelectInput id="f-pos" placeholder="Select position" options={positions} value={form.position} invalid={!!errors.position} onChange={(e) => set("position", e.target.value)} />
              </Field>
              <Field label="Employment Status" htmlFor="f-emp" required>
                <SelectInput id="f-emp" dotColor="#16a37f" options={["Active", "On Leave", "Inactive"]} value={form.employment} onChange={(e) => set("employment", e.target.value as FormState["employment"])} />
              </Field>
              <Field label="Account Status" htmlFor="f-acc" required>
                <SelectInput id="f-acc" dotColor="#16a37f" options={["Active", "Disabled"]} value={form.account} onChange={(e) => set("account", e.target.value as FormState["account"])} />
              </Field>
              <button type="submit" hidden />
            </form>
          ) : (
            <p className="tab-placeholder">{formTab} fields go here — swap in your own form sections.</p>
          )}
          <div className="fa"><Button onClick={cancel}>Cancel</Button><Button variant="primary" onClick={save}><Save size={15} /> Save</Button></div>
        </Panel>
      </div>

      <div className="cg r3">
        <Panel title="5. View Detail (Read)">
          {detail ? (<>
            <div className="detail-h">
              <ArrowLeft size={18} /><h3>Employee Detail</h3>
              <Button size="sm" onClick={() => edit(detail)}><Pencil size={13} /> Edit</Button>
              <IconButton label="More actions" className="sq"><MoreHorizontal size={15} /></IconButton>
            </div>
            <ProfileHeader name={detail.name} avatarIndex={2} subtitle={`${detail.id} • ${detail.position}`}
              badges={<><Badge tone={empTone[detail.employment]}>{detail.employment}</Badge><Badge tone="blue">{detail.account}</Badge></>} />
            <Tabs size="lg" items={detailTabs} value={detailTab} onChange={setDetailTab} label="Detail sections" />
            {detailTab === "Overview" || detailTab === "Employment"
              ? <InfoGrid items={[{ label: "Email", value: detail.email }, { label: "Department", value: detail.dept }, { label: "Phone", value: detail.phone }, { label: "Join Date", value: detail.joined }]} />
              : <p className="tab-placeholder">{detailTab} details are not part of this demo.</p>}
          </>) : <p className="tab-placeholder">No employee selected.</p>}
        </Panel>

        <Panel title="6. Delete Confirmation">
          {toDelete ? (
            <ConfirmDialog title="Delete Employee?" onCancel={() => setDeleteId(null)} onConfirm={confirmDelete}>
              Are you sure you want to delete <b>{toDelete.name}</b><br />({toDelete.id})? This action cannot be undone.
            </ConfirmDialog>
          ) : (
            <div className="del"><p style={{ margin: "60px 0 16px" }}>Choose a row&apos;s “⋯” button to delete.</p></div>
          )}
        </Panel>

        <Panel title="7. States & Feedback">
          {msgs.map((m) => <Toast key={m.id} tone={m.tone} onDismiss={() => setMsgs((x) => x.filter((y) => y.id !== m.id))}>{m.text}</Toast>)}
          {msgs.length === 0 && <p className="tab-placeholder">No messages.</p>}
        </Panel>

        <Panel title="8. Empty State" className="empty-panel">
          <EmptyState title="No employees found" description={<>Try adjusting your filter or add a new employee<br />to get started.</>}
            action={<Button variant="primary" onClick={() => { cancel(); document.getElementById("employee-form")?.scrollIntoView({ behavior: "smooth", block: "center" }); }}><Plus size={15} /> Add Employee</Button>} />
        </Panel>
      </div>
    </>
  );
}
