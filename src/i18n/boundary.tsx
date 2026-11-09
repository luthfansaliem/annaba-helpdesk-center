"use client";

import { Fragment, useEffect } from "react";
import { initLang, setLang, useLang } from "./index";
import { useAuth } from "@/lib/auth";

/**
 * Applies the saved/browser language on first load, follows the signed-in person's own language setting, and re-creates the page tree when the language
 * changes so every string (including ones computed outside React) is rendered again in the new language.
 */
export function LangBoundary({ children }: { children: React.ReactNode }) {
  const lang = useLang(); const { user } = useAuth();
  useEffect(() => { initLang(); }, []);
  useEffect(() => { if (user?.language) setLang(user.language); }, [user?.language]);
  return <Fragment key={lang}>{children}</Fragment>;
}
