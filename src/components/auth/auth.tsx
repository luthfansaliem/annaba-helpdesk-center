"use client";

import { useState } from "react";
import { Check, Eye, EyeOff, Lock } from "lucide-react";

export function FeatureList({ items }: { items: string[] }) {
  return <ul className="checks">{items.map((t) => <li key={t}><span className="ck"><Check size={16} strokeWidth={3} /></span>{t}</li>)}</ul>;
}

export type AppTileData = { name: string; sub: string; icon: React.ReactNode; bg: string; fg: string };

export function AppTiles({ items }: { items: AppTileData[] }) {
  return (
    <div className="apps">
      {items.map((a) => (
        <div key={a.name}><span className="ico" style={{ background: a.bg, color: a.fg }}>{a.icon}</span><strong>{a.name}</strong><small>{a.sub}</small></div>
      ))}
    </div>
  );
}

/** Labelled input row used by the sign-in card (label on top, optional right-aligned link). */
export function AuthField({ id, label, aside, children }: { id: string; label: string; aside?: React.ReactNode; children: React.ReactNode }) {
  return (
    <>
      <label className="l" htmlFor={id}>{label}{aside}</label>
      {children}
    </>
  );
}

export function PasswordInput({ id, ...rest }: { id: string } & Omit<React.InputHTMLAttributes<HTMLInputElement>, "type" | "id">) {
  const [show, setShow] = useState(false);
  return (
    <div className="inp">
      <Lock size={22} />
      <input id={id} type={show ? "text" : "password"} {...rest} />
      <button type="button" aria-label={show ? "Hide password" : "Show password"} onClick={() => setShow(!show)}>{show ? <EyeOff size={22} /> : <Eye size={22} />}</button>
    </div>
  );
}

export function AuthInput({ icon, ...rest }: { icon: React.ReactNode } & React.InputHTMLAttributes<HTMLInputElement>) {
  return <div className="inp">{icon}<input {...rest} /></div>;
}

export function SocialButton({ icon, children, onClick }: { icon: React.ReactNode; children: React.ReactNode; onClick?: () => void }) {
  return <button type="button" onClick={onClick}>{icon}{children}</button>;
}
