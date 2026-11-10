"use client";

import { Suspense, use, useCallback, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { AboutAssetBanner, useAboutAsset } from "@/components/assets/about-asset";
import { KbSuggestions } from "@/components/kb/kb-bits";
import { ArrowLeft } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { DynamicForm, PersonPicker, type FormValues } from "@/components/forms/dynamic-form";
import { PriorityBadge } from "@/components/tickets/status";
import { get, post } from "@/lib/api/client";
import type { PortalLocation, PortalPerson, PortalServiceDetail, TicketDetail } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { uploadFile } from "@/lib/files";
import { buildAnswers } from "@/lib/forms";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

function RequestForm({ detail }: { detail: PortalServiceDetail }) {
  const router = useRouter(); const run = useRun(); const { can } = useAuth();
  const about = useAboutAsset(useSearchParams().get("asset")); const [placeFromAsset, setPlaceFromAsset] = useState<string | null>(null);
  const locations = useQuery(() => get<PortalLocation[]>("/api/portal/locations"), []);
  const [subject, setSubject] = useState(detail.service.name); const [description, setDescription] = useState("");
  const [locationId, setLocationId] = useState(""); const [onBehalf, setOnBehalf] = useState<string | undefined>(undefined);
  const [values, setValues] = useState<FormValues>({});
  const [errs, setErrs] = useState<Record<string, string>>({}); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const searchPeople = useCallback(async (q: string) => (await get<PortalPerson[]>("/api/portal/people", { q })).map((p) => ({ value: p.id, label: p.department ? `${p.name} · ${p.department}` : p.name })), []);
  const placeOptions = (locations.data ?? []).map((l) => ({ value: l.id, label: l.path }));
  if (about?.locationId && placeFromAsset !== about.id) { setPlaceFromAsset(about.id); if (!locationId) setLocationId(about.locationId); }   // start at the asset's place

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setErrs({}); setBusy(true);
    const body = { subject, description: description || null, answers: buildAnswers(detail.fields, values), locationId: locationId || null, onBehalfOfUserId: onBehalf ?? null, assetId: about?.id ?? null };
    const r = await run(() => post<TicketDetail>(`/api/portal/services/${detail.service.id}/requests`, body), undefined, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); setErrs(r.fields ?? {}); return; }
    router.push(`/tickets/${r.data.id}?created=1`);
  }
  const answerErrors = Object.fromEntries(Object.entries(errs).filter(([k]) => k.startsWith("answers.")).map(([k, v]) => [k.slice("answers.".length), v]));

  return (
    <form className="stack" noValidate onSubmit={submit} aria-label={detail.service.name}>
      {err && <div className="form-alert err" role="alert">{err}</div>}
      {about && <AboutAssetBanner asset={about} clearHref={`/requests/new/${detail.service.id}`} />}
      {errs.assetId && <div className="form-alert err" role="alert">{errs.assetId}</div>}
      <Panel title={t("About your request")}>
        <div className="stack">
          <Field label={t("Subject")} htmlFor="rf-subject" required error={errs.subject}><TextInput id="rf-subject" maxLength={200} value={subject} onChange={(e) => setSubject(e.target.value)} /></Field>
          <Field label={t("Where is it?")} htmlFor="rf-loc" error={errs.locationId}>
            <SelectInput id="rf-loc" value={locationId} onChange={(e) => setLocationId(e.target.value)} options={[{ value: "", label: t("Not sure / not applicable") }, ...placeOptions]} />
          </Field>
          <Field label={t("More detail")} htmlFor="rf-desc" error={errs.description}><textarea id="rf-desc" rows={4} maxLength={4000} className="textarea" value={description} onChange={(e) => setDescription(e.target.value)} /></Field>
          {can("tickets.update") && <Field label={t("Request on behalf of")} htmlFor="rf-behalf" error={errs.onBehalfOfUserId}><PersonPicker id="rf-behalf" value={onBehalf ?? ""} onChange={setOnBehalf} search={searchPeople} /></Field>}
        </div>
      </Panel>
      <KbSuggestions serviceId={detail.service.id} text={subject === detail.service.name ? description : `${subject} ${description}`} />
      {detail.fields.length > 0 && <Panel title={t("Details")}>
        <DynamicForm fields={detail.fields} values={values} onChange={setValues} errors={answerErrors} locations={placeOptions} searchPeople={searchPeople} upload={(f) => uploadFile("/api/uploads", f)} idPrefix="rq" />
      </Panel>}
      <span className="row" style={{ gap: 8 }}>
        <Button variant="primary" type="submit" disabled={busy}>{busy ? t("Sending…") : t("Send request")}</Button>
        <Button href="/requests/new">{t("Cancel")}</Button>
      </span>
    </form>
  );
}

function Page({ id }: { id: string }) {
  const detail = useQuery(() => get<PortalServiceDetail>(`/api/portal/services/${id}`), [id]);
  return (
    <QueryBoundary q={detail}>{(d) => (
      <>
        <div className="crud-top">
          <div>
            <Link href="/requests/new" className="back-link"><ArrowLeft size={14} /> {t("New request")}</Link>
            <h1>{d.service.name}</h1>
            <p>{d.service.description ?? d.categoryName}</p>
          </div>
          <PriorityBadge priority={d.defaultPriority} />
        </div>
        <RequestForm detail={d} />
      </>
    )}</QueryBoundary>
  );
}

export default function NewRequestFormPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Suspense><Page id={id} /></Suspense>;
}
