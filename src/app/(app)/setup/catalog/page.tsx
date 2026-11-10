"use client";

import { useMemo, useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Can, Require } from "@/components/page-guard";
import { get, patch, post } from "@/lib/api/client";
import type { Audience, CategoryItem, Priority, SaveCategory, SaveService, ServiceListItem, TeamItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const PRIORITIES: Priority[] = ["low", "medium", "high", "urgent"];
const priorityLabel = (p: Priority) => ({ low: t("Low"), medium: t("Medium"), high: t("High"), urgent: t("Urgent") })[p];
const priorityTone = (p: Priority) => ({ low: "gray", medium: "blue", high: "orange", urgent: "red" } as const)[p];
const audienceLabel = (a: Audience) => (a === "staff" ? t("Staff only") : t("Everyone"));

/** Roots first, each followed by its sub-categories. */
function ordered(rows: CategoryItem[]): { row: CategoryItem; depth: number }[] {
  const ids = new Set(rows.map((r) => r.id));
  const roots = rows.filter((r) => !r.parentId || !ids.has(r.parentId));
  return roots.flatMap((r) => [{ row: r, depth: 0 }, ...rows.filter((c) => c.parentId === r.id).map((c) => ({ row: c, depth: 1 }))]);
}

function CategoryDrawer({ open, onClose, onDone, editing, all }: { open: boolean; onClose: () => void; onDone: () => void; editing: CategoryItem | null; all: CategoryItem[] }) {
  const run = useRun();
  const blank: SaveCategory = { name: "", description: null, icon: null, parentId: null, sortOrder: 0 };
  const [f, setF] = useState<SaveCategory>(blank); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) {
    setKey(current);
    setF(editing ? { name: editing.name, description: editing.description, icon: editing.icon, parentId: editing.parentId, sortOrder: editing.sortOrder, version: editing.version } : blank);
    setErrs({}); setErr("");
  }
  const roots = all.filter((c) => !c.archived && !c.parentId && c.id !== editing?.id);
  const canNest = !editing || editing.children === 0; // a category that has sub-categories stays at the top
  const set = <K extends keyof SaveCategory>(k: K, v: SaveCategory[K]) => setF((x) => ({ ...x, [k]: v }));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => (editing ? patch(`/api/catalog/categories/${editing.id}`, f) : post("/api/catalog/categories", f)), editing ? t("Category saved.") : t("Category created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit category") : t("New category")} description={t("Categories group services. They can be nested one level deep.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cat-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="cat-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="ct-name" required error={errs.name}><TextInput id="ct-name" data-autofocus value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label={t("Inside")} htmlFor="ct-parent" error={errs.parentId}>
          <SelectInput id="ct-parent" value={f.parentId ?? ""} disabled={!canNest} onChange={(e) => set("parentId", e.target.value || null)} options={[{ value: "", label: t("Top level") }, ...roots.map((c) => ({ value: c.id, label: c.name }))]} />
        </Field>
        <Field label={t("Description")} htmlFor="ct-desc" error={errs.description}><TextInput id="ct-desc" value={f.description ?? ""} onChange={(e) => set("description", e.target.value || null)} /></Field>
        <Field label={t("Icon")} htmlFor="ct-icon" error={errs.icon}><TextInput id="ct-icon" placeholder="wrench" value={f.icon ?? ""} onChange={(e) => set("icon", e.target.value || null)} /></Field>
        <Field label={t("Order")} htmlFor="ct-sort" error={errs.sortOrder}><TextInput id="ct-sort" type="number" min={0} max={9999} value={f.sortOrder} onChange={(e) => set("sortOrder", Number(e.target.value) || 0)} /></Field>
      </form>
    </Overlay>
  );
}

