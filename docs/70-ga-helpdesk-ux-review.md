# 70 — Annaba Helpdesk: tinjauan UI/UX menyeluruh dan hasil uji e2e

Tanggal: 22 November 2026. Tinjauan ini menjawab tiga pertanyaan: apakah aplikasinya membantu, apakah prosesnya mudah dimengerti, dan apa yang perlu dibenahi lebih dulu.

## Cara meninjau

- **Data:** helpdesk lokal (API Development, `next start`) berisi data demo lengkap dari `node scripts/demo-seed.mjs`. Isinya 100 tiket, 30 orang, aset, booking, tamu, stok, dan riwayat 85 hari.
- **Persona:** alur dijalankan langsung sebagai Rizky (karyawan), Dimas (atasan), Agus (teknisi), Putri (GA Supervisor), Hendra (GA Manager), dan Sinta (resepsionis).
- **Layar:** desktop 1440×900, serta ponsel 375×812 untuk peran yang bekerja dari ponsel (karyawan, atasan, teknisi).
- **Uji otomatis:** seluruh suite Playwright, termasuk pemeriksaan aksesibilitas axe di tema terang dan gelap.

## Kesimpulan

Aplikasinya **membantu, dan alur utamanya mudah diikuti**. Setiap peran langsung melihat pekerjaannya di Beranda. Tombol utama di setiap halaman jelas, status selalu ditulis dengan kata, bukan warna saja, dan semua alur dari ujung ke ujung lulus uji otomatis.

Masalah terbesar bukan pada alurnya, tapi pada **tiga hal yang membuat pengguna Indonesia ragu**:

1. Bahasanya campur. Tampilan berbahasa Indonesia, tapi nama layanan, pertanyaan formulir, dan catatan aktivitas sistem masih berbahasa Inggris.
2. Di ponsel, isi utama sering terdorong ke bawah oleh kartu angka, dan formulir permintaan baru melebar keluar layar.
3. Ada beberapa pertanyaan ganda dan informasi yang kurang di titik keputusan, misalnya lokasi ditanya dua kali dan atasan menyetujui tanpa melihat siapa yang meminta.

| Persona | Mudah dipahami? | Catatan utama |
| --- | --- | --- |
| Karyawan | Ya | Beranda sangat jelas. Di ponsel, formulir melebar keluar layar dan lokasi ditanya dua kali. |
| Atasan | Cukup | Kotak masuk persetujuan rapi, tapi tidak menampilkan peminta, biaya, atau rincian. |
| Teknisi | Cukup | Checklist, waktu, dan foto lengkap. Daftar di Mode lapangan kurang informasi, dan detailnya muncul jauh di bawah. |
| GA Supervisor | Ya | Tab cepat dan tugaskan massal bagus. Tabel baru terlihat setelah scroll. |
| GA Manager | Ya | Dasbor dan laporan lengkap. Ada beberapa istilah teknis. |
| Resepsionis | Ya | Alur tamu dan paket sederhana. |

## Hasil uji e2e

Seluruh suite Playwright dijalankan pada build produksi (`next build` lalu `next start`) dengan database e2e yang dibuat baru:

| Hasil | Jumlah |
| --- | --- |
| Lulus | **236** |
| Gagal | 0 |
| Flaky | 0 |
| Dilewati | 0 |
| Durasi | 15,1 menit, 1 worker |

Yang dicakup: 40 berkas spec, yaitu masuk dan MFA, RBAC, tiket, SLA, persetujuan dan delegasi, perintah kerja, mode lapangan, portal vendor, aset dan scan QR, perawatan, booking, tamu dan surat, inventaris dan perlengkapan, biaya, laporan dan ekspor, basis pengetahuan, e-mail ke tiket, integrasi, direktori, kanal notifikasi, retensi, hardening, navigasi, serta pemeriksaan aksesibilitas axe di setiap halaman dalam tema terang dan gelap.

Catatan cara menjalankan di mesin ini: `e2e/scripts/start-api.sh` terkunci ke container `annaba-digital-dev-db` di port 5432, padahal keduanya dipakai proyek lain di laptop ini. Uji dijalankan dengan config sementara yang mengarah ke container `annaba-helpdesk-dev-db` di port 5433, tanpa mengubah file repo. Sebaiknya nama container dan port dibuat bisa diatur lewat variabel lingkungan, misalnya `E2E_DB_CONTAINER` dan `E2E_DB_PORT`.

