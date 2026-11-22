import { t } from "@/i18n";
import { priorityLabel, resolutionLabel, RESOLUTION_CODES } from "@/components/tickets/status";
import type { Priority } from "@/lib/api/types";

/**
 * The API writes system activity lines in English (TicketService, AssignmentService, ApprovalService, SlaService, WorkOrderService,
 * RatingService). They are stored as text, so they are translated here when shown: each known sentence is matched and rebuilt from
 * its translation with the same values. Comments and reasons people typed match nothing and are shown as written.
 * When the API gains a new system sentence, add its pattern here and its translation to src/i18n/id.ts.
 */
type Rule = [RegExp, string, string[]];

const prio = (p: string) => priorityLabel(p as Priority) ?? p;

/** Whole-line sentences. The first match wins, so the more specific pattern comes first. */
const LINES: Rule[] = [
  [/^Filed on behalf of ([\s\S]+)\.$/, "Filed on behalf of {name}.", ["name"]],
  [/^Attached ([\s\S]+) \(([^()]+)\)\.$/, "Attached {file} ({size}).", ["file", "size"]],
  [/^Assigned to ([\s\S]+) \(([\s\S]+)\) by rule “([\s\S]+)”\.$/, "Assigned to {name} ({team}) by rule “{rule}”.", ["name", "team", "rule"]],
  [/^Assigned to ([\s\S]+) by the maintenance plan\.$/, "Assigned to {name} by the maintenance plan.", ["name"]],
  [/^Assigned to ([\s\S]+) \(([^()]+)\)\.$/, "Assigned to {name} ({team}).", ["name", "team"]],
  [/^Assigned to ([\s\S]+)\.$/, "Assigned to {name}.", ["name"]],
  [/^Returned to the ([\s\S]+) queue\.$/, "Returned to the {team} queue.", ["team"]],
  [/^Waiting for approval: ([\s\S]+)\.$/, "Waiting for approval: {step}.", ["step"]],
  [/^Approval handed over from ([\s\S]+) to ([\s\S]+)\.$/, "Approval handed over from {from} to {to}.", ["from", "to"]],
  [/^Approved: ([\s\S]+)$/, "Approved: {comment}", ["comment"]],
  [/^Rejected: ([\s\S]+)$/, "Rejected: {comment}", ["comment"]],
  [/^SLA warning: (\d+)% of the time allowed has been used\.$/, "SLA warning: {pct}% of the time allowed has been used.", ["pct"]],
  [/^SLA at risk: (\d+)% of the time allowed has been used\.$/, "SLA at risk: {pct}% of the time allowed has been used.", ["pct"]],
  [/^Work order (\S+) created by the maintenance plan: ([\s\S]+)\.$/, "Work order {number} created by the maintenance plan: {title}.", ["number", "title"]],
  [/^Work order (\S+) created: ([\s\S]+) \(given to ([\s\S]+)\)\.$/, "Work order {number} created: {title} (given to {name}).", ["number", "title", "name"]],
  [/^Work order (\S+) created: ([\s\S]+)\.$/, "Work order {number} created: {title}.", ["number", "title"]],
  [/^Work order (\S+) is no longer given to anyone\.$/, "Work order {number} is no longer given to anyone.", ["number"]],
  [/^Work order (\S+) given to ([\s\S]+)\.$/, "Work order {number} given to {name}.", ["number", "name"]],
  [/^Work order (\S+) started\.$/, "Work order {number} started.", ["number"]],
  [/^Work order (\S+) resumed\.$/, "Work order {number} resumed.", ["number"]],
  [/^Work order (\S+) on hold: ([\s\S]+)$/, "Work order {number} on hold: {reason}", ["number", "reason"]],
  [/^Work order (\S+) done: ([\s\S]+)$/, "Work order {number} done: {note}", ["number", "note"]],
  [/^Work order (\S+) cancelled: ([\s\S]+)$/, "Work order {number} cancelled: {reason}", ["number", "reason"]],
  [/^Rated (\d) out of 5\.$/, "Rated {score} out of 5.", ["score"]],
  [/^Changed the rating from (\d) to (\d) out of 5\.$/, "Changed the rating from {from} to {to} out of 5.", ["from", "to"]],
  [/^Removed ([\s\S]+)\.$/, "Removed {name}.", ["name"]],
];

/** Sentences of an edit, which the API joins with spaces into one line ("Subject changed to … Priority changed from … to …."). */
const EDITS: [RegExp, (...m: string[]) => string][] = [
  [/Subject changed to “([^”]*)”\./g, (s) => t("Subject changed to “{subject}”.", { subject: s })],
  [/Priority changed from (\w+) to (\w+)\./g, (a, b) => t("Priority changed from {from} to {to}.", { from: prio(a).toLowerCase(), to: prio(b).toLowerCase() })],
  [/Location changed to (.+?)\.(?= [A-Z]|$)/g, (l) => t("Location changed to {place}.", { place: l })],
  [/Asset set to (.+?)\.(?= [A-Z]|$)/g, (a) => t("Asset set to {asset}.", { asset: a })],
  [/Location cleared\./g, () => t("Location cleared.")],
  [/Asset cleared\./g, () => t("Asset cleared.")],
];

const RESOLVED = new RegExp(`^(${RESOLUTION_CODES.join("|")}): ([\\s\\S]*)$`);

/** A stored activity line in the reader's language. */
export function eventText(body: string | null | undefined): string {
  if (!body) return "";
  const exact = t(body);
  if (exact !== body) return exact;
  for (const [re, key, names] of LINES) {
    const m = re.exec(body);
    if (m) return t(key, Object.fromEntries(names.map((n, i) => [n, m[i + 1]])));
  }
  const resolved = RESOLVED.exec(body);
  if (resolved) return `${resolutionLabel(resolved[1])}: ${resolved[2]}`;
  let out = body;
  for (const [re, fn] of EDITS) out = out.replace(re, (_, ...m: string[]) => fn(...m));
  return out;
}
