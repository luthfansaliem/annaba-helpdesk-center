"use client";

import { Fragment, Suspense, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import { ArrowRight, Search } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Field, TextInput } from "@/components/ui/field";
import { PERSONAS, TOPICS, type HelpTopic, type Persona } from "@/content/help";
import { useAuth } from "@/lib/auth";
import { t, useLang } from "@/i18n";

/** `**bold**` in the help text names a button or menu on screen. Nothing else is interpreted, so content can never inject markup. */
function Rich({ text }: { text: string }) {
  return <>{text.split(/(\*\*[^*]+\*\*)/g).map((p, i) => (p.startsWith("**") && p.endsWith("**") ? <b key={i}>{p.slice(2, -2)}</b> : <Fragment key={i}>{p}</Fragment>))}</>;
}

function Topic({ topic, lang, showPersona }: { topic: HelpTopic; lang: "en" | "id"; showPersona: boolean }) {
  const persona = PERSONAS.find((p) => p.key === topic.persona)!;
  return (
    <section id={topic.id} aria-label={topic.title[lang]} style={{ scrollMarginTop: 16 }}>
      <Panel title={topic.title[lang]} description={topic.summary[lang]}>
        <div className="stack">
          {showPersona && <div><Badge tone="blue" dot={false}>{persona.title[lang]}</Badge></div>}
          <ol className="stack" style={{ margin: 0, paddingLeft: 20 }}>{topic.steps[lang].map((s, i) => <li key={i}><Rich text={s} /></li>)}</ol>
          {topic.tips && <ul className="stack" style={{ margin: 0, paddingLeft: 20 }} aria-label={t("Good to know")}>{topic.tips[lang].map((s, i) => <li key={i}><small><Rich text={s} /></small></li>)}</ul>}
          {topic.go && <div><Button href={topic.go.href}>{topic.go.label[lang]} <ArrowRight size={14} aria-hidden /></Button></div>}
        </div>
      </Panel>
    </section>
  );
}

function Help() {
  const lang = useLang(); const { can } = useAuth(); const params = useSearchParams();
  const wanted = TOPICS.find((x) => x.id === params.get("topic"));
  const mine: Persona = can("catalog.manage") || can("locations.manage") || can("retention.manage") || can("settings.manage") ? "run" : can("tickets.read_team") || can("tickets.read_all") ? "work" : "ask";
  const [persona, setPersona] = useState<Persona>(wanted?.persona ?? mine); const [q, setQ] = useState("");
  const query = q.trim().toLowerCase();

  // a link such as /help?topic=work-a-ticket opens the right tab and scrolls to the topic
  useEffect(() => {
    if (!wanted) return;
    setPersona(wanted.persona);
    const el = document.getElementById(wanted.id); el?.scrollIntoView({ block: "start" });
  }, [wanted]);

  const shown = useMemo(() => {
    if (!query) return TOPICS.filter((x) => x.persona === persona);
    return TOPICS.filter((x) => [x.title[lang], x.summary[lang], ...x.steps[lang], ...(x.tips?.[lang] ?? [])].join(" ").toLowerCase().includes(query));
  }, [query, persona, lang]);

  return (
    <>
      <PageHeader title={t("Help")} description={t("How to do the things you do here, written for the three kinds of people who use the helpdesk.")} />
      <div className="stack">
        <Field label={t("Search help")} htmlFor="help-q">
          <TextInput id="help-q" type="search" icon={<Search size={15} aria-hidden />} placeholder={t("e.g. approval, deadline, file")} value={q} onChange={(e) => setQ(e.target.value)} />
        </Field>
        {!query && <div role="group" aria-label={t("Who are you today?")} className="row" style={{ gap: 8, flexWrap: "wrap" }}>
          {PERSONAS.map((p) => (
            <Button key={p.key} variant={persona === p.key ? "primary" : "outline"} aria-pressed={persona === p.key} onClick={() => setPersona(p.key)}>{p.title[lang]}</Button>
          ))}
        </div>}
        {!query && <p style={{ margin: 0 }}>{PERSONAS.find((p) => p.key === persona)!.blurb[lang]}</p>}
        {query && <p role="status" style={{ margin: 0 }}>{shown.length === 0 ? t("Nothing in the help matches “{q}”.", { q }) : t("{n} topics match “{q}”.", { n: shown.length, q })}</p>}
        <div className="stack">{shown.map((x) => <Topic key={x.id} topic={x} lang={lang} showPersona={!!query} />)}</div>
        <p className="field-help" style={{ margin: 0 }}>{t("Something missing or wrong? Tell your helpdesk administrator.")}</p>
      </div>
    </>
  );
}

export default function HelpPage() { return <Suspense><Help /></Suspense>; }
