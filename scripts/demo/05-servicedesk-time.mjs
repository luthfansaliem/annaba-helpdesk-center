/**
 * Time and SLA arithmetic for the service-desk seed: a port of the API's WorkingCalendar and SlaClock (api/src/AnnabaDigital.Domain), so that tickets we backdate with
 * SQL carry exactly the due times, states and escalation levels the API itself would have stored had they been created in the past.
 * All wall-clock work is done in WIB (Asia/Jakarta, UTC+7, no daylight saving), the zone of the demo calendars.
 */
export const MIN = 60_000, HOUR = 3_600_000, DAY = 86_400_000, WIB = 7 * HOUR;

/** Midnight (WIB) of the day containing `ms`. */
export const dayStart = (ms) => Math.floor((ms + WIB) / DAY) * DAY - WIB;
/** Day of week in WIB, 0 = Sunday. */
export const dow = (ms) => (Math.floor((ms + WIB) / DAY) + 4) % 7;
export const dateKey = (ms) => new Date(dayStart(ms) + WIB).toISOString().slice(0, 10);
/** A Date at hh:mm WIB on the WIB day containing `base`. */
export const atWib = (base, hh, mm = 0) => new Date(dayStart(+base) + hh * HOUR + mm * MIN);
export const iso = (ms) => new Date(ms).toISOString();

/** A business calendar from the API shape ({ isAlwaysOpen, periods:[{day,startMinute,endMinute}], holidays:[{date}] }). */
export class Cal {
  constructor(c) {
    this.alwaysOpen = !!c?.isAlwaysOpen;
    this.byDay = new Map();
    for (const p of c?.periods ?? []) { if (p.endMinute > p.startMinute) { const l = this.byDay.get(p.day) ?? []; l.push([p.startMinute, p.endMinute]); l.sort((a, b) => a[0] - b[0]); this.byDay.set(p.day, l); } }
    this.holidays = new Set((c?.holidays ?? []).map((h) => h.date));
    this.name = c?.name;
  }
  windowsOf(dayMs) { if (this.holidays.has(dateKey(dayMs))) return []; return (this.byDay.get(dow(dayMs)) ?? []).map(([s, e]) => [dayMs + s * MIN, dayMs + e * MIN]); }
  *from(ms) { const d0 = dayStart(ms); for (let i = 0; i < 4000; i++) for (const w of this.windowsOf(d0 + i * DAY)) if (w[1] > ms) yield w; }
  *before(ms) { const d0 = dayStart(ms); for (let i = 0; i < 4000; i++) { const ws = this.windowsOf(d0 - i * DAY); for (let k = ws.length - 1; k >= 0; k--) if (ws[k][0] < ms) yield ws[k]; } }
  isOpen(ms) { if (this.alwaysOpen) return true; for (const [s, e] of this.from(ms)) return s <= ms && ms < e; return false; }
  /** First moment at or after `ms` when work happens. */
  nextOpening(ms) { if (this.alwaysOpen) return ms; for (const [s] of this.from(ms)) return Math.max(s, ms); return ms; }
  /** Last moment at or before `ms` when work happens (the closing time of the previous window if closed now). */
  prevClosing(ms) { if (this.alwaysOpen || this.isOpen(ms)) return ms; for (const [, e] of this.before(ms)) return Math.min(e, ms); return ms; }
  /** Working minutes between two instants. */
  between(a, b) {
    if (b <= a) return 0; if (this.alwaysOpen) return (b - a) / MIN;
    let t = 0; for (const [s, e] of this.from(a)) { if (s >= b) break; t += (Math.min(e, b) - Math.max(s, a)) / MIN; } return t;
  }
  /** The instant `minutes` of working time after `ms`. */
  add(ms, minutes) {
    if (this.alwaysOpen || minutes <= 0) return ms + minutes * MIN;
    let rem = minutes;
    for (const [s, e] of this.from(ms)) { const st = Math.max(s, ms), avail = (e - st) / MIN; if (rem <= avail) return st + rem * MIN; rem -= avail; }
    return ms + minutes * MIN;
  }
  /** The instant `minutes` of working time before `ms`. */
  sub(ms, minutes) {
    if (this.alwaysOpen || minutes <= 0) return ms - minutes * MIN;
    let rem = minutes;
    for (const [s, e] of this.before(ms)) { const en = Math.min(e, ms), avail = (en - s) / MIN; if (rem <= avail) return en - rem * MIN; rem -= avail; }
    return ms - minutes * MIN;
  }
  /** A natural moment: unchanged if the calendar is open, else the next opening plus a little. */
  snap(ms) { return this.isOpen(ms) ? ms : this.nextOpening(ms); }
}

/** The n-th previous working day (n = 0: today when it is a working day, else the last one) as a WIB midnight, on a calendar. */
export function workdayBack(cal, nowMs, n) {
  let day = dayStart(nowMs), seen = -1;
  for (let i = 0; i < 4000; i++, day -= DAY) {
    if (!cal.windowsOf(day).length) continue;
    // today only counts when it already opened
    if (i === 0 && cal.windowsOf(day)[0][0] > nowMs) continue;
    seen++; if (seen === n) return day;
  }
  return dayStart(nowMs);
}

const levelOf = (used, e1, e2) => (used >= 1 ? 3 : e2 > 0 && used * 100 >= e2 ? 2 : e1 > 0 && used * 100 >= e1 ? 1 : 0);

/**
 * SlaClock.Refresh, as pure arithmetic. `p`: { status, hasPol, start, frMin, resMin, e1, e2, pausedMinutes, pausedAt (ms|null), responded (ms|null), resolved (ms|null) }.
 * Returns { state, level, frDue, resDue, breachedAt } (state names as the API stores them).
 */
export function slaRefresh(p, cal, now) {
  if (!p.hasPol || p.status === "Cancelled" || p.status === "Rejected") return { state: "None", level: 0, frDue: null, resDue: null, breachedAt: null };
  const frDue = cal.add(p.start, p.frMin + p.pausedMinutes), resDue = cal.add(p.start, p.resMin + p.pausedMinutes);
  const at = p.pausedAt ?? now;
  const used = (target) => (target <= 0 ? 1 : Math.max(0, cal.between(p.start, at) - p.pausedMinutes) / target);
  const frMissed = p.responded != null ? p.responded > frDue : at > frDue;
  const resMissed = p.resolved != null ? p.resolved > resDue : at > resDue;
  let level = 0;
  if (p.responded == null) level = Math.max(level, levelOf(used(p.frMin), p.e1, p.e2));
  if (p.resolved == null) level = Math.max(level, levelOf(used(p.resMin), p.e1, p.e2));
  if (frMissed || resMissed) level = 3;
  let state, breachedAt = null;
  if (frMissed || resMissed) { state = "Breached"; breachedAt = frMissed && resMissed ? Math.min(frDue, resDue) : frMissed ? frDue : resDue; }
  else if (p.resolved != null) state = "Met";
  else if (p.pausedAt != null) state = "Paused";
  else state = level >= 2 ? "AtRisk" : "OnTrack";
  return { state, level, frDue, resDue, breachedAt };
}

/** The moment `pct`% of `target` working minutes (plus paused time) have passed since `start`. */
export const crossing = (cal, start, target, pct, paused) => cal.add(start, (target * pct) / 100 + paused);

/** SQL string literal. */
export const q = (s) => (s == null ? "NULL" : `'${String(s).replace(/'/g, "''")}'`);
export const ts = (ms) => (ms == null ? "NULL" : `'${new Date(ms).toISOString()}'`);
