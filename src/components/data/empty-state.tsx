export function EmptyState({ title, description, action }: { title: string; description: React.ReactNode; action?: React.ReactNode }) {
  return (
    <div className="empty">
      <svg viewBox="0 0 200 120" width="190" height="115" aria-hidden>
        <ellipse cx="100" cy="64" rx="70" ry="52" fill="#e9f1ff" />
        <rect x="52" y="22" width="76" height="86" rx="8" fill="#fff" stroke="#c9dbf8" />
        {[40, 54, 68].map((y) => <rect key={y} x="64" y={y} width={y === 68 ? 30 : 52} height="5" rx="2.5" fill="#c9dbf8" />)}
        <circle cx="124" cy="86" r="18" fill="#fff" stroke="#3b6fd8" strokeWidth="5" />
        <line x1="137" y1="99" x2="152" y2="114" stroke="#3b6fd8" strokeWidth="7" strokeLinecap="round" />
      </svg>
      <h3>{title}</h3>
      <p>{description}</p>
      {action}
    </div>
  );
}
