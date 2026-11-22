# 69 — Panduan Demo Annaba Helpdesk

Untuk presenter. Berisi cara menyalakan lingkungan demo berisi data, daftar akun, urutan presentasi yang disarankan, dan catatan yang perlu diketahui sebelum naik panggung. Detail per modul (semua layar, semua alur, angka persis, batasan) ada di `scripts/demo/notes/` (`spaces.md`, `assets.md`, `stock.md`, `servicedesk.md`, `admin.md`).

## 1. Menyalakan demo

Perlu Docker Desktop yang sedang berjalan, .NET 10 SDK, dan Node. Semuanya lokal; database demo terpisah (`annaba_demo`), database dev dan production tidak tersentuh.

```bash
node scripts/demo-up.mjs --reset
```

Perintah ini membuat database kosong, menjalankan API (port 5080), mengisi semua data demo (sekitar 8 menit), membangun dan menjalankan web (port 3000, build produksi supaya halaman cepat). Buka `http://localhost:3000`. Semua akun memakai kata sandi `Passw0rd!demo`. Bahasa tampilan Indonesia.

| Perintah | Fungsinya |
| --- | --- |
| `node scripts/demo-up.mjs --reset` | Database baru + seluruh data demo + API + web |
| `node scripts/demo-up.mjs` | Menyalakan lagi API dan web di data yang sudah ada |
| `node scripts/demo-up.mjs --reset --dev-web` | Seperti `--reset` tetapi web mode dev (tanpa build, halaman pertama lebih lambat) |
| `node scripts/demo-up.mjs --stop` | Mematikan API dan web |
| `node scripts/demo-verify.mjs` | Membuka ±98 layar sebagai persona yang tepat, melaporkan yang kosong/error, dan menyimpan tangkapan layar di `scripts/demo/out/screens` |

**Penting: jalankan `--reset` pada hari (pagi) demo.** Semua tanggal relatif terhadap hari seed, dan beberapa keadaan sengaja "hidup":

- Tamu yang "akan datang", ruang yang bisa di-check-in, dan mobil yang sedang jalan dihitung dari jam seed. Setelah 1–2 jam jendelanya lewat.
- Empat tiket "berisiko SLA" akan menjadi "terlambat" dalam beberapa jam.
- Tiket "selesai, menunggu konfirmasi pemohon" ditutup otomatis 3 hari setelah selesai; demo sebaiknya dalam ±40 jam setelah seed.
- Pengingat kontrak vendor dan garansi dikirim worker latar belakang sekitar 2–3 menit setelah API menyala. Bila API baru dinyalakan ulang, tunggu beberapa menit sebelum menunjukkan notifikasinya.

Seed hanya berjalan di Development lewat `Api:SeedDevUsers` dan `Api:SeedDemoUsers`; Production menolak kedua flag itu.

## 2. Akun

| Peran | Akun (`@annabadigital.io`) | Dipakai untuk |
| --- | --- | --- |
| Karyawan | rizky.pratama, anita.rahmawati, fajar.ramadhan, lina.marlina | Membuat permintaan, memesan ruang/mobil, mendaftarkan tamu, minta perlengkapan, menilai |
| Atasan | dimas.prakoso (Head of Operations, atasan keempat karyawan di atas) | Menyetujui permintaan dan permintaan perlengkapan |
| GA Agent | agus.setiawan (AC & listrik), bayu.nugroho (umum & kebersihan) | Mengerjakan tiket dan perintah kerja; mode lapangan |
| GA Supervisor | putri.anggraini | Antrean tiket, menugaskan, serah perlengkapan, persetujuan booking |
| GA Manager | hendra.kusuma | Pengaturan, aset, vendor, inventaris, laporan, anggaran |
| Resepsionis | sinta.maharani | Check-in tamu, ruang surat |
| Vendor | joko.susilo@sejukabadi.co.id (PT Sejuk Abadi) | Portal vendor: hanya perintah kerja perusahaannya |
| Administrator | sarah.wijaya (super admin), daniel.tan (user admin) | Pengguna, peran, direktori, integrasi, tenant |
| Auditor | maya.putri | Log audit, peristiwa keamanan |

