/**
 * 04 — stock, the supplies desk (ATK) and the money side (cost settings, cost centres, budgets).
 *
 *  - ~47 stock items (ATK, IT supplies, pantry, cleaning, toiletries, spare parts) kept at four places: Gudang Pusat, the Level 3 pantry store, the lobby and the
 *    Bandung branch. Every balance is built from ledger movements only (receipts from named suppliers with invoice numbers, issues to people, adjustments with a
 *    reason, transfers, stock-take corrections) spread over the last ~8 weeks by backdating `stock_movements.at` after the fact. A deterministic generator builds
 *    the history and a simulation proves no balance ever dips below zero and that every item ends on its intended balance.
 *  - 26 supply requests in every state (pending for the manager / for the desk / for the GA supervisor, approved and ready, partly handed over with a back-order,
 *    fully handed over, rejected, cancelled, closed back-order), 3 stock-takes (2 closed with differences posted, 1 OPEN at the lobby for the presenter to finish).
 *  - Cost settings (IDR, labour rate), six cost centres mapped to places, budgets for the current year (by cost centre, site and category).
 *
 * Idempotent: the stock part is skipped when the first item exists; cost centres and budgets are found by code / scope; budgets are recalibrated on every run
 * (see calibrateBudgets: run `--only stock` again after servicedesk so utilisation follows the real work-order costs).
 *
 * Leaves in d.ctx: ctx.stock = { items: { SKU: id }, places: { W, PAN, LOB, BDG }, ids }, ctx.costCenters = { CODE: id }.
 */
import { prevWorkday, at } from "./lib.mjs";

export const name = "stock";

// ---------------------------------------------------------------------------------------------------------------------------------------------------- time helpers
const NOW = new Date();
const WDOFF = Array.from({ length: 48 }, (_, n) => (n === 0 ? 0 : prevWorkday(n)));
/** n-th previous working day at hh:mm (n >= 1), never in the future. */
const wd = (n, h, m = 0) => { const t = at(WDOFF[n], h, m); return t > NOW ? new Date(NOW.getTime() - 3600e3) : t; };
const ago = (minutes) => new Date(NOW.getTime() - minutes * 60e3);
const pad = (x) => String(x).padStart(2, "0");

