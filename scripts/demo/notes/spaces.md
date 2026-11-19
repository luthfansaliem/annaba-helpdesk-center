# Modul `spaces`: booking, kendaraan, tamu, resepsionis, mailroom

File: `scripts/demo/02-spaces.mjs`. Waktu seed sekitar 2,5 menit (menunggu BookingWorker ±1 menit, lihat caveat). Semua waktu "live" dihitung dari saat seed (WIB), jadi **jalankan seed sesaat sebelum demo**: jendela check-in, tamu "akan datang" dan mobil yang sedang jalan basi setelah beberapa jam. Notasi: "H+1" = hari kerja berikutnya setelah hari seed.

## Screens covered

| Layar | Dibuka oleh | Isi |
|---|---|---|
| `/bookings` (grid hari + minggu) | rizky, anita, fajar, lina, dimas | 12 resource, ±460 booking (28 hari lalu s.d. 9 hari kerja ke depan). Judul/pemesan booking orang lain tersembunyi untuk karyawan (by design), booking sendiri terlihat. Tab Mobil/Parkir/Peralatan terisi. Kartu "Booking saya" berisi yang akan datang |
| `/bookings` (sisi GA: pending, siapa memesan apa) | putri, hendra | Antrean persetujuan 3 item, semua judul + pemesan terlihat, KPI hari ini 15 booking / 4 dipakai sekarang |
| `/bookings` Calendar feed | rizky, putri, dimas | Link feed sudah dibuat (punya rizky sudah pernah dibaca, jadi "terakhir dipakai" terisi). URL hanya tampil sekali: buat ulang di UI bila perlu |
| `/setup/resources` | hendra | 12 resource: 5 ruang rapat aktif + 1 diarsipkan, 2 mobil, 2 parkir, 2 peralatan. Dokumen kendaraan (STNK/asuransi/servis) dan 8 SIM pengemudi |
| `/reports/utilisation` | hendra, putri | 28 hari: Garuda 49%, Merak 39%, Bandung 35%, Cenderawasih 27%, Kenari 8% (13 no-show), 16 perjalanan mobil, 1.217 km, biaya Rp 3,76 juta |
| `/visitors` | rizky (15 kunjungan), dimas (11), anita (14) | Masing-masing punya expected/sedang di lokasi/selesai/no-show/dibatalkan (rizky juga satu bertanda block list) |
| `/reception` | sinta | Hari ini 14 baris: 4 menunggu (3 baru akan datang + 1 di-flag), 4 di lokasi (termasuk walk-in), 3 sudah pulang, 1 no-show, 1 dibatalkan. Tanggal H+1: 4 tamu terdaftar |
| `/setup/visitors` | hendra | Teks acknowledgement (5 butir), "minta 4 digit terakhir ID" aktif, retensi 90 hari, 3 entri block list |
| `/mail` | rizky, anita (sisi karyawan); sinta (meja mailroom) | Meja: 8 menunggu (umur 0 s.d. 5 hari, 2 melewati 2 hari), 15 sudah diambil, 11 keluar terkirim, 4 keluar belum dikirim, 1 void |
| Bel notifikasi | semua | Lihat bagian bawah |

## Resource (kunci untuk dicari di UI)

- **Garuda meeting room** (R-301, 12 org, 08-18 Sen-Jum, buffer 15, maks 4 jam) · **Merak meeting room** (R-302, 6 org, maks 3 jam) · **Cenderawasih boardroom** (R-501, 20 org, 07-20 Sen-Sab, buffer 30, **hanya role GA Manager / GA Supervisor**) · **Kenari discussion room** (R-303, 4 org, 24 jam, **check-in 30 menit**) · **Ruang rapat Bandung** (BDG-R1, cabang Bandung) · Flamboyan meeting room (R-304, **diarsipkan**, renovasi).
- **Toyota Innova Zenix** (B 1893 KJP, **perlu persetujuan**) dan **Toyota Avanza Veloz** (B 2745 SKT). Dokumen: Innova asuransi habis ±22 hari lagi ("soon"), Avanza servis 60.000 km terlambat 6 hari ("expired"). SIM: Agus habis 24 hari lagi, Fajar sudah kedaluwarsa (muncul di pemilih pengemudi).
- Parkir tamu P-01, Parkir EV B1-12; Proyektor Epson (EQ-PRJ-01), **Kamera Canon EOS 90D (EQ-CAM-01, perlu persetujuan)**.

## Flows ready to demo

