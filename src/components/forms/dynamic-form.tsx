"use client";

import { useEffect, useState } from "react";
import { Checkbox, Field, SelectInput, TextInput, type Option } from "@/components/ui/field";
import { X } from "lucide-react";
import type { FormField, UploadedFile } from "@/lib/api/types";
import { messageOf } from "@/lib/api/client";
import { FILE_ACCEPT, MAX_FILE_BYTES, fmtSize } from "@/lib/files";
import { isVisible } from "@/lib/forms";
import { t } from "@/i18n";

export type FormValues = Record<string, unknown>;

type Opt = { value: string; label: string };

/** Pick one person by searching: the directory is never listed, only matches for what was typed. */
export function PersonPicker({ id, value, onChange, search, describedBy }: { id: string; value: string; onChange: (v: string | undefined) => void; search: (q: string) => Promise<Opt[]>; describedBy?: string }) {
  const [q, setQ] = useState(""); const [found, setFound] = useState<Opt[]>([]); const [picked, setPicked] = useState<Opt | null>(null);
  const term = q.trim();
  useEffect(() => {
    if (term.length < 2) return;
    let live = true;
    const h = setTimeout(() => { search(term).then((r) => { if (live) setFound(r); }).catch(() => { if (live) setFound([]); }); }, 250);
    return () => { live = false; clearTimeout(h); };
  }, [term, search]);
  const shown = term.length < 2 ? [] : found;
  const options = [...(picked && !shown.some((o) => o.value === picked.value) ? [picked] : []), ...shown];
  return (
    <div className="stack" style={{ gap: 6 }}>
      <TextInput aria-label={t("Search people")} placeholder={t("Type a name to search")} value={q} onChange={(e) => setQ(e.target.value)} />
      <SelectInput id={id} value={value} placeholder={t("Choose…")} options={options} aria-describedby={describedBy}
        onChange={(e) => { const o = options.find((x) => x.value === e.target.value) ?? null; setPicked(o); onChange(e.target.value || undefined); }} />
    </div>
  );
}

/** Files for a question: each is uploaded as soon as it is chosen; the answer is the list of upload ids, which the ticket claims when the request is sent. */
function FileControl({ id, field, ids, known, onPicked, onChange, upload, describedBy }: {
  id: string; field: FormField; ids: string[]; known: Record<string, UploadedFile>; onPicked: (f: UploadedFile) => void; onChange: (ids: string[]) => void;
  upload?: (file: File) => Promise<UploadedFile>; describedBy?: string;
}) {
  const [busy, setBusy] = useState(false); const [problem, setProblem] = useState("");
  const limit = field.max ?? 5;
  async function pick(e: React.ChangeEvent<HTMLInputElement>) {
    const chosen = Array.from(e.target.files ?? []); e.target.value = ""; // so the same file can be chosen again
    if (!upload || chosen.length === 0) return;
    setProblem(""); setBusy(true);
    let next = ids; const issues: string[] = [];
    for (const f of chosen) {
      if (next.length >= limit) { issues.push(t("You can add at most {n} files here.", { n: limit })); break; }
      if (f.size > MAX_FILE_BYTES) { issues.push(`${f.name}: ${t("The file is larger than 8 MB.")}`); continue; }
      try { const up = await upload(f); onPicked(up); next = [...next, up.id]; onChange(next); }
      catch (x) { issues.push(`${f.name}: ${messageOf(x)}`); }
    }
    setBusy(false); setProblem(issues.join(" "));
  }
  return (
    <div className="stack" style={{ gap: 6 }}>
      {upload
        ? <input id={id} type="file" multiple accept={FILE_ACCEPT} onChange={pick} disabled={busy || ids.length >= limit} aria-describedby={describedBy} />
        : <p className="tab-placeholder" id={id}>{t("People will be able to attach files here.")}</p>}
      {busy && <small className="sub" role="status">{t("Uploading…")}</small>}
      {problem && <p className="field-error" role="alert">{problem}</p>}
      {ids.length > 0 && <ul className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 4 }}>
        {ids.map((x) => (
          <li key={x} className="row" style={{ gap: 8 }}>
            <span>{known[x]?.fileName ?? t("File")}{known[x] ? <small className="sub"> · {fmtSize(known[x].size)}</small> : null}</span>
            <button type="button" className="btn sm" aria-label={t("Remove {name}", { name: known[x]?.fileName ?? t("File") })} onClick={() => onChange(ids.filter((i) => i !== x))}><X size={12} /></button>
          </li>
        ))}
      </ul>}
      <small className="sub">{t("Up to {n} files, 8 MB each. Photos, PDF, Office documents, text.", { n: limit })}</small>
    </div>
  );
}