// ---------------------------------------------------------------------------------------------------------------------------------------------------- the catalogue
const SUP = {
  SDAT: { n: "PT Sinar Dunia Alat Tulis", p: "SDAT" }, MKS: { n: "CV Mitra Kantor Sejahtera", p: "MKS" }, BNP: { n: "PT Boga Niaga Prima", p: "BNP" },
  TSG: { n: "CV Tirta Segar", p: "TSG" }, ACK: { n: "PT Aneka Cipta Kebersihan", p: "ACK" }, SLN: { n: "PT Sumber Listrik Nusantara", p: "SLN" },
  TJM: { n: "Toko Teknik Jaya Mandiri", p: "TJM" },
};
// [sku, name, category, unit, reorderLevel, reorderQuantity, unitCost, supplier, final stock by place, flags]
// fast: also restocked once in the last 6 weeks; manual: history written by hand below (stories the demo depends on)
const ITEMS = [
  ["ATK-A4-80", "Kertas A4 80 gsm", "atk", "rim", 15, 60, 52000, "SDAT", { W: 96, LOB: 20, BDG: 24 }, { fast: 1 }],
  ["ATK-A3-80", "Kertas A3 80 gsm", "atk", "rim", 3, 10, 118000, "SDAT", { W: 14, BDG: 4 }, {}],
  ["ATK-PEN-AE7", "Pulpen Standard AE7 hitam", "atk", "pcs", 36, 240, 3500, "SDAT", { W: 156, LOB: 40, BDG: 48 }, { fast: 1 }],
  ["ATK-PEN-PLT", "Pulpen Pilot BPT-P biru", "atk", "pcs", 24, 120, 6500, "SDAT", { W: 70 }, {}],
  ["ATK-HLT-STB", "Stabilo Boss highlighter kuning", "atk", "pcs", 10, 48, 14000, "SDAT", { W: 36 }, {}],
  ["ATK-MRK-WB", "Spidol whiteboard Snowman hitam", "atk", "pcs", 12, 48, 11500, "SDAT", { W: 9 }, { manual: 1 }],
  ["ATK-MRK-PRM", "Spidol permanent Snowman hitam", "atk", "pcs", 10, 48, 9500, "SDAT", { W: 40 }, {}],
  ["ATK-STP-10", "Isi staples Max No.10", "atk", "box", 12, 60, 3500, "SDAT", { W: 48 }, {}],
  ["ATK-ORD-7", "Ordner Bantex folio 7 cm", "atk", "pcs", 6, 24, 42000, "SDAT", { W: 12 }, { manual: 1 }],
  ["ATK-SNL", "Map snelhecter plastik folio", "atk", "pcs", 30, 200, 4500, "SDAT", { W: 140 }, {}],
  ["ATK-PST-654", "Post-it 3M 654 (76 x 76 mm)", "atk", "pad", 10, 48, 18000, "SDAT", { W: 30, LOB: 12 }, {}],
  ["ATK-LKB-48", "Lakban bening 48 mm", "atk", "roll", 8, 36, 12500, "SDAT", { W: 24 }, {}],
  ["ATK-LEM-21", "Lem stick UHU 21 g", "atk", "pcs", 8, 36, 9500, "SDAT", { W: 18 }, {}],
  ["ATK-AMP-F", "Amplop coklat folio (isi 100)", "atk", "pak", 3, 12, 48000, "SDAT", { W: 9 }, {}],
  ["ATK-BDR-200", "Binder clip No.200", "atk", "box", 6, 24, 7500, "SDAT", { W: 20 }, {}],
  ["ATK-LBL-103", "Label Tom & Jerry No.103", "atk", "pak", 6, 24, 11000, "SDAT", { W: 16 }, {}],
  ["IT-TNR-05A", "Toner HP 05A (CE505A)", "it", "pcs", 2, 4, 1150000, "MKS", { W: 0 }, { manual: 1 }],
  ["IT-TNR-MP2014", "Toner Ricoh MP 2014", "it", "pcs", 2, 4, 685000, "MKS", { W: 6, BDG: 3 }, {}],
  ["IT-USB-32", "Flashdisk Sandisk 32 GB", "it", "pcs", 3, 10, 55000, "MKS", { W: 10 }, {}],
  ["IT-HDMI-3M", "Kabel HDMI 3 meter", "it", "pcs", 3, 10, 65000, "MKS", { W: 7 }, {}],
  ["PAN-KPI-CM", "Kopi sachet Indocafe Coffeemix (isi 30)", "pantry", "box", 4, 20, 38500, "BNP", { W: 18, PAN: 6, LOB: 5 }, { fast: 1 }],
  ["PAN-TEH-SOS", "Teh celup Sosro (isi 25)", "pantry", "box", 6, 36, 9800, "BNP", { W: 30, PAN: 9, LOB: 7 }, { fast: 1 }],
  ["PAN-GUL-1KG", "Gula pasir Gulaku 1 kg", "pantry", "pak", 5, 30, 17500, "BNP", { W: 40, PAN: 3 }, { fast: 1 }],
  ["PAN-SKM-370", "Susu kental manis Frisian Flag 370 g", "pantry", "kaleng", 4, 24, 14500, "BNP", { W: 12, PAN: 5 }, {}],
  ["PAN-AQG-19", "Air minum galon 19 liter", "pantry", "galon", 6, 30, 22000, "TSG", { W: 12, PAN: 4, LOB: 7 }, { fast: 1 }],
  ["PAN-AQC-220", "Air mineral gelas 220 ml (dus isi 48)", "pantry", "dus", 5, 20, 42000, "BNP", { W: 14, LOB: 6, BDG: 6 }, {}],
  ["PAN-GLS-8OZ", "Gelas kertas 8 oz (isi 50)", "pantry", "pak", 6, 24, 14000, "BNP", { W: 20, PAN: 8, LOB: 7 }, { fast: 1 }],
  ["PAN-BSC-KG", "Biskuit Khong Guan assorted (kaleng)", "pantry", "kaleng", 3, 12, 82000, "BNP", { W: 7, LOB: 4 }, {}],
  ["CLN-SBN-5L", "Sabun cair cuci tangan 5 liter", "cleaning", "jerigen", 3, 12, 98000, "ACK", { W: 2 }, {}],
  ["CLN-PEL-800", "Pembersih lantai Super Pell 800 ml", "cleaning", "pcs", 6, 24, 24500, "ACK", { W: 18, BDG: 8 }, {}],
  ["CLN-KTG-60", "Kantong sampah hitam 60 x 100 (isi 25)", "cleaning", "pak", 10, 48, 32000, "ACK", { W: 6 }, { fast: 1 }],
  ["CLN-SRG-KR", "Sarung tangan karet", "cleaning", "pasang", 6, 24, 15000, "ACK", { W: 14 }, {}],
  ["CLN-PGK-500", "Pembersih kaca Glassex 500 ml", "cleaning", "pcs", 5, 24, 28000, "ACK", { W: 10 }, {}],
  ["TLT-TSS-JMB", "Tisu toilet jumbo roll", "toiletries", "roll", 20, 96, 21000, "ACK", { W: 84, LOB: 24, BDG: 24 }, { fast: 1 }],
  ["TLT-PTW-IF", "Tisu handuk interfold (isi 250)", "toiletries", "pak", 10, 48, 16500, "ACK", { W: 30, LOB: 12 }, {}],
  ["TLT-PGH-REF", "Pengharum ruangan otomatis, refill Stella", "toiletries", "pcs", 6, 24, 36000, "ACK", { W: 16, LOB: 8 }, {}],
  ["SPR-LED-9W", "Lampu LED Philips 9 W", "other", "pcs", 10, 48, 28000, "SLN", { W: 0 }, { manual: 1 }],
  ["SPR-TL-18W", "Lampu TL LED 18 W 120 cm", "other", "pcs", 8, 24, 45000, "SLN", { W: 19, BDG: 9 }, {}],
  ["SPR-DWN-12W", "Downlight LED panel 12 W", "other", "pcs", 6, 24, 62000, "SLN", { W: 14 }, {}],
  ["SPR-STK-ARD", "Stop kontak arde Panasonic", "other", "pcs", 5, 20, 29000, "TJM", { W: 11 }, {}],
  ["SPR-SKL-1", "Saklar engkel Panasonic", "other", "pcs", 5, 20, 21000, "TJM", { W: 9 }, {}],
  ["SPR-FLT-CST", "Filter udara AC cassette 2 PK", "other", "pcs", 4, 12, 85000, "TJM", { W: 2 }, {}],
  ["SPR-KPS-35", "Kapasitor AC 35 µF", "other", "pcs", 3, 10, 68000, "TJM", { W: 7 }, {}],
  ["SPR-FRN-R32", "Freon R32 (tabung 3 kg)", "other", "tabung", 2, 6, 420000, "TJM", { W: 3 }, {}],
  ["SPR-REM-UNI", "Remote AC universal", "other", "pcs", 2, 8, 85000, "TJM", { W: 5 }, {}],
  ["SPR-KRN-WST", "Kran wastafel stainless", "other", "pcs", 3, 10, 78000, "TJM", { W: 6 }, {}],
  ["SPR-BAT-AA", "Baterai alkaline AA (isi 4)", "other", "pak", 8, 36, 28000, "TJM", { W: 24, LOB: 10 }, {}],
].map(([sku, nm, cat, unit, level, rq, cost, sup, F, flags]) => ({ sku, name: nm, cat, unit, level, rq, cost, sup, F, ...flags }));
const DESC = {
  "ATK-A4-80": "Kertas fotokopi putih untuk printer dan mesin fotokopi kantor.", "IT-TNR-05A": "Untuk printer HP LaserJet P2035 (lantai 5 dan ruang arsip).",
  "IT-TNR-MP2014": "Untuk mesin fotokopi Ricoh MP 2014 di lantai 3.", "SPR-FLT-CST": "Filter udara untuk AC cassette 2 PK di ruang rapat dan open space.",
  "SPR-LED-9W": "Lampu downlight dan bohlam koridor, gedung utama dan basement.", "PAN-AQG-19": "Untuk dispenser pantry dan lobi, dikirim langsung oleh CV Tirta Segar.",
  "SPR-KPS-35": "Kapasitor kompresor AC split dan cassette.", "SPR-FRN-R32": "Isi ulang freon AC R32, hanya dipakai teknisi.",
};

