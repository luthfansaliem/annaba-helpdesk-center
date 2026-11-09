"use client";

import { useAuth } from "@/lib/auth";
import type { RoleListItem } from "@/lib/api/types";
import { t } from "@/i18n";

/** Checkbox list of roles. Roles whose permissions exceed the signed-in user's own are disabled (the server enforces the same rule). */
export function RolePicker({ roles, value, onChange, idPrefix = "role" }: { roles: RoleListItem[]; value: string[]; onChange: (v: string[]) => void; idPrefix?: string }) {
  const { user } = useAuth();
  const mine = new Set(user?.permissions ?? []);
  return (
    <div className="role-picker" role="group" aria-label={t("Roles")}>
      {roles.filter((r) => !r.archived).map((r) => {
        const grantable = r.permissions.every((p) => mine.has(p));
        const selected = value.includes(r.id);
        return (
          <label key={r.id} className={"role-opt" + (grantable || selected ? "" : " off")} title={grantable ? undefined : t("You can't grant a role with permissions you don't hold")}>
            <input id={`${idPrefix}-${r.key}`} type="checkbox" className="cb" disabled={!grantable && !selected} checked={selected} onChange={(e) => onChange(e.target.checked ? [...value, r.id] : value.filter((x) => x !== r.id))} />
            <span><b>{r.name}</b><small>{t(r.description)}</small></span>
          </label>
        );
      })}
    </div>
  );
}
