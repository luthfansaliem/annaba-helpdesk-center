"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { Bell, BellRing, ChevronDown, ChevronUp, ChevronsLeft, ChevronsRight, Lock, LogOut, Menu, Moon, Search, Sun, UserRound, X } from "lucide-react";
import { Logo, LogoMark } from "./logo";
import { Avatar, avatarUrl } from "./ui/avatar";
import { Overlay } from "./ui/overlay";
import { cn } from "@/lib/cn";
import { activeTrail, isGroup, leaves, nav, vendorNav, visibleNav, type NavNode } from "@/config/nav";
import { useAuth } from "@/lib/auth";
import { get, post } from "@/lib/api/client";
import type { Paged, UserListItem } from "@/lib/api/types";
import { useDebounced } from "@/lib/hooks";
import { useTheme } from "@/lib/theme";
import { t } from "@/i18n";

/** Details pages keep their parent highlighted. */
export const activeHref = (p: string) => (/^\/users\/(?!all$|invitations$)[^/]+/.test(p) ? "/users/all" : p.startsWith("/roles/") ? "/roles" : p);

const badgeSum = (nodes: NavNode[], badges: Record<string, number>): number => nodes.reduce((t, n) => t + (isGroup(n) ? badgeSum(n.children, badges) : badges[n.href] ?? 0), 0);

function NavTree({ nodes, path, open, toggle, depth = 0, onNavigate, badges = {} }: { nodes: NavNode[]; path: string; open: Record<string, boolean>; toggle: (k: string) => void; depth?: number; onNavigate: () => void; badges?: Record<string, number> }) {
  return (
    <>
      {nodes.map((n) => {
        if (!isGroup(n)) {
          const active = path === n.href;
          return (
            <Link key={n.href} href={n.href} onClick={onNavigate} className={cn("nav-item", active && "active", depth > 0 && "indent")} aria-current={active ? "page" : undefined}>
              <n.icon size={16} /> <span>{t(n.label)}</span>
              {(badges[n.href] ?? 0) > 0 && <b className="badge" aria-label={`${badges[n.href]} waiting`}>{badges[n.href] > 99 ? "99+" : badges[n.href]}</b>}
            </Link>
          );
        }
        const isOpen = open[n.title] ?? false;
        const id = "grp-" + n.title.replace(/\W+/g, "-");
        return (
          <div className={cn("nav-group", depth > 0 && "sub")} key={n.title}>
            <button type="button" className="nav-title" aria-expanded={isOpen} aria-controls={id} onClick={() => toggle(n.title)}>
              <n.icon size={depth ? 16 : 18} /> <span>{t(n.title)}</span>
              {!isOpen && badgeSum(n.children, badges) > 0 && <b className="badge" aria-label={`${badgeSum(n.children, badges)} waiting`}>{badgeSum(n.children, badges)}</b>}
              {isOpen ? <ChevronUp className="chev" size={15} /> : <ChevronDown className="chev" size={15} />}
            </button>
            {isOpen && <div id={id} className="nav-children"><NavTree nodes={n.children} path={path} open={open} toggle={toggle} depth={depth + 1} onNavigate={onNavigate} badges={badges} /></div>}
          </div>
        );
      })}
    </>
  );
}