Uji otomatis memastikan fungsinya benar, tapi tidak menilai apakah tampilannya nyaman. Temuan di bawah berasal dari pengecekan langsung.

## Yang sudah baik

- **Beranda menyesuaikan peran.** Karyawan melihat kotak "Apa yang Anda butuhkan?", layanan populer, dan permintaannya. Tim GA melihat tiket terbuka, berisiko, dan terlambat. Atasan melihat angka persetujuan di menu.
- **Satu tombol utama per halaman** (Permintaan baru, Pemesanan baru, Minta perlengkapan), selalu di kanan atas.
- **Saran artikel sebelum tiket dibuat.** Formulir permintaan menampilkan artikel basis pengetahuan yang cocok. Ini mengurangi tiket yang sebenarnya bisa diselesaikan sendiri.
- **Status selalu berupa teks berwarna** (Ditugaskan, Sedang dikerjakan, Terlampaui), tidak bergantung pada warna saja. Kontras lolos axe di kedua tema.
- **Alur kerja teknisi lengkap:** checklist wajib, catat waktu, foto sesudah, suku cadang dari stok, dan mode offline.
- **Detail tiket memperlihatkan sebab dan akibat:** siapa menugaskan, aturan mana yang dipakai, kapan SLA terlampaui, dan catatan internal yang ditandai "Khusus staf".
- **Tombol Bantuan di setiap halaman** dan manual berbahasa Indonesia.
- **Menu disaring sesuai izin.** Karyawan hanya melihat 9 menu dan tidak bingung oleh pengaturan.

## Temuan dan perbaikan yang disarankan

Prioritas: **P1** = mengganggu pemakaian sehari-hari atau kesan pertama klien, perbaiki sebelum demo ke klien. **P2** = membuat pekerjaan lebih lambat. **P3** = pemolesan.

### P1

**1. Bahasa campur Indonesia–Inggris.**
- Di mana:
  - Katalog layanan bawaan ("Air conditioning", "Cooling that is too weak…").
  - Pertanyaan formulir ("Where is the problem?", "Tell us more", "Is it stopping people from working?").
  - Nama kebijakan SLA ("Server room: any request").
  - Catatan aktivitas sistem di detail tiket ("Filed on behalf of…", "Assigned to … by rule …", "Priority changed from medium to urgent", "Attached …").
  - Chip organisasi "Default organisation".
  - Label peran di bawah nama ("user", "ga supervisor", "super admin", ditulis seperti kunci teknis).
- Akibat: karyawan Indonesia membaca dua bahasa dalam satu layar, dan aplikasi terasa belum selesai saat didemokan.
- Perbaikan:
  - Pasang katalog awal berbahasa Indonesia untuk organisasi yang berbahasa Indonesia, atau izinkan nama dan pertanyaan layanan diisi dalam dua bahasa.
  - Tulis catatan aktivitas sistem sebagai kode dan parameter, lalu terjemahkan saat ditampilkan (seperti teks antarmuka lain di `src/i18n/id.ts`).
  - Tampilkan nama peran yang ramah ("Karyawan", "GA Supervisor").
  - Beri nama organisasi bawaan yang bisa diterjemahkan.

**2. Formulir Permintaan baru melebar keluar layar di ponsel.**
- Bukti: di layar 375 px, lebar halaman menjadi 520 px. Kolom Subjek dan lokasi terpotong di kanan, sehingga halaman harus digeser ke samping.
- Penyebab: tombol saran artikel di `src/components/kb/kb-bits.tsx:66` memakai kelas `.btn`, yang punya `white-space:nowrap` (`src/app/globals.css:34`). Judul artikel yang panjang tidak bisa turun baris.
- Perbaikan: beri tombol itu kelas yang mengizinkan teks turun baris (`white-space:normal; height:auto; text-align:left; max-width:100%`), atau tampilkan saran sebagai tautan biasa.
- Dampak: ini halaman yang paling sering dipakai karyawan dari ponsel.

