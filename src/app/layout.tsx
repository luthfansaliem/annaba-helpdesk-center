import type { Metadata, Viewport } from "next";
import { headers } from "next/headers";
import { Inter } from "next/font/google";
import "./globals.css";
import { ToastProvider } from "@/lib/toast";
import { AuthProvider } from "@/lib/auth";
import { LangBoundary } from "@/i18n/boundary";
import { ServiceWorkerRegister } from "@/components/sw-register";

const inter = Inter({ subsets: ["latin"], variable: "--font-inter" });

export const metadata: Metadata = {
  title: "Annaba Digital – Identity & Access",
  description: "Enterprise authentication and user management",
  applicationName: "Annaba Digital",
  appleWebApp: { capable: true, title: "Annaba Digital", statusBarStyle: "default" },
  formatDetection: { telephone: false },
};
export const viewport: Viewport = { themeColor: "#0b62e6", width: "device-width", initialScale: 1 };

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const nonce = (await headers()).get("x-nonce") ?? undefined;
  return (
    <html lang="en" className={inter.variable} suppressHydrationWarning>
      <head>
        {/* Sets the theme before first paint so there is no flash: the saved choice, else the device's light/dark setting. */}
        <script nonce={nonce} dangerouslySetInnerHTML={{ __html: `(function(){try{var t=localStorage.getItem('annaba-digital-theme')||'system';var d=t==='dark'||(t==='system'&&window.matchMedia('(prefers-color-scheme: dark)').matches);document.documentElement.dataset.theme=d?'dark':'light'}catch(e){}})()` }} />
      </head>
      <body><ToastProvider><AuthProvider><LangBoundary>{children}</LangBoundary></AuthProvider></ToastProvider><ServiceWorkerRegister /></body>
    </html>
  );
}