function CommandPalette({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { can, user } = useAuth(); const router = useRouter();
  const [q, setQ] = useState(""); const [idx, setIdx] = useState(0); const dq = useDebounced(q, 200);
  const [people, setPeople] = useState<UserListItem[]>([]);
  const pages = useMemo(() => leaves(visibleNav(user?.vendorName ? vendorNav : nav, can)).map((l) => ({ label: t(l.label), sub: t("Page"), href: l.href })), [can, user?.vendorName]);
  useEffect(() => {
    if (!open || !can("users.read") || dq.trim().length < 2) { setPeople([]); return; }
    let live = true;
    get<Paged<UserListItem>>("/api/users", { q: dq, pageSize: 5 }, { noRoute: true }).then((r) => { if (live) setPeople(r.data); }, () => { if (live) setPeople([]); });
    return () => { live = false; };
  }, [dq, open, can]);
  const items = useMemo(() => {
    const t = q.trim().toLowerCase();
    const match = pages.filter((p) => !t || p.label.toLowerCase().includes(t));
    return [...match, ...people.map((u) => ({ label: u.name, sub: u.email, href: `/users/${u.id}` }))].slice(0, 8);
  }, [q, pages, people]);
  useEffect(() => setIdx(0), [q]);
  const go = (href: string) => { onClose(); setQ(""); router.push(href); };
  return (
    <Overlay open={open} onClose={onClose} title={t("Search")}>
      <input data-autofocus className="palette-input" placeholder={t("Search pages and people…")} value={q} aria-label={t("Search pages and people")} onChange={(e) => setQ(e.target.value)}
        onKeyDown={(e) => { if (e.key === "ArrowDown") { e.preventDefault(); setIdx((i) => Math.min(items.length - 1, i + 1)); } if (e.key === "ArrowUp") { e.preventDefault(); setIdx((i) => Math.max(0, i - 1)); } if (e.key === "Enter" && items[idx]) go(items[idx].href); }} />
      <ul className="palette-list" role="listbox">
        {items.map((i, k) => <li key={i.href + i.label} role="option" aria-selected={k === idx}><button type="button" className={k === idx ? "on" : ""} onClick={() => go(i.href)}><strong>{i.label}</strong><small>{i.sub}</small></button></li>)}
        {items.length === 0 && <li className="tbl-empty">{t("No results for “")}{q}”.</li>}
      </ul>
    </Overlay>
  );
}

function UserMenu() {
  const { user, signOut, lock } = useAuth();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const click = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    const key = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", click); document.addEventListener("keydown", key);
    return () => { document.removeEventListener("mousedown", click); document.removeEventListener("keydown", key); };
  }, [open]);
  if (!user) return null;
  return (
    <div className="user-menu" ref={ref}>
      <button type="button" className="user" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
        <Avatar name={user.name} size={36} i={1} src={avatarUrl(user.id, user.avatarVersion)} />
        <div className="user-text"><strong>{user.name}</strong><small>{user.roles[0]?.replace(/_/g, " ") ?? "user"}</small></div>
        <ChevronDown size={16} />
      </button>
      {open && (
        <div className="menu" role="menu">
          <Link role="menuitem" href="/profile" onClick={() => setOpen(false)}><UserRound size={15} /> {t("My Profile")}</Link>
          <button role="menuitem" type="button" onClick={() => { setOpen(false); void lock(); }}><Lock size={15} /> {t("Lock screen")}</button>
          <button role="menuitem" type="button" onClick={() => { setOpen(false); void signOut(); }}><LogOut size={15} /> {t("Sign out")}</button>
        </div>
      )}
    </div>
  );
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const theme = useTheme();
  const { can, user, refresh } = useAuth(); const nav2 = useRouter(); const [leaving, setLeaving] = useState(false);
  async function exitSupport() { setLeaving(true); try { await post("/api/tenants/support/exit"); await refresh(); nav2.push("/admin/tenants"); } finally { setLeaving(false); } }
  const tree = useMemo(() => visibleNav(user?.vendorName ? vendorNav : nav, can), [can, user?.vendorName]);
  const path = activeHref(pathname);
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const [compact, setCompact] = useState(false);
  const [mobile, setMobile] = useState(false);
  const [palette, setPalette] = useState(false);
  const [unack, setUnack] = useState(0);
  const [badges, setBadges] = useState<Record<string, number>>({});
  const [notes, setNotes] = useState(0);
  const drawer = useRef<HTMLElement>(null);
  const opener = useRef<HTMLButtonElement>(null);

  useEffect(() => { setCompact(localStorage.getItem("annaba-digital-sidebar") === "compact"); }, []);
  const toggleCompact = () => setCompact((c) => { localStorage.setItem("annaba-digital-sidebar", c ? "expanded" : "compact"); return !c; });

  // accordion: only the group(s) containing the current page stay open
  useEffect(() => { const trail = activeTrail(tree, path); setOpen(trail ? Object.fromEntries(trail.map((t) => [t, true])) : {}); }, [path, tree]);
  useEffect(() => { setMobile(false); }, [pathname]);

  useEffect(() => {
    if (!mobile) return;
    const first = drawer.current?.querySelector<HTMLElement>("a,button"); first?.focus();
    const trigger = opener.current;
    const key = (e: KeyboardEvent) => { if (e.key === "Escape") setMobile(false); };
    document.addEventListener("keydown", key); document.body.style.overflow = "hidden";
    return () => { document.removeEventListener("keydown", key); document.body.style.overflow = ""; trigger?.focus(); };
  }, [mobile]);

  useEffect(() => {
    const key = (e: KeyboardEvent) => { if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") { e.preventDefault(); setPalette(true); } };
    document.addEventListener("keydown", key); return () => document.removeEventListener("keydown", key);
  }, []);

  // bell: number of security events nobody has reviewed yet (only for people allowed to see them)
  useEffect(() => {
    if (!can("security.read")) { setUnack(0); return; }
    let live = true;
    const load = () => get<Paged<unknown>>("/api/security-events", { acknowledged: false, pageSize: 1 }, { noRoute: true }).then((r) => live && setUnack(r.total), () => {});
    void load(); const t = setInterval(load, 60_000);
    return () => { live = false; clearInterval(t); };
  }, [can, pathname]);

  // everyone: unread notifications (the bell) and approvals waiting for their decision (a sidebar badge)
  useEffect(() => {
    let live = true;
    const load = () => {
      get<{ unread: number }>("/api/notifications/unread-count", undefined, { noRoute: true }).then((r) => live && setNotes(r.unread), () => {});
      get<{ pending: number }>("/api/approvals/mine/count", undefined, { noRoute: true }).then((r) => live && setBadges((b) => ({ ...b, "/approvals": r.pending })), () => {});
    };
    void load(); const t = setInterval(load, 60_000);
    window.addEventListener("annaba:notifications", load);   // the notifications screen says when something was read
    return () => { live = false; clearInterval(t); window.removeEventListener("annaba:notifications", load); };
  }, [pathname]);

  // sidebar badges: requests and review items waiting for this person (only for those who may decide them)
  useEffect(() => {
    if (!can("users.assign_role")) { setBadges((b) => ({ ...b, "/access-requests": 0, "/access-reviews": 0 })); return; }
    let live = true;
    const load = () => get<{ accessRequests: number; reviewItems: number }>("/api/access-requests/pending-count", undefined, { noRoute: true })
      .then((r) => live && setBadges((b) => ({ ...b, "/access-requests": r.accessRequests, "/access-reviews": r.reviewItems })), () => {});
    void load(); const t = setInterval(load, 60_000);
    return () => { live = false; clearInterval(t); };
  }, [can, pathname]);

  const toggle = useCallback((k: string) => setOpen((o) => {
    const siblings = (nodes: NavNode[]): string[] | null => {
      if (nodes.some((n) => isGroup(n) && n.title === k)) return nodes.filter(isGroup).map((n) => n.title);
      for (const n of nodes) if (isGroup(n)) { const r = siblings(n.children); if (r) return r; }
      return null;
    };
    if (o[k]) return { ...o, [k]: false };
    const next = { ...o }; (siblings(tree) ?? []).forEach((t) => { next[t] = false; }); next[k] = true; return next;
  }), [tree]);

  return (
    <div className={cn("shell", compact && "compact")}>
      {mobile && <div className="backdrop" onClick={() => setMobile(false)} aria-hidden />}
      <aside ref={drawer} className={cn("sidebar", mobile && "open")} aria-label={t("Sidebar")}>
        <div className="sb-head">
          {compact ? <LogoMark size={30} /> : <Logo />}
          <button type="button" className="icon-btn sb-close" aria-label={t("Close menu")} onClick={() => setMobile(false)}><X size={16} /></button>
        </div>
        {!compact && user?.tenantName && <div className={cn("tenant-chip", user.actingTenant && "support")} title={user.actingTenant ? t("Support mode: the organisation you are administering") : t("Your organisation")}>{user.actingTenant ?? user.tenantName}</div>}
        <nav aria-label={t("Main navigation")}>
          {compact
            ? tree.map((n) => {
              const rail = (l: ReturnType<typeof leaves>[number]) => <Link key={l.href} href={l.href} className={cn("nav-item", "rail", path === l.href && "active")} aria-label={(badges[l.href] ?? 0) > 0 ? `${t(l.label)}, ${badges[l.href]} ${t("waiting")}` : t(l.label)} title={t(l.label)} aria-current={path === l.href ? "page" : undefined}><l.icon size={18} />{(badges[l.href] ?? 0) > 0 && <i className="rail-dot" aria-hidden="true" />}</Link>;
              // groups stay visible as groups: a divider and a labelled region per group, same order as the full sidebar
              return isGroup(n) ? <div key={n.title} className="rail-group" role="group" aria-label={t(n.title)}>{leaves(n.children).map(rail)}</div> : rail(n);
            })
            : <NavTree nodes={tree} path={path} open={open} toggle={toggle} onNavigate={() => setMobile(false)} badges={badges} />}
        </nav>
        <button type="button" className="collapse desktop-only" onClick={toggleCompact} aria-pressed={compact} aria-label={compact ? t("Expand menu") : t("Collapse menu")} title={compact ? t("Expand menu") : t("Collapse menu")}>
          <span className="ic">{compact ? <ChevronsRight size={16} /> : <ChevronsLeft size={16} />}</span><span className="lbl">{t("Collapse menu")}</span>
        </button>
      </aside>

      <main className="main" id="main">
        {user?.actingTenant && (
          <div className="support-bar" role="status">
            <span><b>{t("Support mode")}</b> {t("· You are administering")}{" "}<b>{user.actingTenant}</b>{t(". Everything you do is recorded in its audit trail, and this ends by itself after 2 hours.")}</span>
            <button type="button" className="btn sm" onClick={exitSupport} disabled={leaving}>{leaving ? t("Leaving…") : t("Exit support mode")}</button>
          </div>
        )}
        <header className="topbar">
          <button ref={opener} type="button" className="icon-btn menu-btn" aria-label={t("Open menu")} aria-expanded={mobile} onClick={() => setMobile(true)}><Menu size={18} /></button>
          <button type="button" className="search" onClick={() => setPalette(true)} aria-label={t("Search anything... ⌘ K (Ctrl or Command K)")}><Search size={16} /> <span>{t("Search anything...")}</span> <kbd>⌘ K</kbd></button>
          <div className="top-right">
            <button type="button" className="theme-btn" aria-label={theme.resolved === "dark" ? "Switch to light mode" : "Switch to dark mode"} title={theme.resolved === "dark" ? t("Light mode") : t("Dark mode")} onClick={() => theme.set(theme.resolved === "dark" ? "light" : "dark")}>
              {theme.resolved === "dark" ? <Sun size={19} /> : <Moon size={19} />}
            </button>
            <Link href="/notifications" className="bell" aria-label={t("{n} unread notifications", { n: notes })}><BellRing size={20} />{notes > 0 && <b>{notes > 99 ? "99+" : notes}</b>}</Link>
            {can("security.read") && (
              <Link href="/security-events" className="bell" aria-label={`${unack} unacknowledged security events`}><Bell size={20} />{unack > 0 && <b>{unack > 99 ? "99+" : unack}</b>}</Link>
            )}
            <UserMenu />
          </div>
        </header>
        {children}
      </main>
      <CommandPalette open={palette} onClose={() => setPalette(false)} />
    </div>
  );
}