/**
 * Renders a service form from its definition. Questions whose condition isn't met are not shown, exactly as the server will treat them.
 * Places and people come from the caller (`locations`, `people`), so the same component serves the admin preview and the request form.
 */
export function DynamicForm({ fields, values, onChange, errors = {}, locations = [], people = [], searchPeople, upload, idPrefix = "df" }: {
  fields: FormField[]; values: FormValues; onChange: (next: FormValues) => void; errors?: Record<string, string>;
  locations?: Option[]; people?: Option[]; searchPeople?: (q: string) => Promise<Opt[]>; upload?: (file: File) => Promise<UploadedFile>; idPrefix?: string;
}) {
  const [uploaded, setUploaded] = useState<Record<string, UploadedFile>>({});
  const set = (key: string, v: unknown) => onChange({ ...values, [key]: v });
  // a question that is hidden must not keep an old answer
  const visible = fields.filter((f, i) => isVisible(f, Object.fromEntries(fields.slice(0, i).filter((p) => isVisible(p, values)).map((p) => [p.key, values[p.key]]))));

  return (
    <div className="stack">
      {visible.length === 0 && <p className="tab-placeholder">{t("This form has no questions yet.")}</p>}
      {visible.map((f) => {
        const id = `${idPrefix}-${f.key}`; const v = values[f.key]; const err = errors[f.key];
        const label = f.label || f.key;
        const help = f.help ? <small className="sub" id={`${id}-help`}>{f.help}</small> : null;
        const described = f.help ? { "aria-describedby": `${id}-help` } : {};
        switch (f.type) {
          case "long_text":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><textarea id={id} rows={3} className="textarea" maxLength={f.maxLength ?? 4000} value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} {...described} />{help}</Field>;
          case "number":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><TextInput id={id} type="number" min={f.min ?? undefined} max={f.max ?? undefined} value={(v as number | string | undefined) ?? ""} onChange={(e) => set(f.key, e.target.value === "" ? undefined : Number(e.target.value))} {...described} />{help}</Field>;
          case "date":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><TextInput id={id} type="date" value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} {...described} />{help}</Field>;
          case "datetime":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><TextInput id={id} type="datetime-local" value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} {...described} />{help}</Field>;
          case "select":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><SelectInput id={id} value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} placeholder={t("Choose…")} options={f.options ?? []} {...described} />{help}</Field>;
          case "multi_select": {
            const picked = (Array.isArray(v) ? v : []) as string[];
            return (
              <fieldset key={f.key} className="fl" style={{ border: 0, padding: 0, margin: 0 }} {...described}>
                <legend style={{ fontWeight: 700, fontSize: 12, padding: 0 }}>{label}{f.required && <> <em aria-hidden>*</em></>}</legend>
                {(f.options ?? []).map((o) => (
                  <label key={o.value} className="row" style={{ gap: 8, padding: "4px 0" }}>
                    <Checkbox checked={picked.includes(o.value)} onChange={(e) => set(f.key, e.target.checked ? [...picked, o.value] : picked.filter((x) => x !== o.value))} /> {o.label}
                  </label>
                ))}
                {help}{err && <p className="field-error" role="alert">{err}</p>}
              </fieldset>
            );
          }
          case "yes_no":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><SelectInput id={id} value={v === true ? "yes" : v === false ? "no" : ""} onChange={(e) => set(f.key, e.target.value === "" ? undefined : e.target.value === "yes")} placeholder={t("Choose…")} options={[{ value: "yes", label: t("Yes") }, { value: "no", label: t("No") }]} {...described} />{help}</Field>;
          case "location":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><SelectInput id={id} value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} placeholder={t("Choose…")} options={locations} {...described} />{help}</Field>;
          case "user":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}>{searchPeople
              ? <PersonPicker id={id} value={(v as string) ?? ""} onChange={(x) => set(f.key, x)} search={searchPeople} describedBy={f.help ? `${id}-help` : undefined} />
              : <SelectInput id={id} value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} placeholder={t("Choose…")} options={people} {...described} />}{help}</Field>;
          case "file":
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><FileControl id={id} field={f} ids={(Array.isArray(v) ? v : []) as string[]} known={uploaded} onPicked={(u) => setUploaded((m) => ({ ...m, [u.id]: u }))} onChange={(ids) => set(f.key, ids)} upload={upload} describedBy={f.help ? `${id}-help` : undefined} />{help}</Field>;
          default:
            return <Field key={f.key} label={label} htmlFor={id} required={f.required} error={err}><TextInput id={id} maxLength={f.maxLength ?? 200} value={(v as string) ?? ""} onChange={(e) => set(f.key, e.target.value)} {...described} />{help}</Field>;
        }
      })}
    </div>
  );
}
