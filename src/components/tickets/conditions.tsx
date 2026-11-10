"use client";

import { Field, SelectInput } from "@/components/ui/field";
import { get } from "@/lib/api/client";
import type { CalendarItem, CategoryItem, LocationItem, Priority, ServiceListItem, TeamItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

export type ConditionValue = { serviceId: string | null; categoryId: string | null; locationId: string | null; priority: Priority | null };

/** Everything the rule and policy screens pick from. A person who may manage rules but not read (say) teams just gets an empty list there. */
export function useLookups() {
  const safe = <T,>(path: string) => get<T[]>(path).catch(() => [] as T[]);
  const services = useQuery(() => safe<ServiceListItem>("/api/catalog/services"), []);
  const categories = useQuery(() => safe<CategoryItem>("/api/catalog/categories"), []);
  const locations = useQuery(() => safe<LocationItem>("/api/locations"), []);
  const calendars = useQuery(() => safe<CalendarItem>("/api/calendars"), []);
  const teams = useQuery(() => safe<TeamItem>("/api/teams"), []);
  return { services: services.data ?? [], categories: categories.data ?? [], locations: locations.data ?? [], calendars: calendars.data ?? [], teams: teams.data ?? [] };
}
export type Lookups = ReturnType<typeof useLookups>;

const PRIORITIES: { value: Priority; label: () => string }[] = [
  { value: "urgent", label: () => t("Urgent") }, { value: "high", label: () => t("High") }, { value: "medium", label: () => t("Medium") }, { value: "low", label: () => t("Low") },
];

/** The "when does this apply" part, shared by SLA policies and assignment rules. Leaving a field empty means "any". */
export function ConditionFields({ value, onChange, lookups, errors = {}, idPrefix }: { value: ConditionValue; onChange: (v: ConditionValue) => void; lookups: Lookups; errors?: Record<string, string>; idPrefix: string }) {
  const set = <K extends keyof ConditionValue>(k: K, v: ConditionValue[K]) => onChange({ ...value, [k]: v });
  const any = [{ value: "", label: t("Any") }];
  return (
    <>
      <Field label={t("Service")} htmlFor={`${idPrefix}-svc`} error={errors.serviceId}>
        <SelectInput id={`${idPrefix}-svc`} value={value.serviceId ?? ""} onChange={(e) => set("serviceId", e.target.value || null)} options={[...any, ...lookups.services.filter((s) => !s.archived).map((s) => ({ value: s.id, label: `${s.categoryName} › ${s.name}` }))]} />
      </Field>
      <Field label={t("Category")} htmlFor={`${idPrefix}-cat`} error={errors.categoryId}>
        <SelectInput id={`${idPrefix}-cat`} value={value.categoryId ?? ""} onChange={(e) => set("categoryId", e.target.value || null)} options={[...any, ...lookups.categories.filter((c) => !c.archived).map((c) => ({ value: c.id, label: c.name }))]} />
      </Field>
      <Field label={t("Place (and everything inside it)")} htmlFor={`${idPrefix}-loc`} error={errors.locationId}>
        <SelectInput id={`${idPrefix}-loc`} value={value.locationId ?? ""} onChange={(e) => set("locationId", e.target.value || null)} options={[...any, ...lookups.locations.filter((l) => !l.archived).map((l) => ({ value: l.id, label: l.name }))]} />
      </Field>
      <Field label={t("Priority")} htmlFor={`${idPrefix}-prio`} error={errors.priority}>
        <SelectInput id={`${idPrefix}-prio`} value={value.priority ?? ""} onChange={(e) => set("priority", (e.target.value || null) as Priority | null)} options={[...any, ...PRIORITIES.map((p) => ({ value: p.value, label: p.label() }))]} />
      </Field>
    </>
  );
}

/** "Service X · Priority Urgent", or "Everything" when nothing narrows it down. */
export function describeConditions(i: { serviceName: string | null; categoryName: string | null; locationName: string | null; priority: Priority | null }): string {
  const parts = [i.serviceName, i.categoryName && `${t("Category")}: ${i.categoryName}`, i.locationName && `${t("Place")}: ${i.locationName}`, i.priority && `${t("Priority")}: ${{ urgent: t("Urgent"), high: t("High"), medium: t("Medium"), low: t("Low") }[i.priority]}`].filter(Boolean);
  return parts.length ? parts.join(" · ") : t("Everything");
}

/** Move one item up or down in a list of ids (the server stores the whole order). */
export function moved(ids: string[], id: string, by: -1 | 1): string[] | null {
  const i = ids.indexOf(id); const j = i + by;
  if (i < 0 || j < 0 || j >= ids.length) return null;
  const next = [...ids]; [next[i], next[j]] = [next[j], next[i]];
  return next;
}
