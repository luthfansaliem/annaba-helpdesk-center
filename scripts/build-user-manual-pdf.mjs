#!/usr/bin/env node
/**
 * Builds the user manual as a print-ready A4 PDF from docs/68-helpdesk-user-manual.md (pattern taken from the Annaba HRIS manual).
 *
 *   node scripts/build-user-manual-pdf.mjs [output.pdf] [--preview <folder>]
 *
 * --preview also writes every chapter page as a PNG (the same layout the PDF gets), to review the manual without a PDF viewer.
 *
 * Needs nothing beyond the Chromium that Playwright already installs for the e2e tests (npm run test:e2e:install).
 * The Markdown stays the single source: this script only lays it out (cover, table of contents with page numbers,
 * one chapter per page run, numbered figures, page footer). The mermaid flowcharts are replaced, in order, by the pictures
 * listed in DIAGRAMS (drawn by scripts/render-manual-diagrams.mjs), because Chromium cannot draw mermaid on its own.
 */
import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { chromium } from "@playwright/test";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const SOURCE = join(ROOT, "docs/68-helpdesk-user-manual.md");
const cli = process.argv.slice(2);
const PREVIEW = cli.includes("--preview") ? resolve(cli[cli.indexOf("--preview") + 1]) : null;
const positional = cli.filter((a, i) => !a.startsWith("--") && cli[i - 1] !== "--preview");
const OUT = resolve(positional[0] ?? join(ROOT, "docs/manual/Panduan-Pengguna-Annaba-Helpdesk.pdf"));
const DIAGRAMS = [
  { file: "manual/90-alur-permintaan.png", caption: "Alur sebuah permintaan: dari karyawan, lewat persetujuan bila perlu, sampai ditutup dan dinilai." },
  { file: "manual/91-alur-perintah-kerja.png", caption: "Status perintah kerja. Bila semua perintah kerja wajib selesai, tiketnya ikut selesai." },
  { file: "manual/92-alur-perlengkapan.png", caption: "Permintaan perlengkapan: persetujuan atasan di atas batas nilai, lalu diserahkan dari stok, bisa sebagian dulu." },
  { file: "manual/93-alur-tamu.png", caption: "Kedatangan tamu: didaftarkan karyawan, dicatat resepsionis saat datang dan pulang." },
];
// Page box in millimetres. The measuring pass below lays chapters out in columns of exactly the printable area, so keep these in step with @page.
const PAGE = { top: 21, side: 19, bottom: 25, width: 210, height: 297 };
const AREA = { w: PAGE.width - 2 * PAGE.side, h: PAGE.height - PAGE.top - PAGE.bottom };
const fileUrl = (rel) => pathToFileURL(join(ROOT, "docs", rel)).href;

// ---------- Markdown (only the constructs the manual uses) ----------
const esc = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const plain = (s) => s.replace(/\*\*|`/g, "").replace(/(^|\s)\*(\S[^*]*)\*/g, "$1$2");
function inline(src) {
  const codes = [];
  let s = esc(src).replace(/`([^`]+)`/g, (_, c) => { codes.push(c); return `\u0000${codes.length - 1}\u0000`; });
  s = s.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>").replace(/\*(.+?)\*/g, "<em>$1</em>");
  return s.replace(/\u0000(\d+)\u0000/g, (_, i) => {
    const c = codes[+i];
    return /^https?:\/\//.test(c) ? `<a class="url" href="${c}">${c.replace(/^https?:\/\//, "")}</a>` : `<code>${c}</code>`;
  });
}
const cells = (line) => line.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim());

