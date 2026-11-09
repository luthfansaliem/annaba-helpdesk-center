import { cn } from "@/lib/cn";

export function Card({ className, children, ...rest }: React.HTMLAttributes<HTMLElement> & { as?: "section" | "div" }) {
  return <section className={cn("card", className)} {...rest}>{children}</section>;
}

/** Padded card with an optional heading. */
export function Panel({ title, description, actions, className, children, ...rest }: {
  title?: React.ReactNode; description?: React.ReactNode; actions?: React.ReactNode;
} & Omit<React.HTMLAttributes<HTMLElement>, "title">) {
  return (
    <Card className={cn("panel", className)} {...rest}>
      {(title || actions) && (
        <div className="panel-head">
          <div>
            {title && <h2 className="sec-t">{title}</h2>}
            {description && <p className="panel-desc">{description}</p>}
          </div>
          {actions && <div className="panel-actions">{actions}</div>}
        </div>
      )}
      {children}
    </Card>
  );
}

/** Heading row with an icon chip, title/subtitle and a trailing slot ("View all", range switch…). */
export function PanelHeader({ icon, title, subtitle, trailing, plainIcon }: {
  icon?: React.ReactNode; title: string; subtitle?: string; trailing?: React.ReactNode; plainIcon?: boolean;
}) {
  return (
    <div className="ph">
      {icon && <span className="pic" style={plainIcon ? { background: "none" } : undefined}>{icon}</span>}
      <div><h3>{title}</h3>{subtitle && <p>{subtitle}</p>}</div>
      {trailing}
    </div>
  );
}
