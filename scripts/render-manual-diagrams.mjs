#!/usr/bin/env node
/**
 * Draws the four process diagrams of the user manual (docs/68-helpdesk-user-manual.md, chapter 12) as PNG pictures, in the manual's colours.
 *
 *   node scripts/render-manual-diagrams.mjs
 *
 * The Markdown keeps mermaid blocks (GitHub draws those); the PDF uses these pictures instead, because the PDF builder's Chromium cannot draw mermaid.
 * Each diagram is a small grid: boxes sit at (column, row), arrows join them, optionally with a label. Needs only the Chromium that Playwright installs.
 */
import { mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "@playwright/test";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const OUT = join(ROOT, "docs/manual");
const C = { navy: "#0e1b4d", blue: "#0b62e6", soft: "#eaf2ff", line: "#9fb3d8", green: "#e7f6ee", greenInk: "#0f7a43", red: "#fdecec", redInk: "#b42318", amber: "#fff4dd", amberInk: "#8a5400", grey: "#f2f4f8" };

/** kind: start | step | ask | good | bad | wait */
const DIAGRAMS = [
  {
    file: "90-alur-permintaan", cols: 5, rows: 3,
    nodes: {
      a: [0, 1, "Karyawan mengajukan", "start"], b: [1, 1, "Perlu persetujuan?", "ask"], c: [1, 0, "Atasan memutuskan", "step"], x: [0, 0, "Ditolak", "bad"],
      d: [2, 1, "Ditugaskan ke tim", "step"], e: [3, 1, "Sedang dikerjakan", "step"], f: [3, 2, "Ditunda", "wait"], g: [4, 1, "Selesai", "good"], h: [4, 0, "Ditutup dan dinilai", "good"],
    },
    edges: [["a", "b"], ["b", "c", "ya"], ["c", "x", "tolak"], ["c", "d", "setuju", "hv"], ["b", "d", "tidak"], ["d", "e"], ["e", "f", "tunda"], ["f", "e", "lanjut", "back"], ["e", "g"], ["g", "h", "dikonfirmasi / 3 hari"], ["g", "e", "belum beres", "over"]],
  },
  {
    file: "91-alur-perintah-kerja", cols: 4, rows: 2,
    nodes: { a: [0, 0, "Baru", "start"], b: [1, 0, "Sedang dikerjakan", "step"], c: [1, 1, "Ditunda", "wait"], d: [2, 0, "Selesai", "good"], e: [3, 0, "Tiket ikut selesai", "good"] },
    edges: [["a", "b", "Mulai"], ["b", "c", "Tunda"], ["c", "b", "Lanjutkan", "back"], ["b", "d", "Selesaikan"], ["d", "e", "wajib selesai"]],
  },
  {
    file: "92-alur-perlengkapan", cols: 5, rows: 2,
    nodes: { a: [0, 0, "Karyawan meminta", "start"], b: [1, 0, "Di atas batas nilai?", "ask"], c: [1, 1, "Atasan menyetujui", "step"], d: [2, 0, "Akan diserahkan", "step"], e: [3, 1, "Diserahkan sebagian", "wait"], f: [4, 0, "Diserahkan", "good"] },
    edges: [["a", "b"], ["b", "c", "ya"], ["c", "d"], ["b", "d", "tidak"], ["d", "e", "stok kurang"], ["e", "f", "sisanya"], ["d", "f", "semua ada"]],
  },
  {
    file: "93-alur-tamu", cols: 4, rows: 2,
    nodes: { a: [0, 0, "Karyawan mendaftarkan tamu", "start"], b: [1, 0, "Diharapkan datang", "step"], c: [2, 0, "Di lokasi", "good"], d: [3, 0, "Sudah pulang", "good"], e: [1, 1, "Tidak datang", "bad"] },
    edges: [["a", "b"], ["b", "c", "Check-in"], ["c", "d", "Catat keluar"], ["b", "e", "tidak muncul"]],
  },
];

const W = 210, H = 70, GX = 100, GY = 56, PAD = 24;
const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;");
function svg(d) {
  const width = PAD * 2 + d.cols * W + (d.cols - 1) * GX, height = PAD * 2 + d.rows * H + (d.rows - 1) * GY;
  const box = (k) => { const [c, r] = d.nodes[k]; return { x: PAD + c * (W + GX), y: PAD + r * (H + GY), w: W, h: H }; };
  const style = (kind) => ({ start: [C.blue, "#fff", C.blue], step: ["#fff", C.navy, C.blue], ask: [C.soft, C.navy, C.blue], good: [C.green, C.greenInk, "#7cc79f"], bad: [C.red, C.redInk, "#efa3a3"], wait: [C.amber, C.amberInk, "#f2c46d"] })[kind];
  const out = [`<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" font-family="Inter, Helvetica, Arial, sans-serif">`,
    `<defs><marker id="arr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="${C.line}"/></marker></defs>`, `<rect width="100%" height="100%" fill="#fff"/>`];
  const labels = [];                                             // drawn last, on top of the boxes
  for (const [from, to, label, how] of d.edges) {
    const a = box(from), b = box(to);
    let path, lx, ly;
    if (how === "back") {                                        // return arrow on the other side of a vertical pair
      const x = a.x + a.w * 0.72, y1 = a.y, y2 = b.y + b.h;
      path = `M${x},${y1} L${x},${y2}`; lx = x + 6; ly = (y1 + y2) / 2 + 4;
    } else if (how === "over") {                                 // loop back through the gap above the row, from right to left
      const y = a.y - GY / 2, sx = a.x + a.w * 0.12, ex = b.x + b.w * 0.72;
      path = `M${sx},${a.y} L${sx},${y} L${ex},${y} L${ex},${b.y}`; lx = (sx + ex) / 2; ly = y - 6;
    } else if (a.y === b.y) {                                    // same row, either direction
      const sx = b.x > a.x ? a.x + a.w : a.x, ex = b.x > a.x ? b.x : b.x + b.w;
      path = `M${sx},${a.y + a.h / 2} L${ex},${b.y + b.h / 2}`; lx = (sx + ex) / 2; ly = a.y + a.h / 2 - 8;
    } else if (a.x === b.x) {                                    // same column
      const x = a.x + a.w * 0.28, down = b.y > a.y;
      path = down ? `M${x},${a.y + a.h} L${x},${b.y}` : `M${x},${a.y} L${x},${b.y + b.h}`; lx = x + 8; ly = (down ? a.y + a.h + b.y : a.y + b.y + b.h) / 2 + 4;
    } else if (b.y > a.y && how === "hv") {                      // elbow going down: across first, then down into the top of the target
      const sx = b.x > a.x ? a.x + a.w : a.x, sy = a.y + a.h / 2, ex = b.x + b.w / 2;
      path = `M${sx},${sy} L${ex},${sy} L${ex},${b.y}`; lx = (sx + ex) / 2; ly = sy - 8;
    } else if (b.y > a.y) {                                      // elbow going down: down first, then across into the side of the target
      const sx = a.x + a.w * 0.72, ey = b.y + b.h / 2, ex = b.x > a.x ? b.x : b.x + b.w;
      path = `M${sx},${a.y + a.h} L${sx},${ey} L${ex},${ey}`; lx = (sx + ex) / 2; ly = ey - 8;
    } else {                                                     // elbow going up: across first, then up into the bottom of the target
      const sx = b.x > a.x ? a.x + a.w : a.x, sy = a.y + a.h / 2, ex = b.x + b.w / 2;
      path = `M${sx},${sy} L${ex},${sy} L${ex},${b.y + b.h}`; lx = (sx + ex) / 2; ly = sy - 8;
    }
    out.push(`<path d="${path}" fill="none" stroke="${C.line}" stroke-width="2" marker-end="url(#arr)"/>`);
    if (label) {
      const anchor = how === "back" || (a.x === b.x && how !== "over") ? "start" : "middle";
      labels.push(`<text x="${lx}" y="${ly}" font-size="15" fill="#5d6b88" text-anchor="${anchor}" paint-order="stroke" stroke="#fff" stroke-width="5">${esc(label)}</text>`);
    }
  }
  for (const [k, [, , text, kind]] of Object.entries(d.nodes)) {
    const b = box(k), [fill, ink, stroke] = style(kind);
    const shape = kind === "ask"
      ? `<path d="M${b.x + 18},${b.y} L${b.x + b.w - 18},${b.y} L${b.x + b.w},${b.y + b.h / 2} L${b.x + b.w - 18},${b.y + b.h} L${b.x + 18},${b.y + b.h} L${b.x},${b.y + b.h / 2} z" fill="${fill}" stroke="${stroke}" stroke-width="1.6"/>`
      : `<rect x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}" rx="12" fill="${fill}" stroke="${stroke}" stroke-width="1.6"/>`;
    const words = text.split(" "), lines = [];
    for (const w of words) { const last = lines.at(-1); if (last && (last + " " + w).length <= 17) lines[lines.length - 1] = last + " " + w; else lines.push(w); }
    const tspans = lines.map((l, i) => `<tspan x="${b.x + b.w / 2}" dy="${i === 0 ? -(lines.length - 1) * 10.5 : 21}">${esc(l)}</tspan>`).join("");
    out.push(shape, `<text x="${b.x + b.w / 2}" y="${b.y + b.h / 2 + 5}" font-size="18" font-weight="700" fill="${ink}" text-anchor="middle">${tspans}</text>`);
  }
  out.push(...labels, "</svg>");
  return { svg: out.join(""), width, height };
}

mkdirSync(OUT, { recursive: true });
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ deviceScaleFactor: 2 });
  for (const d of DIAGRAMS) {
    const { svg: s, width, height } = svg(d);
    await page.setViewportSize({ width, height });
    await page.setContent(`<html><body style="margin:0">${s}</body></html>`);
    await page.screenshot({ path: join(OUT, `${d.file}.png`), clip: { x: 0, y: 0, width, height } });
    console.log(`  ${d.file}.png ${width}×${height}`);
  }
} finally { await browser.close(); }