function parse(md) {
  const lines = md.split("\n");
  const blocks = [];
  let i = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (!line.trim()) { i++; continue; }
    let m;
    if ((m = /^(#{1,3})\s+(.*)$/.exec(line))) { blocks.push({ type: `h${m[1].length}`, text: m[2] }); i++; continue; }
    if (line.startsWith("```")) {
      const lang = line.slice(3).trim(); const body = []; i++;
      while (i < lines.length && !lines[i].startsWith("```")) body.push(lines[i++]);
      i++; blocks.push({ type: "code", lang, body: body.join("\n") }); continue;
    }
    if ((m = /^!\[(.*)\]\((.*)\)\s*$/.exec(line))) {
      let caption = m[1]; i++;
      while (i < lines.length && !lines[i].trim()) i++;
      const c = i < lines.length ? /^\*(.+)\*\s*$/.exec(lines[i]) : null; // an italic line right after a picture is its caption
      if (c) { caption = c[1]; i++; }
      blocks.push({ type: "figure", src: m[2], caption }); continue;
    }
    if (line.startsWith("|")) {
      const rows = [];
      while (i < lines.length && lines[i].startsWith("|")) rows.push(cells(lines[i++]));
      blocks.push({ type: "table", head: rows[0], rows: rows.slice(2) }); continue;
    }
    if (/^- /.test(line)) {
      const items = [];
      while (i < lines.length && /^- /.test(lines[i])) items.push(lines[i++].slice(2));
      blocks.push({ type: "ul", items }); continue;
    }
    if (/^\d+\. /.test(line)) {
      const items = [];
      while (i < lines.length && /^\d+\. /.test(lines[i])) items.push(lines[i++].replace(/^\d+\. /, ""));
      blocks.push({ type: "ol", items }); continue;
    }
    const para = [];
    while (i < lines.length && lines[i].trim() && !/^(#{1,3}\s|```|!\[|\||- |\d+\. )/.test(lines[i])) para.push(lines[i++]);
    blocks.push({ type: "p", text: para.join(" ") });
  }
  return blocks;
}

// ---------- Document model: title, date, chapters ----------
function model(blocks) {
  const title = (blocks.find((b) => b.type === "h1")?.text ?? "Panduan Pengguna").replace(/^\d+\s*[—-]\s*/, "");
  const updated = /Diperbarui ([^.]+)\./.exec(blocks.find((b) => b.type === "p")?.text ?? "")?.[1] ?? "";
  const chapters = [];
  for (const b of blocks) {
    if (b.type === "h2") { const m = /^(\d+)\.\s+(.*)$/.exec(b.text); chapters.push({ no: m ? m[1] : String(chapters.length + 1), title: m ? m[2] : b.text, blocks: [] }); }
    else if (chapters.length) chapters.at(-1).blocks.push(b); // what precedes the first chapter is a note for the repository, not for readers
  }
  return { title, updated, chapters };
}

const counters = { figure: 0, diagram: 0 };
function figure(src, caption, kind) {
  counters.figure++;
  return `<figure class="${kind}"><img src="${fileUrl(src)}" alt="${esc(plain(caption))}"><figcaption><b>Gambar ${counters.figure}.</b> ${inline(caption)}</figcaption></figure>`;
}
function chapterHtml(ch) {
  const out = [`<section class="chapter" id="bab-${ch.no}"><header class="chapter-head"><div class="chapter-no">Bab ${ch.no}</div><h2>${inline(ch.title)}</h2></header>`];
  const bs = ch.blocks;
  for (let i = 0; i < bs.length; i++) {
    const b = bs[i];
    if (b.type === "h3") out.push(`<h3>${inline(b.text)}</h3>`);
    else if (b.type === "p") {
      const purpose = /^Gunanya:\s*(.*)$/.exec(b.text);
      if (purpose) out.push(`<div class="purpose"><span class="tag">Gunanya</span><p>${inline(purpose[1].charAt(0).toUpperCase() + purpose[1].slice(1))}</p></div>`);
      else if (/^Yang perlu diketahui:?$/.test(b.text) && bs[i + 1]?.type === "ul") {
        out.push(`<div class="note"><div class="note-title">Yang perlu diketahui</div><ul>${bs[++i].items.map((t) => `<li>${inline(t)}</li>`).join("")}</ul></div>`);
      } else out.push(`<p>${inline(b.text)}</p>`);
    }
    else if (b.type === "ul") out.push(`<ul>${b.items.map((t) => `<li>${inline(t)}</li>`).join("")}</ul>`);
    else if (b.type === "ol") out.push(`<ol class="steps">${b.items.map((t) => `<li>${inline(t)}</li>`).join("")}</ol>`);
    else if (b.type === "table") out.push(`<table class="cols-${b.head.length}"><thead><tr>${b.head.map((c) => `<th>${inline(c)}</th>`).join("")}</tr></thead><tbody>${b.rows.map((r) => `<tr>${r.map((c) => `<td>${inline(c)}</td>`).join("")}</tr>`).join("")}</tbody></table>`);
    else if (b.type === "figure") out.push(figure(b.src, b.caption, "screen"));
    else if (b.type === "code" && b.lang === "mermaid") {
      const d = DIAGRAMS[counters.diagram++];
      if (!d) throw new Error("The manual has more mermaid diagrams than DIAGRAMS lists pictures for.");
      out.push(figure(d.file, d.caption, "diagram"));
    } else if (b.type === "code") out.push(`<pre>${esc(b.body)}</pre>`);
  }
  out.push("</section>");
  return out.join("\n");
}

// ---------- Page design ----------
const CSS = `
@page { size: A4; margin: ${PAGE.top}mm ${PAGE.side}mm ${PAGE.bottom}mm;
  @bottom-left { content: "Panduan Pengguna Annaba Helpdesk"; font: 8pt "Helvetica Neue", Helvetica, Arial, sans-serif; color: #6b7893; vertical-align: top; margin-top: 6mm; padding-top: 3mm; border-top: .5pt solid #dbe3f1; }
  @bottom-center { content: ""; margin-top: 6mm; border-top: .5pt solid #dbe3f1; }
  @bottom-right { content: "Halaman " counter(page) " dari " counter(pages); font: 8pt "Helvetica Neue", Helvetica, Arial, sans-serif; color: #6b7893; vertical-align: top; margin-top: 6mm; padding-top: 3mm; border-top: .5pt solid #dbe3f1; }
}
@page cover { margin: 0; @bottom-left { content: none; border: 0 } @bottom-center { content: none; border: 0 } @bottom-right { content: none; border: 0 } }
* { box-sizing: border-box; }
html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
body { margin: 0; font: 10.5pt/1.55 Inter, "Helvetica Neue", Helvetica, Arial, sans-serif; color: #1c2a4a; }
p { margin: 0 0 3mm; orphans: 3; widows: 3; }
strong { color: #0e1b4d; font-weight: 700; }
a { color: #0b62e6; text-decoration: none; }
code, a.url { font: 9.2pt/1.3 ui-monospace, Menlo, Consolas, monospace; background: #eef2fa; border-radius: 3px; padding: .3pt 3pt; color: #0e1b4d; white-space: nowrap; }
a.url { color: #0a55c9; }

/* cover */
.cover { page: cover; position: relative; width: 210mm; height: 297mm; overflow: hidden; break-after: page; background: #fff; }
.cover .bar { position: absolute; left: 0; top: 0; bottom: 0; width: 9mm; background: #0b62e6; }
.cover .logo { position: absolute; left: 28mm; top: 27mm; width: 58mm; }
.cover .main { position: absolute; left: 28mm; right: 24mm; top: 96mm; }
.cover .eyebrow { font-size: 11pt; font-weight: 700; letter-spacing: .22em; text-transform: uppercase; color: #0b62e6; margin-bottom: 5mm; }
.cover h1 { margin: 0; font-size: 46pt; line-height: 1.05; letter-spacing: -.02em; color: #0e1b4d; }
.cover .rule { width: 22mm; height: 1.6mm; background: #0b62e6; border-radius: 1mm; margin: 9mm 0 8mm; }
.cover .lead { font-size: 14pt; line-height: 1.5; color: #364260; max-width: 135mm; margin: 0; }
.cover .foot { position: absolute; left: 9mm; right: 0; bottom: 0; height: 68mm; background: #0e1b4d; color: #fff; padding: 15mm 24mm 0 19mm; display: grid; grid-template-columns: 1.25fr 1fr 1fr; gap: 9mm; align-content: start; }
.cover .foot .k { font-size: 7.5pt; font-weight: 700; letter-spacing: .16em; text-transform: uppercase; color: #9fc0ff; margin-bottom: 2mm; }
.cover .foot .v { font-size: 10.5pt; line-height: 1.45; color: #fff; }
.cover .foot .line { grid-column: 1 / -1; border-top: .5pt solid rgba(255,255,255,.22); padding-top: 5mm; margin-top: 3mm; font-size: 8.5pt; color: #c9d6f2; }

/* table of contents */
.toc { break-after: page; }
.toc h2 { font-size: 24pt; line-height: 1.15; letter-spacing: -.01em; color: #0e1b4d; margin: 0 0 2mm; }
.toc .sub { color: #5d6b88; margin: 0 0 8mm; }
.toc-row { display: block; padding: 3.1mm 0 2.9mm; border-top: .5pt solid #e3e9f4; break-inside: avoid; color: inherit; }
.toc-row:last-child { border-bottom: .5pt solid #e3e9f4; }
.toc-line { display: flex; align-items: baseline; gap: 3mm; }
.toc-no { flex: 0 0 8mm; font-weight: 700; color: #0b62e6; font-size: 11pt; }
.toc-title { font-weight: 700; color: #0e1b4d; font-size: 11pt; }
.toc-dots { flex: 1; border-bottom: 1pt dotted #b8c4dc; transform: translateY(-2.5pt); }
.toc-page { font-weight: 700; color: #0e1b4d; font-size: 11pt; font-variant-numeric: tabular-nums; }
.toc-subs { margin: .8mm 0 0 11mm; font-size: 8.6pt; line-height: 1.5; color: #5d6b88; }

/* chapters */
.chapter { break-before: page; }
.chapter-head { margin: 0 0 7mm; padding-bottom: 5mm; border-bottom: 1.6pt solid #0b62e6; }
.chapter-no { font-size: 9pt; font-weight: 700; letter-spacing: .2em; text-transform: uppercase; color: #0b62e6; margin-bottom: 1.5mm; }
.chapter h2 { margin: 0; font-size: 23pt; line-height: 1.15; letter-spacing: -.01em; color: #0e1b4d; }
h3 { margin: 7.5mm 0 2.6mm; font-size: 13.5pt; line-height: 1.25; color: #0e1b4d; break-after: avoid; }
.chapter-head + h3 { margin-top: 0; }

ul { margin: 0 0 3.5mm; padding-left: 5.2mm; }
ul li { margin: 0 0 1.5mm; padding-left: .8mm; break-inside: avoid; }
ul li::marker { color: #0b62e6; }
ol.steps { list-style: none; margin: 0 0 3.5mm; padding: 0; counter-reset: step; }
ol.steps li { counter-increment: step; position: relative; padding-left: 9mm; margin: 0 0 2.1mm; break-inside: avoid; min-height: 5.6mm; }
ol.steps li::before { content: counter(step); position: absolute; left: 0; top: .15mm; width: 5.6mm; height: 5.6mm; border-radius: 50%; background: #0b62e6; color: #fff; font-size: 8.4pt; font-weight: 700; line-height: 5.6mm; text-align: center; }

.purpose { display: flex; gap: 3.5mm; align-items: baseline; background: #eaf2ff; border-radius: 2mm; padding: 2.6mm 4mm; margin: 0 0 4mm; break-inside: avoid; break-after: avoid; }
.purpose .tag { flex: none; font-size: 7.6pt; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; color: #0a55c9; }
.purpose p { margin: 0; color: #0e1b4d; }
.note { border-left: 2.4pt solid #f59e0b; background: #fff8e8; border-radius: 0 2mm 2mm 0; padding: 3mm 4mm 1.6mm 4.5mm; margin: 1mm 0 4mm; }
.note { -webkit-box-decoration-break: clone; box-decoration-break: clone; }
.new-page { break-before: page; }
h3.new-page { margin-top: 0; }
.chapter.tight h3 { margin-top: 6mm; }
.chapter.tight .purpose { margin-bottom: 3mm; padding-top: 2.2mm; padding-bottom: 2.2mm; }
.chapter.tight .note { margin-bottom: 3mm; }
.chapter.tight ol.steps li { margin-bottom: 1.6mm; }
.chapter.tight ul li { margin-bottom: 1.1mm; }
.note-title { font-size: 7.6pt; font-weight: 700; letter-spacing: .14em; text-transform: uppercase; color: #8a5400; margin-bottom: 1.8mm; break-after: avoid; }
.note ul { margin-bottom: 1mm; }
.note ul li::marker { color: #c77d00; }

table { width: 100%; border-collapse: collapse; margin: 1mm 0 5mm; font-size: 9.4pt; line-height: 1.45; }
thead { display: table-header-group; }
th { text-align: left; background: #0e1b4d; color: #fff; font-weight: 700; font-size: 8.6pt; letter-spacing: .03em; padding: 2.2mm 3mm; }
td { padding: 2.1mm 3mm; vertical-align: top; border-bottom: .5pt solid #dbe3f1; }
tbody tr:nth-child(even) td { background: #f5f8fe; }
tr { break-inside: avoid; }
td:first-child { font-weight: 600; color: #0e1b4d; }
td:first-child strong { font-weight: 700; }
table.cols-2 td:first-child { width: 34%; }
table.cols-3 td:first-child { width: 22%; }
td code { white-space: normal; }

figure { margin: 4mm 0 5mm; break-inside: avoid; }
figure img { display: block; width: 100%; }
figure.screen img { width: var(--fig, 90%); margin: 0 auto; border: .6pt solid #cfd9ea; border-radius: 1.6mm; }
figure.diagram { border: .6pt solid #dbe3f1; border-radius: 2mm; padding: 2mm 3mm 0; }
figure.diagram img { width: 92%; margin: 0 auto; }
figcaption { margin-top: 2mm; font-size: 8.6pt; line-height: 1.45; color: #5d6b88; text-align: center; }
figure.diagram figcaption { padding-bottom: 2.5mm; }
figcaption b { color: #364260; }
`;

function coverHtml(doc) {
  return `<section class="cover"><div class="bar"></div>
  <img class="logo" src="${pathToFileURL(join(ROOT, "public/brand/logo-full-light.png")).href}" alt="Annaba Digital Indonesia">
  <div class="main"><div class="eyebrow">Panduan Pengguna</div><h1>Annaba Helpdesk</h1><div class="rule"></div>
    <p class="lead">Cara memakai helpdesk General Affairs Annaba, langkah demi langkah: permintaan, pemesanan ruang dan kendaraan, tamu, perlengkapan, perintah kerja, aset, dan laporan.</p></div>
  <div class="foot">
    <div><div class="k">Untuk</div><div class="v">Karyawan, atasan, tim GA, resepsionis, dan administrator</div></div>
    <div><div class="k">Alamat aplikasi</div><div class="v">helpdesk.annabadigital.io</div></div>
    <div><div class="k">Diperbarui</div><div class="v">${esc(doc.updated)}</div></div>
    <div class="line">PT Annaba Digital Indonesia · Gambar layar di panduan ini memakai data contoh.</div>
  </div></section>`;
}
function tocHtml(doc, pages) {
  const rows = doc.chapters.map((ch) => {
    const subs = ch.blocks.filter((b) => b.type === "h3").map((b) => esc(plain(b.text))).join(" &nbsp;·&nbsp; ");
    return `<a class="toc-row" href="#bab-${ch.no}"><div class="toc-line"><span class="toc-no">${ch.no}</span><span class="toc-title">${inline(ch.title)}</span><span class="toc-dots"></span><span class="toc-page">${pages?.[ch.no] ?? "00"}</span></div>${subs ? `<div class="toc-subs">${subs}</div>` : ""}</a>`;
  }).join("\n");
  return `<section class="toc"><h2>Daftar isi</h2><p class="sub">Bab 1 sampai 3 untuk semua orang. Bab berikutnya cukup dibaca sesuai peran Anda.</p>${rows}</section>`;
}
const page = (doc, body) => `<!doctype html><html lang="id"><head><meta charset="utf-8"><title>${esc(doc.title)}</title><style>${CSS}</style></head><body>${body}</body></html>`;

// ---------- Render ----------
const pageCount = (pdf) => (pdf.toString("latin1").match(/\/Type\s*\/Page(?![A-Za-z])/g) ?? []).length;

const doc = model(parse(readFileSync(SOURCE, "utf8")));
const chapters = doc.chapters.map(chapterHtml);
if (counters.diagram !== DIAGRAMS.length) throw new Error(`The manual has ${counters.diagram} mermaid diagrams but DIAGRAMS lists ${DIAGRAMS.length}.`);

const work = mkdtempSync(join(tmpdir(), "manual-pdf-"));
const browser = await chromium.launch();
try {
  const tab = await browser.newPage();
  const render = async (html, extra = {}) => {
    const file = join(work, "page.html");
    writeFileSync(file, html);
    await tab.goto(pathToFileURL(file).href, { waitUntil: "load" });
    return tab.pdf({ preferCSSPageSize: true, printBackground: true, ...extra });
  };
  // Arranging pass. A picture that does not fit in what is left of a page would jump to the next page and leave a hole behind it.
  // Each chapter is laid out on screen in columns the size of the printable area (the browser breaks columns the way it breaks pages),
  // and where a picture leaves such a hole the paragraphs that follow it, up to the next heading, are moved in front of it to fill the page.
  // If a chapter then ends with only a few lines on its last page, its screenshots are made a little smaller until those lines fit.
  const arrange = async (html) => {
    const results = [];
    for (const { fig, tight } of [{ fig: 90 }, { fig: 84 }, { fig: 84, tight: true }, { fig: 78, tight: true }]) {
      const file = join(work, "arrange.html");
      writeFileSync(file, page(doc, `<div id="flow" style="width:${AREA.w}mm;height:${AREA.h}mm;column-width:${AREA.w}mm;column-gap:0;column-fill:auto">${html}</div><style>#flow .new-page{break-before:column}</style>`));
      await tab.goto(pathToFileURL(file).href, { waitUntil: "load" });
      const got = await tab.evaluate(({ w, h, fig, tight }) => {
        const mm = 96 / 25.4, W = w * mm, H = h * mm;
        const flow = document.getElementById("flow"), sec = flow.firstElementChild, origin = flow.getBoundingClientRect();
        sec.style.setProperty("--fig", `${fig}%`);
        sec.classList.toggle("tight", !!tight);
        const pos = (el) => { const r = el.getClientRects(), a = r[0], z = r[r.length - 1]; return { first: Math.round((a.left - origin.left) / W), last: Math.round((z.left - origin.left) / W), bottom: z.bottom - origin.top }; };
        const keepsWithNext = (el) => el.tagName === "H3" || el.classList.contains("purpose");
        const groupStart = (el) => { // the first of the headings and purpose lines that travel with this block
          let g = el;
          while (g.previousElementSibling && keepsWithNext(g.previousElementSibling) && pos(g.previousElementSibling).first === pos(el).first) g = g.previousElementSibling;
          return g;
        };
        for (const el of [...sec.children]) { // in reading order: a change only moves what comes after it
          if (el.tagName === "FIGURE") {
            for (let guard = 0; guard < 12; guard++) {
              const next = el.nextElementSibling;
              if (!next || next.tagName === "H3" || next.tagName === "FIGURE") break;
              const group = groupStart(el), before = group.previousElementSibling;
              if (!before) break;
              const b = pos(before);
              const hole = pos(group).first > b.last ? H - b.bottom : 0;
              if (hole < 26 * mm) break;
              sec.insertBefore(next, el);
            }
          } else if (el.tagName === "TABLE") {
            // a table that starts with only one or two rows at the foot of a page starts on the next page instead
            const t = pos(el);
            if (t.last > t.first && [...el.tBodies[0].rows].filter((r) => pos(r).first === t.first).length <= 2) groupStart(el).classList.add("new-page");
          } else if (el.classList.contains("note")) {
            // a note box may run over a page, but not with a single point left alone on either side
            const n = pos(el), items = [...el.querySelectorAll("li")];
            if (n.last > n.first) {
              const head = items.filter((li) => pos(li).first === n.first).length;
              if (items.length <= 3 || head < 2) groupStart(el).classList.add("new-page");
              else if (items.length - head < 2) items.at(-2).classList.add("new-page");
            }
          }
        }
        const end = pos(sec.lastElementChild);
        return { html: sec.outerHTML, pages: end.last + 1, tail: end.bottom / mm };
      }, { ...AREA, fig, tight });
      got.fit = `screenshots at ${fig}%${tight ? ", tighter spacing" : ""}`;
      results.push(got);
      if (got.pages === 1 || got.tail > 45) break; // the last page is a proper page, not a few stray lines
    }
    return results.find((r) => r.pages === 1 || r.tail > 45) ?? results[0];
  };
  const arranged = [];
  for (const html of chapters) arranged.push(await arrange(html));
  if (PREVIEW) {
    mkdirSync(PREVIEW, { recursive: true });
    const px = 96 / 25.4, W = Math.round(AREA.w * px), H = Math.round(AREA.h * px);
    for (const [i, a] of arranged.entries()) {
      const file = join(work, "preview.html");
      writeFileSync(file, page(doc, `<div id="flow" style="width:${AREA.w}mm;height:${AREA.h}mm;column-width:${AREA.w}mm;column-gap:0;column-fill:auto">${a.html}</div>`));
      await tab.setViewportSize({ width: W * a.pages, height: H });
      await tab.goto(pathToFileURL(file).href, { waitUntil: "load" });
      for (let n = 0; n < a.pages; n++) await tab.screenshot({ path: join(PREVIEW, `bab-${doc.chapters[i].no.padStart(2, "0")}-${n + 1}.png`), clip: { x: n * W, y: 0, width: W, height: H } });
    }
    // the cover (full A4) and the contents page (printable area), without page numbers
    writeFileSync(join(work, "preview.html"), page(doc, coverHtml(doc)));
    await tab.setViewportSize({ width: Math.round(PAGE.width * px), height: Math.round(PAGE.height * px) });
    await tab.goto(pathToFileURL(join(work, "preview.html")).href, { waitUntil: "load" });
    await tab.screenshot({ path: join(PREVIEW, "00-sampul.png") });
    writeFileSync(join(work, "preview.html"), page(doc, `<div style="width:${AREA.w}mm">${tocHtml(doc)}</div>`));
    await tab.setViewportSize({ width: W, height: H });
    await tab.goto(pathToFileURL(join(work, "preview.html")).href, { waitUntil: "load" });
    await tab.screenshot({ path: join(PREVIEW, "00-daftar-isi.png"), fullPage: true });
    await tab.setViewportSize({ width: 1280, height: 720 });
  }

  // Counting pass: how many pages the contents page and each chapter take. Every chapter starts on a new page, so a chapter laid out alone
  // breaks exactly as it does inside the whole document; that gives the page number of each chapter for the table of contents.
  const tocPages = pageCount(await render(page(doc, tocHtml(doc))));
  const starts = {}; let next = 1 + tocPages + 1; // cover is page 1
  for (const [i, ch] of doc.chapters.entries()) { starts[ch.no] = next; next += pageCount(await render(page(doc, arranged[i].html))); }
  const expected = next - 1;

  const pdf = await render(page(doc, coverHtml(doc) + tocHtml(doc, starts) + arranged.map((a) => a.html).join("\n")), { tagged: true, outline: true });
  const total = pageCount(pdf);
  if (total !== expected) throw new Error(`Page numbers in the table of contents cannot be trusted: expected ${expected} pages, the document has ${total}.`);
  writeFileSync(OUT, pdf);
  console.log(`${OUT}\n${total} pages, ${(pdf.length / 1024 / 1024).toFixed(1)} MB; chapters start on pages ${doc.chapters.map((c) => starts[c.no]).join(", ")}`);
  console.log(arranged.map((a, i) => `  bab ${doc.chapters[i].no}: ${a.pages} pages, last page filled ${Math.round(a.tail)} of ${AREA.h} mm, ${a.fit}`).join("\n"));
} finally {
  await browser.close();
}