**3. Lokasi dan rincian ditanya dua kali di formulir yang sama.**
- Bukti: formulir AC menanyakan "Di mana lokasinya?" (bagian umum, opsional) dan "Where is the problem? *" (pertanyaan layanan, wajib). Formulir yang sama juga punya "Detail tambahan" dan "Tell us more *".
- Akibat: pengguna bingung mana yang harus diisi, dan data bisa berbeda di dua kolom.
- Perbaikan: sembunyikan kolom umum bila layanan sudah punya pertanyaan lokasi atau rincian sendiri, atau hapus pertanyaan itu dari katalog awal.

**4. Persetujuan tanpa konteks.**
- Bukti: daftar "Menunggu keputusan Anda" hanya berisi nomor, judul, jenis layanan, tanggal, dan tombol Setujui, Tolak, dan Alihkan. Peminta, perkiraan biaya, dan jawaban penting (tanggal, tujuan, jumlah penumpang) tidak terlihat.
- Akibat: atasan harus membuka setiap tiket, atau menyetujui tanpa membaca.
- Perbaikan:
  - Tampilkan peminta, biaya, dan 2–3 jawaban kunci di setiap baris.
  - Di ponsel, ubah tabel menjadi kartu.

### P2

**5. Di ponsel, kartu angka mendorong isi utama ke bawah.**
- Beranda teknisi: 6 kartu KPI bertumpuk (±720 px) sebelum daftar pekerjaan.
- Pemesanan: 6 kartu sebelum kalender. Karyawan juga melihat angka manajerial seperti "Tidak hadir" dan "Pemakaian ruangan".
- Beranda karyawan: 7 layanan populer sebelum "Permintaan terbuka saya".
- Perbaikan:
  - Di ponsel, tampilkan KPI dua kolom ringkas atau baris yang bisa digeser.
  - Sembunyikan KPI manajerial dari karyawan.
  - Di ponsel, tampilkan permintaan terbuka sebelum layanan populer.

**6. Tabel Antrean Tiket baru terlihat setelah scroll.**
- Bukti: di 1440×900, baris pertama mulai sekitar 750 px dari atas, karena berada di bawah 5 kartu KPI, 7 tab, 6 filter, dan baris tampilan tersimpan. Di layar laptop kecil (±800×600), tidak satu baris pun terlihat. Tab dan kartu KPI juga menampilkan angka yang sama (Belum ditugaskan, Berisiko, Terlambat).
- Perbaikan:
  - Gabungkan kartu KPI ke dalam tab, karena tab sudah membawa angka.
  - Pindahkan filter selain Cari ke "Filter lainnya".
  - Letakkan "Tampilan tersimpan" sebagai menu kecil di baris tab.

**7. Mode lapangan kurang informasi untuk teknisi.**
- Bukti:
  - Daftar hanya berisi nomor, judul, dan status, tanpa lokasi, prioritas, atau jatuh tempo, sehingga teknisi sulit menentukan urutan kerja.
  - Teks rata tengah, sehingga sulit dipindai.
  - Mengetuk sebuah item membuka detail di bawah daftar (setelah 12 item) tanpa scroll otomatis, sehingga terlihat seolah tidak terjadi apa-apa.
- Perbaikan:
  - Tampilkan lokasi, prioritas, dan jatuh tempo di setiap baris, rata kiri.
  - Buka detail sebagai layar sendiri, atau gulir otomatis ke detail.
  - Tombol "Hapus data di perangkat ini" juga terpotong 2 px di layar 375 px.

**8. Halaman masuk di ponsel.**
- Tombol **Masuk** ada di 827 px, di bawah tinggi layar 812 px, karena judul besar dan paragraf di atasnya.
- Kotak masuk dan pilihan bahasa meluber 29 px ke kanan, sehingga "Indonesia" terpotong.
- Perbaikan: di ponsel, perkecil atau sembunyikan paragraf hero, dan batasi lebar kartu ke lebar layar.

**9. "Permintaan terbuka saya" menampilkan item berstatus "Selesai".**
- Item yang sudah diselesaikan tim, tapi belum dikonfirmasi peminta, berlabel "Selesai" di daftar yang berjudul "terbuka". Ini membingungkan.
- Perbaikan: beri label "Menunggu konfirmasi Anda" dan tombol **Konfirmasi sudah beres** langsung di Beranda.

