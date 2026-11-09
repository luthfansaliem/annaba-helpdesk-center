import { Badge, type BadgeTone } from "./badge";
import { t } from "@/i18n";

const user: Record<string, [BadgeTone, string]> = { active: ["green", "Active"], pending: ["orange", "Pending"], suspended: ["red", "Suspended"], deactivated: ["gray", "Deactivated"], locked: ["orange", "Locked"] };
const inv: Record<string, [BadgeTone, string]> = { pending: ["orange", "Pending"], accepted: ["green", "Accepted"], expired: ["gray", "Expired"], revoked: ["red", "Revoked"] };
const out: Record<string, [BadgeTone, string]> = { success: ["green", "Success"], failure: ["red", "Failure"], denied: ["orange", "Denied"] };
const sev: Record<string, [BadgeTone, string]> = { low: ["gray", "Low"], medium: ["orange", "Medium"], high: ["red", "High"] };
const pick = (m: Record<string, [BadgeTone, string]>, k: string): [BadgeTone, string] => (m[k] ? [m[k][0], t(m[k][1])] : ["gray", k]);

export const UserStatusBadge = ({ status, locked }: { status: string; locked?: boolean }) => locked && status === "active" ? <Badge tone="orange">{t("Locked")}</Badge> : <Badge tone={pick(user, status)[0]}>{pick(user, status)[1]}</Badge>;
export const InviteBadge = ({ status }: { status: string }) => <Badge tone={pick(inv, status)[0]}>{pick(inv, status)[1]}</Badge>;
export const OutcomeBadge = ({ outcome }: { outcome: string }) => <Badge tone={pick(out, outcome)[0]}>{pick(out, outcome)[1]}</Badge>;
export const SeverityBadge = ({ severity }: { severity: string }) => <Badge tone={pick(sev, severity)[0]}>{pick(sev, severity)[1]}</Badge>;
