import { CircleHelp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

/** `help` is the id of a topic in the in-app help (src/content/help.ts): the page gets a "Help" button that opens that topic. */
export function PageHeader({ title, description, actions, help }: { title: string; description?: string; actions?: React.ReactNode; help?: string }) {
  return (
    <div className="crud-top">
      <div><h1>{title}</h1>{description && <p>{description}</p>}</div>
      {(actions || help) && (
        <span className="row" style={{ gap: 8, alignItems: "center" }}>
          {help && <Button size="sm" href={`/help?topic=${help}`} aria-label={`${t("Help")}: ${title}`}><CircleHelp size={14} aria-hidden /> {t("Help")}</Button>}
          {actions}
        </span>
      )}
    </div>
  );
}
