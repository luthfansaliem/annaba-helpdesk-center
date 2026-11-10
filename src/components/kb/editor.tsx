"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Copy, Trash2, Upload } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button, IconButton } from "@/components/ui/button";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { Markdown } from "@/components/kb/markdown";
import { kbFileUrl } from "@/components/kb/kb-bits";
import { del, get, post, put } from "@/lib/api/client";
import type { KbArticleDetail, KbCategoryItem, ServiceListItem } from "@/lib/api/types";
import { FILE_ACCEPT, MAX_FILE_BYTES, fmtSize, uploadFile } from "@/lib/files";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Write or improve an article. Saving makes a new version (a draft until someone publishes it). */
export function ArticleEditor({ article }: { article: KbArticleDetail | null }) {
  const router = useRouter(); const run = useRun();
  const [f, setF] = useState({ title: article?.title ?? "", summary: article?.summary ?? "", body: article?.body ?? "", categoryId: article?.categoryId ?? "", visibility: article?.visibility ?? "everyone", reviewBy: article?.reviewBy ?? "", note: "" });
  const [services, setServices] = useState<string[]>(article?.services.map((s) => s.id) ?? []); const [svcQ, setSvcQ] = useState("");
  const [files, setFiles] = useState(article?.files ?? []); const [preview, setPreview] = useState(false);
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [upErr, setUpErr] = useState("");
  const cats = useQuery(() => get<KbCategoryItem[]>("/api/kb/categories"), []);
  const svcs = useQuery(() => get<ServiceListItem[]>("/api/catalog/services").catch(() => [] as ServiceListItem[]), []);
  const set = (k: keyof typeof f, v: string) => setF((x) => ({ ...x, [k]: v }));
  const shownSvcs = (svcs.data ?? []).filter((s) => !s.archived && (!svcQ || s.name.toLowerCase().includes(svcQ.toLowerCase()))).slice(0, 40);

  async function save(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { title: f.title, summary: f.summary || null, body: f.body, categoryId: f.categoryId || null, visibility: f.visibility, reviewBy: f.reviewBy || null, serviceIds: services, note: f.note || null, version: article?.version };
    const r = await run(() => (article ? put<KbArticleDetail>(`/api/kb/articles/${article.id}`, body) : post<KbArticleDetail>("/api/kb/articles", body)), t("Draft saved."), { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    router.push(`/kb/${r.data.id}?draft=1`);
  }
  async function addFile(file: File | undefined) {
    if (!file || !article) return; setUpErr("");
    if (file.size > MAX_FILE_BYTES) { setUpErr(t("The file is larger than 8 MB.")); return; }
    const r = await run(() => uploadFile<KbArticleDetail>(`/api/kb/articles/${article.id}/files`, file), t("File added."), { silent: true });
    if (!r.ok) { setUpErr(r.fields?.file ?? r.error); return; }
    setFiles(r.data.files);
  }
  async function removeFile(id: string) {
    if (!article) return;
    const r = await run(() => del<KbArticleDetail>(`/api/kb/articles/${article.id}/files/${id}`), t("Removed."));
    if (r.ok && r.data) setFiles(r.data.files);
  }
  const copy = async (code: string) => { try { await navigator.clipboard.writeText(code); } catch { /* the code is shown next to the button */ } };

  return (
    <form className="stack" noValidate onSubmit={save} aria-label={t("Article")}>
      {err && <div className="form-alert err" role="alert">{err}</div>}
      <Panel>
        <div className="stack">
          <Field label={t("Title")} htmlFor="kb-title" required error={errs.title}><TextInput id="kb-title" maxLength={150} value={f.title} onChange={(e) => set("title", e.target.value)} /></Field>
          <Field label={t("Summary")} htmlFor="kb-summary" error={errs.summary}><TextInput id="kb-summary" maxLength={300} placeholder={t("The answer in one sentence")} value={f.summary} onChange={(e) => set("summary", e.target.value)} /></Field>
          <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>
            <Field label={t("Category")} htmlFor="kb-cat" error={errs.categoryId}><SelectInput id="kb-cat" value={f.categoryId} onChange={(e) => set("categoryId", e.target.value)} options={[{ value: "", label: t("None") }, ...(cats.data ?? []).filter((c) => !c.archived || c.id === f.categoryId).map((c) => ({ value: c.id, label: c.name }))]} /></Field>
            <Field label={t("Who can read it")} htmlFor="kb-vis" error={errs.visibility}><SelectInput id="kb-vis" value={f.visibility} onChange={(e) => set("visibility", e.target.value)} options={[{ value: "everyone", label: t("Everyone") }, { value: "staff", label: t("Helpdesk staff only") }]} /></Field>
            <Field label={t("Review by")} htmlFor="kb-review" error={errs.reviewBy}><TextInput id="kb-review" type="date" value={f.reviewBy} onChange={(e) => set("reviewBy", e.target.value)} /></Field>
          </div>
        </div>
      </Panel>
      <Panel title={t("Text")} description={t("Markdown: # heading, - list, 1. steps, **bold**, *italic*, [link](https://…), ![picture](file:…) for an image added below.")}
        actions={<Button size="sm" onClick={() => setPreview((p) => !p)} aria-pressed={preview}>{preview ? t("Edit text") : t("Preview")}</Button>}>
        {errs.body && <p className="field-error" role="alert">{errs.body}</p>}
        {preview
          ? <div data-testid="kb-preview">{f.body.trim() ? <Markdown text={f.body} fileUrl={article ? kbFileUrl(article.id) : undefined} /> : <p className="tab-placeholder">{t("Nothing written yet.")}</p>}</div>
          : <textarea id="kb-body" aria-label={t("Text")} rows={16} maxLength={50000} className="textarea" style={{ fontFamily: "ui-monospace, monospace" }} value={f.body} onChange={(e) => set("body", e.target.value)} />}
      </Panel>
      <Panel title={t("Show with these services")} description={t("People asking for these services see the article first.")}>
        {errs.serviceIds && <p className="field-error" role="alert">{errs.serviceIds}</p>}
        <p className="sub" style={{ margin: "0 0 8px" }}>{t("{n} chosen.", { n: services.length })}</p>
        <Field label={t("Find a service")} htmlFor="kb-svcq"><TextInput id="kb-svcq" value={svcQ} onChange={(e) => setSvcQ(e.target.value)} /></Field>
        <ul className="stack" style={{ listStyle: "none", margin: "8px 0 0", padding: 0, gap: 4, maxHeight: 220, overflow: "auto" }}>
          {shownSvcs.map((s) => <li key={s.id}><label className="row" style={{ gap: 8 }}><Checkbox checked={services.includes(s.id)} onChange={(e) => setServices((x) => (e.target.checked ? [...x, s.id] : x.filter((y) => y !== s.id)))} /> {s.name} <small className="sub">{s.categoryName}</small></label></li>)}
        </ul>
      </Panel>
      <Panel title={t("Files")} description={article ? t("Pictures for the text, and policy or SOP documents.") : t("Save the draft first to add files.")}>
        {article && <div className="stack">
          {files.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0 }}>{files.map((x) => {
            const code = x.contentType.startsWith("image/") ? `![${x.fileName}](file:${x.id})` : `[${x.fileName}](${kbFileUrl(article.id)(x.id)})`;
            return (
              <li key={x.id} className="row" style={{ gap: 8, justifyContent: "space-between", flexWrap: "wrap" }}>
                <span><a href={kbFileUrl(article.id)(x.id)}>{x.fileName}</a> <small>({fmtSize(x.size)})</small><br /><code style={{ fontSize: 12 }}>{code}</code></span>
                <span className="row" style={{ gap: 4 }}>
                  <IconButton label={`${t("Copy code for")} ${x.fileName}`} onClick={() => void copy(code)}><Copy size={14} /></IconButton>
                  <IconButton label={`${t("Remove")} ${x.fileName}`} onClick={() => void removeFile(x.id)}><Trash2 size={14} /></IconButton>
                </span>
              </li>);
          })}</ul>}
          {upErr && <div className="form-alert err" role="alert">{upErr}</div>}
          <div><label className="btn sm"><Upload size={14} /> {t("Add a file")}<input type="file" accept={FILE_ACCEPT} className="sr-only" onChange={(e) => void addFile(e.target.files?.[0])} /></label></div>
        </div>}
      </Panel>
      <Field label={t("What changed? (optional)")} htmlFor="kb-note" error={errs.note}><TextInput id="kb-note" maxLength={200} value={f.note} onChange={(e) => set("note", e.target.value)} /></Field>
      <span className="row" style={{ gap: 8 }}>
        <Button variant="primary" type="submit" disabled={busy}>{busy ? t("Saving…") : t("Save draft")}</Button>
        <Button href={article ? `/kb/${article.id}` : "/kb"}>{t("Cancel")}</Button>
      </span>
    </form>
  );
}
