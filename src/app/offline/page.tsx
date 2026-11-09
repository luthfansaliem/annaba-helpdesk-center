import { WifiOff } from "lucide-react";
import { Logo } from "@/components/logo";

/** Static, non-personalised fallback shown when a navigation fails offline. Safe to precache. */
export const dynamic = "force-static";
export const metadata = { title: "Offline – Annaba Digital" };

export default function OfflinePage() {
  return (
    <main style={{ minHeight: "100vh", display: "grid", placeItems: "center", padding: 24, textAlign: "center" }}>
      <div style={{ maxWidth: 420 }}>
        <div style={{ display: "flex", justifyContent: "center", marginBottom: 20 }}><Logo size={36} /></div>
        <span className="state-ic warn"><WifiOff size={26} /></span>
        <h1 style={{ fontSize: 22, marginBottom: 8 }}>You&apos;re offline</h1>
        <p style={{ color: "var(--muted)", lineHeight: 1.6, marginBottom: 18 }}>
          Annaba Digital needs a connection to show your account data. For your security, user and access data is never stored for offline use.
        </p>
        <a className="btn primary" href="/dashboard">Try again</a>
      </div>
    </main>
  );
}
