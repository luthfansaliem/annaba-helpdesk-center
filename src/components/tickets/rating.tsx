"use client";

import { useState } from "react";
import { Star } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field } from "@/components/ui/field";
import { put } from "@/lib/api/client";
import type { TicketDetail, TicketRating } from "@/lib/api/types";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const WORDS = () => [t("Very poor"), t("Poor"), t("Okay"), t("Good"), t("Excellent")];

export function Stars({ score }: { score: number }) {
  return (
    <span className="row" style={{ gap: 2 }} role="img" aria-label={t("{n} out of 5", { n: score })}>
      {[1, 2, 3, 4, 5].map((i) => <Star key={i} size={16} aria-hidden fill={i <= score ? "#f59e0b" : "none"} color={i <= score ? "#b45309" : "#9ca3af"} />)}
    </span>
  );
}

/** TKT-009. The requester rates a resolved request (and may change it while rating is open); staff see the rating. */
export function RatingPanel({ ticket, onDone }: { ticket: TicketDetail; onDone: (r: TicketRating) => void }) {
  const run = useRun();
  const r = ticket.rating;
  const [score, setScore] = useState<number>(r?.score ?? 0); const [comment, setComment] = useState(r?.comment ?? ""); const [editing, setEditing] = useState(false);
  const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  if (!r || (!r.canRate && r.score === null)) return null;
  const words = WORDS();

  async function save(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    if (score < 1) { setErr(t("Choose 1 to 5 stars.")); return; }
    setBusy(true);
    const res = await run(() => put<TicketRating>(`/api/tickets/${ticket.id}/rating`, { score, comment: comment || null }), t("Thank you for your rating."), { silent: true });
    setBusy(false);
    if (!res.ok) { setErr(res.fields?.score ?? res.fields?.comment ?? res.error); return; }
    setEditing(false); onDone(res.data);
  }

  if (r.score !== null && !editing) {
    return (
      <Panel title={r.canRate ? t("Your rating") : t("Requester's rating")}>
        <div className="stack" data-testid="rating">
          <span className="row" style={{ gap: 8 }}><Stars score={r.score} /> <b>{words[r.score - 1]}</b></span>
          {r.comment && <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{r.comment}</p>}
          {r.at && <small className="sub">{fmtDateTime(r.at)}</small>}
          {r.canRate && <div><Button size="sm" onClick={() => setEditing(true)}>{t("Change rating")}</Button></div>}
        </div>
      </Panel>
    );
  }
  return (
    <Panel title={t("How did it go?")} description={r.rateUntil ? t("You can rate this request until {date}.", { date: fmtDateTime(r.rateUntil) }) : undefined}>
      <form className="stack" noValidate onSubmit={save} data-testid="rate-form">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="sr-only">{t("Your rating")}</legend>
          <div className="row" style={{ gap: 4, flexWrap: "wrap" }} role="radiogroup" aria-label={t("Your rating")}>
            {[1, 2, 3, 4, 5].map((i) => (
              <label key={i} className={`btn sm${score === i ? " primary" : ""}`} style={{ cursor: "pointer" }}>
                <input type="radio" name="csat" className="sr-only" value={i} checked={score === i} onChange={() => setScore(i)} aria-label={`${i} – ${words[i - 1]}`} />
                <Star size={14} aria-hidden fill={i <= score ? "#f59e0b" : "none"} /> {i}
              </label>
            ))}
          </div>
          {score > 0 && <small className="sub">{words[score - 1]}</small>}
        </fieldset>
        <Field label={score > 0 && score <= 2 ? t("What went wrong?") : t("Anything to add? (optional)")} htmlFor="csat-comment">
          <textarea id="csat-comment" rows={3} maxLength={1000} className="textarea" value={comment} onChange={(e) => setComment(e.target.value)} />
        </Field>
        <div className="row" style={{ gap: 8 }}>
          <Button variant="primary" type="submit" disabled={busy}>{busy ? t("Saving…") : t("Send rating")}</Button>
          {editing && <Button onClick={() => { setEditing(false); setScore(r.score ?? 0); setComment(r.comment ?? ""); }}>{t("Cancel")}</Button>}
        </div>
      </form>
    </Panel>
  );
}