### P3

**10. Istilah teknis di layar pengguna bisnis.**
- "waktu jam dinding" di kartu Operasional.
- Teks konfigurasi server di halaman Mulai milik GA Manager ("Storage:ClamAvHost; lihat runbook staging").
- Label filter Pemesanan "Dipegang" dan "Minimal … orang".
- Perbaikan: ganti dengan bahasa sehari-hari, misalnya "termasuk malam dan akhir pekan", "Hubungi administrator untuk mengaktifkan pemindaian virus", "Penanggung jawab", "Kapasitas minimal".

**11. Dua ikon lonceng di header untuk administrator dan auditor.**
- Lonceng kedua berarti peristiwa keamanan (`src/components/app-shell.tsx:232`). Ikonnya hampir sama dengan lonceng notifikasi, dan label aksesibilitasnya belum diterjemahkan ("unacknowledged security events").
- Perbaikan: pakai ikon perisai, beri tooltip, dan terjemahkan labelnya.

**12. Grafik Operasional.**
- Label tanggal memakai format ISO ("10-24", "11-22"), bukan "24 Okt".
- Sumbu Y berskala tidak bulat (0, 6, 13, 19, 25).
- Kurva spline sedikit turun di bawah nol pada hari kosong.
- Perbaikan: format tanggal Indonesia, skala sumbu bulat, dan garis lurus atau monotonic.

### Di luar UX: catatan fungsional

`docs/69-demo-guide.md` §5 mencatat satu kejanggalan aplikasi: stock opname yang ditutup setelah ada pergerakan stok lain di tempat yang sama menggandakan koreksinya. Ini tidak tertangkap uji e2e dan sebaiknya diperbaiki sebelum dipakai untuk inventaris nyata.

## Status perbaikan P1 (22 November 2026)

**1. Bahasa campur: diperbaiki.**
- `td()` (`src/i18n/index.ts`) menerjemahkan teks yang berasal dari data. Cakupannya: nama dan deskripsi kategori dan layanan katalog bawaan, pertanyaan, bantuan, dan pilihan formulir, nama layanan di daftar dan detail tiket, Beranda, persetujuan, laporan SLA, serta nama organisasi bawaan.
- Terjemahannya ada di `src/i18n/id-data.ts`. Teks yang diketik administrator tidak punya entri, jadi tetap tampil apa adanya. Halaman pengaturan katalog sengaja tetap menampilkan nama yang tersimpan.
- `eventText()` (`src/components/tickets/event-text.ts`) menerjemahkan ±30 kalimat aktivitas sistem saat ditampilkan, termasuk data lama, tanpa migrasi. Komentar orang tidak diterjemahkan.
- Label peran di bawah nama memakai nama yang ramah ("Karyawan", "GA Supervisor").
- Pesan validasi dari API kini lewat `td()` di `src/lib/api/client.ts`. Terjemahan baru ditambahkan untuk ±22 pesan yang paling umum. Pesan lain masih berbahasa Inggris sampai entrinya ditambahkan.
- Pencarian katalog dipindah ke klien supaya kata Indonesia ("ruang rapat") cocok dengan nama yang tampil. Layanan diurutkan menurut nama yang tampil.
- Penjaga: `src/__tests__/data-i18n.test.ts` gagal bila ada teks katalog bawaan di `CatalogTemplates.cs` yang belum diterjemahkan.

**2. Formulir melebar di ponsel: diperbaiki.**
- Tombol saran artikel memakai `.btn.wrap`.
- `.stack` dibatasi ke satu kolom selebar wadahnya, karena select lokasi ikut melebar sesuai opsi terpanjang.
- Hasil: lebar halaman 375 px di layar 375 px.