| Flow | Persona | Mulai dari | Hasil |
|---|---|---|---|
| Pesan ruang besar H+1 sore | rizky/anita/fajar/lina | `/bookings` → hari H+1 → Garuda: kosong setelah 10:45 (pagi hanya Weekly Operations Review 09:00-10:30) | booking confirmed; notifikasi pengingat 30 menit sebelumnya |
| Bentrok jadwal | siapa saja | Garuda H+1 09:30-10:30 | pesan "Garuda meeting room is already taken from ... Choose another time" |
| Rapat berulang | dimas (pemilik), siapa saja | "Weekly Operations Review" (Garuda, mingguan x8, mulai H+1); "Daily stand-up Tim Operations" (Merak, harian kerja x15, rizky); "Rapat koordinasi tim GA" (Merak, tiap Rabu, putri); "Sinkronisasi konten mingguan Marketing" (Kenari, tiap Selasa, lina); "Rapat manajemen GA dan Operations" (Cenderawasih, tiap Senin, hendra) | Batalkan **"hanya ini"** atau **"ini dan seterusnya"**. Sudah ada contoh jadi: satu kemunculan Marketing dibatalkan, "Pelatihan onboarding karyawan baru" (anita) dipotong setelah sesi 2 (muncul di filter "dibatalkan") |
| Persetujuan booking kendaraan/alat | putri atau hendra | `/bookings` → antrean persetujuan: **Innova H+2 07:30 (anita, perlu pengemudi)**, **Innova H+4 (fajar)**, **Kamera H+3 (lina)** | Approve/Reject (reject wajib alasan). Peminta dapat notifikasi "approved / not approved". Pada Innova anita: tetapkan pengemudi (mis. Agus, SIM sah) di drawer perjalanan; Fajar ditolak sebagai pengemudi karena SIM kedaluwarsa |
| Check-in ruang | lina, anita | Kenari: **"Diskusi cepat konten media sosial"** (mulai ±25 menit dari seed, lina) dan **"Coaching 1-on-1 dengan staf baru"** (±60 menit, anita). Tombol Check in muncul 15 menit sebelum mulai sampai 30 menit sesudahnya | checked in; kalau dibiarkan, dilepas otomatis |
| Ruang dilepas (no-show) | fajar/putri | Kenari hari ini: **"Diskusi cepat rekonsiliasi dengan auditor"** (fajar) status released; notifikasi "Kenari discussion room was released" sudah ada | tampil di daftar dibatalkan/dilepas, KPI no-show 7 hari |
| Serah terima mobil | anita (pemesan) atau putri | Avanza **"Antar dokumen ke kantor pajak dan bank"** (mulai ±15 menit dari seed) → drawer perjalanan → Pick up, isi odometer (terakhir 62.405 km) | trip: picked up, odometer out tersimpan |
| Pengembalian mobil | rizky (pemesan) atau putri | Innova **"Kunjungan ke klien PT Bintang Logistik, Cikarang"** sedang jalan (odometer keluar sudah dicatat, terakhir 19.175 km) → Return: odometer masuk, BBM/tol/parkir, checklist (5 butir; "no_new_damage" gagal wajib catatan kerusakan) | trip returned, booking diperpendek ke jam kembali, mobil bebas lagi |
| Tamu didaftarkan karyawan, di-check-in, lalu check-out | rizky/dimas/anita, lalu sinta | Menunggu: **Bambang Hermawan (PT Bintang Logistik, host rizky, ±30 menit lagi)**, **Ir. Andreas Wijaya (host dimas, jendela sudah dibuka)**, **Dewi Anggraeni (host anita)**, **Yoga Prasetya (host lina)**. Sinta: `/reception` → Check in (no. badge, 4 digit ID, centang acknowledgement) | host dapat notifikasi "has arrived"; lalu Check out dari baris tamu yang sama atau dari yang sudah di lokasi: Yohanes Pratama (fajar), Linda Susanti (dimas), Agus Salim (rizky) |
| Walk-in | sinta | Sudah ada contoh: Hendrik Gunawan (CV Cipta Kreasi Mandiri, host lina) didaftarkan sinta dan sudah masuk. Untuk demo baru: tombol daftar tamu di `/reception` | |
| Tamu tidak datang / diblokir | sinta, hendra | No-show: Maria Ulfa (anita). Terblokir: **Heru Santoso, CV Mitra Sejahtera Abadi** (host rizky, ditandai "On the block list"). Check-in ditolak dengan alasan block list; hendra sudah dapat notifikasi "A blocked visitor was registered". Block list: hendra di `/setup/visitors` | |
| Paket tiba, lalu diambil | sinta, rizky | Rizky punya 2 paket menunggu (PT Kurnia Printindo kartu nama; Tokopedia ATK). Sinta: `/mail` → Collect (nama pengambil) | status collected; hari ini "collected" naik |
| Surat keluar ditandai terkirim | sinta | 4 surat keluar menunggu (mis. rizky: kontrak ke PT Bintang Logistik, tanpa kurir/waybill; anita: offering letter, kurir JNE tanpa waybill) → Send, isi kurir + waybill | pengirim dapat "Your mail ... has left with JNE" |
| Catat surat/paket baru | sinta | tombol log di `/mail` (kantor: Reception) | penerima dapat notifikasi |

