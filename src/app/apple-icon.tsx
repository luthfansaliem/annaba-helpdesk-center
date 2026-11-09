import { ImageResponse } from "next/og";
import { markDataUrl } from "@/lib/brand-icon";

export const size = { width: 180, height: 180 };
export const contentType = "image/png";

export default async function AppleIcon() {
  const src = await markDataUrl("white");
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: "#0b62e6" }}>
        <img src={src} width="86" height="104" alt="" />
      </div>
    ),
    size,
  );
}
