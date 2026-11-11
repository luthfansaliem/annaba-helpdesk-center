import {
  Armchair, BadgeCheck, Bug, CalendarDays, Car, Coffee, DoorOpen, Droplet, IdCard, Mail, MoveRight, Package, PartyPopper, Pencil, Sparkles, SprayCan, Truck, UserPlus, Wind, Wrench, Zap,
  Building2, Boxes, Lightbulb, Printer, Shield, Utensils, Leaf, Briefcase, CircleHelp, type LucideIcon,
} from "lucide-react";

/** The catalog stores an icon name (from the starter catalog or typed by an admin); unknown names fall back to a neutral icon. */
const ICONS: Record<string, LucideIcon> = {
  armchair: Armchair, badge: BadgeCheck, bug: Bug, calendar: CalendarDays, car: Car, coffee: Coffee, "door-open": DoorOpen, droplet: Droplet, "id-card": IdCard, mail: Mail,
  move: MoveRight, package: Package, "party-popper": PartyPopper, pencil: Pencil, sparkles: Sparkles, "spray-can": SprayCan, truck: Truck, "user-plus": UserPlus, wind: Wind, wrench: Wrench, zap: Zap,
  building: Building2, boxes: Boxes, lightbulb: Lightbulb, printer: Printer, shield: Shield, utensils: Utensils, leaf: Leaf, briefcase: Briefcase,
};

/** Soft background / ink pairs; a category keeps its colour everywhere because the index comes from its position in the catalog. */
export const TONES = [
  { bg: "var(--blue-50)", fg: "var(--blue-ink)" }, { bg: "var(--green-50)", fg: "var(--green-ink)" }, { bg: "var(--orange-50)", fg: "var(--orange-ink)" },
  { bg: "var(--red-50)", fg: "var(--red-ink)" }, { bg: "var(--chip)", fg: "var(--ink-strong)" },
];

export function CatalogIcon({ name, size = 18 }: { name: string | null | undefined; size?: number }) {
  const I = (name && ICONS[name.trim().toLowerCase()]) || CircleHelp;
  return <I size={size} aria-hidden />;
}
