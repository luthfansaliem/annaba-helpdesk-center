"use client";

import { useState } from "react";
import Link from "next/link";
import { CheckCircle2, Circle } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { AssigneeOption, QuickStartResult, SetupStatus, SetupStep } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TEXT: Record<string, () => { title: string; why: string; cta: string }> = {
  site: () => ({ title: t("Add your first site"), why: t("A site is where your people work. Requests can point to a place so the right team goes to the right door."), cta: t("Open locations") }),
  calendar: () => ({ title: t("Working hours"), why: t("Service-level clocks only run in working hours. A default calendar comes with every organisation; adjust it to your office."), cta: t("Open calendars") }),
  team: () => ({ title: t("A team with people in it"), why: t("Tickets are worked by a team. Add the people who will pick them up."), cta: t("Open teams") }),
  catalog: () => ({ title: t("Publish at least one service"), why: t("The service catalog is what people choose from when they ask for help. A starter catalog is installed and published."), cta: t("Open catalog") }),
  sla: () => ({ title: t("Service-level targets"), why: t("Targets for first response and resolution by priority, so overdue work is visible."), cta: t("Open SLA policies") }),
  routing: () => ({ title: t("Route new requests to a team"), why: t("Without a rule, new tickets wait unassigned. One rule that sends everything to your team is enough to start."), cta: t("Open assignment rules") }),
  approvals: () => ({ title: t("Approvals (optional)"), why: t("Require a manager's approval for services that spend money or need authorisation."), cta: t("Open approval policies") }),
  scanner: () => ({ title: t("Virus scanning of uploads"), why: t("Files people attach should be scanned. Ask your administrator to connect ClamAV (Storage:ClamAvHost); see the staging runbook."), cta: t("Open the checklist") }),
  test: () => ({ title: t("Send a test request"), why: t("File a request yourself and follow it through the queue before you tell everyone."), cta: t("New request") }),
};

function QuickStart({ status, onDone }: { status: SetupStatus; onDone: () => void }) {
  const run = useRun();
  const missing = (k: string) => !status.steps.find((s) => s.key === k)?.done;
  const needSite = missing("site"); const needTeam = missing("team");
  const people = useQuery(() => get<AssigneeOption[]>("/api/tickets/assignees").catch(() => [] as AssigneeOption[]), []);
  const [siteName, setSiteName] = useState(""); const [teamName, setTeamName] = useState("General Affairs"); const [members, setMembers] = useState<string[]>([]); const [lead, setLead] = useState("");
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false); const [result, setResult] = useState<QuickStartResult | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { siteName: needSite ? siteName : null, teamName: needTeam ? teamName : null, memberIds: needTeam ? members : [], leadId: lead || null };
    const r = await run(() => post<QuickStartResult>("/api/setup/quick-start", body), undefined, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    setResult(r.data); onDone();
  }
  const toggle = (id: string, on: boolean) => { setMembers((m) => (on ? [...m, id] : m.filter((x) => x !== id))); if (!on && lead === id) setLead(""); };
  const picked = (people.data ?? []).filter((p) => members.includes(p.id));

  return (
    <Panel title={t("Quick start")} description={t("Fills in what is missing with sensible defaults. Nothing you already set up is changed, and you can edit everything afterwards.")}>
      <form className="stack" noValidate onSubmit={submit} aria-label={t("Quick start")}>
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {result && <div className="form-alert" role="status">
          {result.created.length > 0 ? t("Created: {list}.", { list: result.created.map((k) => TEXT[k]?.().title ?? k).join(", ") }) : t("Nothing needed to be created.")}
        </div>}
        {needSite && <Field label={t("Name of your first site")} htmlFor="qs-site" required error={errs.siteName}><TextInput id="qs-site" maxLength={120} placeholder={t("e.g. Head Office")} value={siteName} onChange={(e) => setSiteName(e.target.value)} /></Field>}
        {needTeam && <>
          <Field label={t("Name of your first team")} htmlFor="qs-team" error={errs.teamName}><TextInput id="qs-team" maxLength={100} value={teamName} onChange={(e) => setTeamName(e.target.value)} /></Field>
          <fieldset className="fl" style={{ border: 0, padding: 0, margin: 0 }} aria-describedby="qs-people-help">
            <legend style={{ fontWeight: 700, fontSize: 12, padding: 0 }}>{t("Who works the tickets?")} <em aria-hidden>*</em></legend>
            {(people.data ?? []).map((p) => (
              <label key={p.id} className="row" style={{ gap: 8, padding: "4px 0" }}><Checkbox checked={members.includes(p.id)} onChange={(e) => toggle(p.id, e.target.checked)} /> {p.name} <small className="sub">{p.email}</small></label>
            ))}
            <small className="sub" id="qs-people-help">{t("Only people who can work tickets are listed. Invite colleagues and give them the GA Agent role in Users first.")}</small>
            {errs.memberIds && <p className="field-error" role="alert">{errs.memberIds}</p>}
          </fieldset>
          {picked.length > 1 && <Field label={t("Team lead (optional)")} htmlFor="qs-lead" error={errs.leadId}>
            <SelectInput id="qs-lead" value={lead} onChange={(e) => setLead(e.target.value)} options={[{ value: "", label: t("No lead") }, ...picked.map((p) => ({ value: p.id, label: p.name }))]} />
          </Field>}
        </>}
        <span className="row" style={{ gap: 8 }}><Button variant="primary" type="submit" disabled={busy}>{busy ? t("Setting up…") : t("Set up for me")}</Button></span>
      </form>
    </Panel>
  );
}

