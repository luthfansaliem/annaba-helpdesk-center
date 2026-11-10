"use client";

import { Badge, type BadgeTone } from "@/components/ui/badge";
import type { ApprovalStatus, Priority, SlaState, TicketAction, TicketStatus } from "@/lib/api/types";
import { t } from "@/i18n";

export const STATUSES: TicketStatus[] = ["pendingapproval", "submitted", "assigned", "inprogress", "onhold", "resolved", "closed", "cancelled", "reopened", "rejected"];

export const statusLabel = (s: TicketStatus) => ({
  submitted: t("Submitted"), assigned: t("Assigned"), inprogress: t("In progress"), onhold: t("On hold"),
  resolved: t("Resolved"), closed: t("Closed"), cancelled: t("Cancelled"), reopened: t("Reopened"), pendingapproval: t("Pending approval"), rejected: t("Rejected"),
})[s];

const statusTone: Record<TicketStatus, BadgeTone> = { submitted: "gray", assigned: "blue", inprogress: "blue", onhold: "orange", resolved: "green", closed: "gray", cancelled: "gray", reopened: "orange", pendingapproval: "orange", rejected: "red" };

export function TicketStatusBadge({ status }: { status: TicketStatus }) { return <Badge tone={statusTone[status]}>{statusLabel(status)}</Badge>; }

export const priorityLabel = (p: Priority) => ({ low: t("Low"), medium: t("Medium"), high: t("High"), urgent: t("Urgent") })[p];
const priorityTone: Record<Priority, BadgeTone> = { low: "gray", medium: "blue", high: "orange", urgent: "red" };
export function PriorityBadge({ priority }: { priority: Priority }) { return <Badge tone={priorityTone[priority]}>{priorityLabel(priority)}</Badge>; }

export const actionLabel = (a: TicketAction) => ({
  assign: t("Assign"), start: t("Start work"), hold: t("Put on hold"), resume: t("Resume"), resolve: t("Resolve"), cancel: t("Cancel request"), confirm: t("Confirm it is solved"), reopen: t("Reopen"),
})[a];

export const RESOLUTION_CODES = ["fixed", "workaround", "no_fault_found", "cannot_reproduce", "duplicate", "declined", "other"] as const;
export const resolutionLabel = (c: string) => ({
  fixed: t("Fixed"), workaround: t("Workaround given"), no_fault_found: t("No fault found"), cannot_reproduce: t("Cannot reproduce"), duplicate: t("Duplicate"), declined: t("Declined"), other: t("Other"),
} as Record<string, string>)[c] ?? c;

export const SLA_STATES: SlaState[] = ["ontrack", "atrisk", "breached", "paused", "met"];
export const slaLabel = (s: SlaState) => ({ none: t("No SLA"), ontrack: t("On track"), atrisk: t("At risk"), breached: t("Breached"), paused: t("Paused"), met: t("Met") })[s];
const slaTone: Record<SlaState, BadgeTone> = { none: "gray", ontrack: "green", atrisk: "orange", breached: "red", paused: "gray", met: "blue" };
/** Where a ticket stands against its targets; nothing at all when it has none. */
export function SlaBadge({ state }: { state: SlaState | null | undefined }) { return state && state !== "none" ? <Badge tone={slaTone[state]}>{slaLabel(state)}</Badge> : null; }

export const strategyLabel = (s: string) => ({ none: t("Team queue only"), roundrobin: t("Round robin"), leastloaded: t("Least loaded") } as Record<string, string>)[s] ?? s;
export const approvalLabel = (s: ApprovalStatus) => ({ waiting: t("Waiting for earlier step"), pending: t("Waiting for decision"), approved: t("Approved"), rejected: t("Rejected"), skipped: t("Not needed"), cancelled: t("Cancelled") })[s];
const approvalTone: Record<ApprovalStatus, BadgeTone> = { waiting: "gray", pending: "orange", approved: "green", rejected: "red", skipped: "gray", cancelled: "gray" };
export function ApprovalBadge({ status }: { status: ApprovalStatus }) { return <Badge tone={approvalTone[status]}>{approvalLabel(status)}</Badge>; }
