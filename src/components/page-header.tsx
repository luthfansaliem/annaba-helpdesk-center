export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: React.ReactNode }) {
  return (
    <div className="crud-top">
      <div><h1>{title}</h1>{description && <p>{description}</p>}</div>
      {actions}
    </div>
  );
}