function StepRow({ s }: { s: SetupStep }) {
  const x = TEXT[s.key]?.() ?? { title: s.key, why: "", cta: t("Open") };
  const badge = s.done ? <Badge tone="green">{t("Done")}</Badge> : s.required ? <Badge tone="orange">{t("To do")}</Badge> : <Badge tone="gray">{t("Recommended")}</Badge>;
  return (
    <li className="row" style={{ gap: 12, alignItems: "flex-start", padding: "10px 0", borderTop: "1px solid var(--border, #e5e7eb)" }}>
      {s.done ? <CheckCircle2 size={18} aria-hidden style={{ marginTop: 2 }} /> : <Circle size={18} aria-hidden style={{ marginTop: 2 }} />}
      <div style={{ flex: 1 }}>
        <b>{x.title}</b> {badge}
        <p style={{ margin: "2px 0 0" }}>{x.why}</p>
      </div>
      {s.key !== "scanner" && <Link href={s.link} className="btn sm">{x.cta}</Link>}
    </li>
  );
}

function Body({ status, reload }: { status: SetupStatus; reload: () => void }) {
  const pct = status.total ? Math.round((status.done / status.total) * 100) : 0;
  const open = status.steps.some((s) => s.required && !s.done && ["site", "team", "sla", "routing"].includes(s.key));
  return (
    <div className="stack">
      <Panel title={status.ready ? t("Your helpdesk is ready to take requests") : t("{n} steps left before you can take requests", { n: status.total - status.done })}>
        <div role="progressbar" aria-valuemin={0} aria-valuemax={status.total} aria-valuenow={status.done} aria-label={t("Setup progress")} style={{ height: 8, borderRadius: 4, background: "var(--border, #e5e7eb)", overflow: "hidden" }}>
          <div style={{ width: `${pct}%`, height: "100%", background: "var(--green, #15803d)" }} />
        </div>
        <p style={{ margin: "8px 0 0" }}>{t("{done} of {total} required steps done.", { done: status.done, total: status.total })}</p>
      </Panel>
      {open && <QuickStart status={status} onDone={reload} />}
      <Panel title={t("Checklist")}>
        <ol className="stack" style={{ listStyle: "none", margin: 0, padding: 0, gap: 0 }}>{status.steps.map((s) => <StepRow key={s.key} s={s} />)}</ol>
      </Panel>
    </div>
  );
}

export default function GetStartedPage() {
  const q = useQuery(() => get<SetupStatus>("/api/setup/status"), []);
  return (
    <Require perm="locations.manage">
      <PageHeader help="go-live" title={t("Get started")} description={t("Everything a new helpdesk needs before it takes real requests. Do it once; each step opens the screen where you can fine-tune it.")} />
      <QueryBoundary q={q} rows={6}>{(s) => <Body status={s} reload={q.reload} />}</QueryBoundary>
    </Require>
  );
}
