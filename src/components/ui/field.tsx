import { ChevronDown } from "lucide-react";
import { cn } from "@/lib/cn";

export function Field({ label, htmlFor, required, full, error, children }: {
  label: string; htmlFor?: string; required?: boolean; full?: boolean; error?: string; children: React.ReactNode;
}) {
  return (
    <div className={cn("fl", full && "full")}>
      <label htmlFor={htmlFor}>{label}{required && <> <em aria-hidden>*</em></>}</label>
      {children}
      {error && <p className="field-error" role="alert">{error}</p>}
    </div>
  );
}

type InputProps = Omit<React.InputHTMLAttributes<HTMLInputElement>, "prefix"> & { icon?: React.ReactNode; prefix?: string; invalid?: boolean };

export function TextInput({ icon, prefix, invalid, className, ...rest }: InputProps) {
  return (
    <div className={cn("ctl", prefix && "pre", invalid && "invalid", className)}>
      {prefix && <span>{prefix}</span>}
      {icon}
      <input aria-invalid={invalid || undefined} {...rest} />
    </div>
  );
}

export type Option = string | { value: string; label: string };

export function SelectInput({ options, placeholder, dotColor, invalid, className, ...rest }: {
  options: Option[]; placeholder?: string; dotColor?: string; invalid?: boolean;
} & React.SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className={cn("ctl", invalid && "invalid", className)}>
      {dotColor && <i className="dot" style={{ background: dotColor, margin: 0 }} />}
      <select aria-invalid={invalid || undefined} {...rest}>
        {placeholder && <option value="" disabled>{placeholder}</option>}
        {options.map((o) => {
          const v = typeof o === "string" ? { value: o, label: o } : o;
          return <option key={v.value} value={v.value}>{v.label}</option>;
        })}
      </select>
      <ChevronDown size={14} />
    </div>
  );
}

export function Checkbox({ className, ...rest }: Omit<React.InputHTMLAttributes<HTMLInputElement>, "type">) {
  return <input type="checkbox" className={cn("cb", className)} {...rest} />;
}
