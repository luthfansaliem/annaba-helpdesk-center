import { cn } from "@/lib/cn";

export type BadgeTone = "green" | "orange" | "red" | "gray" | "blue";

export function Badge({ tone = "gray", dot = true, className, children }: { tone?: BadgeTone; dot?: boolean; className?: string; children: React.ReactNode }) {
  return <span className={cn("pill", tone, className)}>{dot && <i />}{children}</span>;
}
