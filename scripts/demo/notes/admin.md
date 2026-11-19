# Catatan demo: modul `admin` (orang & akses, administrasi, platform)

File: `scripts/demo/06-admin.mjs` (key `admin`). Dijalankan sesudah `servicedesk`, sebelum `history`. Waktu jalan: sekitar 2 menit (di mesin dev), lalu cetak satu baris **LIVE INVITATION** (tautan undangan yang bisa diterima presenter di panggung) dan path webhook e-mail masuk.

Semua data dibuat lewat API sebagai pemeran (cast). 14 akun cast **tidak pernah** di-suspend, dikunci, dihapus, atau diganti perannya; semua orang tambahan fiktif (13 rekan kerja + 3 anak magang). Kata sandi semua rekan yang sudah menerima undangan: `Passw0rd!demo`.

## Layar yang terisi (screen → siapa yang buka → isi)

| Layar | Dibuka oleh | Isi |
|---|---|---|
| `/users` (ringkasan) | daniel, admin | 30 orang: 25 aktif, 3 pending, 1 suspended, 1 deactivated, 1 terkunci. Grafik pertumbuhan 12 bulan (14 → 30), sebaran peran, 5 registrasi terbaru |
| `/users/all` | daniel, admin | 30 orang, 9 departemen/2 kantor (Jakarta, Bandung), manajer terisi. Filter status: aktif/pending/suspended/deactivated/locked masing-masing ada isinya. Bambang Hartono memakai MFA |
| `/users/[id]` | daniel, admin, maya | peran, sesi aktif, aktivitas (audit), manajer, ID karyawan, site, `externalId` dari SCIM. Contoh kaya: Bambang Hartono, Yusuf Maulana (3 peran), Dian Anggraeni (terkunci) |
| `/users/invitations` | daniel, admin | 20 undangan: 5 pending (Farhan Alamsyah = untuk presenter, Gita Permata, dan 3 anak magang dari impor CSV), 13 diterima, 1 kedaluwarsa (Novi Andriani), 1 dicabut (Hasan Basri) |
| `/roles`, `/permissions`, `/access-matrix` | admin (matrix/permissions butuh `permissions.read`; daniel hanya `/roles`) | 12 peran (9 sistem + Finance Viewer, Facilities Coordinator, HR Onboarding), 71 permission, pemegang peran kustom: Bambang+Siti (Finance Viewer), Wawan+Yusuf (Facilities Coordinator), Retno+Wulan (HR Onboarding). Satu perubahan matriks sudah disimpan (Facilities Coordinator + `bookings.read_all`) |
| `/access` (ikhtisar) | admin, maya, daniel | KPI, login 14 hari (berhasil dan gagal), peristiwa keamanan terbaru, undangan pending |
| `/access-reviews` | daniel, admin | 1 tinjauan selesai ("Tinjauan akses triwulan III 2026": 17 item, 16 dipertahankan, 1 dicabut: peran Receptionist Ahmad Fauzi) dan 1 terbuka ("triwulan IV 2026": 16 item, 8 sudah diputuskan, **8 menunggu**). Jadwal triwulanan aktif |
| `/access-requests` | admin, daniel | 9 permintaan: 4 pending (Rizky Pratama → Facilities Coordinator, Fajar Ramadhan → Finance Viewer, Anita Rahmawati → HR Onboarding, Yoga Pratama → Finance Viewer), 2 disetujui (Siti, Yusuf), 2 ditolak (Dewi, Wulan), 1 dibatalkan (Rina) |
| `/account-closures` | admin, daniel | 3 permintaan: Rina Kartika **pending**, Ahmad Fauzi disetujui (masa tunggu 14 hari, jalan), "Closed account" (Nurul Hidayati) **selesai** |
| `/audit` | maya, admin | ratusan baris, sebagian besar tersebar di 85 hari terakhir (lihat "Backdating"): sign-in, undangan, perubahan peran, impor, review, API key, webhook, retensi, e-mail masuk, ekspor audit |
| `/security-events` | maya, admin | 47 peristiwa: 20 belum diakui (termasuk 2 High: kunci akun Dian Anggraeni, reset MFA Yoga Pratama), 27 sudah diakui oleh Maya. Jenis: login.failed, account.locked, privilege.change, mfa.enrolled/reset, password.reset(_required), session.revoked, tenant.suspended, retention.change |
| `/sessions` | siapa pun | admin: 6 sesi (Chrome Windows, Edge Windows, Safari Mac, iPhone Safari, Firefox Linux, ...) dengan IP berbeda; daniel 4, maya 4, hendra/putri/rizky/sinta 3. Total 42 sesi aktif (halaman semua-sesi untuk admin) |
| `/admin/settings` | admin | sistem: from-email "GA Helpdesk Annaba Digital", retensi audit 730 hari, undangan 7 hari, kebijakan sign-in bawaan |
| `/admin/security-policy` | admin **dalam support mode** pada "PT Annaba Logistik Nusantara" | halaman ini hanya untuk organisasi (bukan platform). Organisasi itu sudah punya kebijakan lebih ketat: kata sandi 14, idle 20 menit, kunci 4 percobaan, MFA wajib, undangan 5 hari |
| `/admin/retention` | admin | kebijakan aktif (tiket selesai 730 hari anonymise, lampiran 365, notifikasi 180), 1 legal hold (tiket "Kehilangan laptop kantor di ruang rapat Garuda"), 1 proses manual (0 baris), pratinjau |
| `/admin/email` | admin | 85 e-mail (83 terkirim, 2 gagal: undangan ke alamat yang tidak ada) |
| `/admin/email-wording` | admin | nama pengirim, awalan subjek "[GA Helpdesk]", penutup EN + ID (berlaku ke semua e-mail sesudahnya) |
| `/admin/inbound-email` | admin | alamat `helpdesk@annabadigital.io`, aktif, layanan bawaan "General request"; 7 pesan masuk: 4 jadi tiket, 1 balasan jadi komentar, 1 diabaikan (auto-reply), 1 ditolak (pengirim tak dikenal) |
| `/admin/integrations` | admin | 4 API key (Integrasi ERP Keuangan, Dasbor Monitoring Gedung, Provisioning Azure AD (SCIM), satu dicabut), 3 webhook dengan 28 pengiriman berhasil (ChatOps, BMS) dan 6 gagal (ERP selalu 500). **Ketiga webhook dalam keadaan dimatikan** (lihat caveat) |
| `/admin/directory` | admin, daniel | 4 pemetaan grup → peran (`SG-GA-Teknisi`→GA Agent, `SG-Finance-Viewer`, `SG-Facilities-Coordinator`, `SG-HR-Onboarding`), 4 grup SCIM, 13 orang dengan `externalId`, sinkronisasi CSV HR sudah dipratinjau dan diterapkan |
| `/admin/channels` | admin | WhatsApp dan Slack aktif (kredensial palsu, terenkripsi), Teams terpasang tapi **mati**; 3 orang terhubung Teams; 5 pengiriman gagal, 2 di antaranya jatuh kembali ke e-mail |
| `/admin/tenants` | admin | Default organisation (30 pengguna), PT Annaba Logistik Nusantara (aktif), CV Mitra Properti Sejahtera (**suspended**) |
| `/notifications/settings` | hendra, putri, sinta, rizky, anita, maya | preferensi kustom (badge "custom") |
| `/profile` | semua cast | departemen, telepon, bahasa Indonesia, zona Asia/Jakarta (admin, daniel, maya dilengkapi) |
| `/invite` (publik) | presenter | undangan pending untuk **farhan.alamsyah@annabadigital.io**, tautannya dicetak di akhir modul |
| `/forgot-password`, `/reset-password` | publik | alur sudah dijalankan untuk Wulan Safitri (jejak di audit) dan alamat yang tidak ada |
| `/register` | publik | mode registrasi = invitation-only (bawaan); bisa dibuka di `/admin/settings` bila ingin ditunjukkan |

