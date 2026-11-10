import {
  LayoutDashboard, Users, UserRound, UsersRound, MailPlus, ShieldCheck, KeyRound, Grid3x3, MonitorSmartphone, ScrollText, ShieldAlert,
  Settings, SlidersHorizontal, Fingerprint, BarChart3, Mail, KeySquare, ClipboardCheck, Building2, UserX, MapPin, CalendarClock, Headset, ListChecks, Timer, GitFork, Gauge, Stamp, BellRing, CheckCheck, Inbox, FilePlus2, ListTodo, LifeBuoy, Ticket, Rocket, Archive, CircleHelp, MailOpen, Boxes, Tags, Wrench, ClipboardList, Truck, CalendarCheck, Smile, BookOpen, Wallet, PiggyBank, Landmark, Plug, type LucideIcon,
} from "lucide-react";

export type NavLeaf = { label: string; href: string; icon: LucideIcon; perm?: string };
export type NavGroup = { title: string; icon: LucideIcon; children: (NavLeaf | NavGroup)[] };
export type NavNode = NavLeaf | NavGroup;
export const isGroup = (n: NavNode): n is NavGroup => "children" in n;

/** Add User is intentionally NOT here: it is the primary action on All Users (NAV-U1). */
export const nav: NavNode[] = [
  { label: "Dashboard", href: "/dashboard", icon: LayoutDashboard },
  { title: "Requests", icon: Inbox, children: [
    { label: "New request", href: "/requests/new", icon: FilePlus2 },
    { label: "My requests", href: "/requests", icon: ListTodo },
    { label: "Knowledge base", href: "/kb", icon: BookOpen },
    { label: "Approvals", href: "/approvals", icon: CheckCheck },
    { label: "Notifications", href: "/notifications", icon: BellRing },
  ] },
  { title: "Helpdesk", icon: LifeBuoy, children: [
    { label: "Ticket Queue", href: "/tickets", icon: Ticket, perm: "tickets.read_team" },
    { label: "Operations", href: "/reports", icon: BarChart3, perm: "tickets.read_team" },
    { label: "SLA Report", href: "/reports/sla", icon: Gauge, perm: "tickets.read_team" },
    { label: "Satisfaction", href: "/reports/csat", icon: Smile, perm: "tickets.read_team" },
    { label: "Work Orders", href: "/work-orders", icon: Wrench, perm: "workorders.read" },
    { label: "Assets", href: "/assets", icon: Boxes, perm: "assets.read" },
    { label: "Vendors", href: "/vendors", icon: Truck, perm: "vendors.read" },
    { label: "Maintenance", href: "/maintenance", icon: CalendarCheck, perm: "pm.read" },
    { label: "Cost Report", href: "/costs", icon: Wallet, perm: "cost.read" },
    { label: "Budgets", href: "/costs/budgets", icon: PiggyBank, perm: "budgets.read" },
    { label: "Cost Centres", href: "/costs/centers", icon: Landmark, perm: "budgets.read" },
  ] },
  { title: "Helpdesk Setup", icon: Headset, children: [
    { label: "Get started", href: "/setup/start", icon: Rocket, perm: "locations.manage" },
    { label: "Locations", href: "/setup/locations", icon: MapPin, perm: "locations.read" },
    { label: "Teams", href: "/setup/teams", icon: UsersRound, perm: "teams.read" },
    { label: "Business Calendars", href: "/setup/calendars", icon: CalendarClock, perm: "calendars.read" },
    { label: "Service Catalog", href: "/setup/catalog", icon: ListChecks, perm: "catalog.read" },
    { label: "SLA Policies", href: "/setup/sla", icon: Timer, perm: "sla.read" },
    { label: "Assignment Rules", href: "/setup/assignment-rules", icon: GitFork, perm: "rules.read" },
    { label: "Approval Policies", href: "/setup/approvals", icon: Stamp, perm: "approvals.read" },
    { label: "Asset Types", href: "/setup/asset-types", icon: Tags, perm: "assets.read" },
    { label: "Checklists", href: "/setup/checklists", icon: ClipboardList, perm: "workorders.read" },
  ] },
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
    { label: "Help", href: "/help", icon: CircleHelp },
  ] },
  { title: "Administration", icon: SlidersHorizontal, children: [
    { label: "System Settings", href: "/admin/settings", icon: SlidersHorizontal, perm: "settings.system" },
    { label: "Security Policy", href: "/admin/security-policy", icon: ShieldCheck, perm: "settings.tenant" },
    { label: "Data Retention", href: "/admin/retention", icon: Archive, perm: "retention.manage" },
    { label: "Email Delivery", href: "/admin/email", icon: Mail, perm: "settings.manage" },
    { label: "Email Wording", href: "/admin/email-wording", icon: MailOpen, perm: "settings.manage" },
    { label: "Email to Ticket", href: "/admin/inbound-email", icon: Inbox, perm: "settings.manage" },
    { label: "Integrations", href: "/admin/integrations", icon: Plug, perm: "integrations.manage" },
    { label: "Tenants", href: "/admin/tenants", icon: Building2, perm: "tenants.manage" },
  ] },
];

/** Filter by permission; drop groups that end up empty. */
/** R2-11: what a vendor's person sees — their company's work orders and their notifications, nothing else of the organisation. */
export const vendorNav: NavNode[] = [
  { label: "My work orders", href: "/work-orders", icon: Wrench },
  { label: "Notifications", href: "/notifications", icon: BellRing },
];

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
