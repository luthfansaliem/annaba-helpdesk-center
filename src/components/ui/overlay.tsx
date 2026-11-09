"use client";

import { useEffect, useRef } from "react";
import { X } from "lucide-react";
import { cn } from "@/lib/cn";
import { t } from "@/i18n";

const FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])';

/** Accessible modal/drawer: focus trap, Esc to close, backdrop click, focus restore, body scroll lock. */
export function Overlay({ open, onClose, title, description, side, children, footer, width }: {
  open: boolean; onClose: () => void; title: string; description?: string; side?: "right"; children: React.ReactNode; footer?: React.ReactNode; width?: number;
}) {
  const panel = useRef<HTMLDivElement>(null);
  const prev = useRef<HTMLElement | null>(null);
  // keep the latest onClose without re-running the focus/scroll-lock effect on every parent render (which stole focus while typing)
  const closeRef = useRef(onClose); closeRef.current = onClose;

  useEffect(() => {
    if (!open) return;
    prev.current = document.activeElement as HTMLElement;
    const el = panel.current!;
    const first = el.querySelector<HTMLElement>("[data-autofocus]") ?? el.querySelector<HTMLElement>(FOCUSABLE);
    first?.focus();
    document.body.style.overflow = "hidden";
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") { e.stopPropagation(); closeRef.current(); }
      if (e.key !== "Tab") return;
      const f = [...el.querySelectorAll<HTMLElement>(FOCUSABLE)].filter((x) => x.offsetParent !== null);
      if (!f.length) return;
      const a = f[0], b = f[f.length - 1];
      if (e.shiftKey && document.activeElement === a) { e.preventDefault(); b.focus(); }
      else if (!e.shiftKey && document.activeElement === b) { e.preventDefault(); a.focus(); }
    };
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("keydown", onKey); document.body.style.overflow = ""; prev.current?.focus(); };
  }, [open]);

  if (!open) return null;
  return (
    <div className={cn("overlay", side === "right" && "drawer-wrap")} onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={panel} role="dialog" aria-modal="true" aria-label={title} className={cn(side === "right" ? "drawer" : "card dialog wide")} style={width ? { width: `min(100%, ${width}px)` } : undefined}>
        <div className="ov-head">
          <div><h2>{title}</h2>{description && <p>{description}</p>}</div>
          <button type="button" className="icon-btn" aria-label={t("Close")} onClick={onClose}><X size={16} /></button>
        </div>
        <div className="ov-body">{children}</div>
        {footer && <div className="ov-foot">{footer}</div>}
      </div>
    </div>
  );
}
