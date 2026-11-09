"use client";

import { useCallback, useEffect, useState } from "react";

export type ThemeChoice = "system" | "light" | "dark";
const KEY = "annaba-digital-theme";

export function resolveTheme(choice: ThemeChoice): "light" | "dark" {
  if (choice === "system") return typeof window !== "undefined" && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  return choice;
}

/** The person's appearance choice (system / light / dark), kept on this device and applied to <html data-theme>. */
export function useTheme() {
  const [choice, setChoice] = useState<ThemeChoice>("system");
  useEffect(() => { const v = localStorage.getItem(KEY); if (v === "light" || v === "dark" || v === "system") setChoice(v); }, []);
  useEffect(() => {
    const apply = () => { document.documentElement.dataset.theme = resolveTheme(choice); };
    apply();
    if (choice !== "system") return;
    const mq = window.matchMedia("(prefers-color-scheme: dark)"); mq.addEventListener("change", apply);
    return () => mq.removeEventListener("change", apply);
  }, [choice]);
  const set = useCallback((c: ThemeChoice) => { localStorage.setItem(KEY, c); setChoice(c); }, []);
  return { choice, set, resolved: resolveTheme(choice) };
}