Tips: buka tiap persona di jendela peramban terpisah (profil/incognito berbeda) supaya bisa berpindah peran tanpa keluar-masuk.

## 3. Urutan presentasi yang disarankan (±40 menit)

1. **Karyawan meminta bantuan (rizky), 5 menit.** Beranda → Permintaan baru → "Air conditioning", isi formulir, kirim. Tunjukkan Permintaan saya (status beragam), Basis pengetahuan, dan satu tiket "selesai, menunggu konfirmasi" (**"Lampu ruang Garuda berkedip"**): Konfirmasi sudah beres + beri bintang.
2. **Persetujuan atasan (dimas), 3 menit.** Persetujuan: 5 menunggu (pool car, set-up acara). Setujui satu, tolak satu dengan alasan. Tunjukkan delegasi "Tidak di kantor". Di Perlengkapan: 4 permintaan menunggu persetujuan Dimas.
3. **Service desk (putri lalu agus), 8 menit.** Antrean Tiket: tile angka, tab cepat (Belum ditugaskan, Berisiko, Terlambat…), tampilan tersimpan. Tugaskan satu tiket, lalu massal. Buka **"Suhu server room 31°C, AC presisi alarm"**: aktivitas, catatan internal, perintah kerja vendor, jejak eskalasi SLA. Agus: Mulai → komentar → Tunda (alasan) → Lanjutkan → Selesaikan. Perintah kerja: checklist wajib, foto sebelum/sesudah, waktu, biaya, suku cadang dari stok.
4. **Vendor dan mode lapangan (joko, agus), 4 menit.** Joko hanya melihat perintah kerja PT Sejuk Abadi; kerjakan **"Cek tekanan dan isi freon AC Garuda"** sampai selesai (tiket ikut selesai otomatis). Agus: Mode lapangan → simpan ke perangkat → matikan jaringan → kerjakan → nyalakan → sinkron.
5. **Ruang, mobil, tamu, surat (rizky, putri, sinta), 7 menit.** Pemesanan: pesan Garuda besok sore, coba jam 09:30 untuk melihat pesan bentrok, rapat berulang. Putri menyetujui booking Innova. Serah terima dan pengembalian mobil (odometer, BBM). Rizky mendaftarkan tamu; Sinta di Resepsionis: check-in (kartu tamu, 4 digit ID, aturan gedung), tamu langsung, tamu terblokir, check-out. Ruang surat: paket menunggu → Sudah diambil; surat keluar → Terkirim (kurir + resi).
6. **Aset, perawatan, vendor, stok (hendra, putri), 7 menit.** Aset: tile, QR `/a/AC-GRD-01` lapor kerusakan, serahkan/pindahkan aset, impor CSV (`scripts/demo/samples/assets-import.csv` lalu `assets-import-corrected.csv`). Perawatan: rencana dan Kepatuhan. Vendor: kontrak hampir habis, vendor diblokir, kinerja. Inventaris: Perlu dipesan ulang, terima kiriman, stock opname yang terbuka di Ground Floor. Perlengkapan: Putri menyerahkan permintaan penuh dan sebagian (back-order).
7. **Laporan dan pengaturan (hendra), 4 menit.** Operasional, SLA, Kepuasan, Biaya (per pusat biaya, vendor, aset), Anggaran (satu hampir habis, satu terlampaui), Utilisasi, Ekspor laporan (Kirim sekarang). Pengaturan Helpdesk: Katalog, SLA, Aturan Penugasan, Kebijakan Persetujuan.
8. **Orang & akses (sarah, daniel, maya), 4 menit.** Undang pengguna, terima undangan langsung (tautan "LIVE INVITATION" dicetak di akhir bagian `[admin]` log seed), tetapkan peran, permintaan akses (rizky → admin), tinjauan akses, log audit, peristiwa keamanan, integrasi (API key, webhook), direktori (sinkron HR), kanal notifikasi.