Tiket yang ikut dibuat modul ini (terlihat di daftar tiket, milik pemohon cast/rekan): 4 tiket dari API publik (kunci ERP) mengatasnamakan Rizky/Anita/Fajar/Lina ("Dispenser air di pantry Lantai 3 bocor", "Tolong ganti lampu koridor depan ruang HR yang mati", "Kursi kerja di meja Finance patah sandarannya", "Papan nama ruang rapat Merak terlepas dari dinding") dan 4 tiket dari e-mail masuk ("Mohon bantu pindahkan 2 meja ke area event lantai 5", "Kehilangan laptop kantor di ruang rapat Garuda" (legal hold), "Permintaan kunci laci arsip Finance cadangan", "AC ruang Finance bocor menetes ke meja"), satu balasan e-mail menjadi komentar. Layanan "General request" dibuat oleh Hendra bila belum ada.

## Alur yang siap didemokan

Penting: tindakan kritis (menetapkan peran, setujui/tolak permintaan akses, buat API key/webhook/tenant, ubah pengaturan, sinkronisasi) meminta **konfirmasi kata sandi** (jendela 5 menit). Presenter cukup mengisi `Passw0rd!demo` bila diminta.

1. **Mengundang orang (daniel)**: `/users/invitations` → Undang → e-mail baru + peran User. E-mail "terkirim" ke konsol (lihat `/admin/email`). Ada contoh pending: Gita Permata.
2. **Menerima undangan**: buka tautan LIVE INVITATION yang dicetak modul (`/invite?token=...`), isi nama + kata sandi (mis. `Passw0rd!demo`). Sebelum: undangan pending, sesudah: diterima, pengguna aktif. Bila tautan hilang: kirim ulang undangan Farhan (resend) lalu ambil tautan dari log API.
3. **Menetapkan peran (admin)**: `/users/all` → klik rekan (mis. Dewi Lestari) → ubah peran (misal tambah Finance Viewer) → kebijakan konfirmasi kata sandi. Di `/access-matrix` perubahan terlihat.
4. **Permintaan akses**: login sebagai **rizky** → "Minta akses" (sudah ada 1 permintaan pending Facilities Coordinator; buat baru untuk peran lain bila perlu). Lalu **admin** (`/access-requests`) setujui/tolak dengan catatan: pending → disetujui/ditolak, rizky mendapat peran. Catatan: **daniel hanya bisa menyetujui permintaan Anita (HR Onboarding)**; peran lain memuat permission yang tidak dimiliki User Admin (aturan anti-eskalasi), jadi harus admin.
5. **Tinjauan akses (daniel/admin)**: `/access-reviews` → "triwulan IV 2026" → 8 item menunggu → Pertahankan/Cabut. Peran Receptionist, GA Manager dll. milik cast: **jangan dicabut** dalam demo (itu akun login). Pilih rekan fiktif (Bambang, Siti, Wawan, Yusuf, Wulan, Retno) untuk "Cabut". Nama sendiri tidak bisa ditinjau sendiri (admin tidak bisa memutuskan item Sarah).
6. **Suspend/unlock**: Dian Anggraeni terkunci (kunci berlaku 3 hari) → daniel `Buka kunci`. Suspend: rekan mana saja (mis. Retno) → `Suspend` + alasan; `Aktifkan` kembali. Ilham Ramadhan sudah suspended (contoh).
7. **Penutupan akun**: `/account-closures` → Rina Kartika pending → Setujui (masa tunggu 14 hari) atau Tolak. Ahmad Fauzi sudah disetujui; Nurul sudah selesai.
8. **API key / webhook**: `/admin/integrations` → buat API key (token tampil sekali) dan webhook. Untuk melihat pengiriman, jalankan penerima lokal (webhook boleh ke 127.0.0.1 di tumpukan demo):
   `node -e "require('http').createServer((q,s)=>{let b='';q.on('data',c=>b+=c);q.on('end',()=>{console.log(q.method,q.url,q.headers['x-annaba-event']);s.end('ok')})}).listen(9191,'127.0.0.1')"`
   lalu nyalakan webhook "Notifikasi ke kanal obrolan GA" (URL `http://127.0.0.1:9191/hooks/chatops`, event tiket dibuat/ditugaskan) atau buat yang baru, lalu buat/tugaskan tiket: baris pengiriman muncul dengan status 200.