**3. Pertanyaan ganda: diperbaiki.**
- Kolom umum "Di mana lokasinya?" dan "Detail tambahan" disembunyikan bila layanan punya pertanyaan lokasi sendiri atau pertanyaan panjang berkunci `details`.
- Jawabannya tetap dikirim sebagai lokasi dan deskripsi tiket, sehingga aturan penugasan per lokasi dan laporan tetap jalan. Ini sudah dicek: permintaan AC di Garuda ditugaskan oleh aturan "Facilities → Engineering".
- Detail tiket tidak lagi menampilkan rincian yang sama dua kali.
- Subjek bawaan yang terlalu pendek ("AC") menjadi "Permintaan AC".

**4. Persetujuan tanpa konteks: diperbaiki.**
- `GET /api/approvals/mine` kini mengirim deskripsi, lokasi, dan jawaban formulir (tanpa berkas) untuk setiap persetujuan yang menunggu. Pembentuk jawabannya dipindah ke `TicketAnswers.BuildAsync` dan dipakai bersama halaman tiket.
- Kotak masuk menampilkan peminta, lokasi, deskripsi, dan hingga 4 jawaban di setiap baris, juga di ponsel.

## Status perbaikan P2 (22 November 2026)

**5. KPI di ponsel: diperbaiki.**
- Di layar ≤560 px, strip KPI (`KpiGrid`, dipakai di semua modul) menjadi dua kolom ringkas tanpa ikon. Beranda teknisi kini menampilkan "Tiket saya" di layar pertama.
- Pemesanan menyembunyikan "Tidak hadir" dan "Pemakaian ruangan dan kendaraan" bagi yang tidak punya `bookings.read_all`.
- Beranda karyawan menampilkan permintaan terbuka sebelum layanan populer bila hanya ada satu kolom, dan di ponsel cukup 4 layanan populer.

**6. Antrean Tiket: diperbaiki.**
- Kartu KPI dilebur ke tab cepat, yang sudah membawa jumlahnya. Jumlah Belum ditugaskan dan Berisiko berwarna oranye, Terlambat berwarna merah.
- "Tampilan tersimpan" pindah ke baris tab.
- SLA dan dua checkbox (yang sama dengan tab) pindah ke "Filter lainnya". Tampilan tersimpan yang memakai filter itu membuka "Filter lainnya" sendiri.
- Hasil: baris pertama tabel di ±346 px pada 1440×900 (sebelumnya ±750 px), dengan 8 baris terlihat.

**7. Mode lapangan: diperbaiki.**
- `WorkOrderDetail` (dan snapshot offline) kini membawa prioritas dan lokasi tiket.
- Setiap baris menampilkan lokasi, aset, dan jatuh tempo, plus prioritas bila tinggi atau mendesak, dengan teks rata kiri.
- Mengetuk item menggulir ke detailnya.
- Tombol header turun baris di ponsel. Teks checklist yang panjang tetap di samping checkbox-nya.

**8. Halaman masuk di ponsel: diperbaiki.**
- Grid tidak lagi melebar.
- Paragraf hero disembunyikan dan judul diperkecil.
- Pilihan bahasa tidak lagi melayang.
- Hasil: tombol **Masuk** berakhir di 639 px dari 812 px, dan lebar halaman 375 px.

**9. "Selesai" di permintaan terbuka: diperbaiki.**
- Bagi peminta, status Selesai kini berbunyi **Menunggu konfirmasi Anda**, dengan tautan **Periksa & konfirmasi** di Beranda. Ini juga berlaku di Permintaan saya.

## Urutan pengerjaan yang disarankan

1. **Sebelum demo ke klien (±2–3 hari kerja):**
   - Temuan 2 (perbaikan CSS kecil).
   - Temuan 3 (hapus pertanyaan ganda di katalog awal).
   - Temuan 1, bagian katalog awal dan label peran berbahasa Indonesia.
   - Temuan 8 (halaman masuk di ponsel).
2. **Sprint berikutnya:**
   - Temuan 1, bagian catatan aktivitas sistem.
   - Temuan 4 (konteks persetujuan).
   - Temuan 5 dan 6 (KPI di ponsel, antrean).
   - Temuan 7 (mode lapangan).
   - Temuan 9.
3. **Pemolesan:** temuan 10–12.

Setiap perbaikan sebaiknya disertai uji Playwright di ukuran ponsel (375×812). Suite sekarang memakai `Desktop Chrome` saja, sehingga temuan 2, 5, 7, dan 8 tidak tertangkap.
