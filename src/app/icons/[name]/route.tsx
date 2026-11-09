import { ImageResponse } from "next/og";
import { markDataUrl } from "@/lib/brand-icon";

const SPECS: Record<string, { size: number; maskable: boolean }> = {
  "192": { size: 192, maskable: false },
  "512": { size: 512, maskable: false },
  "maskable-512": { size: 512, maskable: true },
};
export const dynamic = "force-static";
export function generateStaticParams() { return Object.keys(SPECS).map((name) => ({ name })); }

/** Generated app icons (no binary assets to maintain). Maskable variant keeps the mark inside the 80% safe zone. */
export async function GET(_req: Request, { params }: { params: Promise<{ name: string }> }) {
  const spec = SPECS[(await params).name];
  if (!spec) return new Response("Not found", { status: 404 });
  const src = await markDataUrl(spec.maskable ? "white" : "blue");
  const mark = spec.size * (spec.maskable ? 0.5 : 0.62);
  return new ImageResponse(
    (
      <div style={{ width: "100%", height: "100%", display: "flex", alignItems: "center", justifyContent: "center", background: spec.maskable ? "#0b62e6" : "#ffffff", borderRadius: spec.maskable ? 0 : spec.size * 0.22 }}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src={src} width={Math.round(mark * 0.826)} height={mark} alt="" />
      </div>
    ),
    { width: spec.size, height: spec.size },
  );
}
