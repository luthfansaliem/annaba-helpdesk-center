
import { t } from "@/i18n";export function scorePassword(p: string): { score: number; label: string; checks: { ok: boolean; text: string }[] } {
  const checks = [
    { ok: p.length >= 12, text: t("At least 12 characters") },
    { ok: /[a-z]/.test(p) && /[A-Z]/.test(p), text: t("Upper and lower case letters") },
    { ok: /\d/.test(p), text: t("A number") },
    { ok: /[^A-Za-z0-9]/.test(p), text: t("A symbol") },
  ];
  const score = checks.filter((c) => c.ok).length + (p.length >= 16 ? 1 : 0);
  return { score, label: ["Too weak", "Weak", "Fair", "Good", "Strong", "Excellent"][score] ?? "Strong", checks };
}

export function PasswordStrength({ value }: { value: string }) {
  const { score, label, checks } = scorePassword(value);
  const color = score <= 1 ? "#ef4444" : score <= 2 ? "#f59e0b" : score <= 3 ? "#3b82f6" : "#16a37f";
  return (
    <div className="pw-meter" aria-live="polite">
      <div className="pw-bars">{[0, 1, 2, 3, 4].map((i) => <i key={i} style={{ background: value && i < score ? color : "#e3e9f4" }} />)}</div>
      <small>{value ? label : t("Use 12+ characters, mixed case, a number and a symbol")}</small>
      <ul>{checks.map((c) => <li key={c.text} className={c.ok ? "ok" : ""}>{c.ok ? "✓" : "○"} {c.text}</li>)}</ul>
    </div>
  );
}