9. **Sinkronisasi direktori**: `/admin/directory` → Sinkronisasi CSV → tempel CSV di bawah → **Pratinjau** (2 diperbarui, 1 ditangguhkan: Ahmad Fauzi, 1 tidak ditemukan) → Terapkan.
10. **Peran baru lalu lihat di matriks**: `/roles` → peran baru dengan beberapa permission → muncul di `/access-matrix`.
11. **Peristiwa keamanan**: maya `/security-events` → filter "belum diakui" → Akui (ada ~20, termasuk 2 High).
12. **Ekspor audit**: maya `/audit` → Ekspor (CSV/JSON); tercatat di audit sebagai `audit.export`.
13. **Impor CSV pengguna**: `/users/invitations` → Impor CSV, peran User, tempel (pratinjau lalu impor; 3 orang pertama dari CSV ini sudah diimpor, jadi gunakan e-mail baru):
```
name,email,phone,department,jobTitle
Gilang Saputra,gilang.saputra@annabadigital.io,+62 813 5500 2201,Operations,Intern Operasional
Hana Pertiwi,hana.pertiwi@annabadigital.io,+62 813 5500 2202,Finance,Intern Akuntansi
```
14. **E-mail masuk**: kirim e-mail mentah (MIME) ke path webhook yang dicetak di akhir modul (atau putar token baru di `/admin/inbound-email`; path tampil sekali):
    `curl -X POST "http://localhost:5080<path>" -H "X-Internal-Proxy-Key: <kunci proxy>" -H "Content-Type: message/rfc822" --data-binary @surat.eml` (pengirim harus e-mail pengguna aktif).
