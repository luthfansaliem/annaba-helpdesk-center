"use client";

import Link from "next/link";
import { Building, Logo } from "@/components/logo";
import { setLang, t, useLang } from "@/i18n";

/** Language switch for screens before sign-in; once signed in the language follows the person's own setting. */
function LangSwitch() {
  const lang = useLang();
  return (
    <div className="lang-switch" role="group" aria-label={t("Language")}>
      <button type="button" aria-pressed={lang === "en"} onClick={() => setLang("en")} lang="en">English</button>
      <button type="button" aria-pressed={lang === "id"} onClick={() => setLang("id")} lang="id">Indonesia</button>
    </div>
  );
}

/** Shared frame for all public auth screens (hero left on desktop, card right). */
export function AuthFrame({ children, hero = true, signInLink = false }: { children: React.ReactNode; hero?: boolean; signInLink?: boolean }) {
  return (
    <div className={"login auth" + (hero ? "" : " nohero")}>
      {hero && (
        <section className="login-left">
          <Logo size={44} />
          <span className="tag">{t("General Affairs Helpdesk")}</span>
          <h1>{t("Ask once,")}<br />{t("we take it from there")}</h1>
          <p className="lead">{t("Repairs, rooms, supplies, visitors and vehicles: request it, follow it and rate it, while the team keeps assets, vendors and maintenance on track.")}</p>
          <Building className="building" />
        </section>
      )}
      <section className="login-right">
        <LangSwitch />
        {!hero && <div className="lang" style={{ alignSelf: "flex-start" }}><Logo size={30} /></div>}
        <div className="login-card auth-card">{children}</div>
        {signInLink && <p className="auth-foot"><Link href="/login">{t("Sign in")}</Link></p>}
      </section>
    </div>
  );
}

export function AuthHeading({ icon, title, sub, tone }: { icon?: React.ReactNode; title: string; sub?: React.ReactNode; tone?: "ok" | "warn" | "err" }) {
  return (
    <div className="auth-head">
      {icon && <span className={"state-ic " + (tone ?? "")}>{icon}</span>}
      <h2>{title}</h2>
      {sub && <p className="sub">{sub}</p>}
    </div>
  );
}
