
import { useSyncExternalStore } from "react";
import id from "./id";

export type Lang = "en" | "id";
const KEY = "annaba-digital-lang";
const dicts: Record<Lang, Record<string, string>> = { en: {}, id };

let lang: Lang = "en";
const subs = new Set<() => void>();
const subscribe = (f: () => void) => { subs.add(f); return () => { subs.delete(f); }; };

export const getLang = () => lang;
/** Date/number locale that goes with the language (used by lib/format). */
export const formatLocale = () => (lang === "id" ? "id-ID" : "en-GB");

/** Switch language. `persist` also remembers it on this device so sign-in screens open in it. */
export function setLang(next: Lang, persist = true) {
  if (next === lang) return;
  lang = next;
  if (typeof document !== "undefined") document.documentElement.lang = next;
  if (persist && typeof localStorage !== "undefined") localStorage.setItem(KEY, next);
  subs.forEach((f) => f());
}

/** First load: the saved choice, else the browser's language (Indonesian browsers start in Indonesian). */
export function initLang() {
  if (typeof window === "undefined") return;
  const saved = localStorage.getItem(KEY);
  setLang(saved === "id" || saved === "en" ? saved : navigator.language?.toLowerCase().startsWith("id") ? "id" : "en", false);
}

/** Re-renders the caller when the language changes. */
export function useLang(): Lang { return useSyncExternalStore(subscribe, getLang, () => "en" as Lang); }

/**
 * Translate. The English text IS the key, so code reads naturally and a missing translation simply shows English.
 * `{name}` placeholders are filled from `vars`. Safe to call anywhere (components, helpers); components re-render on a language change because the page tree is re-keyed.
 */
export function t(key: string, vars?: Record<string, string | number>): string {
  const s = dicts[lang][key] ?? key;
  return vars ? s.replace(/\{(\w+)\}/g, (_, k: string) => String(vars[k] ?? `{${k}}`)) : s;
}
