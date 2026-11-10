"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import type { ConditionOp, FieldType, FormField } from "@/lib/api/types";
import { FIELD_TYPES, OPS, isChoice, opLabel, optionsText, parseOptions, slugKey, typeLabel, uniqueKey } from "@/lib/forms";
import { t } from "@/i18n";

const KEY = /^[a-z][a-z0-9_]{0,39}$/;

/** Edits one question of a form definition. `types` limits the kinds of question offered (asset types ask simpler questions than service forms). */
export function FieldDrawer({ open, onClose, onSave, initial, others, isNew, types = FIELD_TYPES }: { open: boolean; onClose: () => void; onSave: (f: FormField) => void; initial: FormField; others: FormField[]; isNew: boolean; types?: readonly FieldType[] }) {
  const [f, setF] = useState<FormField>(initial); const [opts, setOpts] = useState(optionsText(initial.options)); const [keyTouched, setKeyTouched] = useState(false);
  const [vals, setVals] = useState((initial.visibleWhen?.values ?? []).join(", ")); const [errs, setErrs] = useState<Record<string, string>>({}); const [seed, setSeed] = useState<FormField | null>(null);
  if (open && seed !== initial) { // load when opened for another question
    setSeed(initial); setF(initial); setOpts(optionsText(initial.options)); setKeyTouched(!isNew); setVals((initial.visibleWhen?.values ?? []).join(", ")); setErrs({});
  }
  const set = <K extends keyof FormField>(k: K, v: FormField[K]) => setF((x) => ({ ...x, [k]: v }));
  const cond = f.visibleWhen ?? null;
  const source = cond ? others.find((o) => o.key === cond.field) : undefined;
  const needsValue = cond && (cond.op === "equals" || cond.op === "not_equals" || cond.op === "in");

  function submit(e: React.FormEvent) {
    e.preventDefault();
    const out: FormField = { ...f, label: f.label.trim(), key: f.key.trim() };
    const errors: Record<string, string> = {};
    if (!out.label) errors.label = t("Write the question people will see.");
    if (!KEY.test(out.key)) errors.key = t("Use lower-case letters, digits and underscores, starting with a letter.");
    else if (others.some((o) => o.key === out.key)) errors.key = t("Another question already uses this key.");
    if (isChoice(out.type)) { out.options = parseOptions(opts); if (out.options.length === 0) errors.options = t("Give at least one choice."); } else out.options = null;
    if (out.type !== "number") out.min = null;
    if (out.type !== "number" && out.type !== "file") out.max = null;
    if (out.type === "file" && out.max != null && (!Number.isInteger(out.max) || out.max < 1 || out.max > 10)) errors.max = t("Use a whole number from 1 to 10.");
    if (out.type !== "text" && out.type !== "long_text") out.maxLength = null;
    if (out.type !== "text") out.pattern = null;
    out.help = out.help?.trim() || null; out.pattern = out.pattern?.trim() || null;
    if (cond) {
      const values = needsValue ? (cond.op === "in" ? vals.split(",") : [vals]).map((x) => x.trim()).filter(Boolean) : [];
      if (!cond.field) errors.condition = t("Choose the question this depends on.");
      else if (needsValue && values.length === 0) errors.condition = t("Give the answer that shows this question.");
      out.visibleWhen = { field: cond.field, op: cond.op, ...(values.length ? { values } : {}) };
    } else out.visibleWhen = null;
    setErrs(errors);
    if (Object.keys(errors).length === 0) { onSave(out); onClose(); }
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" title={isNew ? t("Add question") : t("Edit question")} description={t("People see the label; the key identifies the answer in reports and automations.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="field-form">{t("Done")}</Button></>}>
      <form id="field-form" className="stack" noValidate onSubmit={submit}>
        <Field label={t("Question")} htmlFor="fd-label" required error={errs.label}>
          <TextInput id="fd-label" data-autofocus value={f.label} onChange={(e) => { const label = e.target.value; setF((x) => ({ ...x, label, key: keyTouched ? x.key : uniqueKey(slugKey(label), others.map((o) => o.key)) })); }} />
        </Field>
        <Field label={t("Type")} htmlFor="fd-type" required>
          <SelectInput id="fd-type" value={f.type} onChange={(e) => set("type", e.target.value as FieldType)} options={types.map((x) => ({ value: x, label: typeLabel(x) }))} />
        </Field>
        <Field label={t("Key")} htmlFor="fd-key" required error={errs.key}><TextInput id="fd-key" value={f.key} onChange={(e) => { setKeyTouched(true); set("key", e.target.value); }} /></Field>
        <Field label={t("Help text")} htmlFor="fd-help"><TextInput id="fd-help" value={f.help ?? ""} onChange={(e) => set("help", e.target.value)} /></Field>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={!!f.required} onChange={(e) => set("required", e.target.checked)} /> {t("Required")}</label>

        {isChoice(f.type) && <Field label={t("Choices")} htmlFor="fd-opts" required error={errs.options}>
          <textarea id="fd-opts" rows={5} className="textarea" value={opts} onChange={(e) => setOpts(e.target.value)} aria-describedby="fd-opts-help" />
          <small className="sub" id="fd-opts-help">{t("One per line. Write “value = Label” to set the stored value, or just the label.")}</small>
        </Field>}
        {f.type === "number" && <div className="row" style={{ gap: 12 }}>
          <Field label={t("Minimum")} htmlFor="fd-min"><TextInput id="fd-min" type="number" value={f.min ?? ""} onChange={(e) => set("min", e.target.value === "" ? null : Number(e.target.value))} /></Field>
          <Field label={t("Maximum")} htmlFor="fd-max"><TextInput id="fd-max" type="number" value={f.max ?? ""} onChange={(e) => set("max", e.target.value === "" ? null : Number(e.target.value))} /></Field>
        </div>}
        {f.type === "file" && <Field label={t("Most files per request")} htmlFor="fd-files" error={errs.max}><TextInput id="fd-files" type="number" min={1} max={10} placeholder="5" value={f.max ?? ""} onChange={(e) => set("max", e.target.value === "" ? null : Number(e.target.value))} /></Field>}
        {(f.type === "text" || f.type === "long_text") && <Field label={t("Longest answer (characters)")} htmlFor="fd-len"><TextInput id="fd-len" type="number" min={1} max={4000} value={f.maxLength ?? ""} onChange={(e) => set("maxLength", e.target.value === "" ? null : Number(e.target.value))} /></Field>}
        {f.type === "text" && <Field label={t("Required format (pattern)")} htmlFor="fd-pat"><TextInput id="fd-pat" placeholder="^[A-Z]{3}-\d+$" value={f.pattern ?? ""} onChange={(e) => set("pattern", e.target.value)} /></Field>}

        {others.length > 0 && <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }}>
          <legend style={{ fontWeight: 700, fontSize: 12, padding: 0 }}>{t("Show this question")}</legend>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={!cond} onChange={(e) => set("visibleWhen", e.target.checked ? null : { field: others[others.length - 1].key, op: "filled" })} /> {t("Always")}</label>
          {cond && <div className="stack" style={{ marginTop: 8 }}>
            <Field label={t("When the question")} htmlFor="fd-cf"><SelectInput id="fd-cf" value={cond.field} onChange={(e) => { setVals(""); set("visibleWhen", { field: e.target.value, op: cond.op === "in" && !isChoice(others.find((o) => o.key === e.target.value)?.type ?? "text") ? "equals" : cond.op }); }} options={others.map((o) => ({ value: o.key, label: o.label || o.key }))} /></Field>
            <Field label={t("Condition")} htmlFor="fd-co"><SelectInput id="fd-co" value={cond.op} onChange={(e) => set("visibleWhen", { field: cond.field, op: e.target.value as ConditionOp })} options={OPS.map((o) => ({ value: o, label: opLabel(o) }))} /></Field>
            {needsValue && (source?.type === "yes_no"
              ? <Field label={t("Answer")} htmlFor="fd-cv"><SelectInput id="fd-cv" value={vals} onChange={(e) => setVals(e.target.value)} placeholder={t("Choose…")} options={[{ value: "true", label: t("Yes") }, { value: "false", label: t("No") }]} /></Field>
              : source && isChoice(source.type) && cond.op !== "in"
                ? <Field label={t("Answer")} htmlFor="fd-cv"><SelectInput id="fd-cv" value={vals} onChange={(e) => setVals(e.target.value)} placeholder={t("Choose…")} options={(source.options ?? []).map((o) => ({ value: o.value, label: o.label }))} /></Field>
                : <Field label={cond.op === "in" ? t("Answers (comma separated)") : t("Answer")} htmlFor="fd-cv"><TextInput id="fd-cv" value={vals} onChange={(e) => setVals(e.target.value)} /></Field>)}
            {errs.condition && <p className="field-error" role="alert">{errs.condition}</p>}
          </div>}
        </fieldset>}
      </form>
    </Overlay>
  );
}
