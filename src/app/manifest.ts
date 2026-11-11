import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Annaba Helpdesk",
    short_name: "Helpdesk",
    description: "General Affairs helpdesk: requests, work orders, assets, vendors and maintenance",
    start_url: "/dashboard",
    scope: "/",
    display: "standalone",
    orientation: "any",
    background_color: "#f5f8fe",
    theme_color: "#0b62e6",
    categories: ["business", "productivity"],
    icons: [
      { src: "/icons/192", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/512", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/maskable-512", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
    shortcuts: [
      { name: "New request", short_name: "Request", url: "/requests/new" },
      { name: "My requests", short_name: "Requests", url: "/requests" },
    ],
  };
}
