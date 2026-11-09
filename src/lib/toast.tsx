"use client";

import { createContext, useCallback, useContext, useMemo, useState } from "react";
import { Toast, type ToastTone } from "@/components/ui/toast";

type Ctx = { notify: (tone: ToastTone, text: string) => void };
const C = createContext<Ctx | null>(null);

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [items, setItems] = useState<{ id: number; tone: ToastTone; text: string }[]>([]);
  const notify = useCallback((tone: ToastTone, text: string) => {
    const id = Date.now() + Math.random();
    setItems((t) => [...t.slice(-3), { id, tone, text }]);
    setTimeout(() => setItems((t) => t.filter((x) => x.id !== id)), tone === "error" ? 8000 : 5000);
  }, []);
  const value = useMemo(() => ({ notify }), [notify]);
  return (
    <C.Provider value={value}>
      {children}
      <div className="toast-stack" aria-live="polite">
        {items.map((t) => <Toast key={t.id} tone={t.tone} onDismiss={() => setItems((x) => x.filter((y) => y.id !== t.id))}>{t.text}</Toast>)}
      </div>
    </C.Provider>
  );
}

export function useToast() {
  const c = useContext(C);
  if (!c) throw new Error("useToast must be used inside <ToastProvider>");
  return c;
}
