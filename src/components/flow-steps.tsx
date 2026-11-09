import { ChevronRight } from "lucide-react";

export type FlowStep = { icon: React.ReactNode; title: string; description: string; color: string; bg: string };

export function FlowSteps({ steps }: { steps: FlowStep[] }) {
  return (
    <ol className="flow">
      {steps.map((s, i) => (
        <li key={s.title} style={{ display: "contents" }}>
          <div className="step"><span className="fi" style={{ background: s.bg, color: s.color }}>{s.icon}</span><strong>{s.title}</strong>{s.description}</div>
          {i < steps.length - 1 && <ChevronRight className="arrow" size={18} aria-hidden />}
        </li>
      ))}
    </ol>
  );
}
