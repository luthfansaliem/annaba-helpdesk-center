/**
 * 02 — spaces: bookable resources and bookings, vehicles (trips, documents, driver licences), visitors and reception, the mailroom.
 *
 * Everything goes through the public API as the demo cast. The API refuses past-dated bookings and stamps "now" on everything, so the history that
 * the utilisation report and the visitor/mail summaries need is made by booking far ahead (+98 days, same weekday) or registering at "now" and then moving
 * those rows into the past with d.sql (only rows this module created). Live items (in use now, starts in 20 minutes, expected in 30 minutes …) are
 * relative to the moment of seeding, in Jakarta time, so the seed can run at any hour or weekday.
 *
 * Leaves in d.ctx.spaces: { res, ids, feedUrl }.
 */
export const name = "spaces";

// ----------------------------------------------------------------------------------------------------------------- time (Jakarta, UTC+7, no DST)
const MIN = 60_000, HR = 3_600_000, DAY = 86_400_000, JK = 7 * HR, SHIFT_DAYS = 98;   // 98 = 14 weeks: the same weekday, outside every window the live data uses
const jkMidnight = (off = 0) => { const p = new Date(Date.now() + JK); return Date.UTC(p.getUTCFullYear(), p.getUTCMonth(), p.getUTCDate() + off) - JK; };
const at = (off, min) => jkMidnight(off) + min * MIN;                            // epoch ms of Jakarta day `off` at `min` minutes after midnight
const dowOf = (off) => new Date(jkMidnight(off) + 12 * HR + JK).getUTCDay();   // 0 Sunday … 6 Saturday
const isWork = (off) => dowOf(off) !== 0 && dowOf(off) !== 6;
const nw = (n) => { let off = 0, seen = 0; for (;;) { off++; if (isWork(off)) { seen++; if (seen === n) return off; } } };      // n-th working day after today
const pw = (n) => { let off = 0, seen = 0; for (;;) { off--; if (isWork(off)) { seen++; if (seen === n) return off; } } };      // n-th working day before today
const nextDow = (dow) => { for (let off = 1; ; off++) if (dowOf(off) === dow) return off; };                                     // first day after today with that weekday
const iso = (ms) => new Date(ms).toISOString();
const floor5 = (ms) => ms - (ms % (5 * MIN));
const ceil5 = (ms) => floor5(ms + 5 * MIN - 1);
const nowMin = () => (Date.now() - jkMidnight(0)) / MIN;
const hm = (s) => { const [h, m] = s.split(":").map(Number); return h * 60 + m; };

