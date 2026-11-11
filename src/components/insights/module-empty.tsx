/** What an empty module shows: what it is for, in a sentence, and the first thing to do. */
export function ModuleEmpty({ icon, title, text, children }: { icon: React.ReactNode; title: string; text: string; children?: React.ReactNode }) {
  return (
    <div className="m-empty">
      <span className="ic" aria-hidden>{icon}</span>
      <h3>{title}</h3>
      <p>{text}</p>
      {children && <div className="row">{children}</div>}
    </div>
  );
}