## Notifikasi yang ada (belum dibaca)

- rizky: paket di resepsionis x2, tamu tiba (Slamet, Agus Salim), booking Innova disetujui. anita: paket/surat, pengingat Avanza, tamu tiba, kamera disetujui. fajar: paket x3, "Kenari was released", tamu tiba. lina: paket, pengingat Kenari, tamu tiba, Innova "not approved".
- putri/hendra: banyak "needs your approval" (Innova, kamera), "Innova needs a driver", hendra juga "blocked visitor was registered". sinta: tidak ada (receptionist memang tidak menerima notifikasi dari aksi ini).
- Worker harian akan menambah pengingat "mail still waiting" (item >2 hari) dan "dokumen kendaraan kedaluwarsa/segera habis" ke hendra dalam satu jam pertama.

## Not demonstrable / bugs / caveats

- **Pemilih role di `/setup/resources`** kosong untuk hendra (GA Manager tidak punya `roles.read`, `/api/roles` 403): UI menampilkan petunjuk "Restricting by role needs permission to see roles". Pembatasan Cenderawasih dibuat lewat API dengan id role dari admin. Pemegang `bookings.manage` (putri, hendra, admin) selalu boleh memesan ruang terbatas (aturan app), jadi yang diblok hanya karyawan biasa. Karyawan memintanya lewat GA (booking atas nama: putri memesankan untuk dimas dan sebagainya).
- **Karyawan tidak bisa mencatat surat keluar sendiri** (`POST /api/mail` butuh `mail.manage`): surat keluar dicatat sinta atas nama karyawan (muncul di `/mail` rizky/anita sebagai milik mereka).
- Walk-in tidak punya penanda khusus di UI/API (hanya terdaftar oleh sinta dengan host pilihannya).
- Jendela live (check-in Kenari, tamu "akan datang", Innova sedang jalan, Avanza siap diambil) basi sesudah ±1-2 jam; BookingWorker (tiap menit) otomatis melepas booking Kenari yang tidak check-in dan menolak booking pending yang sudah lewat waktu mulai. Seed kembali sebelum demo.
- Bila seed dijalankan di luar jam buka (mis. 20:00 atau akhir pekan), item live untuk Garuda/Merak yang butuh jam buka dilewati (ada log "skipped"); Kenari dan mobil 24 jam tetap ada. Tamu/mail tidak terpengaruh.
- Menjalankan ulang: modul langsung keluar ("already seeded") bila kode R-301 sudah ada; tidak ada pemulihan parsial bila run pertama terhenti di tengah (reset DB).
- Jumlah resource 12 (di atas 6-10 di brief) karena daftar permintaan sudah 11 aktif + 1 arsip.
- **Audit log**: aksi history (booking, trip, tamu, surat) tercatat di audit dengan waktu seed, bukan tanggal backdate; metadata `starts` booking history bisa berisi tanggal +98 hari (karena dipesan jauh ke depan lalu digeser). Modul `history` bisa menggeser audit bila perlu.
- Notifikasi akibat aksi history dihapus (tanggal di teksnya sudah tidak cocok); hanya notifikasi item live yang tersisa.
- Kolom "Booking saya" memuat booking karyawan hari ini; status "released" tidak ikut daftar default.

## Backdating done (SQL pada baris buatan modul ini)

- **Booking sejarah (±335, 28 hari lalu + sebagian hari ini)**: dibuat via API pada +98 hari (hari-minggu sama), lalu `bookings.starts_at/ends_at/blocked_until/created_at/updated_at` digeser ke tanggal sebenarnya; yang dibatalkan: `cancelled_at`; yang disetujui/ditolak: `decided_at`; Kenari: `checked_in_at` (hadir) atau `status='Released'` (no-show, sekitar 12%).
- **Perjalanan mobil (16)**: booking dibuat "sekarang", pick-up dan return lewat API, lalu waktu booking dan `vehicle_trips.picked_up_at/returned_at/created_at` digeser 1 s.d. 20 hari kerja ke belakang. Dua permintaan Innova yang ditolak digeser ke 4 dan 10 hari kerja lalu.
- **Tamu**: 32 kunjungan sejarah (`expected_from/until`, `checked_in_at`, `acknowledged_at`, `checked_out_at`, `created_at`, `updated_at`; no-show: `status='NoShow'`) + 3 tambahan untuk rizky/dimas; hari ini: jam check-in (-75 s.d. -12 menit), tamu sudah pulang, satu no-show, semua relatif terhadap saat seed.
- **Mail**: 15 diambil + 11 terkirim (`logged_at/done_at/updated_at` sampai 26 hari lalu), 8 yang menunggu (umur 0 s.d. 5 hari), 1 void, 2 surat keluar tertunda dibuat "kemarin".
- **Notifikasi**: yang dihasilkan fase sejarah (booking/vehicle/visitor/mail) dihapus.
