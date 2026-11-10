import type { ConditionOp, FieldOption, FieldType, FormField } from "@/lib/api/types";
import { t } from "@/i18n";

export const FIELD_TYPES: FieldType[] = ["text", "long_text", "number", "date", "datetime", "select", "multi_select", "yes_no", "location", "user", "file"];
export const typeLabel = (x: FieldType) => ({
  text: t("Short text"), long_text: t("Long text"), number: t("Number"), date: t("Date"), datetime: t("Date and time"),
  select: t("Choose one"), multi_select: t("Choose several"), yes_no: t("Yes / No"), location: t("Location"), user: t("Person"), file: t("Photos / files"),
})[x];

export const OPS: ConditionOp[] = ["equals", "not_equals", "in", "filled", "empty"];
export const opLabel = (x: ConditionOp) => ({ equals: t("is"), not_equals: t("is not"), in: t("is one of"), filled: t("is answered"), empty: t("is not answered") })[x];

export const isChoice = (type: FieldType) => type === "select" || type === "multi_select";

/** A machine key from a human label: lower-case, underscores, starts with a letter, at most 40 characters. */
export function slugKey(label: string): string {
  const s = label.toLowerCase().normalize("NFKD").replace(/[̀-ͯ]/g, "").replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "").slice(0, 40).replace(/_+$/g, "");
  return /^[a-z]/.test(s) ? s : s ? `q_${s}`.slice(0, 40) : "";
}

/** A key not used yet: `name`, `name_2`, `name_3`… */
export function uniqueKey(base: string, taken: Iterable<string>): string {
  const used = new Set(taken); const root = base || "question";
  if (!used.has(root)) return root;
  for (let i = 2; ; i++) { const k = `${root.slice(0, 36)}_${i}`; if (!used.has(k)) return k; }
}

/** One choice per line. `value = Label` sets the stored value; a bare label gets a value made from it. */
export function parseOptions(text: string): FieldOption[] {
  const out: FieldOption[] = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim(); if (!line) continue;
    const eq = line.indexOf("=");
    const label = (eq > 0 ? line.slice(eq + 1) : line).trim();
    const value = (eq > 0 ? line.slice(0, eq).trim() : uniqueKey(slugKey(label) || "option", out.map((o) => o.value)));
    out.push({ value, label: label || value });
  }
  return out;
}
export const optionsText = (opts?: FieldOption[] | null) => (opts ?? []).map((o) => `${o.value} = ${o.label}`).join("\n");

const texts = (v: unknown): string[] => (v === undefined || v === null || v === "" ? [] : Array.isArray(v) ? v.map(String) : [String(v)]);

/** Mirrors the server's visibility rule so a preview behaves like the real form. The server stays the authority. */
export function isVisible(f: FormField, values: Record<string, unknown>): boolean {
  const c = f.visibleWhen; if (!c) return true;
  const actual = texts(values[c.field]); const wanted = c.values ?? [];
  switch (c.op) {
    case "filled": return actual.length > 0;
    case "empty": return actual.length === 0;
    case "equals": return actual.length > 0 && wanted.length > 0 && actual.includes(wanted[0]);
    case "not_equals": return wanted.length === 0 || !actual.includes(wanted[0]);
    case "in": return actual.some((a) => wanted.includes(a));
    default: return false;
  }
}

/** What to send to the server: only visible, answered questions; date-times become ISO instants with their offset (the server refuses ambiguous local times). */
export function buildAnswers(fields: FormField[], values: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const f of fields) {
    if (!isVisible(f, out)) continue;
    const v = values[f.key];
    if (v === undefined || v === null || v === "" || (Array.isArray(v) && v.length === 0)) continue;
    out[f.key] = f.type === "datetime" && typeof v === "string" ? new Date(v).toISOString() : v;
  }
  return out;
}

/** Turns the server's `fields[2].options` style keys into "Question 3 (Label): message". */
export function describeErrors(errors: Record<string, string>, fields: FormField[]): string[] {
  return Object.entries(errors).map(([k, msg]) => {
    const m = /^fields\[(\d+)\](?:\.(\w+))?$/.exec(k);
    if (!m) return msg;
    const i = Number(m[1]); const label = fields[i]?.label || fields[i]?.key || "";
    return `${t("Question {n}", { n: i + 1 })}${label ? ` (${label})` : ""}: ${msg}`;
  });
}

export const newField = (taken: Iterable<string>): FormField => ({ key: uniqueKey("question", taken), type: "text", label: "", required: false });
