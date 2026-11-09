"use client";
import { Clock } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

export default function SessionExpiredPage() {
  return (
    <AuthFrame>
      <AuthHeading icon={<Clock size={26} />} tone="warn" title={t("Session expired")} sub={t("For your security you were signed out after a period of inactivity.")} />
      <Button variant="primary" href="/login?expired=1" style={{ width: "100%", height: 46 }}>{t("Sign in again")}</Button>
    </AuthFrame>
  );
}
