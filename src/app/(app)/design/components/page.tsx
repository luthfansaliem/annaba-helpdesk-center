"use client";

import { useState } from "react";
import { Plus, Trash2, Mail, Users } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Button } from "@/components/ui/button";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Toast } from "@/components/ui/toast";
import { Avatar } from "@/components/ui/avatar";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect, SearchBox } from "@/components/data/filter-bar";
import { ConfirmDialog } from "@/components/data/confirm-dialog";
import { EmptyState } from "@/components/data/empty-state";
import { StatCard } from "@/components/charts/stat-card";
import { LineChart, ChartLegend } from "@/components/charts/line-chart";
import { DonutChart, DonutLegend } from "@/components/charts/donut-chart";
import { performanceByRange, projectStatus, stats, employees } from "@/data/dummy";

export default function ComponentGallery() {
  const [tab, setTab] = useState("a");
  const [q, setQ] = useState("");
  const [dept, setDept] = useState("All");
  const [page, setPage] = useState(1);
  const [dlg, setDlg] = useState(false);
  const s = stats[0];

  return (
    <>
      <PageHeader title="Component Library" description="Every reusable building block used by the login, dashboard and CRUD pages." />
      <div className="gallery cg r1">
        <Panel title="Button">
          <div className="row">
            <Button variant="primary"><Plus size={15} /> Primary</Button><Button>Outline</Button><Button variant="soft">Soft</Button>
            <Button variant="danger"><Trash2 size={14} /> Danger</Button><Button variant="danger-outline">Danger outline</Button><Button size="sm">Small</Button><Button disabled>Disabled</Button>
          </div>
        </Panel>
        <Panel title="Badge · Avatar · Toast">
          <div className="row"><Badge tone="green">Active</Badge><Badge tone="orange">On Leave</Badge><Badge tone="red">Inactive</Badge><Badge tone="gray">Disabled</Badge><Badge tone="blue">Info</Badge>
            <Avatar name="Andi Pratama" /><Avatar name="Siti Rahma" size={36} /></div>
          <div style={{ marginTop: 12 }}><Toast tone="success">Saved.</Toast><Toast tone="error" onDismiss={() => {}}>Something failed.</Toast></div>
        </Panel>
        <Panel title="Tabs">
          <Tabs items={[{ key: "a", label: "General" }, { key: "b", label: "Access" }]} value={tab} onChange={setTab} />
          <Tabs variant="segmented" items={[{ key: "a", label: "One" }, { key: "b", label: "Two" }]} value={tab} onChange={setTab} />
        </Panel>
        <Panel title="Form fields">
          <div className="fgrid">
            <Field label="Email" required htmlFor="g-email"><TextInput id="g-email" icon={<Mail size={14} />} placeholder="name@company.com" /></Field>
            <Field label="Department" htmlFor="g-dept"><SelectInput id="g-dept" placeholder="Select" options={["Engineering", "Design"]} defaultValue="" /></Field>
            <Field label="With error" error="This field is required"><TextInput invalid placeholder="..." /></Field>
            <Field label="Prefixed"><TextInput prefix="EMP-" /></Field>
          </div>
        </Panel>
        <Panel title="Filter bar + DataTable + Pagination" className="wide">
          <FilterBar>
            <SearchBox value={q} onChange={setQ} placeholder="Search..." label="Search" />
            <FilterSelect label="Department" value={dept} onChange={setDept} options={["All", "Engineering", "Design"]} />
          </FilterBar>
          <DataTable selectable rowKey={(e) => e.id} rows={employees.filter((e) => (dept === "All" || e.dept === dept) && e.name.toLowerCase().includes(q.toLowerCase()))}
            columns={[{ key: "n", header: "Name", sortable: true, cell: (e) => e.name }, { key: "d", header: "Department", cell: (e) => e.dept }]} />
          <Pagination page={page} pageCount={10} onPageChange={setPage} pageSize={10} summary="Showing 1–5 of 50 results" />
        </Panel>
        <Panel title="Dialog · Empty state">
          <Button variant="danger-outline" onClick={() => setDlg(true)}><Trash2 size={14} /> Open modal</Button>
          {dlg && <ConfirmDialog modal title="Delete item?" onCancel={() => setDlg(false)} onConfirm={() => setDlg(false)}>This action cannot be undone.</ConfirmDialog>}
          <EmptyState title="Nothing here" description="Add something to get started." />
        </Panel>
        <Panel title="Charts">
          <div style={{ maxWidth: 260, marginBottom: 12 }}><StatCard label={s.label} value={s.value} delta={s.delta} up={s.up} icon={<Users size={26} />} color={s.color} bg={s.bg} spark={s.spark} /></div>
          <LineChart series={performanceByRange["30D"].series} labels={performanceByRange["30D"].labels} /><ChartLegend items={performanceByRange["30D"].series} />
          <div className="donut-wrap"><DonutChart segments={projectStatus} centerLabel="Total" /><DonutLegend segments={projectStatus} /></div>
        </Panel>
      </div>
    </>
  );
}
