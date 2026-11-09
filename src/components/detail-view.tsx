import { Avatar } from "@/components/ui/avatar";

export function ProfileHeader({ name, avatarIndex, badges, subtitle }: { name: string; avatarIndex?: number; badges?: React.ReactNode; subtitle?: string }) {
  return (
    <div className="profile">
      <Avatar name={name} size={50} i={avatarIndex} />
      <div><h4>{name}</h4><div className="badges">{badges}</div>{subtitle && <small>{subtitle}</small>}</div>
    </div>
  );
}

export function InfoGrid({ items }: { items: { label: string; value: string }[] }) {
  return <dl className="info">{items.map((i) => <div key={i.label}><dt>{i.label}</dt><dd>{i.value}</dd></div>)}</dl>;
}
