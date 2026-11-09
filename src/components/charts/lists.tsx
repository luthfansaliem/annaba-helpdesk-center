import { Avatar } from "@/components/ui/avatar";

export type ActivityItem = { title: string; meta: string; icon: React.ReactNode; bg: string; color: string };

export function ActivityList({ items }: { items: ActivityItem[] }) {
  return (
    <ul className="act-list">
      {items.map((a) => (
        <li className="act" key={a.title}>
          <span className="aic" style={{ background: a.bg, color: a.color }}>{a.icon}</span>
          <div><strong>{a.title}</strong><small>{a.meta}</small></div>
        </li>
      ))}
    </ul>
  );
}

export function ProgressBar({ value, color }: { value: number; color: string }) {
  return <div className="bar" role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={100}><i style={{ width: value + "%", background: color }} /></div>;
}

export type ProjectItem = { name: string; sub: string; due: string; progress: number; color: string; icon: React.ReactNode };

export function ProjectList({ items }: { items: ProjectItem[] }) {
  return (
    <ul className="proj-list">
      {items.map((p) => (
        <li className="proj" key={p.name}>
          <span className="pi" style={{ background: p.color }}>{p.icon}</span>
          <div><strong>{p.name}</strong><small>{p.sub}</small></div>
          <span className="due">{p.due}</span>
          <ProgressBar value={p.progress} color={p.color} />
        </li>
      ))}
    </ul>
  );
}

export function PersonCell({ name, avatarIndex }: { name: string; avatarIndex?: number }) {
  return <div className="who"><Avatar name={name} size={26} i={avatarIndex} />{name}</div>;
}
