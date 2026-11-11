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
  title: "Annaba Helpdesk",
  description: "General Affairs helpdesk: requests, work orders, assets, vendors and maintenance",
  applicationName: "Annaba Helpdesk",
  appleWebApp: { capable: true, title: "Annaba Helpdesk", statusBarStyle: "default" },
  formatDetection: { telephone: false },
};
export const viewport: Viewport = { themeColor: "#0b62e6", width: "device-width", initialScale: 1 };

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const nonce = (await headers()).get("x-nonce") ?? undefined;
  return (
    <html lang="en" className={inter.variable} suppressHydrationWarning>
      <head>
        {/* Sets the theme before first paint so there is no flash: the saved choice, else the device's light/dark setting.
            Browsers hide a nonce from the DOM once the page is parsed (it reads back as ""), so React would report a hydration mismatch on every page: the warning is suppressed for this one element only. */}
        <script nonce={nonce} suppressHydrationWarning dangerouslySetInnerHTML={{ __html: `(function(){try{var t=localStorage.getItem('annaba-digital-theme')||'system';var d=t==='dark'||(t==='system'&&window.matchMedia('(prefers-color-scheme: dark)').matches);document.documentElement.dataset.theme=d?'dark':'light'}catch(e){}})()` }} />
      </head>
      <body><ToastProvider><AuthProvider><LangBoundary>{children}</LangBoundary></AuthProvider></ToastProvider><ServiceWorkerRegister /></body>
    </html>
  );
}
