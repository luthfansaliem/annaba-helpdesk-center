import Link from "next/link";
import { cn } from "@/lib/cn";

export type ButtonVariant = "primary" | "outline" | "soft" | "danger" | "danger-outline";
export type ButtonSize = "md" | "sm";

const variantClass: Record<ButtonVariant, string> = {
  primary: "primary",
  outline: "",
  soft: "soft",
  danger: "danger",
  "danger-outline": "danger-o",
};

type Common = { variant?: ButtonVariant; size?: ButtonSize; className?: string; children: React.ReactNode };
type AsButton = Common & Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, keyof Common> & { href?: undefined };
type AsLink = Common & { href: string } & Omit<React.AnchorHTMLAttributes<HTMLAnchorElement>, keyof Common | "href">;

export function Button(props: AsButton | AsLink) {
  const { variant = "outline", size = "md", className, children, ...rest } = props;
  const cls = cn("btn", variantClass[variant], size === "sm" && "sm", className);
  if ("href" in rest && rest.href !== undefined) {
    return <Link {...(rest as Omit<AsLink, keyof Common>)} className={cls}>{children}</Link>;
  }
  const { type = "button", ...btn } = rest as Omit<AsButton, keyof Common>;
  return <button type={type} {...btn} className={cls}>{children}</button>;
}

/** Small square icon-only button (used for row actions, dialog close, etc.). */
export function IconButton({ label, className, children, ...rest }: { label: string } & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button type="button" aria-label={label} title={label} className={cn("icon-btn", className)} {...rest}>{children}</button>;
}