// ---------------------------------------------------------------------------------------------------------------------------------------------------- deterministic random
const hashStr = (s) => { let h = 2166136261; for (const c of s) { h ^= c.charCodeAt(0); h = Math.imul(h, 16777619); } return h >>> 0; };
const rng = (seed) => { let a = hashStr(seed); return () => { a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; };
const split = (total, k, r) => { k = Math.max(1, Math.min(k, total)); const a = Array(k).fill(1); for (let i = 0; i < total - k; i++) a[Math.floor(r() * k)]++; return a; };
const pick = (r, list) => list[Math.floor(r() * list.length)];

const PEOPLE = [["Rizky Pratama", "Operations"], ["Anita Rahmawati", "HR"], ["Fajar Ramadhan", "Finance"], ["Lina Marlina", "Marketing"], ["Dimas Prakoso", "Operations"], ["Sinta Maharani", "Resepsionis"], ["Putri Anggraini", "General Affairs"]];
const TECH = [["Agus Setiawan", "Teknik AC dan listrik"], ["Bayu Nugroho", "Teknik umum dan kebersihan"]];
const WHY = {
  atk: ["kebutuhan ATK bulanan tim", "cetak dokumen rapat", "persiapan audit internal", "dokumen onboarding karyawan baru", "keperluan presentasi ke klien"],
  it: ["ganti toner printer lantai", "perlengkapan ruang rapat"], pantry: ["rapat divisi", "jamuan tamu", "kegiatan townhall", "lembur akhir bulan"],
  cleaning: ["perawatan rutin lantai", "pembersihan toilet", "area parkir dan tangga darurat"], toiletries: ["isi ulang toilet lantai", "persiapan kunjungan tamu"],
  other: ["perawatan rutin AC", "penggantian lampu koridor", "perbaikan toilet", "ganti lampu ruang rapat"], uniform: ["seragam"],
};
const SAT_REF = { PAN: "Pemakaian pantry Level 3 (rekap mingguan petugas kebersihan)", LOB: "Pemakaian meja resepsionis dan tamu (rekap mingguan Sinta)", BDG: "Pemakaian kantor cabang Bandung (laporan Rina Susanti, admin cabang)" };

// ---------------------------------------------------------------------------------------------------------------------------------------------------- the plan (pure: no API)
const effects = (o, REQ) => {
  switch (o.k) {
    case "receipt": return [{ sku: o.sku, place: o.place, d: o.qty }];
    case "issue": return [{ sku: o.sku, place: o.place, d: -o.qty }];
    case "adjust": return [{ sku: o.sku, place: o.place, d: o.qty }];
    case "transfer": return [{ sku: o.sku, place: o.from, d: -o.qty }, { sku: o.sku, place: o.to, d: o.qty }];
    case "sr_fulfil": return Object.entries(o.lines).map(([sku, q]) => ({ sku, place: REQ[o.key].place, d: -q }));
    case "take_close": return Object.entries(o.deltas).map(([sku, d]) => ({ sku, place: o.place, d }));
    default: return [];
  }
};
const byTime = (a, b) => a.t - b.t || a.seq - b.seq;

export function buildPlan() {
  const plan = []; let seq = 0; const REQ = {};
  const add = (o) => { plan.push({ ...o, seq: seq++ }); return o; };
  const rcpt = (n, h, m, sku, place, qty, cost) => add({ t: wd(n, h, m), k: "receipt", sku, place, qty, cost, by: "hendra" });
  const iss = (n, h, m, sku, place, qty, who, why) => add({ t: wd(n, h, m), k: "issue", sku, place, qty, ref: `Untuk ${who} · ${why}`, by: "hendra" });
  const adj = (n, h, m, sku, place, qty, why) => add({ t: wd(n, h, m), k: "adjust", sku, place, qty, ref: why, by: "putri" });

  // ----- hand-written stories (the demo relies on these exact balances)
  // Spidol whiteboard: runs out, a back-order is closed, restocked, ends low
  rcpt(40, 9, 15, "ATK-MRK-WB", "W", 40, 11000);
  iss(36, 12, 10, "ATK-MRK-WB", "W", 12, "Putri Anggraini (General Affairs)", "ruang rapat Garuda dan Merak");
  iss(31, 9, 30, "ATK-MRK-WB", "W", 12, "Dimas Prakoso (Operations)", "workshop tim Operations");
  rcpt(22, 10, 20, "ATK-MRK-WB", "W", 48, 11500);
  iss(20, 10, 40, "ATK-MRK-WB", "W", 10, "Rizky Pratama (Operations)", "rapat mingguan");
  iss(17, 14, 5, "ATK-MRK-WB", "W", 14, "Anita Rahmawati (HR)", "sesi pelatihan karyawan baru");
  adj(12, 15, 10, "ATK-MRK-WB", "W", 3, "Ditemukan 3 spidol di lemari arsip lantai 3 saat merapikan");
  iss(11, 9, 50, "ATK-MRK-WB", "W", 10, "Fajar Ramadhan (Finance)", "rapat penutupan buku");
  iss(6, 11, 30, "ATK-MRK-WB", "W", 8, "Lina Marlina (Marketing)", "brainstorming kampanye");
  // Ordner: partly handed over, restocked, handed over in full
  rcpt(40, 9, 40, "ATK-ORD-7", "W", 10, 40000);
  rcpt(8, 10, 30, "ATK-ORD-7", "W", 16, 42000);
  // Toner HP 05A: runs out on the partly handed-over request
  rcpt(40, 10, 5, "IT-TNR-05A", "W", 6, 1150000);
  iss(36, 11, 0, "IT-TNR-05A", "W", 1, "Sinta Maharani (Resepsionis)", "printer meja resepsionis");
  iss(30, 10, 15, "IT-TNR-05A", "W", 1, "Putri Anggraini (General Affairs)", "printer ruang GA");
  iss(19, 13, 20, "IT-TNR-05A", "W", 1, "Hendra Kusuma (General Affairs)", "printer ruang manajer GA");
  iss(12, 9, 45, "IT-TNR-05A", "W", 1, "Dimas Prakoso (Operations)", "printer tim Operations");
  // Lampu LED 9 W: ends at zero after a partly handed-over request
  rcpt(40, 10, 30, "SPR-LED-9W", "W", 36, 28000);
  iss(34, 9, 10, "SPR-LED-9W", "W", 6, "Bayu Nugroho (Teknik umum)", "ganti lampu koridor lantai 3 dan 5");
  iss(30, 15, 0, "SPR-LED-9W", "W", 4, "Agus Setiawan (Teknik AC dan listrik)", "lampu ruang rapat Merak");
  iss(18, 10, 25, "SPR-LED-9W", "W", 6, "Agus Setiawan (Teknik AC dan listrik)", "lampu basement dan parkir");
  adj(16, 14, 40, "SPR-LED-9W", "W", -1, "Pecah saat dipindahkan dari rak ke troli teknisi");
  iss(9, 13, 10, "SPR-LED-9W", "W", 5, "Bayu Nugroho (Teknik umum)", "area parkir dan tangga darurat");

  // ----- adjustments with a reason (supervisor)
  adj(26, 11, 15, "ATK-A4-80", "W", -2, "Rusak terkena rembesan air di rak gudang bagian belakang, dibuang");
  adj(17, 10, 30, "PAN-GUL-1KG", "PAN", -1, "Kemasan bocor dan basah, tidak layak pakai");
  adj(18, 13, 45, "PAN-KPI-CM", "PAN", 2, "Koreksi pencatatan: 2 box dari kiriman sebelumnya belum tercatat");
  adj(19, 9, 20, "PAN-AQG-19", "PAN", -1, "Galon bocor, dikembalikan ke CV Tirta Segar");
  adj(22, 15, 40, "PAN-BSC-KG", "W", -1, "Kaleng penyok dan mendekati tanggal kedaluwarsa, dibuang");
  adj(24, 11, 50, "CLN-SRG-KR", "W", -2, "Sarung tangan robek saat bongkar muat");
  adj(27, 15, 30, "SPR-KRN-WST", "W", -1, "Kran cacat produksi, diretur ke Toko Teknik Jaya Mandiri");

  // ----- supply requests
  const R = (key, o) => { REQ[key] = { key, place: "W", ...o }; const q = REQ[key];
    add({ t: q.tc, k: "sr_create", key });
    if (q.decide) add({ t: q.decide.t, k: "sr_decide", key });
    for (const f of q.fulfil ?? []) add({ t: f.t, k: "sr_fulfil", key, by: f.by ?? "putri", lines: f.lines });
    if (q.cancel) add({ t: q.cancel.t, k: "sr_cancel", key });
    if (q.close) add({ t: q.close.t, k: "sr_close", key });
  };
  // history
  R("S1", { who: "rizky", tc: wd(36, 10, 5), note: "ATK bulanan tim Operations", lines: [["ATK-A4-80", 4], ["ATK-PEN-AE7", 6], ["ATK-SNL", 10], ["ATK-PST-654", 2]], expect: "open", fulfil: [{ t: wd(35, 9, 40), lines: { "ATK-A4-80": 4, "ATK-PEN-AE7": 6, "ATK-SNL": 10, "ATK-PST-654": 2 } }] });
  R("S2", { who: "anita", tc: wd(33, 11, 20), note: "Map dan ordner untuk berkas personalia", lines: [["ATK-ORD-7", 2], ["ATK-SNL", 10], ["ATK-A4-80", 2], ["ATK-LKB-48", 3]], expect: "open", fulfil: [{ t: wd(32, 14, 0), lines: { "ATK-ORD-7": 2, "ATK-SNL": 10, "ATK-A4-80": 2, "ATK-LKB-48": 3 } }] });
  R("S3", { who: "fajar", tc: wd(29, 9, 30), note: "Kebutuhan penutupan buku kuartal III", lines: [["ATK-A4-80", 7], ["ATK-AMP-F", 1], ["ATK-LBL-103", 2], ["ATK-BDR-200", 2], ["ATK-STP-10", 3]], expect: "pending", appr: "dimas",
    decide: { t: wd(29, 11, 15), by: "dimas", ok: true, note: "Disetujui, untuk penutupan buku kuartal." }, fulfil: [{ t: wd(28, 10, 0), lines: { "ATK-A4-80": 7, "ATK-AMP-F": 1, "ATK-LBL-103": 2, "ATK-BDR-200": 2, "ATK-STP-10": 3 } }] });
  R("S4", { who: "lina", tc: wd(27, 10, 0), note: "Materi workshop kampanye akhir tahun", lines: [["ATK-MRK-WB", 24], ["ATK-A3-80", 2]], expect: "pending", appr: "dimas",
    decide: { t: wd(27, 13, 30), by: "dimas", ok: true, note: null }, fulfil: [{ t: wd(26, 10, 30), lines: { "ATK-MRK-WB": 16, "ATK-A3-80": 2 } }],
    close: { t: wd(24, 15, 0), by: "putri", note: "Sisa 8 spidol tidak diadakan: stok habis dan workshop sudah selesai." } });
  R("S5", { who: "bayu", tc: wd(25, 8, 50), note: "Perlengkapan kebersihan lantai 3 dan 5", lines: [["CLN-KTG-60", 4], ["CLN-PEL-800", 4], ["CLN-SRG-KR", 6], ["CLN-PGK-500", 2]], expect: "open", fulfil: [{ t: wd(25, 10, 40), lines: { "CLN-KTG-60": 4, "CLN-PEL-800": 4, "CLN-SRG-KR": 6, "CLN-PGK-500": 2 } }] });
  R("S15", { who: "sinta", place: "LOB", tc: wd(22, 13, 30), note: "Persediaan tamu di lobi minggu ini", lines: [["PAN-AQC-220", 2], ["TLT-TSS-JMB", 6], ["TLT-PGH-REF", 2]], expect: "open", fulfil: [{ t: wd(21, 10, 0), lines: { "PAN-AQC-220": 2, "TLT-TSS-JMB": 6, "TLT-PGH-REF": 2 } }] });
  R("S6", { who: "dimas", tc: wd(23, 14, 10), note: "Jamuan rapat pimpinan bulanan", lines: [["PAN-BSC-KG", 3], ["PAN-KPI-CM", 4], ["PAN-SKM-370", 2]], expect: "pending", appr: null,
    decide: { t: wd(23, 15, 0), by: "putri", ok: true, note: "Disetujui GA untuk rapat pimpinan." }, fulfil: [{ t: wd(22, 10, 0), lines: { "PAN-BSC-KG": 3, "PAN-KPI-CM": 4, "PAN-SKM-370": 2 } }] });
  R("S7", { who: "agus", tc: wd(21, 9, 15), note: "Penggantian lampu koridor dan ruang rapat lantai 5", lines: [["SPR-LED-9W", 6], ["SPR-TL-18W", 4], ["SPR-DWN-12W", 2]], expect: "pending", appr: "putri",
    decide: { t: wd(21, 9, 40), by: "putri", ok: true, note: null }, fulfil: [{ t: wd(20, 11, 0), by: "hendra", lines: { "SPR-LED-9W": 6, "SPR-TL-18W": 4, "SPR-DWN-12W": 2 } }] });
  R("S8", { who: "rizky", tc: wd(19, 10, 30), note: "Toner untuk printer tim Operations", lines: [["IT-TNR-05A", 3]], expect: "pending", appr: "dimas",
    decide: { t: wd(19, 15, 45), by: "dimas", ok: false, note: "Printer lantai 3 memakai Ricoh MP 2014, bukan HP. Mohon ajukan ulang toner Ricoh, cukup 1 pcs." } });
  R("S9", { who: "rizky", tc: wd(18, 9, 20), note: "Toner Ricoh untuk mesin fotokopi lantai 3", lines: [["IT-TNR-MP2014", 1]], expect: "pending", appr: "dimas",
    decide: { t: wd(18, 11, 0), by: "dimas", ok: true, note: null }, fulfil: [{ t: wd(17, 10, 10), lines: { "IT-TNR-MP2014": 1 } }] });
  R("S10", { who: "fajar", tc: wd(16, 15, 10), note: "Kertas A3 untuk laporan audit", lines: [["ATK-A3-80", 4]], expect: "pending", appr: "dimas", cancel: { t: wd(15, 9, 5) } });
  R("S11", { who: "anita", tc: wd(14, 8, 45), note: "Starter kit karyawan baru (3 orang)", lines: [["ATK-PEN-PLT", 6], ["ATK-PEN-AE7", 6], ["ATK-SNL", 6], ["ATK-PST-654", 3], ["ATK-HLT-STB", 4], ["ATK-LEM-21", 3]], expect: "open", fulfil: [{ t: wd(14, 13, 20), lines: { "ATK-PEN-PLT": 6, "ATK-PEN-AE7": 6, "ATK-SNL": 6, "ATK-PST-654": 3, "ATK-HLT-STB": 4, "ATK-LEM-21": 3 } }] });
  R("S12", { who: "lina", place: "PAN", tc: wd(12, 9, 30), note: "Konsumsi acara townhall, diambil di pantry lantai 3", lines: [["PAN-TEH-SOS", 4], ["PAN-KPI-CM", 3], ["PAN-GUL-1KG", 2], ["PAN-GLS-8OZ", 4]], expect: "open", fulfil: [{ t: wd(11, 10, 15), lines: { "PAN-TEH-SOS": 4, "PAN-KPI-CM": 3, "PAN-GUL-1KG": 2, "PAN-GLS-8OZ": 4 } }] });
  R("S13", { who: "fajar", tc: wd(11, 11, 0), note: "Ordner arsip pajak tahun berjalan", lines: [["ATK-ORD-7", 12]], expect: "pending", appr: "dimas",
    decide: { t: wd(11, 14, 30), by: "dimas", ok: true, note: "Setuju. Kalau stok kurang, sisanya menyusul." }, fulfil: [{ t: wd(10, 10, 0), lines: { "ATK-ORD-7": 8 } }, { t: wd(7, 9, 30), lines: { "ATK-ORD-7": 4 } }] });
  R("S14", { who: "rizky", tc: wd(9, 11, 40), note: "Flashdisk dan kabel HDMI untuk presentasi", lines: [["IT-USB-32", 3], ["IT-HDMI-3M", 2]], expect: "open",
    close: { t: wd(9, 14, 0), by: "putri", note: "Perangkat IT diadakan oleh tim IT, bukan dari stok GA. Silakan buat tiket Permintaan perangkat." } });
  // live
  R("L5", { who: "fajar", tc: wd(5, 10, 0), note: "Toner untuk 4 printer lantai 5 dan ruang arsip", lines: [["IT-TNR-05A", 4]], expect: "pending", appr: "dimas",
    decide: { t: wd(5, 14, 0), by: "dimas", ok: true, note: "Disetujui, untuk 4 printer lantai 5 dan arsip." }, fulfil: [{ t: wd(4, 10, 30), lines: { "IT-TNR-05A": 2 } }] });
  R("L6", { who: "agus", tc: wd(4, 14, 0), note: "Lampu LED untuk basement dan tangga darurat", lines: [["SPR-LED-9W", 12]], expect: "open", fulfil: [{ t: wd(3, 9, 50), lines: { "SPR-LED-9W": 8 } }] });
  R("Lf", { who: "fajar", tc: wd(2, 11, 30), note: "Perlengkapan surat-menyurat akhir tahun", lines: [["ATK-AMP-F", 6], ["ATK-LBL-103", 6], ["ATK-STP-10", 10], ["ATK-LEM-21", 6]], expect: "pending", appr: "dimas" });
  R("Lan2", { who: "anita", tc: wd(2, 15, 20), note: "Konsumsi rapat divisi HR dan sesi wawancara", lines: [["PAN-TEH-SOS", 4], ["PAN-KPI-CM", 3], ["PAN-GUL-1KG", 4], ["PAN-GLS-8OZ", 4], ["PAN-BSC-KG", 3]], expect: "pending", appr: "dimas",
    decide: { t: wd(1, 9, 30), by: "dimas", ok: true, note: "Setuju untuk rapat divisi HR." } });
  R("Ld", { who: "dimas", tc: wd(1, 11, 10), note: "Jamuan tamu dari kantor pusat grup", lines: [["PAN-BSC-KG", 2], ["PAN-KPI-CM", 4], ["PAN-TEH-SOS", 4], ["PAN-GUL-1KG", 3], ["PAN-GLS-8OZ", 4]], expect: "pending", appr: null });
  R("Lag", { who: "agus", tc: wd(1, 14, 20), note: "Suku cadang AC cassette lantai 3", lines: [["SPR-KPS-35", 4], ["SPR-REM-UNI", 2], ["SPR-FLT-CST", 2]], expect: "pending", appr: "putri" });
  R("Lr", { who: "rizky", tc: wd(1, 16, 5), note: "Kertas untuk laporan kuartalan dan rapat evaluasi", lines: [["ATK-A4-80", 10], ["ATK-PST-654", 4]], expect: "pending", appr: "dimas" });
  R("Lri2", { who: "rizky", tc: ago(210), note: "Ordner dan perlengkapan arsip proyek", lines: [["ATK-ORD-7", 4], ["ATK-BDR-200", 5], ["ATK-LEM-21", 4], ["ATK-HLT-STB", 4]], expect: "open" });
  R("Lbayu", { who: "bayu", tc: ago(150), note: "Perawatan lantai 3 dan toilet", lines: [["CLN-PEL-800", 6], ["CLN-PGK-500", 4], ["TLT-PGH-REF", 3]], expect: "open" });
  R("Lan", { who: "anita", tc: ago(180), note: "Perlengkapan hari rekrutmen (walk-in interview)", lines: [["ATK-PEN-PLT", 20], ["ATK-SNL", 10], ["ATK-ORD-7", 4], ["IT-USB-32", 2]], expect: "pending", appr: "dimas" });
  R("Llina", { who: "lina", tc: ago(60), note: "Materi event peluncuran produk", lines: [["ATK-MRK-WB", 12], ["ATK-A3-80", 2], ["ATK-LKB-48", 6], ["ATK-PST-654", 3]], expect: "pending", appr: "dimas" });

  // ----- stock-takes
  const take = (id, place, tStart, tClose, note, deltas) => { add({ t: tStart, k: "take_start", id, place, note }); if (tClose) add({ t: tClose, k: "take_close", id, place, deltas }); };
  take("A", "W", wd(31, 14, 0), wd(31, 17, 30), "Stock opname gudang akhir kuartal", { "ATK-A4-80": -1, "CLN-KTG-60": 2, "ATK-MRK-PRM": -2, "CLN-PEL-800": -1, "TLT-TSS-JMB": -3, "ATK-LBL-103": 1, "PAN-AQC-220": -1 });
  take("B", "PAN", wd(13, 14, 0), wd(13, 16, 30), "Stock opname pantry Level 3", { "PAN-GLS-8OZ": -3, "PAN-TEH-SOS": -1, "PAN-KPI-CM": -1, "PAN-AQG-19": 1 });
  take("C", "LOB", wd(1, 16, 30), null, "Stock opname rutin lobi dan meja resepsionis", null);
  add({ t: wd(1, 16, 50), k: "take_count", id: "C", deltas: { "ATK-PEN-AE7": -2, "ATK-PST-654": -1 }, only: ["ATK-A4-80", "ATK-PEN-AE7", "ATK-PST-654", "PAN-KPI-CM", "PAN-TEH-SOS", "PAN-AQC-220", "PAN-GLS-8OZ", "PAN-BSC-KG", "TLT-TSS-JMB"] });

  // ----- generated history for every other item
  const blocked = (places, t) => (places.includes("W") && t >= wd(31, 13, 0) && t <= wd(31, 18, 30)) || (places.includes("PAN") && t >= wd(13, 13, 0) && t <= wd(13, 17, 30)) || (places.includes("LOB") && t >= wd(1, 16, 0));
  const explicit = [...plan];
  const generated = [];
  for (const it of ITEMS.filter((x) => !x.manual)) {
    const mine = explicit.filter((o) => effects(o, REQ).some((e) => e.sku === it.sku));
    const net = {}; for (const o of mine) for (const e of effects(o, REQ)) if (e.sku === it.sku) net[e.place] = (net[e.place] ?? 0) + e.d;
    let done = null;
    for (let attempt = 0; attempt < 120 && !done; attempt++) {
      const r = rng(`${it.sku}:${attempt}`);
      const rt = (nHi, nLo, places, h0 = 8, h1 = 16) => { for (let i = 0; i < 40; i++) { const n = nLo + Math.floor(r() * (nHi - nLo + 1)); const t = wd(n, h0 + Math.floor(r() * (h1 - h0)), Math.floor(r() * 60)); if (!blocked(places, t)) return t; } return wd(nLo, 9, 7); };
      const gen = []; let outW = 0; const g = (o) => gen.push({ ...o, seq: 1e6 + gen.length });
      for (const s of Object.keys(it.F).filter((p) => p !== "W")) {
        const Fs = it.F[s], ns = net[s] ?? 0;
        const nRepl = s === "BDG" ? 1 : it.fast ? 2 + Math.floor(r() * 2) : (r() < 0.5 ? 1 : 0);
        const q = Array.from({ length: nRepl }, () => Math.max(1, Math.round(Fs * (0.35 + r() * 0.5))));
        let Cs = Math.max(2, Math.round(Fs * (0.4 + r() * 0.9)));
        let S0 = Fs - ns - q.reduce((a, b) => a + b, 0) + Cs; const minS0 = Math.max(2, Math.ceil(Fs * 0.6));
        if (S0 < minS0) { Cs += minS0 - S0; S0 = minS0; }
        const dest = { PAN: "pantry Level 3", LOB: "lobi dan meja resepsionis", BDG: "cabang Bandung" }[s];
        g({ t: rt(s === "BDG" ? 37 : 38, s === "BDG" ? 36 : 37, ["W", s]), k: "transfer", sku: it.sku, from: "W", to: s, qty: S0, ref: s === "BDG" ? `Kiriman awal ke cabang Bandung via ekspedisi, resi JNT-${100000 + Math.floor(r() * 899999)}` : `Stok awal ${dest}`, by: "hendra" });
        for (const qq of q) g({ t: rt(30, 6, ["W", s]), k: "transfer", sku: it.sku, from: "W", to: s, qty: qq, ref: s === "BDG" ? `Kiriman lanjutan ke cabang Bandung, resi JNT-${100000 + Math.floor(r() * 899999)}` : s === "PAN" ? "Pengisian ulang stok pantry Level 3 (permintaan Bayu)" : "Pengisian stok lobi dan meja resepsionis", by: "hendra" });
        outW += S0 + q.reduce((a, b) => a + b, 0);
        const k = Math.max(2, Math.min(6, Math.round(Cs / (Fs * 0.2 + 1))));
        for (const c of split(Cs, k, r)) g({ t: rt(34, 4, [s]), k: "issue", sku: it.sku, place: s, qty: c, ref: SAT_REF[s], by: "hendra" });
      }
      const Fw = it.F.W, nw = net.W ?? 0, Q = it.fast ? it.rq : 0;
      let Cw = Math.max(3, Math.round(Fw * (0.2 + r() * 0.5)) + (it.fast ? Math.round(it.rq * 0.4) : 0));
      let R0 = Fw - nw + outW + Cw - Q;
      const floor = it.fast ? Math.ceil(it.rq * 0.7) : 2; if (R0 < floor) { Cw += floor - R0; R0 = floor; }
      g({ t: wd(40, 8 + Math.floor(r() * 3), Math.floor(r() * 60)), k: "receipt", sku: it.sku, place: "W", qty: R0, cost: Q ? Math.max(100, Math.round((it.cost * 0.96) / 100) * 100) : it.cost, by: "hendra" });
      if (Q) g({ t: rt(20, 12, ["W"]), k: "receipt", sku: it.sku, place: "W", qty: Q, cost: it.cost, by: "hendra" });
      const people = it.cat === "other" ? TECH : PEOPLE;
      for (const c of split(Cw, Math.max(2, Math.min(8, Math.round(Cw / (Fw * 0.12 + 2)))), r)) { const [who, dept] = pick(r, people); g({ t: rt(34, 4, ["W"]), k: "issue", sku: it.sku, place: "W", qty: c, ref: `Untuk ${who} (${dept}) · ${pick(r, WHY[it.cat] ?? WHY.other)}`, by: "hendra" }); }
      if (simulate([...mine, ...gen].sort(byTime), REQ, it).ok) done = gen;
    }
    if (!done) throw new Error(`could not build a consistent history for ${it.sku}`);
    generated.push(...done);
  }
  plan.push(...generated);
  plan.sort(byTime);
  for (const it of ITEMS) { const s = simulate(plan, REQ, it); if (!s.ok) throw new Error(`history of ${it.sku} is inconsistent: ${JSON.stringify(s.bal)} (min ${s.min}) expected ${JSON.stringify(it.F)}`); }
  return { plan, REQ };
}

/** Replays the movements of one item in time order: no balance below zero, every place ends on its intended balance. */
function simulate(ops, REQ, it) {
  const bal = {}; let min = 0, at0 = null;
  for (const o of ops) for (const e of effects(o, REQ)) { if (e.sku !== it.sku) continue; bal[e.place] = (bal[e.place] ?? 0) + e.d; if (bal[e.place] < min) { min = bal[e.place]; at0 = o; } }
  const places = new Set([...Object.keys(bal), ...Object.keys(it.F)]);
  const final = [...places].every((p) => (bal[p] ?? 0) === (it.F[p] ?? 0));
  return { ok: min >= 0 && final, bal, min, at0 };
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------- the run
export async function run(d) {
  const L = d.ctx.loc;
  const places = { W: L.warehouse, PAN: L.pantry, LOB: L.lobby, BDG: L.bandung };
  await seedStock(d, places);
  await seedCosts(d, L);
}

async function seedStock(d, places) {
  const hendra = await d.as("hendra"), putri = await d.as("putri");
  const items = {};
  const existing = d.rows(d.must(await hendra.get("/api/inventory/items?archived=true"), "reading items"));
  for (const it of existing) items[it.sku] = it.id;
  d.ctx.stock = { items, places: Object.fromEntries(Object.entries(places).map(([k, v]) => [k, v.id])) };
  if (items["ATK-A4-80"]) { d.log(`  stock: ${existing.length} items already there, skipped`); return; }

  // supply rules: requests of 400.000 or more need the requester's manager; the default pickup place is the warehouse
  d.must(await hendra.put("/api/supplies/settings", { approvalAbove: 400000, defaultLocationId: places.W.id }), "supply settings");

  // items (the catalogue): created through the API, then backdated to the day of the first delivery
  for (const it of ITEMS) {
    const made = d.must(await hendra.post("/api/inventory/items", { sku: it.sku, name: it.name, category: it.cat, unit: it.unit, reorderLevel: it.level, reorderQuantity: it.rq, unitCost: it.cost, description: DESC[it.sku] ?? null }), `item ${it.sku}`);
    items[it.sku] = made.id;
  }
  const old = d.must(await hendra.post("/api/inventory/items", { sku: "ATK-A4-70", name: "Kertas HVS A4 70 gsm (diganti 80 gsm)", category: "atk", unit: "rim", reorderLevel: 10, reorderQuantity: 40, unitCost: 46000, description: "Tidak dipakai lagi sejak pindah ke kertas 80 gsm." }), "retired item");
  d.must(await hendra.post(`/api/inventory/items/${old.id}/archive`), "archive retired item");

  const { plan, REQ } = buildPlan();
  const skuOf = Object.fromEntries(Object.entries(items).map(([s, i]) => [i, s]));
  const pid = (k) => places[k].id;
  const stampMoves = []; const srState = {}; const takes = {}; let nMoves = 0, nSr = 0;
  const invoice = {}; const iso = (t) => t.toISOString();
  const invoiceOf = (sku, t) => { const sup = SUP[ITEMS.find((x) => x.sku === sku).sup]; invoice[sup.p] = (invoice[sup.p] ?? 380) + 7 + (hashStr(sku + t.getTime()) % 29); return `${sup.n} · Faktur ${sup.p}-${String(t.getFullYear()).slice(2)}${pad(t.getMonth() + 1)}-${String(invoice[sup.p]).padStart(4, "0")} · SJ ${String(20000 + (hashStr(sku + t.getDate()) % 79999))}`; };
  const clients = {}; const as = async (k) => (clients[k] ??= await d.as(k));

  const LIVE = new Set(["L5", "L6", "Lf", "Lan2", "Ld", "Lag", "Lr", "Lri2", "Lbayu", "Lan", "Llina"]); const windows = [];
  for (const o of plan) {
    const t0 = new Date();
    switch (o.k) {
      case "receipt": case "issue": case "adjust": case "transfer": {
        const cli = await as(o.by);
        const body = o.k === "transfer" ? { kind: "transfer", locationId: pid(o.from), toLocationId: pid(o.to), quantity: o.qty, reference: o.ref }
          : o.k === "receipt" ? { kind: "receipt", locationId: pid(o.place), quantity: o.qty, unitCost: o.cost, reference: invoiceOf(o.sku, o.t) }
          : { kind: o.k === "adjust" ? "adjustment" : "issue", locationId: pid(o.place), quantity: o.qty, reference: o.ref };
        const made = d.must(await cli.post(`/api/inventory/items/${items[o.sku]}/moves`, body), `${o.k} ${o.sku}`);
        for (const m of made) stampMoves.push([m.id, o.t]); nMoves += made.length; break;
      }
      case "sr_create": {
        const q = REQ[o.key]; const cli = await as(q.who);
        const r = d.must(await cli.post("/api/supplies/requests", { locationId: pid(q.place), note: q.note, lines: q.lines.map(([sku, n]) => ({ itemId: items[sku], quantity: n })) }), `request ${o.key}`);
        if (r.status !== q.expect) throw new Error(`request ${o.key} is ${r.status}, expected ${q.expect} (estimate ${r.estimatedCost})`);
        if (q.expect === "pending") { const want = { dimas: "Dimas Prakoso", putri: "Putri Anggraini" }[q.appr] ?? null; if ((r.approver ?? null) !== want) throw new Error(`request ${o.key}: approver is ${r.approver ?? "nobody"}, expected ${want ?? "nobody (the desk)"}`); }
        q.id = r.id; q.number = r.number; srState[o.key] = { id: r.id, created: o.t, updated: o.t }; nSr++; break;
      }
      case "sr_decide": {
        const q = REQ[o.key]; const cli = await as(q.decide.by);
        d.must(await cli.post(`/api/supplies/requests/${q.id}/decide`, { approve: q.decide.ok, note: q.decide.note }), `decide ${o.key}`);
        Object.assign(srState[o.key], { decided: o.t, updated: o.t }); break;
      }
      case "sr_fulfil": {
        const q = REQ[o.key]; const cli = await as(o.by);
        const r = d.must(await cli.post(`/api/supplies/requests/${q.id}/fulfil`, { lines: Object.fromEntries(Object.entries(o.lines).map(([sku, n]) => [items[sku], n])) }), `fulfil ${o.key}`);
        d.sql(`UPDATE stock_movements SET at = '${iso(o.t)}' WHERE group_id = '${q.id}' AND at > now() - interval '20 minutes'`);
        Object.assign(srState[o.key], { updated: o.t }); if (r.status === "fulfilled") srState[o.key].fulfilled = o.t; nMoves += Object.keys(o.lines).length; break;
      }
      case "sr_cancel": { const q = REQ[o.key]; const cli = await as(q.who); d.must(await cli.post(`/api/supplies/requests/${q.id}/cancel`), `cancel ${o.key}`); srState[o.key].updated = o.t; break; }
      case "sr_close": { const q = REQ[o.key]; const cli = await as(q.close.by); d.must(await cli.post(`/api/supplies/requests/${q.id}/close`, { note: q.close.note }), `close ${o.key}`); srState[o.key].updated = o.t; break; }
      case "take_start": {
        const cli = await as("putri");
        const tk = d.must(await cli.post("/api/inventory/stock-takes", { locationId: pid(o.place), note: o.note }), `stock-take ${o.id}`);
        takes[o.id] = { id: tk.id, started: o.t, lines: tk.lines }; break;
      }
      case "take_count": {
        const cli = await as("putri"); const tk = takes[o.id];
        const counts = {}; for (const l of tk.lines) if (o.only.includes(l.sku)) counts[l.itemId] = l.expected + (o.deltas[l.sku] ?? 0);
        d.must(await cli.put(`/api/inventory/stock-takes/${tk.id}/counts`, { counts }), `count ${o.id}`); break;
      }
      case "take_close": {
        const cli = await as("putri"); const tk = takes[o.id];
        const counts = {}; for (const l of tk.lines) counts[l.itemId] = l.expected + (o.deltas[l.sku] ?? 0);
        d.must(await cli.put(`/api/inventory/stock-takes/${tk.id}/counts`, { counts }), `count ${o.id}`);
        d.must(await cli.post(`/api/inventory/stock-takes/${tk.id}/close`), `close count ${o.id}`);
        tk.closed = o.t; break;
      }
    }
    windows.push({ t0, t1: new Date(), t: o.t, live: LIVE.has(o.key) && !(o.k === "sr_create" && (REQ[o.key].decide || REQ[o.key].fulfil)) });
  }

  // ---- backdating: movements (by id), requests, stock-takes and the items themselves
  const sql = [];
  for (let i = 0; i < stampMoves.length; i += 250) sql.push(`UPDATE stock_movements m SET at = v.t::timestamptz FROM (VALUES ${stampMoves.slice(i, i + 250).map(([id, t]) => `('${id}','${iso(t)}')`).join(",")}) AS v(id, t) WHERE m.id = v.id::uuid;`);
  for (const s of Object.values(srState)) sql.push(`UPDATE supply_requests SET created_at='${iso(s.created)}', updated_at='${iso(s.updated)}'${s.decided ? `, decided_at='${iso(s.decided)}'` : ""}${s.fulfilled ? `, fulfilled_at='${iso(s.fulfilled)}'` : ""} WHERE id='${s.id}';`);
  for (const tk of Object.values(takes)) { sql.push(`UPDATE stock_takes SET started_at='${iso(tk.started)}'${tk.closed ? `, closed_at='${iso(tk.closed)}'` : ""} WHERE id='${tk.id}';`); if (tk.closed) sql.push(`UPDATE stock_movements SET at='${iso(tk.closed)}', reference='Stock-take ${tk.started.getFullYear()}-${pad(tk.started.getMonth() + 1)}-${pad(tk.started.getDate())}' WHERE group_id='${tk.id}';`); }
  // the notifications the API sent while seeding get the date of the event; only the ones of requests still in flight stay unread
  for (const w of windows) { const readAt = new Date(Math.min(w.t.getTime() + 3 * 3600e3, NOW.getTime())); sql.push(`UPDATE notifications SET created_at='${iso(w.t)}', read_at=${w.live ? "NULL" : `'${iso(readAt)}'`} WHERE kind IN ('stock.low','supply.new','supply.update','supply.approval') AND created_at >= '${iso(w.t0)}' AND created_at <= '${iso(w.t1)}';`); }
  const first = wd(41, 9, 0);
  sql.push(`UPDATE inventory_items SET created_at='${iso(first)}', updated_at='${iso(first)}' WHERE sku = ANY(ARRAY[${Object.keys(items).map((s) => `'${s}'`).join(",")}]);`);
  d.sql(sql.join("\n"));

  // ---- read back what the desk would see
  const sum = d.must(await hendra.get("/api/inventory/summary"), "inventory summary"), reorder = d.must(await hendra.get("/api/inventory/reorder"), "reorder list");
  const ss = d.must(await putri.get("/api/supplies/summary"), "supply summary");
  d.log(`  inventory: ${sum.items} items (+1 archived), ${nMoves} ledger movements over 8 weeks, ${reorder.length} lines on the reorder list, stock value Rp ${Math.round(sum.stockValue).toLocaleString("id-ID")}`);
  d.log(`  supplies: ${nSr} requests (waiting ${ss.pending}, to issue ${ss.open}, partly issued ${ss.partlyFulfilled}, issued last 30 days ${ss.fulfilled30}), 3 stock-takes (2 closed, 1 open at the lobby)`);
}

// ---------------------------------------------------------------------------------------------------------------------------------------------------- costs
/** Cost centres attach to PLACES: a work order's cost goes to the cost centre of the nearest place (walking up the tree) of the ticket's place. */
async function seedCosts(d, L) {
  const hendra = await d.as("hendra");
  d.must(await hendra.put("/api/costs/settings", { currency: "IDR", labourRatePerHour: 85000 }), "cost settings");

  // the HR floor does not exist yet in the setup: Level 4 of Tower A
  const locs = d.rows(d.must(await hendra.get("/api/locations?includeArchived=true"), "locations"));
  let lvl4 = locs.find((l) => l.name === "Level 4");
  lvl4 ??= d.must(await hendra.post("/api/locations", { type: "floor", parentId: L.tower.id, code: "TWR-A-L4", name: "Level 4", address: null, timeZone: null, calendarId: null, managerUserId: null, capacity: 60 }), "Level 4");
  d.ctx.loc.lvl4 = lvl4;

  const CENTERS = [
    ["GA-100", "General Affairs", [L.lobby.id]], ["OPS-300", "Operations", [L.lvl3.id]], ["HR-400", "Human Resources", [lvl4.id]],
    ["FIN-500", "Finance", [L.lvl5.id]], ["MKT-600", "Marketing", [L.bandung.id]], ["FAC-900", "Facilities", [L.basement.id, L.warehouse.id, L.server.id]],
  ];
  const have = d.rows(d.must(await hendra.get("/api/costs/centers"), "cost centres"));
  const centers = {};
  for (const [code, nm, locationIds] of CENTERS) {
    const f = have.find((c) => c.code === code);
    centers[code] = (f ?? d.must(await hendra.post("/api/costs/centers", { code, name: nm, locationIds }), `cost centre ${code}`)).id;
  }
  d.ctx.costCenters = centers;

  // budgets of this year (nominal amounts; calibrateBudgets moves them to the planned utilisation once there are work-order costs)
  const year = new Date().getFullYear();
  const list = d.must(await hendra.get(`/api/costs/budgets?year=${year}`), "budgets");
  const sites = d.rows(d.must(await hendra.get("/api/locations"), "locations")).filter((l) => !l.parentId);
  const cats = d.rows(await (async () => { const r = await hendra.get("/api/catalog/categories"); return r.ok ? r.data : []; })());
  const site = (n) => sites.find((s) => s.name === n)?.id; const cat = (n) => cats.find((c) => c.name === n)?.id;
  const BUDGETS = [
    ["cost_center", centers["FAC-900"], 360_000_000, "Perawatan gedung, AC, listrik dan gudang"], ["cost_center", centers["GA-100"], 150_000_000, "Operasional GA dan resepsionis"],
    ["cost_center", centers["OPS-300"], 120_000_000, "Lantai 3: ruang rapat, pantry dan area kerja Operations"], ["cost_center", centers["FIN-500"], 60_000_000, "Lantai 5: area kerja Finance"],
    ["cost_center", centers["HR-400"], 45_000_000, "Lantai 4: area kerja HR"], ["cost_center", centers["MKT-600"], 90_000_000, "Kantor cabang Bandung (pemasaran dan penjualan)"],
    ["site", site("Head Office Jakarta"), 600_000_000, "Anggaran pemeliharaan Head Office Jakarta"], ["site", site("Branch Office Bandung"), 120_000_000, "Anggaran pemeliharaan kantor cabang Bandung"],
    ["category", cat("Facilities & Maintenance"), 380_000_000, "Semua permintaan Facilities & Maintenance"], ["category", cat("Cleaning & Hygiene"), 80_000_000, "Kebersihan dan hama"],
  ];
  let made = 0;
  for (const [scope, scopeId, amount, note] of BUDGETS) {
    if (!scopeId || list.items.some((b) => b.scope === scope && b.scopeId === scopeId)) continue;
    await d.soft(`budget ${scope}`, async () => { d.must(await hendra.post("/api/costs/budgets", { scope, scopeId, year, amount, note }), "budget"); made++; });
  }
  d.log(`  costs: IDR with labour rate Rp 85.000/jam, ${CENTERS.length} cost centres, ${made} new budgets for ${year}`);
  await calibrateBudgets(d);
}

/** Target utilisation per budget (share of the amount spent). Once work orders have costs the amount is set to spent / target, so the page shows one budget above
 *  80 % (near), one over budget and the rest comfortable. Cost centres are ranked by what they spent (biggest first) because we cannot know in advance which places the
 *  servicedesk module raises costs at; sites and categories have fixed targets. Safe to call repeatedly (a no-op while nothing is spent): call it again after servicedesk. */
const CENTER_PATTERN = [0.86, 1.14, 0.48, 0.31, 0.55, 0.22];
const FIXED = { "site:Head Office Jakarta": 0.63, "site:Branch Office Bandung": 0.4, "cat:Facilities & Maintenance": 0.52, "cat:Cleaning & Hygiene": 0.7 };
export async function calibrateBudgets(d) {
  const hendra = await d.as("hendra"); const year = new Date().getFullYear();
  const list = d.must(await hendra.get(`/api/costs/budgets?year=${year}`), "budgets");
  const spent = list.items.filter((b) => b.spent > 0);
  const centres = spent.filter((b) => b.scope === "cost_center").sort((a, b) => b.spent - a.spent || a.scopeName.localeCompare(b.scopeName));
  let moved = 0;
  for (const b of spent) {
    const target = b.scope === "cost_center" ? CENTER_PATTERN[centres.indexOf(b)] : FIXED[(b.scope === "site" ? "site:" : "cat:") + b.scopeName];
    if (!target) continue;
    const step = b.spent / target >= 20_000_000 ? 1_000_000 : b.spent / target >= 2_000_000 ? 100_000 : 10_000;
    const amount = Math.max(step, Math.round(b.spent / target / step) * step);
    if (Math.abs(amount - b.amount) / b.amount < 0.03) continue;
    d.must(await hendra.put(`/api/costs/budgets/${b.id}`, { scope: b.scope, scopeId: b.scopeId, year, amount, note: b.note, version: b.version }), `budget ${b.scopeName}`); moved++;
  }
  d.log(spent.length === 0 ? "  budgets: no work-order costs yet, amounts are provisional (run `--only stock` again after servicedesk to calibrate utilisation)" : `  budgets: ${spent.length} with spend, ${moved} recalibrated`);
}