function ServiceDrawer({ open, onClose, onDone, editing, categories, teams }: { open: boolean; onClose: () => void; onDone: () => void; editing: ServiceListItem | null; categories: CategoryItem[]; teams: TeamItem[] }) {
  const run = useRun();
  const blank: SaveService = { name: "", description: null, icon: null, categoryId: null, owningTeamId: null, defaultPriority: "medium", audience: "everyone" };
  const [f, setF] = useState<SaveService>(blank); const [key, setKey] = useState<string | null>(null);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const current = open ? (editing?.id ?? "new") : null;
  if (current !== key) {
    setKey(current);
    setF(editing ? { name: editing.name, description: editing.description, icon: editing.icon, categoryId: editing.categoryId, owningTeamId: editing.owningTeamId, defaultPriority: editing.defaultPriority, audience: editing.audience, version: editing.version } : blank);
    setErrs({}); setErr("");
  }
  const set = <K extends keyof SaveService>(k: K, v: SaveService[K]) => setF((x) => ({ ...x, [k]: v }));
  const live = ordered(categories.filter((c) => !c.archived));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const r = await run(() => (editing ? patch(`/api/catalog/services/${editing.id}`, f) : post("/api/catalog/services", f)), editing ? t("Service saved.") : t("Service created."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    onClose(); onDone();
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={editing ? t("Edit service") : t("New service")} description={t("A service starts as a draft. Build its form, then publish it.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="svc-form" disabled={busy}>{busy ? t("Saving…") : t("Save")}</Button></>}>
      <form id="svc-form" className="stack" noValidate onSubmit={submit}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Name")} htmlFor="sv-name" required error={errs.name}><TextInput id="sv-name" data-autofocus value={f.name} onChange={(e) => set("name", e.target.value)} /></Field>
        <Field label={t("Category")} htmlFor="sv-cat" required error={errs.categoryId}>
          <SelectInput id="sv-cat" value={f.categoryId ?? ""} onChange={(e) => set("categoryId", e.target.value || null)} placeholder={t("Choose a category")}
            options={live.map(({ row, depth }) => ({ value: row.id, label: `${depth ? "— " : ""}${row.name}` }))} />
        </Field>
        <Field label={t("Description")} htmlFor="sv-desc" error={errs.description}><TextInput id="sv-desc" value={f.description ?? ""} onChange={(e) => set("description", e.target.value || null)} /></Field>
        <Field label={t("Owning team")} htmlFor="sv-team" error={errs.owningTeamId}>
          <SelectInput id="sv-team" value={f.owningTeamId ?? ""} onChange={(e) => set("owningTeamId", e.target.value || null)} options={[{ value: "", label: t("No team yet") }, ...teams.filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))]} />
        </Field>
        <Field label={t("Default priority")} htmlFor="sv-prio" error={errs.defaultPriority}>
          <SelectInput id="sv-prio" value={f.defaultPriority} onChange={(e) => set("defaultPriority", e.target.value as Priority)} options={PRIORITIES.map((p) => ({ value: p, label: priorityLabel(p) }))} />
        </Field>
        <Field label={t("Who can request it")} htmlFor="sv-aud" error={errs.audience}>
          <SelectInput id="sv-aud" value={f.audience} onChange={(e) => set("audience", e.target.value as Audience)} options={[{ value: "everyone", label: t("Everyone") }, { value: "staff", label: t("Staff only") }]} />
        </Field>
        <Field label={t("Icon")} htmlFor="sv-icon" error={errs.icon}><TextInput id="sv-icon" placeholder="wrench" value={f.icon ?? ""} onChange={(e) => set("icon", e.target.value || null)} /></Field>
      </form>
    </Overlay>
  );
}

function Catalog() {
  const run = useRun();
  const [tab, setTab] = useState<"services" | "categories">("services");
  const [archived, setArchived] = useState(false); const [filter, setFilter] = useState(""); const [categoryId, setCategoryId] = useState("");
  const cats = useQuery(() => get<CategoryItem[]>("/api/catalog/categories", { includeArchived: true }), []);
  const svcs = useQuery(() => get<ServiceListItem[]>("/api/catalog/services", { includeArchived: archived }), [archived]);
  const teams = useQuery(() => get<TeamItem[]>("/api/teams").catch(() => [] as TeamItem[]), []); // a catalog admin without teams.read simply gets no team choices
  const [svcDrawer, setSvcDrawer] = useState<{ open: boolean; row: ServiceListItem | null }>({ open: false, row: null });
  const [catDrawer, setCatDrawer] = useState<{ open: boolean; row: CategoryItem | null }>({ open: false, row: null });
  const reloadAll = () => { cats.reload(); svcs.reload(); };

  const rows = useMemo(() => (svcs.data ?? []).filter((s) => (!categoryId || s.categoryId === categoryId) && (!filter || `${s.name} ${s.description ?? ""}`.toLowerCase().includes(filter.toLowerCase()))), [svcs.data, categoryId, filter]);
  const act = async (s: ServiceListItem, what: "publish" | "unpublish" | "archive" | "restore") => {
    const done = { publish: t("Service published."), unpublish: t("Service unpublished."), archive: t("Service archived."), restore: t("Service restored.") }[what];
    if ((await run(() => post(`/api/catalog/services/${s.id}/${what}`), done)).ok) svcs.reload();
  };
  const actCat = async (c: CategoryItem, what: "archive" | "restore") => { if ((await run(() => post(`/api/catalog/categories/${c.id}/${what}`), what === "archive" ? t("Category archived.") : t("Category restored."))).ok) reloadAll(); };

  const svcCols: Column<ServiceListItem>[] = [
    { key: "n", header: t("Service"), cell: (s) => <><b>{s.name}</b>{s.description && <><br /><small>{s.description}</small></>}</> },
    { key: "c", header: t("Category"), className: "hide-sm", cell: (s) => s.categoryName },
    { key: "t", header: t("Owning team"), className: "hide-sm", cell: (s) => s.owningTeamName ?? "—" },
    { key: "p", header: t("Priority"), cell: (s) => <Badge tone={priorityTone(s.defaultPriority)}>{priorityLabel(s.defaultPriority)}</Badge> },
    { key: "a", header: t("Audience"), className: "hide-sm", cell: (s) => audienceLabel(s.audience) },
    { key: "f", header: t("Form"), className: "hide-sm", cell: (s) => (s.currentFormVersion ? `v${s.currentFormVersion}` : "—") },
    { key: "s", header: t("Status"), cell: (s) => <Badge tone={s.archived ? "gray" : s.isPublished ? "green" : "orange"}>{s.archived ? t("Archived") : s.isPublished ? t("Published") : t("Draft")}</Badge> },
    { key: "x", header: t("Actions"), cell: (s) => (
      <span className="row" style={{ gap: 6, flexWrap: "wrap" }}>
        <Button size="sm" variant="outline" href={`/setup/catalog/${s.id}`} aria-label={`${t("Form")} ${s.name}`}>{t("Form")}</Button>
        <Can perm="catalog.manage">
          {!s.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${s.name}`} onClick={() => setSvcDrawer({ open: true, row: s })}>{t("Edit")}</Button>}
          {!s.archived && <Button size="sm" variant="outline" aria-label={`${s.isPublished ? t("Unpublish") : t("Publish")} ${s.name}`} onClick={() => void act(s, s.isPublished ? "unpublish" : "publish")}>{s.isPublished ? t("Unpublish") : t("Publish")}</Button>}
          <Button size="sm" variant="outline" aria-label={`${s.archived ? t("Restore") : t("Archive")} ${s.name}`} onClick={() => void act(s, s.archived ? "restore" : "archive")}>{s.archived ? t("Restore") : t("Archive")}</Button>
        </Can>
      </span>) },
  ];
  const catCols: Column<{ row: CategoryItem; depth: number }>[] = [
    { key: "n", header: t("Category"), cell: ({ row, depth }) => <span style={{ paddingLeft: depth * 20 }}><b>{depth ? "↳ " : ""}{row.name}</b>{row.description && <><br /><small style={{ paddingLeft: depth * 20 }}>{row.description}</small></>}</span> },
    { key: "s", header: t("Services"), cell: ({ row }) => row.services },
    { key: "o", header: t("Order"), className: "hide-sm", cell: ({ row }) => row.sortOrder },
    { key: "st", header: t("Status"), cell: ({ row }) => <Badge tone={row.archived ? "gray" : "green"}>{row.archived ? t("Archived") : t("Active")}</Badge> },
    { key: "a", header: t("Actions"), cell: ({ row }) => <Can perm="catalog.manage"><span className="row" style={{ gap: 6 }}>
      {!row.archived && <Button size="sm" variant="outline" aria-label={`${t("Edit")} ${row.name}`} onClick={() => setCatDrawer({ open: true, row })}>{t("Edit")}</Button>}
      <Button size="sm" variant="outline" aria-label={`${row.archived ? t("Restore") : t("Archive")} ${row.name}`} onClick={() => void actCat(row, row.archived ? "restore" : "archive")}>{row.archived ? t("Restore") : t("Archive")}</Button></span></Can> },
  ];

  return (
    <>
      <PageHeader title={t("Service Catalog")} description={t("What people can ask for, and the questions each request form asks.")}
        actions={<Can perm="catalog.manage">{tab === "services"
          ? <Button variant="primary" onClick={() => setSvcDrawer({ open: true, row: null })}><Plus size={16} /> {t("New service")}</Button>
          : <Button variant="primary" onClick={() => setCatDrawer({ open: true, row: null })}><Plus size={16} /> {t("New category")}</Button>}</Can>} />
      <Panel>
        <Tabs label={t("Catalog sections")} value={tab} onChange={setTab} items={[{ key: "services", label: t("Services") }, { key: "categories", label: t("Categories") }]} />
        <div className="row" style={{ gap: 12, padding: "12px 0", flexWrap: "wrap", alignItems: "flex-end" }}>
          {tab === "services" && <>
            <Field label={t("Search")} htmlFor="cg-q"><TextInput id="cg-q" value={filter} onChange={(e) => setFilter(e.target.value)} /></Field>
            <Field label={t("Category")} htmlFor="cg-cat"><SelectInput id="cg-cat" value={categoryId} onChange={(e) => setCategoryId(e.target.value)} options={[{ value: "", label: t("All categories") }, ...ordered((cats.data ?? []).filter((c) => !c.archived)).map(({ row, depth }) => ({ value: row.id, label: `${depth ? "— " : ""}${row.name}` }))]} /></Field>
          </>}
          <label className="row" style={{ gap: 8 }}><Checkbox checked={archived} onChange={(e) => setArchived(e.target.checked)} /> {t("Show archived")}</label>
        </div>
        {tab === "services"
          ? <DataTable label={t("Services")} columns={svcCols} rows={rows} rowKey={(s) => s.id} emptyMessage={t("No services match.")} loading={svcs.data === undefined && svcs.loading} error={svcs.error?.message ?? null} onRetry={svcs.reload} />
          : <DataTable label={t("Categories")} columns={catCols} rows={ordered((cats.data ?? []).filter((c) => archived || !c.archived))} rowKey={({ row }) => row.id} emptyMessage={t("No categories yet.")} loading={cats.data === undefined && cats.loading} error={cats.error?.message ?? null} onRetry={cats.reload} />}
      </Panel>
      <ServiceDrawer open={svcDrawer.open} onClose={() => setSvcDrawer({ open: false, row: null })} onDone={reloadAll} editing={svcDrawer.row} categories={cats.data ?? []} teams={teams.data ?? []} />
      <CategoryDrawer open={catDrawer.open} onClose={() => setCatDrawer({ open: false, row: null })} onDone={reloadAll} editing={catDrawer.row} all={cats.data ?? []} />
    </>
  );
}

export default function CatalogPage() { return <Require perm="catalog.read"><Catalog /></Require>; }
