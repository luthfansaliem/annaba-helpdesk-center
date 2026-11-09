"use client";

import { useRef } from "react";

/** 6-digit one-time code input with paste support and auto-advance. */
export function OtpInput({ value, onChange, length = 6, invalid, label = "Verification code" }: { value: string; onChange: (v: string) => void; length?: number; invalid?: boolean; label?: string }) {
  const refs = useRef<(HTMLInputElement | null)[]>([]);
  const digits = value.padEnd(length, " ").split("");
  const setAt = (i: number, ch: string) => { const a = digits.slice(); a[i] = ch || " "; onChange(a.join("").replace(/ /g, "").slice(0, length)); };
  return (
    <div className="otp" role="group" aria-label={label}
      onPaste={(e) => { const t = e.clipboardData.getData("text").replace(/\D/g, "").slice(0, length); if (t) { e.preventDefault(); onChange(t); refs.current[Math.min(t.length, length - 1)]?.focus(); } }}>
      {digits.map((d, i) => (
        <input key={i} ref={(el) => { refs.current[i] = el; }} inputMode="numeric" autoComplete={i === 0 ? "one-time-code" : "off"} maxLength={length} aria-label={`Digit ${i + 1}`} aria-invalid={invalid || undefined}
          className={invalid ? "invalid" : ""} value={d.trim()}
          onChange={(e) => {
            const raw = e.target.value.replace(/\D/g, "");
            if (raw.length > 1) { // autofill / fast typing: spread across the boxes from this position
              const merged = (value.slice(0, i) + raw).slice(0, length); onChange(merged); refs.current[Math.min(merged.length, length - 1)]?.focus(); return;
            }
            setAt(i, raw); if (raw && i < length - 1) refs.current[i + 1]?.focus();
          }}
          onKeyDown={(e) => { if (e.key === "Backspace" && !digits[i].trim() && i > 0) refs.current[i - 1]?.focus(); }} />
      ))}
    </div>
  );
}