## 4. Peta alur: mulai dari mana

Setiap alur punya item di keadaan awal. Rinciannya (nama item persis dan hasil yang diharapkan) ada di catatan modul; ringkasannya:

| Alur | Akun | Mulai dari |
| --- | --- | --- |
| Permintaan → persetujuan → dikerjakan | rizky → dimas → putri/agus | Buat "Pool car booking", atau pakai **"Mobil operasional ke pabrik klien di Cikarang, Kamis"** (menunggu Dimas) |
| Persetujuan dua langkah | dimas → hendra | **"Set-up gathering akhir tahun HR…"** (lihat catatan delegasi di §5) |
| Menugaskan tiket / massal | putri | Antrean Tiket → tab **Belum ditugaskan** (14) |
| Mengerjakan, tunda, selesai | agus | Tab "Ditugaskan ke saya"; **"AC area Operasional berisik…"** |
| Konfirmasi dan nilai / buka kembali | rizky / anita | **"Lampu ruang Garuda berkedip"** / **"Lampu ruang HR berkedip-kedip"** |
| Perintah kerja wajib menahan tiket | agus | **"Ganti baterai UPS dan uji beban server room"** (foto sesudah wajib) |
| Portal vendor | joko | **"Cek tekanan dan isi freon AC Garuda"** |
| Mode lapangan | agus | Mode lapangan: 7 perintah kerja baru |
| Booking butuh persetujuan | putri/hendra | Pemesanan → Innova besok+2 (Anita, perlu sopir), Innova (Fajar), Kamera (Lina) |
| Check-in ruang | lina, anita | Kenari: "Diskusi cepat konten media sosial" (±25 menit dari seed) |
| Mobil: ambil / kembalikan | anita / rizky | Avanza "Antar dokumen ke kantor pajak dan bank"; Innova "Kunjungan ke klien PT Bintang Logistik" (sedang jalan) |
| Tamu check-in / check-out | sinta | Resepsionis → Bambang Hermawan, Ir. Andreas Wijaya, Dewi Anggraeni, Yoga Prasetya |
| Tamu terblokir | sinta, hendra | Heru Santoso, CV Mitra Sejahtera Abadi |
| Paket diambil / surat dikirim | sinta | Ruang surat: 8 paket menunggu, 4 surat keluar menunggu |
| Lapor kerusakan lewat QR | rizky | `/a/AC-GRD-01` |
| Serahkan / pindah / pensiunkan aset | hendra | LTP-006, FRN-005, FRN-003 |
| Impor aset | hendra | Impor → `assets-import.csv` (1 baris salah) lalu `assets-import-corrected.csv` |
| Permintaan perlengkapan → setuju → serah | rizky/lina → dimas → putri | Perlengkapan; 4 permintaan menunggu Dimas; 3 siap serah |
| Back-order | hendra lalu putri | Terima Toner HP 05A di Gudang Pusat, lalu serahkan sisa permintaan Fajar |
| Stock opname | putri | Stock opname Ground Floor yang terbuka |
| Undangan, peran, akses, tinjauan | daniel, sarah | Lihat `scripts/demo/notes/admin.md` (15 alur, termasuk CSV siap tempel) |

## 5. Hal yang perlu diketahui sebelum demo

**Izin menentukan akun yang dipakai** (ini perilaku aplikasi, bukan kekurangan data):
- Hanya Hendra dan Sarah yang dapat menyerahkan, memindahkan, atau mengimpor aset, mengelola vendor, serta menerima/mengeluarkan/memindahkan stok. Putri dapat menyesuaikan stok, stock opname, dan menyerahkan permintaan perlengkapan.
- Karyawan tidak mencatat surat keluar sendiri; Sinta mencatat atas nama mereka.
- Daniel hanya bisa menyetujui permintaan akses Anita (HR Onboarding); yang lain butuh Sarah. Aksi kritis (peran, API key, tenant, sinkronisasi) meminta kata sandi lagi: isi `Passw0rd!demo`.
- `/admin/security-policy` hanya berisi data bila Sarah masuk mode dukungan ke "PT Annaba Logistik Nusantara" (Administrasi → Tenant).
- Hendra tidak bisa membuka daftar peran, sehingga pemilih peran di Sumber daya yang bisa dipesan kosong baginya (pembatasan peran Cenderawasih sudah diatur lewat API).

