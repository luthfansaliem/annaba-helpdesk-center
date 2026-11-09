"use client";

import { useEffect, useRef, useState } from "react";
import { MoreHorizontal } from "lucide-react";

export type MenuItem = { label: string; onSelect: () => void; danger?: boolean; hidden?: boolean; disabled?: boolean };

/** Kebab menu with outside-click, Esc and arrow-key support. */
export function RowMenu({ label, items }: { label: string; items: MenuItem[] }) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const visible = items.filter((i) => !i.hidden);
  useEffect(() => {
    if (!open) return;
    const click = (e: MouseEvent) => { if (!root.current?.contains(e.target as Node)) setOpen(false); };
    const key = (e: KeyboardEvent) => {
      if (e.key === "Escape") { setOpen(false); root.current?.querySelector<HTMLElement>("button")?.focus(); }
      if (e.key === "ArrowDown" || e.key === "ArrowUp") {
        e.preventDefault();
        const els = [...root.current!.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled])')];
        const i = els.indexOf(document.activeElement as HTMLElement);
        els[(i + (e.key === "ArrowDown" ? 1 : -1) + els.length) % els.length]?.focus();
      }
    };
    document.addEventListener("mousedown", click); document.addEventListener("keydown", key);
    root.current?.querySelector<HTMLElement>('[role="menuitem"]')?.focus();
    return () => { document.removeEventListener("mousedown", click); document.removeEventListener("keydown", key); };
  }, [open]);
  if (!visible.length) return null;
  return (
    <div className="row-menu" ref={root}>
      <button type="button" className="icon-btn" aria-label={label} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}><MoreHorizontal size={14} /></button>
      {open && (
        <div className="menu" role="menu">
          {visible.map((i) => <button key={i.label} role="menuitem" type="button" disabled={i.disabled} className={i.danger ? "danger" : ""} onClick={() => { setOpen(false); i.onSelect(); }}>{i.label}</button>)}
        </div>
      )}
    </div>
  );
}