15. **Support mode**: `/admin/tenants` → "PT Annaba Logistik Nusantara" → Support mode (konfirmasi kata sandi) → `/admin/security-policy` menampilkan kebijakan organisasi itu; keluar dengan tombol di banner.

CSV sinkronisasi direktori untuk dipratinjau/diterapkan langsung (kolom lain boleh kosong; kode site kantor pusat: `HEADOFFICEJA`, Bandung: `BDG`):
```
email,employee_id,department,job_title,manager_email,site_code,status
retno.wulandari@annabadigital.io,AD-0101,Human Resources,Head of Human Resources,,HEADOFFICEJA,
rina.kartika@annabadigital.io,AD-0110,Sales,Senior Sales Executive,yoga.pratama@annabadigital.io,BDG,
ahmad.fauzi@annabadigital.io,AD-0109,,,,,inactive
pegawai.baru@annabadigital.io,AD-0199,Finance,Staf Baru,,HEADOFFICEJA,
```

## Tidak bisa didemokan / bug / catatan jujur

- **Log API kosong bila API dijalankan lewat `stack.mjs`** (`dotnet run` di-spawn dengan stdout ke file tidak menulis apa pun; semua `demo-api-*.log` 0 byte). Modul tetap bekerja: bila tautan satu-kali tidak ada di log (`DEMO_API_LOG`, atau `%TEMP%/demo-api-main.log`, atau `demo-api-admin.log`), modul mengganti `token_hash` undangan/reset dengan hash dari token buatannya sendiri lewat SQL, lalu menerima undangan lewat API seperti biasa. Menjalankan `AnnabaDigital.Api.exe` langsung (bukan `dotnet run`) membuat log terisi (sudah dicoba: tautan dibaca dari log).
- **Tabel audit append-only.** Untuk memberi riwayat, modul mematikan trigger `audit_events_no_update` hanya di dalam satu transaksi (matikan, geser `ts` baris milik modul ini, nyalakan lagi). Bila tidak ingin, jalankan dengan `DEMO_NO_BACKDATE=1` (maka audit/peristiwa keamanan semuanya "hari ini", tanggal orang dan sesi tidak digeser).
- **Teams dimatikan di akhir** (WhatsApp dan Slack hidup, tak ada orang yang tersambung). Alasan: bila Teams hidup, setiap notifikasi untuk Agus/Putri/Hendra akan dikirim ke alamat palsu di server Microsoft (panggilan keluar). Pengiriman gagal + jatuh ke e-mail dibuat dengan mematikan kanal sesaat setelah pesan masuk antrean, sehingga worker menandainya gagal tanpa menghubungi luar. Menyalakan Teams lagi di panggung = pengiriman gagal dan e-mail pengganti (jika offline akan diulang beberapa kali dengan jeda dulu).
- **Tidak ada pengiriman kanal yang berhasil** (butuh Slack/Microsoft/Meta sungguhan). Tidak ada pengguna yang terhubung ke WhatsApp/Slack (WhatsApp memerlukan kode dari Meta, Slack mencari e-mail lewat API Slack): dilewati.
- **Webhook dimatikan di akhir modul** karena penerima lokal (port 9191, `DEMO_HOOK_PORT`) hanya hidup selama modul; tanpa itu tiket modul `history` akan menumpuk pengiriman gagal. Riwayat pengiriman (28 berhasil, 6 gagal) tetap tampil. Satu pengiriman ERP "ditinggalkan" (8 kali gagal) ditandai lewat SQL.
- **Nurul Hidayati** (penutupan selesai): bila worker penutupan akun belum jalan (ia mulai ±2 menit setelah API hidup, lalu tiap jam), modul menjalankan langkah yang sama lewat SQL (hapus kredensial/sesi/peran, nama jadi "Closed account"); catatan audit `account.close` oleh sistem tidak ada pada kasus ini.
- **MFA Bambang Hartono**: rahasia TOTP tidak disimpan, jadi tidak bisa login sebagai Bambang (hanya tampil sebagai "MFA aktif"). Pengguna lain bisa login dengan `Passw0rd!demo` kecuali Dian (terkunci), Ilham (suspended), Siti (wajib ganti kata sandi), Wulan (kata sandi diganti lewat reset), Nurul (ditutup), dan 3 anak magang (belum menerima undangan).
- **Daniel tidak bisa membuka `/permissions` dan `/access-matrix`** (User Admin tidak punya `permissions.read`) dan tidak bisa menetapkan peran dengan permission di luar miliknya; itu aturan aplikasi, bukan masalah data.
- **`/admin/security-policy` kosong untuk admin platform** (aplikasi: organisasi platform memakai pengaturan sistem). Perlu support mode, lihat alur 15. Support mode otomatis berakhir setelah 2 jam.
- **Kebijakan retensi aktif.** Worker harian menghapus/menganonimkan tiket selesai lebih tua dari 730 hari (lampiran 365, notifikasi 180). Aman bila data `history` tidak lebih tua dari itu.
- **Pemeriksaan kebocoran kata sandi** (layanan online HIBP) dimatikan selama modul memberi kata sandi dan dinyalakan lagi di akhir (dua baris `settings.update` di audit).
- Pengguna "locked": kunci 15 menit bawaan diperpanjang 3 hari lewat SQL; setelah itu Dian bisa login lagi dengan `Passw0rd!demo`. Mengunci ulang: 5 kali sign-in salah.
- Sesi yang dibuat modul dibuat "remember" (idle 5 hari) lewat SQL agar halaman `/sessions` tetap terisi hari-hari berikutnya (bawaan idle 30 menit). IP sesi diganti dengan alamat ISP Indonesia (cosmetic).
- Pengiriman e-mail "gagal" (2 baris) dibuat dengan SQL; pengirim konsol tidak pernah gagal.
- Grup SCIM memakai kunci "Provisioning Azure AD (SCIM)" (token tidak disimpan; buat kunci baru bila ingin memanggil SCIM saat demo: `POST /api/v1/scim/v2/Users`, header `Authorization: Bearer adk_...`).
- Modul menolak dijalankan dua kali (cek keberadaan Retno Wulandari): kedua kali mencetak "already seeded". Bila run pertama terputus di tengah, reset database.