**Delegasi Hendra → Putri aktif 5 hari.** Selama aktif, langkah persetujuan kedua yang jatuh ke Hendra untuk tiket yang bukan diajukan Putri dialihkan ke Putri. Untuk memperagakan Hendra sebagai pemberi persetujuan kedua, akhiri delegasi dulu (Persetujuan → Akhiri), atau pakai tiket yang diajukan Putri sendiri.

**Yang tidak bisa dipertunjukkan secara nyata di lingkungan lokal:**
- E-mail tidak keluar (pengirim konsol); lihat Administrasi → Pengiriman Email untuk isi e-mail yang akan terkirim.
- Kanal WhatsApp/Slack/Teams tidak mengirim (butuh layanan eksternal); semua pengiriman kanal tercatat gagal lalu jatuh ke e-mail. Teams dibiarkan nonaktif.
- Tiga webhook dibiarkan nonaktif; untuk memperlihatkan pengiriman, jalankan penerima lokal satu baris dari `scripts/demo/notes/admin.md` (alur 8) lalu nyalakan webhook.
- Tidak ada status aset "hilang": aset hilang dicatat sebagai pensiun dengan alasan.

**Catatan data:**
- Log audit dan peristiwa e-mail masuk bertanggal hari seed untuk aksi baru; riwayat 85 hari ke belakang dibuat dengan mematikan sementara trigger pelindung tabel `audit_events` di database demo (set `DEMO_NO_BACKDATE=1` bila tidak mau).
- Tiket perawatan terjadwal memakai nomor tiket pertama (GA-2026-000001 dst.), tiket permintaan bernomor lebih tinggi.
- Aset AC-SRV-01 dan GEN-001 tampil sebagai "sering bermasalah" karena tiket perawatan terjadwal ikut dihitung.
- Stock opname mengambil angka "seharusnya" saat sebuah baris dihitung, jadi pergerakan stok lain di tempat yang sama selama stock opname terbuka tidak lagi menggandakan koreksi (diperbaiki 22 November 2026).

## 6. Isi data (ringkas)

| Area | Isi |
| --- | --- |
| Organisasi | 15 lokasi (Head Office Jakarta: Tower A, lantai, ruang; Gudang Pusat; Branch Bandung), 4 tim, 3 kalender kerja, 7 kebijakan SLA, 5 aturan penugasan, 3 kebijakan persetujuan |
| Tiket | 100 tiket selama 10 minggu (+ 47 dari perawatan) di semua status; 44 penilaian; 31 + 47 perintah kerja; 20 artikel basis pengetahuan |
| Ruang & tamu | 12 sumber daya, ±460 pemesanan, 16 perjalanan mobil (1.217 km), 13 tidak hadir; 35 kunjungan tamu; 8 paket menunggu, 15 diambil, 11 terkirim |
| Aset | 8 jenis, 54 aset, 10 vendor dengan kontrak dan dokumen, 10 daftar periksa, 14 rencana perawatan (kepatuhan 76%) |
| Stok & biaya | 47 barang, 463 pergerakan, 26 permintaan perlengkapan, 3 stock opname, 6 pusat biaya, 10 anggaran |
| Pengguna & akses | 14 akun inti + 16 rekan fiktif (aktif, tertunda, ditangguhkan, nonaktif, terkunci), 3 peran khusus, permintaan akses dan tinjauan, 47 peristiwa keamanan, 2 tenant tambahan |

## 7. Cara kerja seed (untuk pengembang)

