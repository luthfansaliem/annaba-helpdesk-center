import {
  LayoutDashboard, Users, UserRound, UsersRound, MailPlus, ShieldCheck, KeyRound, Grid3x3, MonitorSmartphone, ScrollText, ShieldAlert,
  Settings, SlidersHorizontal, Fingerprint, BarChart3, Mail, KeySquare, ClipboardCheck, Building2, UserX, type LucideIcon,
} from "lucide-react";

export type NavLeaf = { label: string; href: string; icon: LucideIcon; perm?: string };
export type NavGroup = { title: string; icon: LucideIcon; children: (NavLeaf | NavGroup)[] };
export type NavNode = NavLeaf | NavGroup;
export const isGroup = (n: NavNode): n is NavGroup => "children" in n;

/** Add User is intentionally NOT here: it is the primary action on All Users (NAV-U1). */
export const nav: NavNode[] = [
  { label: "Dashboard", href: "/dashboard", icon: LayoutDashboard },
  { title: "Identity & Access", icon: Fingerprint, children: [
    { title: "Users", icon: Users, children: [
      { label: "Overview", href: "/users", icon: BarChart3, perm: "users.read" },
      { label: "All Users", href: "/users/all", icon: UsersRound, perm: "users.read" },
      { label: "Invitations", href: "/users/invitations", icon: MailPlus, perm: "invitations.read" },
    ] },
    { title: "Roles & Permissions", icon: ShieldCheck, children: [
      { label: "Roles", href: "/roles", icon: ShieldCheck, perm: "roles.read" },
      { label: "Permissions", href: "/permissions", icon: KeyRound, perm: "permissions.read" },
      { label: "Access Matrix", href: "/access-matrix", icon: Grid3x3, perm: "permissions.read" },
    ] },
    { label: "Access Requests", href: "/access-requests", icon: KeySquare },
    { label: "Access Reviews", href: "/access-reviews", icon: ClipboardCheck, perm: "users.assign_role" },
    { label: "Account Closures", href: "/account-closures", icon: UserX, perm: "users.suspend" },
    { label: "Sessions & Devices", href: "/sessions", icon: MonitorSmartphone },
  ] },
  { title: "Monitoring", icon: ScrollText, children: [
    { label: "Audit Logs", href: "/audit", icon: ScrollText, perm: "audit.read" },
    { label: "Security Events", href: "/security-events", icon: ShieldAlert, perm: "security.read" },
  ] },
  { title: "Personal", icon: UserRound, children: [
    { label: "My Profile", href: "/profile", icon: UserRound },
    { label: "Settings", href: "/settings", icon: Settings },
  ] },
  { title: "Administration", icon: SlidersHorizontal, children: [
    { label: "System Settings", href: "/admin/settings", icon: SlidersHorizontal, perm: "settings.system" },
    { label: "Security Policy", href: "/admin/security-policy", icon: ShieldCheck, perm: "settings.tenant" },
    { label: "Email Delivery", href: "/admin/email", icon: Mail, perm: "settings.manage" },
    { label: "Tenants", href: "/admin/tenants", icon: Building2, perm: "tenants.manage" },
  ] },
];

/** Filter by permission; drop groups that end up empty. */
export function visibleNav(nodes: NavNode[], can: (p: string) => boolean): NavNode[] {
  return nodes.flatMap((n): NavNode[] => {
    if (!isGroup(n)) return !n.perm || can(n.perm) ? [n] : [];
    const kids = visibleNav(n.children, can);
    return kids.length ? [{ ...n, children: kids }] : [];
  });
}
export function leaves(nodes: NavNode[]): NavLeaf[] { return nodes.flatMap((n) => (isGroup(n) ? leaves(n.children) : [n])); }
export function activeTrail(nodes: NavNode[], path: string, trail: string[] = []): string[] | null {
  for (const n of nodes) {
    if (isGroup(n)) { const r = activeTrail(n.children, path, [...trail, n.title]); if (r) return r; }
    else if (path === n.href || (n.href !== "/dashboard" && path.startsWith(n.href + "/") && !leavesMatchBetter(nodes, path, n))) return trail;
  }
  return null;
}
function leavesMatchBetter(siblings: NavNode[], path: string, current: NavLeaf) {
  return leaves(siblings).some((l) => l.href !== current.href && l.href.length > current.href.length && (path === l.href || path.startsWith(l.href + "/")));
}