## Backdating yang dilakukan (SQL)

- `audit_events.ts` dan `security_events.ts` baris milik modul ini: dipetakan ke 85 hari terakhir (kejadian awal modul = tua, akhir modul = hari ini); `acknowledged_at` mengikuti. Peristiwa direktori (SCIM/sinkron) ditaruh ±30 jam lalu.
- `users.created_at` rekan = saat menerima undangan (dipetakan, antara ±70 dan 50 hari lalu); `invitations.created_at/accepted_at/expires_at` mengikuti; undangan kedaluwarsa Novi: dibuat 12 hari lalu, habis 5 hari lalu. Empat akun dev (Sarah, Daniel, Maya, Rizky) dibuat 120 hari lalu supaya grafik pertumbuhan tidak mulai hari ini. Anak magang: 2 hari lalu.
- `users.last_login_at` rekan 0,1 s.d. 30 hari lalu; `sessions` (created/last_active/IP, idle/absolute expiry, remember) untuk semua sesi aktif organisasi default.
- `access_reviews`/`access_review_items` (tinjauan III: ±2 bulan lalu, selesai; tinjauan IV: 9 hari lalu, jatuh tempo 21 hari lagi), `access_requests` (pending 1 s.d. 4 hari, diputuskan ±12 hari), `account_closure_requests` (Nurul 17 hari lalu selesai, Ahmad 6 hari lalu, Rina kemarin), `api_keys` (dibuat/dipakai terakhir), `outbox_emails` (2 gagal), `users.locked_until` (Dian), `webhook_deliveries` (1 ditinggalkan).