- `scripts/demo-seed.mjs` menjalankan modul di `scripts/demo/` berurutan: `01-setup`, `02-spaces`, `03-assets`, `04-stock`, `05-servicedesk`, `06-admin`, `07-history`. Semua data dibuat lewat API sebagai tokoh demo; SQL hanya dipakai menggeser tanggal ke belakang agar laporan dan tren punya riwayat.
- Modul idempoten: dijalankan dua kali tidak menggandakan data. `--only <modul>` menjalankan sebagian (setup selalu ikut).
- `scripts/demo/stack.mjs <nama> <port>` menyalakan satu API dengan database sendiri (dipakai saat mengembangkan modul secara paralel). `scripts/demo/lib.mjs` adalah pustaka bersama; `scripts/demo/BRIEF.md` aturan menulis modul.
- Akun peran ditanam oleh `api/src/AnnabaDigital.Api/DemoPersonas.cs` (hanya Development).
- Skrip lama `scripts/demo-data.mjs` (data ringan untuk tangkapan layar manual) tidak diubah.

## 8. Demo di deployment (Coolify), bukan di laptop

Dipakai untuk menaruh data demo di helpdesk yang sudah berjalan (di-uji lebih dulu pada API mode Production di lokal). Seed **menambah** data; tidak menimpa apa pun, dan akun yang sudah ada tidak disentuh.

1. Penjalan seed adalah layanan sementara `demo-seed` (Node + klien `psql` + skrip seed, tanpa port, hanya di jaringan compose; `deploy/Dockerfile.demo-seed`). Layanan ini sudah **dihapus** dari `deploy/docker-compose.coolify.yml` setelah data dimuat karena ia memegang kata sandi database di environment-nya. Untuk menjalankan seed lagi, tambahkan kembali blok `demo-seed` dari commit `eb75bbb` (`git show eb75bbb:deploy/docker-compose.coolify.yml`), deploy, jalankan perintah di bawah, lalu hapus lagi.
2. Karena Production menolak akun dev, akun peran dibuat lewat undangan (`scripts/demo/bootstrap-cast.mjs`): token di-hash lewat SQL, diterima lewat API, sehingga kata sandi di-hash dengan pepper deployment itu. Kata sandi demo dipilih saat menjalankan (`DEMO_PASSWORD`), bukan `Passw0rd!demo`.
3. Di terminal container `demo-seed` (Coolify → Terminal):

```bash
DEMO_PASSWORD='<kata sandi demo>' DEMO_NO_LOCAL_RECEIVER=1 DEMO_KEEP_SETTINGS=1 DEMO_KEEP_AUDIT=1 \
  nohup node scripts/demo-seed.mjs --yes --bootstrap --mute-mail > /tmp/seed.log 2>&1 &
tail -f /tmp/seed.log
```

Saklar yang dipakai di deployment sungguhan:

| Saklar | Fungsinya |
| --- | --- |
| `--bootstrap` | Membuat 14 akun peran lewat undangan |
| `--mute-mail` | Memasang trigger sementara di `outbox_emails` yang menandai e-mail seed sebagai sudah ditangani, supaya tidak ada e-mail ke alamat fiktif lewat SMTP asli; trigger dilepas di akhir |
| `DEMO_NO_LOCAL_RECEIVER=1` | Webhook dibuat nonaktif ke alamat https (production menolak http dan 127.0.0.1) |
| `DEMO_KEEP_SETTINGS=1` | Tidak mengubah alamat pengirim, redaksi e-mail, dan kebijakan retensi organisasi |
| `DEMO_KEEP_AUDIT=1` | Tidak mematikan trigger pelindung tabel audit; log audit bertanggal hari seed |

Seed berjalan sekitar 4 menit. Untuk mengembalikan keadaan sebelum seed, pulihkan cadangan `pg_dump` terakhir sebelum seed dari volume `annaba-digital-backups` (layanan `backup` mengambilnya otomatis setiap kali stack dinyalakan).
