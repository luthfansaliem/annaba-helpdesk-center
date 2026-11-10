"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { KbCategoryItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function Categories() {
  const run = useRun();
  const list = useQuery(() => get<KbCategoryItem[]>("/api/kb/categories"), []);
  const [name, setName] = useState(""); const [err, setErr] = useState(""); const [editing, setEditing] = useState<{ id: string; name: string } | null>(null);
  async function add(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    const r = await run(() => post("/api/kb/categories", { name }), t("Category added."), { silent: true });
    if (!r.ok) { setErr(r.fields?.name ?? r.error); return; }
    setName(""); list.reload();
  }
  async function rename(e: React.FormEvent) {
    e.preventDefault(); if (!editing) return; setErr("");
    const r = await run(() => put(`/api/kb/categories/${editing.id}`, { name: editing.name }), t("Category saved."), { silent: true });
    if (!r.ok) { setErr(r.fields?.name ?? r.error); return; }
    setEditing(null); list.reload();
  }
  const toggle = async (c: KbCategoryItem) => { const r = await run(() => post(`/api/kb/categories/${c.id}/${c.archived ? "restore" : "archive"}`), c.archived ? t("Category restored.") : t("Category archived.")); if (r.ok) list.reload(); };
  const cols: Column<KbCategoryItem>[] = [
    { key: "n", header: t("Name"), cell: (c) => editing?.id === c.id
      ? <form className="row" style={{ gap: 6 }} onSubmit={rename}><TextInput aria-label={t("New name")} value={editing.name} onChange={(e) => setEditing({ id: c.id, name: e.target.value })} /><Button size="sm" variant="primary" type="submit">{t("Save")}</Button><Button size="sm" onClick={() => setEditing(null)}>{t("Cancel")}</Button></form>
      : <>{c.name}{c.archived && <> <Badge tone="gray">{t("Archived")}</Badge></>}</> },
    { key: "a", header: t("Articles"), cell: (c) => c.articles },
    { key: "x", header: <span className="sr-only">{t("Actions")}</span>, cell: (c) => <span className="row" style={{ gap: 6 }}>
      {!c.archived && <Button size="sm" onClick={() => setEditing({ id: c.id, name: c.name })}>{t("Rename")}</Button>}
      <Button size="sm" onClick={() => void toggle(c)}>{c.archived ? t("Restore") : t("Archive")}</Button></span> },
  ];
  return (
    <>
      <PageHeader title={t("Knowledge base categories")} description={t("Shelves for the articles. Archiving a category keeps its articles; they just lose the shelf for new edits.")} />
      <Panel>
        <form className="row" style={{ gap: 8, alignItems: "flex-end", flexWrap: "wrap" }} noValidate onSubmit={add}>
          <Field label={t("New category")} htmlFor="kc-name" error={err && !editing ? err : undefined}><TextInput id="kc-name" maxLength={60} value={name} onChange={(e) => setName(e.target.value)} /></Field>
          <Button variant="primary" type="submit">{t("Add")}</Button>
        </form>
        {err && editing && <div className="form-alert err" role="alert">{err}</div>}
      </Panel>
      <Panel>
        <DataTable label={t("Knowledge base categories")} columns={cols} rows={list.data ?? []} rowKey={(c) => c.id} emptyMessage={t("No categories yet.")} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />
      </Panel>
    </>
  );
}

export default function KbCategoriesPage() { return <Require perm="kb.publish"><Categories /></Require>; }
