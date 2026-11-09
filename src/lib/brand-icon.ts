import { readFile } from "node:fs/promises";
import path from "node:path";

/** Brand shield as a data URL for generated icons (ImageResponse cannot fetch a relative URL at build time). */
export async function markDataUrl(tone: "blue" | "white"): Promise<string> {
  const buf = await readFile(path.join(process.cwd(), "public", "brand", tone === "blue" ? "mark-light.png" : "mark-dark.png"));
  return `data:image/png;base64,${buf.toString("base64")}`;
}