function rngOf(seed) { let a = seed >>> 0; return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const pick = (rng, arr) => arr[Math.floor(rng() * arr.length)];
const int = (rng, a, b) => a + Math.floor(rng() * (b - a + 1));
const wpick = (rng, pairs) => { const sum = pairs.reduce((s, [, w]) => s + w, 0); let x = rng() * sum; for (const [v, w] of pairs) { x -= w; if (x < 0) return v; } return pairs[0][0]; };

// ----------------------------------------------------------------------------------------------------------------- the module
export async function run(d) {
  const { must, soft, rows, log } = d;
  const L = d.ctx.loc;
  const KEYS = ["admin", "hendra", "putri", "agus", "bayu", "sinta", "dimas", "rizky", "anita", "fajar", "lina"];
  const U = {}, ID = {};
  for (const k of KEYS) { U[k] = await d.as(k); ID[k] = await d.userId(k); }
  const hendra = U.hendra, putri = U.putri, sinta = U.sinta;

  const have = rows(must(await hendra.get("/api/resources?archived=true"), "resources"));
  if (have.some((r) => r.code === "R-301")) { log("  spaces: already seeded, skipped"); return; }

  const sqlQueue = [];
  const q = (s) => sqlQueue.push(s);
  const flush = () => { while (sqlQueue.length) d.sql(sqlQueue.splice(0, 80).join("\n")); };
  const ts = (ms) => `'${iso(ms)}'`;
  /** Notifications the history phases raised are about moved dates: drop them so the bells only carry the live items. */
  const dropNotifications = (from, kinds, to = Date.now() + 60_000) => d.sql(`DELETE FROM notifications WHERE created_at >= '${iso(from - 2000)}' AND created_at <= '${iso(to + 2000)}' AND (${kinds.map((k) => `kind LIKE '${k}'`).join(" OR ")});`);
  const BELL = ["booking.%", "vehicle.alert", "resource.document", "visitor.%", "mail.%"];

  // ================================================================================================= 1. visitor rules and block list (hendra)
  must(await hendra.put("/api/visitors/settings", {
    acknowledgement: "Dengan masuk ke gedung ini, saya setuju untuk: (1) memakai badge tamu selama berada di area kantor dan mengembalikannya kepada resepsionis saat pulang; (2) hanya berada di area yang disetujui oleh tuan rumah dan selalu didampingi; (3) tidak memotret atau merekam di area kantor tanpa izin tertulis; (4) menjaga kerahasiaan informasi yang saya lihat atau dengar selama kunjungan; (5) bersedia diperiksa barang bawaannya oleh petugas keamanan. Data saya disimpan sesuai kebijakan retensi perusahaan dan hanya dipakai untuk keamanan gedung.",
    askIdLast4: true, retentionDays: 90,
  }), "visitor settings");
  for (const [nm, company, reason] of [
    ["Heru Santoso", "CV Mitra Sejahtera Abadi", "Pernah memaksa masuk ke area kerja tanpa janji temu dan menolak mengikuti prosedur keamanan (Mei 2026). Hubungi GA sebelum menerima."],
    ["Rudi Hartono", "PT Karya Sinergi Utama", "Berulang kali mengganggu karyawan di lobi untuk menagih pembayaran; sudah diperingatkan oleh manajemen."],
    ["Teguh Wibowo", null, "Penawaran door-to-door tanpa janji temu; sudah beberapa kali diminta meninggalkan gedung."],
  ]) must(await hendra.post("/api/visitors/blocks", { name: nm, company, reason }), `block ${nm}`);
  log("  visitor rules: acknowledgement, ID digits, 90 days retention, 3 on the block list");

  // ================================================================================================= 2. resources (hendra), vehicle papers, licences
  const roleList = rows(must(await (await d.as("admin")).get("/api/roles"), "roles"));
  const roleId = (key) => roleList.find((r) => r.key === key || r.code === key)?.id;
  const SPEC = {
    garuda: { kind: "room", name: "Garuda meeting room", code: "R-301", loc: L.garuda, capacity: 12, features: ["projector", "screen", "video_call", "whiteboard", "air_conditioning"], openFrom: "08:00", openUntil: "18:00", openDays: 31, bufferMinutes: 15, maxMinutes: 240,
      description: "Ruang rapat besar Lantai 3 untuk rapat divisi, presentasi vendor dan sesi pelatihan. Proyektor, layar dan konferensi video siap pakai; kursi bisa ditata ulang." },
    merak: { kind: "room", name: "Merak meeting room", code: "R-302", loc: L.merak, capacity: 6, features: ["screen", "whiteboard", "air_conditioning"], openFrom: "08:00", openUntil: "18:00", openDays: 31, bufferMinutes: 0, maxMinutes: 180,
      description: "Ruang rapat kecil untuk diskusi tim, wawancara dan stand-up harian." },
    cenderawasih: { kind: "room", name: "Cenderawasih boardroom", code: "R-501", loc: L.lvl5, capacity: 20, features: ["projector", "screen", "video_call", "phone", "accessible", "air_conditioning"], openFrom: "07:00", openUntil: "20:00", openDays: 63, bufferMinutes: 30, maxMinutes: 480,
      description: "Ruang rapat direksi dan tamu penting di Lantai 5. Hanya dapat dipesan oleh tim GA (manajer dan supervisor) atas nama pimpinan; karyawan menghubungi GA.", roles: ["ga_manager", "ga_supervisor"] },
    kenari: { kind: "room", name: "Kenari discussion room", code: "R-303", loc: L.lvl3, capacity: 4, features: ["whiteboard", "screen"], openFrom: null, openUntil: null, openDays: 127, bufferMinutes: 0, maxMinutes: 180, checkInMinutes: 30,
      description: "Ruang diskusi kecil yang bisa dipakai kapan saja. Ruangan dilepas otomatis bila tidak ada yang check-in dalam 30 menit setelah jadwal mulai, supaya bisa dipakai tim lain." },
    bandung: { kind: "room", name: "Ruang rapat Bandung", code: "BDG-R1", loc: L.bdgFloor, capacity: 8, features: ["video_call", "screen", "whiteboard"], openFrom: "08:00", openUntil: "17:00", openDays: 31, bufferMinutes: 0, maxMinutes: 240,
      description: "Satu-satunya ruang rapat di Branch Office Bandung; dipakai juga untuk rapat daring dengan kantor pusat." },
    flamboyan: { kind: "room", name: "Flamboyan meeting room", code: "R-304", loc: L.lvl3, capacity: 8, features: ["screen", "air_conditioning"], openFrom: "08:00", openUntil: "18:00", openDays: 31, bufferMinutes: 0, maxMinutes: 240, archive: true,
      description: "Ditutup untuk renovasi plafon dan penggantian AC; direncanakan dibuka kembali awal tahun." },
    innova: { kind: "vehicle", name: "Toyota Innova Zenix", code: "B 1893 KJP", loc: L.basement, capacity: 7, features: ["air_conditioning"], openFrom: null, openUntil: null, openDays: 127, bufferMinutes: 30, maxMinutes: 4320, needsApproval: true,
      description: "Mobil operasional utama untuk perjalanan klien dan dinas luar. Pemesanan perlu persetujuan GA. Kunci diambil di meja resepsionis." },
    avanza: { kind: "vehicle", name: "Toyota Avanza Veloz", code: "B 2745 SKT", loc: L.basement, capacity: 6, features: ["air_conditioning"], openFrom: null, openUntil: null, openDays: 127, bufferMinutes: 30, maxMinutes: 4320,
      description: "Mobil operasional untuk urusan kantor dalam kota: bank, kantor pajak, vendor, bandara." },
    parkTamu: { kind: "parking", name: "Parkir tamu P-01", code: "P-01", loc: L.basement, capacity: null, features: [], openFrom: "07:00", openUntil: "19:00", openDays: 127, bufferMinutes: 0, maxMinutes: 600,
      description: "Satu slot parkir dekat lift lobi untuk tamu penting. Pesan lewat GA atau sendiri." },
    parkEv: { kind: "parking", name: "Parkir EV B1-12", code: "EV-12", loc: L.basement, capacity: null, features: ["ev_charger"], openFrom: "06:00", openUntil: "22:00", openDays: 127, bufferMinutes: 0, maxMinutes: 360,
      description: "Slot parkir dengan charger kendaraan listrik; maksimal 6 jam per pemakaian." },
    proyektor: { kind: "equipment", name: "Proyektor portabel Epson EB-X49", code: "EQ-PRJ-01", loc: L.lvl3, capacity: null, features: ["projector"], openFrom: "08:00", openUntil: "17:00", openDays: 31, bufferMinutes: 0, maxMinutes: 4320,
      description: "Proyektor dan kabel HDMI untuk rapat di luar ruang berproyektor atau acara luar kantor. Diambil di meja GA Lantai 3." },
    kamera: { kind: "equipment", name: "Kamera Canon EOS 90D + lensa 18-135", code: "EQ-CAM-01", loc: L.lvl3, capacity: null, features: [], openFrom: "08:00", openUntil: "17:00", openDays: 31, bufferMinutes: 0, maxMinutes: 4320, needsApproval: true,
      description: "Kamera untuk dokumentasi acara dan konten Marketing. Peminjaman perlu persetujuan GA; kembalikan beserta kartu memori." },
  };
  const R = {};   // key → resource item
  for (const [key, s] of Object.entries(SPEC)) {
    const body = { kind: s.kind, name: s.name, code: s.code, description: s.description, locationId: s.loc.id, capacity: s.capacity, features: s.features, openFrom: s.openFrom, openUntil: s.openUntil, openDays: s.openDays, bufferMinutes: s.bufferMinutes, maxMinutes: s.maxMinutes, needsApproval: !!s.needsApproval, checkInMinutes: s.checkInMinutes ?? null, allowedRoleIds: (s.roles ?? []).map(roleId).filter(Boolean) };
    R[key] = must(await hendra.post("/api/resources", body), `resource ${s.name}`);
    R[key].spec = s;
  }
  must(await hendra.post(`/api/resources/${R.flamboyan.id}/archive`), "archive Flamboyan");
  log(`  resources: ${Object.keys(R).length} (5 rooms + 1 archived, 2 cars, 2 parking slots, 2 equipment); approval: Innova, kamera; check-in: Kenari; role-restricted: Cenderawasih`);

  const inDays = (n) => new Date(jkMidnight(n)).toISOString().slice(0, 10);
  const docs = [
    ["innova", "registration", "STNK B 1893 KJP · NRKB 6Y4A1", inDays(240), "Pajak tahunan dibayar Maret; perpanjang STNK 5 tahunan 2031."],
    ["innova", "insurance", "Polis Astra Garda Oto 0412/MV/2026", inDays(22), "All risk. Perpanjangan sedang diproses bagian Finance."],
    ["innova", "service", "Servis berkala 20.000 km", inDays(41), "Auto2000 Pasar Minggu; jadwalkan saat mobil tidak dipesan."],
    ["avanza", "registration", "STNK B 2745 SKT", inDays(96), null],
    ["avanza", "insurance", "Polis Sinar Mas MV-2025/118820", inDays(280), "Total loss only."],
    ["avanza", "service", "Servis berkala 60.000 km", inDays(-6), "Terlambat: belum sempat dijadwalkan karena mobil terus dipakai. Segera hubungi bengkel."],
  ];
  for (const [rk, kind, number, expiresOn, note] of docs) must(await hendra.post(`/api/resources/${R[rk].id}/documents`, { kind, number, expiresOn, note }), `document ${rk}/${kind}`);
  const licences = [["putri", "A", "120511021177", 700], ["hendra", "A", "110812039951", 580], ["agus", "A", "130215004482", 24], ["bayu", "A", "130702018365", 1100], ["rizky", "A", "140608027714", 430], ["dimas", "A", "100317035586", 800], ["anita", "A", "150106041928", 270], ["fajar", "A", "140921033360", -64]];
  for (const [k, cls, number, expDays] of licences) must(await hendra.post("/api/drivers/licences", { email: d.CAST[k].email, number, class: cls, expiresOn: inDays(expDays) }), `licence ${k}`);
  log(`  vehicle papers: ${docs.length} documents (one due soon, one overdue), ${licences.length} driver licences (one due soon, one expired)`);

  // ================================================================================================= 3. bookings
  const fails = {};
  const tolerate = (why) => { fails[why] = (fails[why] ?? 0) + 1; return null; };
  const bookBody = (res, s, e, title, att, note, forKey) => ({ resourceId: res.id, startsAt: iso(s), endsAt: iso(e), title, attendees: att ?? null, note: note ?? null, bookedForId: forKey ? ID[forKey] : null });
  async function book(who, res, s, e, title, att, note, forKey) {
    const r = await U[who].post("/api/bookings", bookBody(res, s, e, title, att, note, forKey));
    if (!r.ok) { tolerate(`${res.name}: ${(r.data?.error?.message ?? r.data?.message ?? r.text ?? "").slice(0, 80)}`); return null; }
    return r.data;
  }
  const taken = {};   // resKey → dayOff → [[fromMin, toMinIncludingBuffer]]
  const reserve = (rk, off, s, e) => { ((taken[rk] ??= {})[off] ??= []).push([s, e + (R[rk].spec.bufferMinutes ?? 0)]); };
  const offOf = (ms) => Math.floor((ms - jkMidnight(0)) / DAY);
  const minOf = (ms) => Math.round((ms - jkMidnight(offOf(ms))) / MIN);

  // --- 3a. repeating meetings (live series)
  const series = {};
  async function makeSeries(key, who, rk, every, off, from, to, title, att, note, count) {
    const r = await U[who].post("/api/bookings/series", { resourceId: R[rk].id, startsAt: iso(at(off, hm(from))), endsAt: iso(at(off, hm(to))), title, attendees: att, note, every, count });
    if (!r.ok) { tolerate(`series ${title}: ${(r.text ?? "").slice(0, 100)}`); return null; }
    series[key] = r.data;
    for (const b of r.data.made) reserve(rk, offOf(new Date(b.startsAt).getTime()), minOf(new Date(b.startsAt).getTime()), minOf(new Date(b.endsAt).getTime()));
    return r.data;
  }
  await makeSeries("ops", "dimas", "garuda", "week", nw(1), "09:00", "10:30", "Weekly Operations Review", 10, "Review mingguan KPI operasional dan tindak lanjut dari minggu lalu. Bahan dikirim sehari sebelumnya.", 8);
  await makeSeries("standup", "rizky", "merak", "weekday", nw(1), "08:30", "08:45", "Daily stand-up Tim Operations", 6, null, 15);
  await makeSeries("ga", "putri", "merak", "week", nextDow(3), "14:00", "15:00", "Rapat koordinasi tim GA", 5, "Bahas permintaan layanan yang tertunda, jadwal teknisi dan vendor.", 6);
  await makeSeries("content", "lina", "kenari", "week", nextDow(2), "15:00", "16:00", "Sinkronisasi konten mingguan Marketing", 3, null, 6);
  await makeSeries("onboard", "anita", "merak", "week", nextDow(4), "13:00", "15:00", "Pelatihan onboarding karyawan baru", 6, "Sesi kebijakan perusahaan, K3 dan penggunaan helpdesk. Materi dari HR.", 4);
  await makeSeries("mgmt", "hendra", "cenderawasih", "week", nextDow(1), "10:00", "12:00", "Rapat manajemen GA dan Operations", 8, "Rapat bulanan pimpinan; notulen dikirim HR.", 6);
  // a cancelled occurrence ("just this one") and a series cut short ("this and following")
  if (series.content) { const b = series.content.made[1]; if (b) must(await U.lina.post(`/api/bookings/${b.id}/cancel`, { reason: "Tim Marketing sedang di pameran, rapat digeser ke chat.", scope: "this" }), "cancel one occurrence"); }
  if (series.onboard) { const b = series.onboard.made[2]; if (b) must(await U.anita.post(`/api/bookings/${b.id}/cancel`, { reason: "Program onboarding dipadatkan menjadi dua sesi; sisa sesi dibatalkan.", scope: "following" }), "cancel following"); }
  log(`  bookings: ${Object.keys(series).length} repeating meetings (${Object.values(series).reduce((n, s) => n + s.made.length, 0)} occurrences), one cancelled occurrence, one series cut short`);

  // --- 3b. the plan: every other booking of the last 4 weeks, today and the next ~9 working days
  const TITLES = {
    rizky: ["Koordinasi jadwal pengiriman mingguan", "Evaluasi performa armada bulan lalu", "Review SOP pengiriman ke pelanggan", "Rapat tindak lanjut komplain pelanggan", "Perencanaan rute distribusi Jabodetabek", "Diskusi kapasitas gudang dengan tim Inventori"],
    dimas: ["Rapat pimpinan divisi Operations", "Review KPI Operations kuartal berjalan", "Pembahasan anggaran Operations dengan Finance", "Evaluasi vendor logistik", "Sesi 1-on-1 dengan tim Operations", "Persiapan audit ISO 9001"],
    anita: ["Wawancara kandidat Staff Admin", "Sosialisasi peraturan perusahaan", "Rapat evaluasi masa percobaan karyawan", "Diskusi penilaian kinerja semester", "Wawancara kandidat Staff Accounting", "Briefing program kesehatan karyawan"],
    fajar: ["Rekonsiliasi akhir bulan", "Rapat anggaran 2027", "Review umur piutang pelanggan", "Persiapan audit eksternal", "Rapat penutupan buku bulanan", "Presentasi vendor aplikasi akuntansi"],
    lina: ["Brainstorming kampanye promosi akhir tahun", "Review materi media sosial", "Rapat dengan vendor percetakan", "Briefing agency desain", "Evaluasi performa campaign bulan ini", "Perencanaan event gathering pelanggan"],
    putri: ["Evaluasi vendor kebersihan", "Perencanaan pemeliharaan gedung", "Review permintaan layanan minggu ini", "Briefing petugas lapangan", "Koordinasi dengan vendor AC"],
    hendra: ["Rapat manajemen GA", "Review anggaran fasilitas 2027", "Wawancara vendor baru", "Pembahasan kebijakan penggunaan ruang"],
    agus: ["Koordinasi jadwal perawatan AC", "Briefing teknisi vendor listrik"],
    bayu: ["Perencanaan relokasi gudang", "Briefing petugas kebersihan dan pantri"],
    admin: ["Rapat tinjauan keamanan sistem"],
  };
  const EXEC = ["Rapat Direksi bulanan", "Presentasi hasil audit eksternal", "Penerimaan tamu — PT Bintang Logistik", "Rapat Komite Anggaran", "Town hall manajemen", "Review strategi 2027", "Pertemuan dengan calon mitra distribusi"];
  const BDG = ["Rapat koordinasi tim cabang Bandung", "Review target penjualan wilayah Jawa Barat", "Pelatihan produk untuk staf cabang", "Rapat daring dengan kantor pusat", "Wawancara kandidat staf administrasi Bandung"];
  const COMPANIES = ["PT Bintang Logistik", "PT Sejuk Abadi", "PT Solusi Data Nusantara", "CV Cipta Kreasi Mandiri", "PT Mitra Konstruksi Utama", "KAP Santoso & Rekan", "PT Nusa Pack"];
  const ROOMS = {
    garuda: { busy: 0.62, durs: [60, 60, 90, 120, 120, 180], who: [["dimas", 3], ["anita", 2], ["fajar", 2], ["lina", 2], ["rizky", 2], ["putri", 2], ["hendra", 1], ["agus", 1]], minStart: 480 },
    merak: { busy: 0.55, durs: [30, 60, 60, 90], who: [["rizky", 2], ["anita", 3], ["fajar", 2], ["lina", 2], ["putri", 1], ["bayu", 1], ["agus", 1], ["dimas", 1]], minStart: 540 },
    cenderawasih: { busy: 0.22, durs: [120, 180, 240], who: [["putri", 3], ["hendra", 2]], minStart: 480 },
    kenari: { busy: 0.45, durs: [30, 30, 60, 60, 90], who: [["lina", 2], ["anita", 2], ["fajar", 2], ["rizky", 2], ["agus", 1], ["bayu", 1]], minStart: 480 },
    bandung: { busy: 0.45, durs: [60, 90, 120], who: [["anita", 2], ["lina", 2], ["fajar", 1], ["putri", 1]], minStart: 540 },
  };
  const plan = [];   // { rk, off, s, e, who, forKey, title, att, note, out }
  const forced = {}; // rk → off → [{s,e,...}] (live items for today)
  const addForced = (rk, off, s, e, who, title, att, note, out = "confirmed", extra = {}) => { (forced[rk] ??= {})[off] ??= []; forced[rk][off].push({ rk, off, s, e, who, title, att, note, out, ...extra }); reserve(rk, off, s, e); };

  // live items today (relative to now). Skipped when the room is closed at that moment.
  const todayOpen = (rk, s, e) => { const sp = R[rk].spec; if ((sp.openDays & (1 << ((dowOf(0) + 6) % 7))) === 0) return false; if (!sp.openFrom) return true; return s >= hm(sp.openFrom) && e <= hm(sp.openUntil); };
  const nm = () => floor5(Date.now()) / MIN - jkMidnight(0) / MIN;   // current minute of the Jakarta day, on a 5-minute step
  const liveToday = [
    // [room, start offset from now (min), length, who, title, attendees, note, outcome, extra]
    ["garuda", -35, 90, "dimas", "Rapat koordinasi mingguan Operations dan Finance", 8, null, "confirmed"],
    ["garuda", 100, 60, "putri", "Presentasi vendor AC — PT Sejuk Abadi", 6, "Penawaran perawatan AC terjadwal 2027.", "confirmed"],
    ["merak", -20, 60, "anita", "Wawancara kandidat Staff Admin", 3, null, "confirmed"],
    ["merak", 50, 60, "lina", "Sinkronisasi tim Marketing dengan agency", 5, null, "confirmed"],
    ["kenari", -40, 60, "fajar", "Diskusi cepat rekonsiliasi dengan auditor", 3, "Ruang dipesan sejak pagi; belum ada yang check-in.", "released-by-worker"],
    ["kenari", 25, 30, "lina", "Diskusi cepat konten media sosial", 3, null, "confirmed", { live: "checkin" }],
    ["kenari", 85, 30, "anita", "Coaching 1-on-1 dengan staf baru", 2, null, "confirmed", { live: "checkin" }],
    ["cenderawasih", 120, 120, "putri", "Penerimaan tamu — PT Bintang Logistik", 12, "Atas nama Dimas Prakoso.", "confirmed", { forKey: "dimas" }],
  ];
  for (const [rk, delta, len, who, title, att, note, out, extra] of liveToday) {
    const s = nm() + delta; const e = s + len;
    if (s < 0 || e > 1440 || !todayOpen(rk, s, e)) { log(`    - live item "${title}" skipped (closed at this hour)`); continue; }
    addForced(rk, 0, s, e, who, title, att, note, out, extra ?? {});
  }

  const spans = [];
  for (let off = -28; off <= nw(9); off++) {
    const futureIdx = off > 0 ? [...Array(10).keys()].find((i) => nw(i + 1) >= off) + 1 : 0;
    const density = off <= 0 ? 1 : futureIdx <= 3 ? 1 : futureIdx <= 6 ? 0.75 : 0.5;
    const saturday = dowOf(off) === 6;
    for (const [rk, cfg] of Object.entries(ROOMS)) {
      const sp = R[rk].spec;
      if (!isWork(off) && !(saturday && rk === "cenderawasih" && off < 0 && off % 3 === 0)) continue;
      if (off === nw(1) && rk === "garuda") continue;            // tomorrow: the Garuda afternoon stays free for the live booking demo (only the weekly review is in the morning)
      if (off === 0 && rk === "kenari") { for (const f of forced.kenari?.[0] ?? []) plan.push(f); continue; }
      const rng = rngOf(7919 * (off + 40) + 13 * Object.keys(ROOMS).indexOf(rk) + 101);
      const open = sp.openFrom ? hm(sp.openFrom) : 480, close = sp.openUntil ? hm(sp.openUntil) : 1080;   // around the clock: the random slots stay in office hours
      const busy = [...(taken[rk]?.[off] ?? []), ...((off === 0 ? forced[rk]?.[0] : null) ?? []).map((f) => [f.s, f.e + sp.bufferMinutes])];
      const out = [];
      let cur = Math.max(open, cfg.minStart) + 30 * int(rng, 0, 3);
      while (cur < close) {
        const dur = pick(rng, cfg.durs.filter((x) => x <= (sp.maxMinutes ?? 240)));
        if (cur + dur > close) break;
        const hit = busy.find(([a, b]) => cur < b && cur + dur + sp.bufferMinutes > a);
        if (hit) { cur = Math.ceil(hit[1] / 30) * 30; continue; }
        if (rng() < cfg.busy * density) { out.push({ s: cur, e: cur + dur }); cur += dur + sp.bufferMinutes + 30 * int(rng, 0, 2); } else cur += 30 * int(rng, 1, 2);
        cur = Math.ceil(cur / 30) * 30;
      }
      for (const o of out) {
        const r2 = rngOf(o.s * 31 + off * 977 + rk.length);
        let who = wpick(r2, cfg.who), forKey = null, title;
        if (rk === "cenderawasih") { forKey = wpick(r2, [["dimas", 2], ["hendra", 2], ["admin", 1]]); title = pick(r2, EXEC); }
        else if (rk === "bandung") title = pick(r2, BDG);
        else title = pick(r2, TITLES[who]);
        const att = Math.min(sp.capacity, int(r2, 2, rk === "kenari" ? 4 : rk === "merak" ? 6 : rk === "bandung" ? 8 : sp.capacity));
        const outcome = off < 0 || off === 0 ? (rk === "kenari" ? (r2() < 0.12 ? "released" : "confirmed") : r2() < 0.07 ? "cancelled" : "confirmed") : (r2() < 0.04 ? "cancelled" : "confirmed");
        plan.push({ rk, off, s: o.s, e: o.e, who, forKey, title, att, note: r2() < 0.15 ? pick(r2, ["Mohon siapkan 6 gelas air minum.", "Perlu kabel HDMI tambahan.", "Peserta dari luar: 2 orang, mohon beri tahu resepsionis.", "Mohon layar dan mikrofon dicek sebelum mulai."]) : null, out: outcome });
      }
      if (off === 0) for (const f of forced[rk]?.[0] ?? []) plan.push(f);
    }
    // parking and equipment: sparse
    const rng = rngOf(104729 * (off + 40));
    if (isWork(off)) {
      if (rng() < 0.5) { const s0 = 60 * int(rng, 8, 13); const who = pick(rng, ["rizky", "dimas", "anita", "putri", "fajar"]); plan.push({ rk: "parkTamu", off, s: s0, e: s0 + 60 * int(rng, 2, 5), who, title: `Parkir tamu — ${pick(rng, COMPANIES)}`, att: null, note: null, out: rng() < 0.05 ? "cancelled" : "confirmed" }); }
      if (rng() < 0.35) { const s0 = 60 * int(rng, 8, 16); plan.push({ rk: "parkEv", off, s: s0, e: s0 + 60 * int(rng, 2, 4), who: pick(rng, ["lina", "anita", "fajar", "agus"]), title: "Mengisi daya mobil listrik", att: null, note: null, out: "confirmed" }); }
      if (rng() < 0.3) { const s0 = 60 * int(rng, 9, 11); plan.push({ rk: "proyektor", off, s: s0, e: s0 + 60 * int(rng, 3, 6), who: pick(rng, ["lina", "rizky", "anita", "putri"]), title: pick(rng, ["Presentasi di acara komunitas", "Pelatihan di area gudang", "Rapat dengan klien di luar kantor", "Sosialisasi di cabang"]), att: null, note: null, out: "confirmed" }); }
      if (off <= 0 && rng() < 0.12) { const s0 = 60 * int(rng, 9, 11); plan.push({ rk: "kamera", off, s: s0, e: s0 + 60 * int(rng, 3, 6), who: pick(rng, ["lina", "lina", "anita"]), title: pick(rng, ["Dokumentasi gathering karyawan", "Foto produk untuk katalog", "Liputan acara peresmian"]), att: null, note: null, out: rng() < 0.2 ? "rejected" : "confirmed" }); }
    }
  }
  // explicit future items that carry flows
  const FUT = [
    ["kamera", nw(3), 540, 960, "lina", "Dokumentasi acara gathering pelanggan", "Foto dan video untuk media sosial; kartu memori dikembalikan hari yang sama.", "pending"],
    ["kamera", nw(5), 540, 900, "anita", "Foto profil karyawan baru", null, "approved"],
    ["proyektor", nw(1), 540, 900, "rizky", "Presentasi hasil audit di gudang", "Diambil pagi hari di meja GA.", "confirmed"],
    ["parkTamu", nw(1), 480, 1020, "dimas", "Parkir tamu — Konsultan ISO 9001", null, "confirmed"],
    ["parkEv", nw(2), 540, 780, "lina", "Mengisi daya mobil listrik agency", null, "confirmed"],
  ];
  for (const [rk, off, s, e, who, title, note, out] of FUT) { plan.push({ rk, off, s, e, who, title, att: null, note, out, explicit: true }); }

  // --- 3c. execute the plan
  const absStart = (p) => at(p.off, p.s), absEnd = (p) => at(p.off, p.e);
  const counters = { made: 0, past: 0, cancelled: 0, released: 0, pending: 0, rejected: 0 };
  const REASONS = ["Rapat dibatalkan karena peserta berhalangan.", "Jadwal dipindah ke minggu depan.", "Ruangan tidak jadi dipakai, rapat dilakukan daring.", "Agenda ditunda oleh pimpinan."];
  const REJECT = ["Peralatan sudah dijadwalkan untuk acara lain pada hari itu.", "Tolong ajukan lagi dengan jadwal yang lebih awal agar bisa disiapkan.", "Pada tanggal itu GA sedang inventarisasi; silakan pilih hari lain."];
  async function execute(p) {
    const res = R[p.rk], sp = res.spec;
    const s = absStart(p), e = absEnd(p);
    const trick = s < Date.now() - 10 * MIN;                          // already started: book far ahead, move back with SQL afterwards
    const shift = trick ? SHIFT_DAYS * DAY : 0;
    const creator = sp.roles ? (p.who === "putri" || p.who === "hendra" ? p.who : "putri") : p.who;
    const b = await book(creator, res, s + shift, e + shift, p.title, p.att, p.note, p.forKey ?? (creator !== p.who ? p.who : null));
    if (!b) return;
    counters.made++;
    let status = b.status;
    if (status === "pending" && p.out !== "pending") {
      if (p.out === "rejected") { must(await putri.post(`/api/bookings/${b.id}/reject`, { note: pick(rngOf(s), REJECT) }), "reject"); status = "rejected"; counters.rejected++; }
      else { must(await (Math.random() < 0.5 ? putri : hendra).post(`/api/bookings/${b.id}/approve`, { note: null }), "approve"); status = "confirmed"; }
    } else if (status === "pending") counters.pending++;
    if (p.out === "cancelled" && status !== "rejected") {
      must(await U[creator].post(`/api/bookings/${b.id}/cancel`, { reason: pick(rngOf(s + 1), REASONS), scope: "this" }), "cancel"); status = "cancelled"; counters.cancelled++;
    }
    if (p.out === "released" ) { status = "released"; counters.released++; }
    if (trick) {
      counters.past++;
      const created = s - int(rngOf(s), 1, 8) * DAY - int(rngOf(s + 5), 1, 6) * HR;
      const sets = [`starts_at=${ts(s)}`, `ends_at=${ts(e)}`, `blocked_until=${ts(e + sp.bufferMinutes * MIN)}`, `created_at=${ts(created)}`, `updated_at=${ts(Math.min(created + 2 * HR, s))}`];
      if (status === "confirmed" && sp.checkInMinutes && p.out !== "released-by-worker") sets.push(`checked_in_at=${ts(s + (1 + (s / MIN) % 7) * MIN)}`);
      if (status === "confirmed") sets.push(`reminded_at=${ts(s - 30 * MIN)}`);
      if (status === "released") sets.push(`status='Released'`);
      if (status === "cancelled") sets.push(`cancelled_at=${ts(Math.min(created + 20 * HR, s - HR))}`);
      if (res.needsApproval) sets.push(`decided_at=${ts(created + 3 * HR)}`);
      q(`UPDATE bookings SET ${sets.join(", ")} WHERE id='${b.id}';`);
    }
    return b;
  }
  const t0 = Date.now();
  // history first (far-ahead bookings are moved into the past straight away, so they never meet the live ones), then what is today or later
  const sorted = [...plan].sort((a, b) => a.off - b.off || a.s - b.s);
  const histEntries = sorted.filter((p) => absStart(p) < Date.now() - 10 * MIN), liveEntries = sorted.filter((p) => absStart(p) >= Date.now() - 10 * MIN);
  for (const p of histEntries) {
    await execute(p);
    if (sqlQueue.length >= 60) flush();
  }
  flush();
  dropNotifications(t0, BELL);   // what the history raised talks about moved dates; the live items below keep theirs
  for (const p of liveEntries) await execute(p);
  flush();
  log(`  bookings: ${counters.made} made (${counters.past} in the past/under way, ${counters.cancelled} cancelled, ${counters.released} released, ${counters.rejected} rejected, ${counters.pending} waiting for approval) in ${((Date.now() - t0) / 1000).toFixed(0)} s`);
  if (Object.keys(fails).length) log(`    skipped: ${Object.entries(fails).map(([k, v]) => `${v}× ${k}`).slice(0, 6).join("; ")}`);

  // =================================================================================================== 4. vehicles: trips of the last 4 weeks, then live states
  const carsFrom = Date.now();
  const odo = { innova: 18_350, avanza: 61_820 };
  const checklistOk = Object.fromEntries(["fuel", "clean", "no_new_damage", "keys", "documents"].map((k) => [k, true]));
  const TRIPS = [
    [20, "avanza", "rizky", null, "Kantor Pajak KPP Pratama Jakarta Selatan I", "Menyerahkan berkas SPT dan bukti potong", "09:00", 3, 28, 85_000, 24_000, 15_000],
    [19, "innova", "hendra", "agus", "Bogor — pertemuan asosiasi pengusaha", "Mendampingi manajer GA ke acara asosiasi", "08:00", 8, 118, 220_000, 78_000, 20_000],
    [17, "avanza", "anita", null, "Bandara Soekarno-Hatta Terminal 3", "Menjemput tamu dari kantor pusat", "10:30", 4, 74, 190_000, 96_000, 35_000],
    [16, "innova", "dimas", null, "PT Bintang Logistik, Cikarang", "Kunjungan evaluasi kontrak distribusi", "08:30", 7, 96, 230_000, 88_000, 0],
    [15, "avanza", "lina", null, "Percetakan Cipta Grafika, Kelapa Gading", "Memeriksa hasil cetak brosur", "13:00", 3, 41, 120_000, 22_000, 10_000],
    [13, "innova", "putri", "bayu", "Gudang Cakung", "Memindahkan arsip dan perlengkapan ke gudang", "08:00", 6, 52, 135_000, 30_000, 0],
    [12, "avanza", "fajar", null, "Bank Mandiri KCP Sudirman dan KPP Setiabudi", "Setoran dan pengurusan administrasi pajak", "09:30", 3, 22, 60_000, 0, 20_000],
    [11, "innova", "anita", "agus", "Kantor Cabang Bandung", "Kunjungan audit HR dan penyerahan dokumen karyawan", "06:30", 13, 338, 620_000, 245_000, 30_000],
    [9, "avanza", "rizky", null, "Gudang Pusat dan vendor di Bekasi", "Mengambil sampel barang dan tanda terima", "09:00", 5, 66, 150_000, 42_000, 0],
    [8, "innova", "dimas", null, "Auto2000 Pasar Minggu", "Servis berkala dan penggantian aki", "08:30", 4, 31, 75_000, 0, 10_000],
    [7, "innova", "hendra", null, "Kementerian Ketenagakerjaan, Kuningan", "Pertemuan terkait wajib lapor ketenagakerjaan", "10:00", 4, 25, 70_000, 0, 25_000],
    [6, "avanza", "lina", null, "Lokasi event, Ancol", "Survei lokasi gathering pelanggan bersama fotografer", "09:00", 6, 48, 110_000, 36_000, 30_000, "Goresan kecil di bumper belakang kanan terkena tiang parkir di Ancol; sudah difoto dan dilaporkan."],
    [5, "innova", "dimas", null, "Bandara Halim Perdanakusuma", "Menjemput direksi dari kunjungan regional", "14:00", 4, 60, 140_000, 12_000, 20_000],
    [3, "avanza", "anita", null, "Kantor cabang Tangerang", "Sosialisasi peraturan perusahaan ke staf cabang", "08:30", 7, 70, 160_000, 56_000, 15_000, null, "clean"],
    [2, "innova", "putri", "agus", "Kantor Pos dan JNE pusat, Jakarta Timur", "Mengirim dokumen kontrak dan paket sampel", "10:00", 3, 36, 95_000, 8_000, 12_000],
    [1, "avanza", "rizky", null, "Cikarang — pengambilan sampel produk", "Mengambil sampel produk dari pabrik mitra", "08:00", 8, 112, 240_000, 70_000, 10_000],
  ];
  const histTripBookings = [];
  async function trip(spec) {
    const [pwN, rk, who, driver, dest, purpose, startHm, hours, km, fuel, toll, park, damage, flaw] = spec;
    const res = R[rk];
    const s0 = floor5(Date.now() - 10 * MIN), e0 = s0 + 60 * MIN;
    const b = must(await U[who].post("/api/bookings", bookBody(res, s0, e0, `Perjalanan dinas: ${dest}`, int(rngOf(km), 2, 4), null, null)), `trip booking ${dest}`);
    if (b.status === "pending") must(await putri.post(`/api/bookings/${b.id}/approve`, { note: null }), "approve trip booking");
    must(await (driver ? putri : U[who]).put(`/api/bookings/${b.id}/trip`, { destination: dest, purpose, needsDriver: false, driverId: driver ? ID[driver] : null }), "trip details");
    const actor = U[driver ?? who];
    const out = odo[rk] + int(rngOf(km + 3), 3, 25);
    must(await actor.post(`/api/bookings/${b.id}/trip/pick-up`, { odometerOut: out }), "pick up");
    const back = out + km;
    const checklist = { ...checklistOk }; if (damage) checklist.no_new_damage = false; if (flaw === "clean") checklist.clean = false;
    must(await actor.post(`/api/bookings/${b.id}/trip/return`, { odometerIn: back, fuelCost: fuel, tollCost: toll, parkingCost: park, damageNotes: damage ?? null, checklist }), "return");
    odo[rk] = back;
    const s = at(pw(pwN), hm(startHm)), e = s + hours * HR, picked = s + 6 * MIN, ret = e - 12 * MIN;
    const created = s - 2 * DAY - 3 * HR;
    q(`UPDATE bookings SET starts_at=${ts(s)}, ends_at=${ts(e)}, blocked_until=${ts(e + res.spec.bufferMinutes * MIN)}, created_at=${ts(created)}, updated_at=${ts(ret)}, checked_in_at=${ts(picked)}, reminded_at=${ts(s - 30 * MIN)}${res.needsApproval ? `, decided_at=${ts(created + 4 * HR)}` : ""} WHERE id='${b.id}';`);
    q(`UPDATE vehicle_trips SET picked_up_at=${ts(picked)}, returned_at=${ts(ret)}, created_at=${ts(created + HR)}, updated_at=${ts(ret)} WHERE booking_id='${b.id}';`);
    flush();   // the car is free again for the next trip
  }
  for (const spec of TRIPS) await trip(spec);
  // two rejected requests and a cancelled one in the past for the approval screen
  const rejected = [["innova", "fajar", 10, "Mengantar keluarga ke acara pribadi", "Mobil operasional tidak dapat dipakai untuk keperluan pribadi."], ["innova", "lina", 4, "Antar barang pribadi ke Bandung", "Pemakaian di luar keperluan kantor tidak disetujui."]];
  for (const [rk, who, pwN, title, reason] of rejected) {
    const s = at(nw(1) + 70 + pwN, 9 * 60);   // book far ahead on a free day, then move
    const b = await book(who, R[rk], s, s + 4 * HR, title, 3, null);
    if (b) { must(await hendra.post(`/api/bookings/${b.id}/reject`, { note: reason }), "reject"); const s2 = at(pw(pwN), 9 * 60); q(`UPDATE bookings SET starts_at=${ts(s2)}, ends_at=${ts(s2 + 4 * HR)}, blocked_until=${ts(s2 + 4 * HR + 30 * MIN)}, created_at=${ts(s2 - 3 * DAY)}, updated_at=${ts(s2 - 2 * DAY)}, decided_at=${ts(s2 - 2 * DAY)} WHERE id='${b.id}';`); }
  }
  flush();
  dropNotifications(carsFrom, BELL);
  log(`  vehicles: ${TRIPS.length} returned trips in the last 4 weeks (${TRIPS.reduce((n, t) => n + t[8], 0)} km, one with damage notes), 2 rejected requests`);

  // --- live vehicle states: one in use (return it), one waiting for pick-up, requests waiting for approval, confirmed ones ahead
  const liveCars = {};
  {
    const s = floor5(Date.now() - 10 * MIN);
    const b = must(await U.rizky.post("/api/bookings", bookBody(R.innova, s, s + 4 * HR, "Kunjungan ke klien PT Bintang Logistik, Cikarang", 3, "Membawa contoh produk dan dokumen penawaran.", null)), "live car booking (Innova)");
    if (b.status === "pending") must(await putri.post(`/api/bookings/${b.id}/approve`, { note: "Silakan, kunci di resepsionis." }), "approve Innova");
    must(await U.rizky.put(`/api/bookings/${b.id}/trip`, { destination: "PT Bintang Logistik, Cikarang", purpose: "Presentasi penawaran layanan distribusi dan penandatanganan addendum kontrak", needsDriver: false }), "trip");
    must(await U.rizky.post(`/api/bookings/${b.id}/trip/pick-up`, { odometerOut: odo.innova + 8 }), "pick up Innova");
    liveCars.inUse = b;
    const s2 = ceil5(Date.now() + 15 * MIN);
    const b2 = must(await U.anita.post("/api/bookings", bookBody(R.avanza, s2, s2 + 4 * HR, "Antar dokumen ke kantor pajak dan bank", 2, null, null)), "live car booking (Avanza)");
    must(await U.anita.put(`/api/bookings/${b2.id}/trip`, { destination: "KPP Pratama Jakarta Setiabudi dan Bank BCA Sudirman", purpose: "Menyerahkan berkas pajak karyawan dan setoran tunai", needsDriver: false }), "trip");
    liveCars.handover = b2;
  }
  const carFuture = [
    // [car, who, off, from, to, title, destination, purpose, needsDriver, decision]
    ["innova", "anita", nw(2), "07:30", "18:30", "Kunjungan audit ke Branch Bandung", "Branch Office Bandung, Jl. Asia Afrika 88", "Audit dokumen karyawan dan penyerahan perlengkapan; perlu pengemudi karena perjalanan jauh.", true, "wait"],
    ["innova", "fajar", nw(4), "09:00", "16:00", "Rapat dengan bank di Kuningan", "Bank Mandiri Prioritas, Kuningan", "Pertemuan pembahasan fasilitas kredit modal kerja.", false, "wait"],
    ["innova", "lina", nw(3), "09:00", "15:00", "Survei lokasi event di Puncak", "Hotel dan ruang pertemuan di Puncak, Bogor", "Survei tempat acara gathering pelanggan.", false, "reject"],
    ["innova", "dimas", nw(5), "08:00", "17:00", "Kunjungan pabrik mitra di Karawang", "PT Nusa Pack, Karawang", "Audit kualitas pemasok bersama tim Operations.", false, "approve"],
    ["innova", "hendra", nw(6), "10:00", "14:00", "Pertemuan di Kementerian Ketenagakerjaan", "Kementerian Ketenagakerjaan, Kuningan", "Konsultasi peraturan baru.", false, "direct"],
    ["avanza", "rizky", nw(1), "08:30", "12:30", "Pengambilan sampel ke vendor di Cikarang", "Kawasan industri MM2100, Cikarang", "Mengambil sampel kemasan baru.", false, "direct"],
    ["avanza", "putri", nw(2), "13:00", "17:00", "Pengurusan perpanjangan STNK dan pajak kendaraan", "Samsat Jakarta Selatan", "Perpanjangan pajak Avanza dan survei servis.", false, "direct"],
    ["avanza", "fajar", nw(4), "09:00", "13:00", "Setoran dan pengambilan buku cek", "Bank BCA KCU Sudirman", "Urusan administrasi perbankan.", false, "direct"],
    ["avanza", "lina", nw(6), "10:00", "16:00", "Kunjungan ke agency desain", "Studio Kreasi Visual, Kemang", "Review materi kampanye akhir tahun.", false, "direct"],
  ];
  for (const [rk, who, off, from, to, title, dest, purpose, needsDriver, decision] of carFuture) {
    const b = await book(who, R[rk], at(off, hm(from)), at(off, hm(to)), title, int(rngOf(off + from.length), 2, 4), null);
    if (!b) continue;
    await soft("trip details", async () => must(await U[who].put(`/api/bookings/${b.id}/trip`, { destination: dest, purpose, needsDriver, driverId: null }), "trip details"));
    if (decision === "reject") must(await hendra.post(`/api/bookings/${b.id}/reject`, { note: "Innova dipakai menjemput tamu direksi di hari yang sama. Silakan gunakan Avanza atau ganti jadwal." }), "reject");
    if (decision === "approve") must(await putri.post(`/api/bookings/${b.id}/approve`, { note: null }), "approve");
  }
  log(`  vehicles live: Innova on the road since ${new Date(liveCars.inUse.startsAt).toISOString().slice(11, 16)}Z (rizky; return it), Avanza waiting for hand-over (anita), 2 Innova requests waiting for approval (one needs a driver)`);

  // =================================================================================================== 5. visitors
  const visitsFrom = Date.now();
  const FIRST = ["Ahmad", "Budi", "Citra", "Dewi", "Eko", "Fitri", "Gunawan", "Hadi", "Indah", "Joko", "Kurniawan", "Lestari", "Mulyadi", "Nurul", "Oki", "Putu", "Rahmat", "Sari", "Taufik", "Umar", "Vina", "Wahyu", "Yusuf", "Zainal", "Ratna", "Bambang", "Siti", "Dian", "Rina", "Agung"];
  const LAST = ["Santoso", "Wijaya", "Kusuma", "Hidayat", "Pratama", "Saputra", "Nugraha", "Lestari", "Wibowo", "Setiawan", "Permana", "Maulana", "Firmansyah", "Hartono", "Gunadi", "Anggraini", "Purnomo", "Halim", "Susanti", "Rahayu"];
  const FIRMS = ["PT Bintang Logistik", "PT Sejuk Abadi", "PT Solusi Data Nusantara", "CV Cipta Kreasi Mandiri", "PT Mitra Konstruksi Utama", "KAP Santoso & Rekan", "PT Nusa Pack", "CV Berkah Teknik", "PT Cipta Grafika Prima", "Studio Kreasi Visual", "PT Asuransi Sentra Jaya", "Bank Mandiri KCP Sudirman", "PT Kurnia Printindo", "PT Telkom Indonesia", "CV Anugerah Furniture"];
  const PURPOSES = ["Presentasi penawaran layanan", "Rapat evaluasi kontrak", "Pemeliharaan AC dan inspeksi rutin", "Pengiriman dan serah terima barang", "Wawancara kerja", "Audit dokumen", "Demo produk", "Diskusi kerja sama distribusi", "Survei lokasi renovasi", "Penagihan dan rekonsiliasi pembayaran", "Pelatihan K3 untuk vendor", "Pertemuan dengan konsultan"];
  const HOSTS = [["rizky", 3], ["dimas", 3], ["anita", 3], ["fajar", 2], ["lina", 2], ["putri", 2], ["hendra", 1]];
  const phone = (r) => `+62 8${int(r, 11, 99)}-${int(r, 1000, 9999)}-${int(r, 1000, 9999)}`;
  const plate = (r) => `B ${int(r, 1000, 9899)} ${pick(r, ["UKF", "SKT", "KJP", "TZA", "RFD", "PLM", "NHD"])}`;
  // a visit whose window is already over cannot be registered: register it with a placeholder window and move it back afterwards
  const registerVisit = async (by, hostKey, who) => {
    const over = who.until < Date.now() + 5 * MIN;
    const v = must(await U[by].post("/api/visits", {
      visitorName: who.name, company: who.company ?? null, email: who.email ?? null, phone: who.phone ?? null, vehiclePlate: who.plate ?? null, hostId: hostKey ? ID[hostKey] : null,
      locationId: (who.place ?? L.reception).id, purpose: who.purpose, expectedFrom: iso(over ? Date.now() + 30 * MIN : who.from), expectedUntil: iso(over ? Date.now() + 3 * HR : who.until),
    }), `visit ${who.name}`);
    if (over) q(`UPDATE visits SET expected_from=${ts(who.from)}, expected_until=${ts(who.until)}, created_at=${ts(who.from - 3 * HR)} WHERE id='${v.id}';`);
    return v;
  };
  let badgeSeq = 20;
  const checkIn = async (v, last4) => must(await sinta.post(`/api/visits/${v.id}/check-in`, { badgeNumber: `V-${String(++badgeSeq % 40 + 1).padStart(2, "0")}`, idLast4: last4 ?? String(int(rngOf(badgeSeq), 1000, 9999)), acknowledged: true }), `check in ${v.visitorName}`);
  const vsql = (v, f) => q(`UPDATE visits SET ${Object.entries(f).map(([k, x]) => `${k}=${x === null ? "NULL" : typeof x === "number" ? ts(x) : x}`).join(", ")} WHERE id='${v.id}';`);
  const visitNames = new Set(["Heru Santoso", "Rudi Hartono", "Teguh Wibowo"]);
  const personName = (r) => { for (;;) { const n = `${pick(r, FIRST)} ${pick(r, LAST)}`; if (!visitNames.has(n)) { visitNames.add(n); return n; } } };

  // 5a. history: the last 28 working days
  let hv = 0; const hist = { out: 0, noShow: 0, cancelled: 0 };
  for (let n = 28; n >= 1; n--) {
    const off = pw(n); const rng = rngOf(31 * n + 7);
    const count = rng() < 0.2 ? 0 : rng() < 0.6 ? 1 : 2;
    for (let i = 0; i < count; i++) {
      const host = wpick(rng, HOSTS); const from = at(off, 60 * int(rng, 9, 15) + 30 * int(rng, 0, 1)); const until = from + 60 * MIN * int(rng, 1, 3);
      const kind = rng() < 0.08 ? "noShow" : rng() < 0.08 ? "cancelled" : "out";
      const company = pick(rng, FIRMS);
      const v = await registerVisit(host === "putri" || host === "hendra" ? host : host, null, { name: personName(rng), company, phone: phone(rng), plate: rng() < 0.4 ? plate(rng) : null, purpose: pick(rng, PURPOSES), from: Date.now() + 30 * MIN, until: Date.now() + 3 * HR });
      hv++;
      if (kind === "cancelled") { must(await U[host].post(`/api/visits/${v.id}/cancel`), "cancel visit"); vsql(v, { expected_from: from, expected_until: until, created_at: from - 2 * DAY, updated_at: from - 20 * HR }); hist.cancelled++; }
      else if (kind === "noShow") { vsql(v, { expected_from: from, expected_until: until, created_at: from - 2 * DAY, updated_at: until + HR, status: "'NoShow'" }); hist.noShow++; }
      else {
        const inn = await checkIn(v); must(await sinta.post(`/api/visits/${v.id}/check-out`), "check out");
        const cin = from + int(rng, 0, 20) * MIN, cout = Math.min(until, cin + int(rng, 40, 150) * MIN);
        vsql(v, { expected_from: from, expected_until: until, created_at: from - 2 * DAY, updated_at: cout, checked_in_at: cin, acknowledged_at: cin, checked_out_at: cout });
        hist.out++;
      }
    }
    if (sqlQueue.length >= 60) flush();
  }
  flush();
  dropNotifications(visitsFrom, ["visitor.%"]);
  log(`  visitors history: ${hv} visits in 28 working days (${hist.out} left, ${hist.noShow} no-shows, ${hist.cancelled} cancelled)`);

  // every host should see the awkward cases in their own list: a no-show for rizky and dimas, a cancelled one for dimas
  for (const [host, kind, pwN, nm2, company, purpose] of [["rizky", "noShow", 6, "Dedi Kurniawan", "PT Kurnia Printindo", "Penawaran cetak ulang formulir pengiriman"], ["dimas", "noShow", 9, "Susi Susilawati", "PT Telkom Indonesia", "Presentasi layanan konektivitas kantor"], ["dimas", "cancelled", 4, "Benny Halim", "CV Anugerah Furniture", "Survei kebutuhan furnitur ruang arsip"]]) {
    const from = at(pw(pwN), 10 * 60), until = from + 2 * HR;
    const v = await registerVisit(host, null, { name: nm2, company, phone: "+62 812-7700-3" + String(pwN).padStart(3, "0"), purpose, from: Date.now() + 30 * MIN, until: Date.now() + 3 * HR });
    if (kind === "cancelled") must(await U[host].post(`/api/visits/${v.id}/cancel`), "cancel visit");
    vsql(v, { expected_from: from, expected_until: until, created_at: from - 2 * DAY, updated_at: until, ...(kind === "noShow" ? { status: "'NoShow'" } : {}) });
  }
  flush();
  dropNotifications(visitsFrom, ["visitor.%"]);

  // 5b. today and ahead (relative to now)
  const N = Date.now();
  const dayStart = jkMidnight(0) + 5 * MIN;
  const clampDay = (ms) => Math.max(ms, dayStart);
  const NOWV = {};
  NOWV.bambang = await registerVisit("rizky", null, { name: "Bambang Hermawan", company: "PT Bintang Logistik", email: "bambang.hermawan@bintanglogistik.co.id", phone: "+62 812-3344-7788", plate: "B 9012 UKF", purpose: "Presentasi penawaran layanan armada dan pembahasan kontrak 2027", place: L.garuda, from: N + 30 * MIN, until: N + 3 * HR });
  NOWV.andreas = await registerVisit("dimas", null, { name: "Ir. Andreas Wijaya", company: "PT Mitra Konstruksi Utama", phone: "+62 811-9055-2210", plate: "B 1647 TZA", purpose: "Survei lokasi renovasi ruang arsip Lantai 5", place: L.lvl5, from: N - 20 * MIN, until: N + 4 * HR });
  NOWV.dewi = await registerVisit("anita", null, { name: "Dewi Anggraeni", company: null, phone: "+62 857-1122-9034", purpose: "Wawancara kandidat Staff Accounting (tahap 2)", place: L.lvl3, from: N + 60 * MIN, until: N + 3 * HR });
  NOWV.yoga = await registerVisit("lina", null, { name: "Yoga Prasetya", company: "Studio Kreasi Visual", email: "yoga@kreasivisual.id", phone: "+62 813-6677-4501", purpose: "Presentasi konsep desain kampanye akhir tahun", place: L.lvl3, from: N + 90 * MIN, until: N + 4 * HR });
  NOWV.yohanes = await registerVisit("fajar", null, { name: "Yohanes Pratama", company: "PT Solusi Data Nusantara", phone: "+62 811-2200-6431", plate: "B 3380 PLM", purpose: "Demo aplikasi akuntansi dan integrasi pajak", place: L.lvl3, from: N - 100 * MIN, until: N + 2 * HR });
  NOWV.linda = await registerVisit("dimas", null, { name: "Linda Susanti, S.T.", company: "Lembaga Sertifikasi Mutu Indonesia", phone: "+62 819-0033-8872", purpose: "Audit pendahuluan ISO 9001 — wawancara PIC Operations", place: L.garuda, from: N - 75 * MIN, until: N + 5 * HR });
  NOWV.agus = await registerVisit("rizky", null, { name: "Agus Salim", company: "CV Berkah Teknik", phone: "+62 877-4411-2090", plate: "B 7712 RFD", purpose: "Pemeriksaan dan servis forklift gudang", place: L.warehouse, from: N - 50 * MIN, until: N + 3 * HR });
  NOWV.slamet = await registerVisit("rizky", null, { name: "Slamet Riyadi", company: "PT Nusa Pack", phone: "+62 812-8800-1745", purpose: "Serah terima sampel kemasan baru", place: L.lvl3, from: N - 4 * HR, until: N - 1 * HR });
  NOWV.fitri = await registerVisit("anita", null, { name: "Fitri Handayani", company: "BPJS Kesehatan Cabang Jakarta Selatan", phone: "+62 815-9000-3321", purpose: "Verifikasi data kepesertaan karyawan", place: L.lvl3, from: N - 3 * HR - 20 * MIN, until: N - 1 * HR });
  NOWV.tono = await registerVisit("putri", null, { name: "Tono Sutrisno", company: "PT Sejuk Abadi", phone: "+62 811-7788-2299", purpose: "Pemeriksaan unit AC Lantai 3 (teknisi vendor)", place: L.lvl3, from: N - 5 * HR, until: N - 3 * HR });
  NOWV.maria = await registerVisit("anita", null, { name: "Maria Ulfa", company: "PT Asuransi Sentra Jaya", phone: "+62 858-1234-5670", purpose: "Presentasi asuransi kesehatan karyawan", place: L.lvl3, from: N - 4 * HR, until: N - 2 * HR });
  NOWV.rina = await registerVisit("anita", null, { name: "Rina Marlina", company: "PT Gunung Mas", phone: "+62 812-4455-9007", purpose: "Penawaran jasa katering karyawan", place: L.lvl3, from: N + 2 * HR, until: N + 4 * HR });
  NOWV.heru = await registerVisit("rizky", null, { name: "Heru Santoso", company: "CV Mitra Sejahtera Abadi", phone: "+62 813-2299-1018", purpose: "Menawarkan kerja sama pengadaan alat tulis kantor", place: L.reception, from: N + 2 * HR, until: N + 5 * HR });
  // arrivals at reception
  await checkIn(NOWV.yohanes, "4821"); await checkIn(NOWV.linda, "9034"); await checkIn(NOWV.agus, "2276");
  vsql(NOWV.yohanes, { checked_in_at: clampDay(N - 75 * MIN), acknowledged_at: clampDay(N - 75 * MIN), updated_at: clampDay(N - 75 * MIN) });
  vsql(NOWV.linda, { checked_in_at: clampDay(N - 55 * MIN), acknowledged_at: clampDay(N - 55 * MIN), updated_at: clampDay(N - 55 * MIN) });
  vsql(NOWV.agus, { checked_in_at: clampDay(N - 30 * MIN), acknowledged_at: clampDay(N - 30 * MIN), updated_at: clampDay(N - 30 * MIN) });
  // left today
  for (const [v, last4, inn, outt] of [[NOWV.slamet, "7305", N - 3 * HR - 50 * MIN, N - 2 * HR - 10 * MIN], [NOWV.fitri, "1198", N - 3 * HR, N - 100 * MIN], [NOWV.tono, "6642", N - 4 * HR - 55 * MIN, N - 3 * HR - 5 * MIN]]) {
    await checkIn(v, last4); must(await sinta.post(`/api/visits/${v.id}/check-out`), "check out");
    vsql(v, { checked_in_at: clampDay(inn), acknowledged_at: clampDay(inn), checked_out_at: clampDay(outt), updated_at: clampDay(outt) });
  }
  // no-show (his window has passed), cancelled, the blocked one stays flagged
  vsql(NOWV.maria, { expected_from: clampDay(N - 4 * HR), expected_until: clampDay(N - 2 * HR), status: "'NoShow'", updated_at: clampDay(N - 2 * HR) });
  must(await U.anita.post(`/api/visits/${NOWV.rina.id}/cancel`), "cancel visit");
  // walk-in registered by reception: the visitor stands at the desk, the host is not even aware
  const walk = await registerVisit("sinta", "lina", { name: "Hendrik Gunawan", company: "CV Cipta Kreasi Mandiri", phone: "+62 856-3300-4412", purpose: "Tanpa janji temu: mengantar sampel kain untuk tim Marketing", place: L.reception, from: N - 15 * MIN, until: N + 2 * HR });
  await checkIn(walk, "3350"); vsql(walk, { checked_in_at: clampDay(N - 12 * MIN), acknowledged_at: clampDay(N - 12 * MIN), updated_at: clampDay(N - 12 * MIN), created_at: clampDay(N - 14 * MIN) });
  // tomorrow and the days after
  const aheadV = [
    ["rizky", nw(1), "10:00", "12:00", "Hadi Susanto", "PT Bintang Logistik", "Penandatanganan addendum kontrak distribusi", "+62 812-5566-7000", "B 4410 SKT"],
    ["dimas", nw(1), "13:00", "16:00", "Prof. Maria Kristina", "Lembaga Sertifikasi Mutu Indonesia", "Lanjutan audit ISO 9001: dokumen dan rekaman", "+62 811-3321-9005", null],
    ["anita", nw(1), "09:30", "10:30", "Rizal Ramadhan", null, "Wawancara kandidat HR Officer", "+62 878-9090-1122", null],
    ["anita", nw(1), "11:00", "12:00", "Nadia Safitri", null, "Wawancara kandidat HR Officer", "+62 877-1020-3040", null],
    ["lina", nw(2), "14:00", "16:00", "Michael Tanuwijaya", "PT Cipta Grafika Prima", "Review hasil cetak materi kampanye", "+62 813-7000-5511", "B 2209 NHD"],
    ["fajar", nw(2), "08:30", "17:00", "Tim auditor KAP Santoso & Rekan (3 orang)", "KAP Santoso & Rekan", "Audit interim laporan keuangan — hari pertama", "+62 21-5551-0099", null],
    ["fajar", nw(3), "08:30", "17:00", "Tim auditor KAP Santoso & Rekan (3 orang)", "KAP Santoso & Rekan", "Audit interim laporan keuangan — hari kedua", "+62 21-5551-0099", null],
    ["rizky", nw(3), "10:00", "11:30", "Eko Prasetyo", "PT Karya Mandiri", "Kunjungan pemasok baru", "+62 812-0099-8800", null],
    ["hendra", nw(4), "10:00", "12:00", "Anton Wijayakusuma", "PT Sejuk Abadi", "Pembahasan kontrak perawatan AC 2027", "+62 811-9000-1234", "B 1188 UKF"],
    ["dimas", nw(5), "09:00", "11:00", "Dr. Irawan Setiadi", "Universitas Indonesia", "Diskusi program magang mahasiswa", "+62 812-3000-4567", null],
  ];
  const aheadMade = [];
  for (const [host, off, from, to, nm2, company, purpose, ph, pl] of aheadV) aheadMade.push(await registerVisit(host, null, { name: nm2, company, phone: ph, plate: pl, purpose, place: L.lvl3, from: at(off, hm(from)), until: at(off, hm(to)) }));
  // one cancelled for tomorrow and an older cancelled one for the host's list
  const ek = await registerVisit("rizky", null, { name: "Joko Widodo Saputra", company: "PT Karya Mandiri", phone: "+62 812-6655-4001", purpose: "Presentasi produk baru", place: L.lvl3, from: at(nw(2), 600), until: at(nw(2), 660) });
  must(await U.rizky.post(`/api/visits/${ek.id}/cancel`), "cancel visit");
  flush();
  log(`  visitors: today ${Object.keys(NOWV).length + 1} (3 expected soon, 4 on site incl. the walk-in, 3 left, 1 no-show, 1 cancelled, 1 blocked) + ${aheadMade.length} ahead`);

  // =================================================================================================== 6. the mailroom
  const mailFrom = Date.now();
  const EMP = { rizky: d.CAST.rizky.email, anita: d.CAST.anita.email, dimas: d.CAST.dimas.email, fajar: d.CAST.fajar.email, lina: d.CAST.lina.email, putri: d.CAST.putri.email, hendra: d.CAST.hendra.email };
  const addr = { "PT Bintang Logistik": "Jl. Raya Cikarang–Cibarusah Km 8, Cikarang Selatan, Bekasi 17530", "PT Nusa Pack": "Kawasan Industri KIIC Blok C-12, Karawang 41361", "PT Solusi Data Nusantara": "Gedung Menara Rajawali Lt. 9, Jl. Mega Kuningan, Jakarta Selatan 12950", "Kantor Pajak KPP Pratama Jakarta Setiabudi": "Jl. H.R. Rasuna Said Kav. 5, Jakarta Selatan 12940", "Bank Mandiri KCP Sudirman": "Jl. Jend. Sudirman Kav. 54, Jakarta Selatan 12190", "PT Cipta Grafika Prima": "Jl. Boulevard Barat Raya, Kelapa Gading, Jakarta Utara 14240", "Notaris Hendra Gunadi, S.H.": "Jl. Wijaya I No. 15, Kebayoran Baru, Jakarta Selatan 12170" };
  const way = { JNE: (r) => String(int(r, 1e15, 9e15)).slice(0, 16), "J&T Express": (r) => `JP${int(r, 1e9, 9e9)}`, SiCepat: (r) => `00${int(r, 1e9, 9e9)}`, "Pos Indonesia": (r) => `P${String(int(r, 1e12, 9e12))}`, Anteraja: (r) => `10${int(r, 1e10, 9e10)}` };
  async function logMail(o) {
    return must(await sinta.post("/api/mail", { direction: o.dir, kind: o.kind, personEmail: EMP[o.to], otherParty: o.other, address: o.address ?? null, carrier: o.carrier ?? null, waybill: o.waybill ?? null, description: o.desc ?? null, locationId: L.reception.id }), `mail ${o.desc ?? o.other}`);
  }
  const msql = (m, f) => q(`UPDATE mail_items SET ${Object.entries(f).map(([k, x]) => `${k}=${x === null ? "NULL" : typeof x === "number" ? ts(x) : x}`).join(", ")} WHERE id='${m.id}';`);
  const mrng = rngOf(424242);
  const INCOMING = [
    ["parcel", "Tokopedia — pesanan perlengkapan kantor", "JNE", "Satu dus ukuran sedang berisi ATK"], ["parcel", "PT Kurnia Printindo — kartu nama", "J&T Express", "Kartu nama 5 boks"], ["letter", "BPJS Ketenagakerjaan Cabang Setiabudi", "Kurir langsung", "Surat pemberitahuan iuran"],
    ["parcel", "Gramedia Online", "SiCepat", "Buku referensi manajemen, 3 eksemplar"], ["document", "Kantor Pajak KPP Pratama Jakarta Setiabudi", "Pos Indonesia", "Surat ketetapan dan lampiran"], ["parcel", "Shopee — Official Store", "Anteraja", "Paket kecil"],
    ["letter", "Bank BCA KCU Sudirman", "Kurir langsung", "Surat konfirmasi rekening giro"], ["parcel", "PT Cipta Grafika Prima — sampel brosur", "JNE", "Sampel cetak brosur A5, 1 dus"], ["document", "Notaris Hendra Gunadi, S.H.", "Kurir langsung", "Salinan akta perubahan"],
    ["parcel", "Blibli", "J&T Express", "Satu paket kecil"], ["letter", "Dinas Tenaga Kerja Provinsi DKI Jakarta", "Pos Indonesia", "Undangan sosialisasi wajib lapor"], ["parcel", "Lazada", "SiCepat", "Satu dus"],
    ["other", "PT Sejuk Abadi", "Kurir langsung", "Filter AC cadangan titipan teknisi"], ["parcel", "Tokopedia — toner printer", "JNE", "Toner HP 2 buah"], ["letter", "PT Asuransi Sentra Jaya", "Kurir langsung", "Polis dan kuitansi premi"],
  ];
  const incomingFor = ["rizky", "anita", "fajar", "lina", "dimas", "hendra", "putri"];
  // collected earlier (and collected today): logged, collected by reception, then dated
  let mi = 0; const mail = { collected: 0, waiting: 0, sent: 0, pending: 0 };
  for (let n = 26; n >= 1; n -= 2) {
    const [kind, other, carrier, desc] = INCOMING[mi % INCOMING.length]; const to = incomingFor[(mi * 3 + 1) % incomingFor.length]; mi++;
    const m = await logMail({ dir: "incoming", kind, to, other, carrier, waybill: carrier === "Kurir langsung" ? null : (way[carrier] ?? way.JNE)(mrng), desc });
    const loggedAt = at(pw(Math.ceil(n * 0.72)), 60 * int(mrng, 9, 15) + int(mrng, 0, 5) * 10);
    const doneAt = loggedAt + int(mrng, 2, 30) * HR * (mrng() < 0.6 ? 1 : 3);
    const colleague = mrng() < 0.25; const by = colleague ? d.CAST[incomingFor[(mi + 2) % incomingFor.length]].name : d.CAST[to].name;
    must(await sinta.post(`/api/mail/${m.id}/collect`, { collectedBy: by, note: colleague ? "Diambil rekan satu tim atas nama penerima." : null }), "collect");
    msql(m, { logged_at: loggedAt, done_at: Math.min(doneAt, Date.now() - 2 * HR), updated_at: Math.min(doneAt, Date.now() - 2 * HR), reminded_at: mrng() < 0.4 ? loggedAt + 49 * HR : null });
    mail.collected++;
  }
  // outgoing sent in the last 4 weeks
  const OUT = [
    ["document", "rizky", "PT Bintang Logistik — Bpk. Hadi Susanto", "PT Bintang Logistik", "Kontrak kerja sama distribusi (asli, 2 rangkap)", "JNE"], ["letter", "anita", "Kandidat — Sdr. Rizal Ramadhan", null, "Surat panggilan wawancara", "Pos Indonesia"],
    ["parcel", "lina", "PT Cipta Grafika Prima — Ibu Wulan", "PT Cipta Grafika Prima", "Master desain brosur dan contoh warna", "SiCepat"], ["document", "fajar", "Kantor Pajak KPP Pratama Jakarta Setiabudi", "Kantor Pajak KPP Pratama Jakarta Setiabudi", "Berkas SPT Masa dan bukti potong", "Kurir langsung"],
    ["parcel", "dimas", "PT Nusa Pack — Bpk. Slamet Riyadi", "PT Nusa Pack", "Sampel produk untuk uji kemasan", "J&T Express"], ["letter", "hendra", "Notaris Hendra Gunadi, S.H.", "Notaris Hendra Gunadi, S.H.", "Surat kuasa dan salinan KTP direksi", "Anteraja"],
    ["document", "putri", "PT Solusi Data Nusantara", "PT Solusi Data Nusantara", "Surat penunjukan pemenang tender", "JNE"], ["parcel", "anita", "Karyawan cabang Bandung — Sdr. Yusuf", null, "Laptop pengganti dan kartu akses", "J&T Express"],
    ["letter", "rizky", "Bank Mandiri KCP Sudirman", "Bank Mandiri KCP Sudirman", "Surat permohonan fasilitas", "Kurir langsung"], ["document", "dimas", "PT Bintang Logistik — Bpk. Hadi Susanto", "PT Bintang Logistik", "Addendum kontrak tanda tangan basah", "JNE"],
  ];
  for (let i = 0; i < OUT.length; i++) {
    const [kind, who, other, company, desc, carrier] = OUT[i];
    const m = await logMail({ dir: "outgoing", kind, to: who, other, address: company ? addr[company] ?? null : "Jl. Asia Afrika No. 88, Bandung 40111", desc });
    const wb = carrier === "Kurir langsung" ? `KURIR-${int(mrng, 100, 999)}` : (way[carrier] ?? way.JNE)(mrng);
    must(await sinta.post(`/api/mail/${m.id}/send`, { carrier, waybill: wb, note: null }), "send");
    const loggedAt = at(pw(2 + i * 2), 60 * int(mrng, 9, 14)); const sentAt = loggedAt + int(mrng, 1, 5) * HR;
    msql(m, { logged_at: loggedAt, done_at: sentAt, updated_at: sentAt }); mail.sent++;
  }
  flush();
  dropNotifications(mailFrom, ["mail.%"]);   // history: the notices carry dates that were moved
  // two collected today (logged this morning), two sent today
  for (const [to, kind, other, carrier, desc] of [["lina", "parcel", "PT Cipta Grafika Prima — sampel brosur", "JNE", "Sampel cetak brosur A5, 1 dus"], ["fajar", "document", "Kantor Pajak KPP Pratama Jakarta Setiabudi", "Pos Indonesia", "Surat ketetapan dan lampiran"]]) {
    const m = await logMail({ dir: "incoming", kind, to, other, carrier, waybill: (way[carrier])(mrng), desc });
    must(await sinta.post(`/api/mail/${m.id}/collect`, { collectedBy: d.CAST[to].name }), "collect today");
    msql(m, { logged_at: clampDay(N - 5 * HR), done_at: clampDay(N - 70 * MIN), updated_at: clampDay(N - 70 * MIN) }); mail.collected++;
  }
  const sentToday = await logMail({ dir: "outgoing", kind: "document", to: "hendra", other: "PT Sejuk Abadi — Bpk. Anton Wijayakusuma", address: "Jl. Raya Bogor Km 24, Cimanggis, Depok 16451", desc: "Surat penunjukan perawatan AC 2027 (asli)" });
  must(await sinta.post(`/api/mail/${sentToday.id}/send`, { carrier: "Kurir langsung", waybill: "KURIR-214", note: "Diantar kurir internal." }), "send today"); mail.sent++;
  // waiting at reception (several ages), voided one
  const WAITING = [
    ["rizky", "parcel", "PT Kurnia Printindo — kartu nama", "J&T Express", "Kartu nama 5 boks", 0], ["rizky", "parcel", "Tokopedia — pesanan perlengkapan kantor", "JNE", "Satu dus ukuran sedang berisi ATK", 1],
    ["anita", "letter", "BPJS Ketenagakerjaan Cabang Setiabudi", "Kurir langsung", "Surat pemberitahuan iuran", 3], ["anita", "parcel", "Gramedia Online", "SiCepat", "Buku referensi manajemen, 3 eksemplar", 0],
    ["dimas", "document", "Dinas Tenaga Kerja Provinsi DKI Jakarta", "Pos Indonesia", "Undangan sosialisasi wajib lapor", 1], ["fajar", "letter", "Bank BCA KCU Sudirman", "Kurir langsung", "Surat konfirmasi rekening giro", 5],
    ["lina", "parcel", "Shopee — Official Store", "Anteraja", "Paket kecil", 2], ["hendra", "document", "Notaris Hendra Gunadi, S.H.", "Kurir langsung", "Salinan akta perubahan", 0],
  ];
  for (const [to, kind, other, carrier, desc, ageDays] of WAITING) {
    const m = await logMail({ dir: "incoming", kind, to, other, carrier, waybill: carrier === "Kurir langsung" ? null : way[carrier](mrng), desc });
    if (ageDays > 0) msql(m, { logged_at: at(-ageDays, 60 * int(mrng, 9, 15)), updated_at: N - ageDays * DAY }); else if (mail.waiting % 2 === 1) msql(m, { logged_at: clampDay(N - 3 * HR), updated_at: clampDay(N - 3 * HR) });
    mail.waiting++;
  }
  const mistaken = await logMail({ dir: "incoming", kind: "parcel", to: "fajar", other: "Pengirim tidak dikenal", carrier: "SiCepat", waybill: "0012345678", desc: "Paket salah alamat" });
  must(await sinta.post(`/api/mail/${mistaken.id}/void`, { note: "Salah alamat; paket dikembalikan ke kurir." }), "void");
  msql(mistaken, { logged_at: at(-3, 600), updated_at: at(-3, 660) });
  // outgoing still at reception: waiting to be marked as sent
  const PENDING = [
    ["document", "rizky", "PT Bintang Logistik — Bpk. Hadi Susanto", "Kontrak kerja sama distribusi 2027 (asli, 2 rangkap)", "PT Bintang Logistik", null],
    ["letter", "anita", "Kandidat — Sdri. Nadia Safitri", "Surat penawaran kerja (offering letter)", null, "JNE"],
    ["parcel", "dimas", "PT Nusa Pack — Bpk. Slamet Riyadi", "Sampel produk untuk uji ketahanan kemasan", "PT Nusa Pack", null],
    ["document", "fajar", "Kantor Pajak KPP Pratama Jakarta Setiabudi", "Bukti potong PPh 23 periode Oktober", "Kantor Pajak KPP Pratama Jakarta Setiabudi", null],
  ];
  for (const [kind, who, other, desc, company, carrier] of PENDING) {
    const m = await logMail({ dir: "outgoing", kind, to: who, other, address: company ? addr[company] : "Jl. Asia Afrika No. 88, Bandung 40111", carrier, desc });
    if (mail.pending % 2 === 1) msql(m, { logged_at: at(-1, 60 * int(mrng, 10, 16)), updated_at: N - 18 * HR }); mail.pending++;
  }
  flush();
  log(`  mail: ${mail.collected} collected, ${mail.waiting} waiting at reception (oldest 5 days), ${mail.sent} sent, ${mail.pending} outgoing waiting to be sent, 1 voided`);

  // =================================================================================================== 7. the personal calendar feed
  const feeds = {};
  for (const k of ["rizky", "putri", "dimas"]) {
    const f = must(await U[k].post("/api/bookings/feed"), `feed ${k}`);
    feeds[k] = f.url;
  }
  const token = (feeds.rizky ?? "").split("/").pop();
  if (token) await soft("read the feed once", async () => { const r = await fetch(`${d.api}/api/calendar/${token}`, { headers: { "X-Internal-Proxy-Key": d.key, Origin: d.origin } }); if (!r.ok) throw new Error(String(r.status)); });
  log(`  calendar feed: links for rizky, dimas, putri (rizky's was read once so "last used" shows)`);

  // =================================================================================================== 8. let the booking worker release the room nobody checked in to (it runs every minute)
  const releaseTarget = rows(must(await U.admin.get(`/api/bookings?scope=all&cancelled=true&resourceId=${R.kenari.id}&pageSize=100`), "kenari bookings")).find((b) => b.title === "Diskusi cepat rekonsiliasi dengan auditor");
  if (releaseTarget) {
    let status = releaseTarget.status;
    for (let i = 0; i < 14 && status !== "released"; i++) {
      await d.sleep(6_000);
      status = must(await U.admin.get(`/api/bookings/${releaseTarget.id}`), "poll").status;
    }
    if (status !== "released") { d.sql(`UPDATE bookings SET status='Released', updated_at=now() WHERE id='${releaseTarget.id}' AND status='Confirmed';`); log("    (the booking worker did not run in time: marked the released booking directly)"); }
    else log("    the booking worker released the unattended Kenari booking (notification sent to fajar)");
  }

  // =================================================================================================== 9. check what is there
  const left = d.sql(`SELECT count(*) FROM bookings WHERE starts_at > now() + interval '${SHIFT_DAYS - 30} days';`).trim();
  if (left !== "0") log(`    ! ${left} bookings are still far in the future (the move into the past did not happen for them)`);
  const sum = must(await putri.get("/api/bookings/summary"), "summary");
  const util = must(await hendra.get("/api/reports/utilisation?days=28"), "utilisation");
  const vs = must(await sinta.get("/api/visits/summary"), "visit summary");
  const ms = must(await sinta.get("/api/mail/summary"), "mail summary");
  log(`  check: bookings today ${sum.bookingsToday}, in use ${sum.inUseNow}, pending approvals ${sum.pendingApprovals}, no-shows(7d) ${sum.noShows7}; utilisation total ${util.total.percent}% (${util.total.bookings} bookings, ${util.total.noShows} no-shows, ${util.total.km} km);`);
  log(`         reception: expected today ${vs.expectedToday}, on site ${vs.onSiteNow}, left today ${vs.checkedOutToday}, no-shows 30d ${vs.noShows30}, flagged ${vs.flagged}; mail: waiting ${ms.waiting}, outgoing pending ${ms.outgoingPending}`);
  d.ctx.spaces = { res: R, feedUrl: feeds.rizky, liveCars };
}
