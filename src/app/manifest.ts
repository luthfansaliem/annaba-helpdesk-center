import type { MetadataRoute } from "next";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "Annaba Digital Identity & Access",
    short_name: "Annaba Digital",
    description: "Enterprise authentication and user management workspace",
    start_url: "/dashboard",
    scope: "/",
    display: "standalone",
    orientation: "any",
    background_color: "#f5f8fe",
    theme_color: "#0b62e6",
    categories: ["business", "productivity", "security"],
    icons: [
      { src: "/icons/192", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/512", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/maskable-512", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
    shortcuts: [
      { name: "Users", short_name: "Users", url: "/users/all" },
      { name: "My Sessions", short_name: "Sessions", url: "/sessions" },
    ],
  };
}
