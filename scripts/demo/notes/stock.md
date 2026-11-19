# Modul `stock` (04-stock.mjs): persediaan, permintaan ATK, biaya dan anggaran

Dijalankan sendiri: `node scripts/demo-seed.mjs --api http://localhost:<port> --db <db> --only stock` (setup otomatis dijalankan dulu). Waktu: sekitar 40 detik pada database baru, 2 detik pada run kedua (idempoten: bagian stok dilewati bila `ATK-A4-80` sudah ada; pusat biaya dan anggaran dicari per kode/cakupan).

Nomor permintaan (SR-2026-00xx) di bawah adalah nomor pada database baru yang di-seed berurutan; kalau ragu, cari berdasarkan peminta dan catatan.

## Screens covered

| Layar | Dibuka oleh | Isinya |
|---|---|---|
| `/inventory` | putri, hendra, admin (juga agus/bayu: baca saja) | 47 barang aktif + 1 diarsipkan (`ATK-A4-70`). Kategori: ATK 16, IT 4, pantry 8, cleaning 5, toiletries 3, "Other" 11 (suku cadang: filter AC, lampu LED/TL/downlight, stop kontak, saklar, kapasitor, freon, remote AC, kran, baterai). Stok di 4 tempat: Gudang Pusat (47 barang), Ground Floor/lobi (13), Pantry Level 3 (6), Branch Office Bandung (8). Nilai stok sekitar Rp 39,9 juta, kartu ringkasan semua terisi (issued 30 hari ~945, received 30 hari ~600, 8 baris stok rendah). |
| Daftar reorder (`/inventory`, filter "Low" / `/api/inventory/reorder`) | putri, hendra | 8 baris: Spidol whiteboard (9/12), Kantong sampah (6/10), Sabun cair 5 L (2/3), Toner HP 05A (0/2), Filter AC cassette (2/4), Lampu LED 9 W (0/10) di Gudang Pusat; Air galon (4/6) dan Gula pasir (3/5) di Pantry Level 3 (gudang masih banyak: contoh untuk transfer). |
| `/inventory/[id]` | putri, hendra | Riwayat 8 minggu tiap barang (463 gerakan total): penerimaan dari pemasok dengan nomor faktur ("PT Sinar Dunia Alat Tulis · Faktur SDAT-2610-0xxx · SJ ..."), pengeluaran ke orang ("Untuk Rizky Pratama (Operations) · persiapan audit"), pemakaian harian satelit, penyesuaian dengan alasan, transfer gudang ke pantry/lobi/Bandung (resi JNT), koreksi stock-take. Barang paling kaya riwayatnya: Kertas A4 (31 baris), Kopi Coffeemix, Air galon, Gula, Pulpen AE7, Tisu toilet. Cerita khusus: Spidol whiteboard, Ordner, Toner HP 05A, Lampu LED 9 W. |
| `/inventory/stock-takes` | putri, hendra | 3 hitungan: **Gudang Pusat** ditutup ~6 minggu lalu (47 baris, 7 selisih dibukukan), **Pantry Level 3** ditutup ~2,5 minggu lalu (6 baris, 4 selisih), **Ground Floor (lobi)** TERBUKA sejak kemarin sore: 13 baris, 9 sudah dihitung (2 selisih: Pulpen AE7 -2, Post-it -1), 4 belum (Tisu handuk, Pengharum, Baterai, Air galon). |
| `/supplies` (desk "all requests") | putri, hendra | 26 permintaan: menunggu persetujuan 6, siap diserahkan 3, sebagian diserahkan (ada back-order) 2, selesai diserahkan 11, ditolak 2, dibatalkan 1, ditutup (back-order ditutup) 1. Ringkasan: pending/open/partly/fulfilled30/nilai issued 30 hari (Rp ~4,9 juta)/barang terbanyak diminta. |
| `/supplies` (permintaan saya) | rizky (6), anita (4), fajar (5), lina (3), dimas (2), agus (3), bayu (2), sinta (1) | masing-masing punya riwayat sendiri. |
| `/supplies` ("menunggu persetujuan Anda") | dimas: 4 (rizky, anita, fajar, lina); putri: 2 (agus, dimas); hendra: 1 (dimas) | Dimas tidak punya atasan, jadi permintaannya jatuh ke desk (putri/hendra bisa menyetujui). Permintaan agus menunggu atasannya, putri. |
| Aturan permintaan (tombol pengaturan di `/supplies`, hendra) | hendra | Persetujuan atasan untuk perkiraan biaya **mulai Rp 400.000** (batas "di atas" ternyata "sama dengan atau di atas"); tempat ambil bawaan = Gudang Pusat. |
| `/costs/centers` | hendra, putri, admin | 6 pusat biaya (lihat bawah). |
| `/costs/budgets` | hendra, putri, admin | 10 anggaran tahun berjalan (6 pusat biaya, 2 site, 2 kategori). Pengaturan biaya: IDR, tarif tenaga kerja Rp 85.000/jam. |
| `/costs` (laporan biaya) | hendra, putri, admin | Barisnya berasal dari biaya work order (modul servicedesk). Modul ini hanya menyiapkan pusat biaya, anggaran, pengaturan. |

### Pusat biaya melekat ke TEMPAT, bukan ke orang
Pusat biaya memuat daftar lokasi (`locationIds`). Biaya work order masuk ke pusat biaya dari **lokasi tiket** (isian "lokasi" di formulir layanan / `locationId` saat tiket dibuat): sistem naik dari lokasi itu ke induknya dan memakai pusat biaya terdekat yang punya lokasi tersebut. Site (anggaran per site) = lokasi paling atas. Kategori anggaran = kategori layanan tiket. Jadi untuk membuat biaya masuk ke pusat biaya tertentu: buat tiket di lokasi itu, lalu tambah biaya di work order-nya.

| Kode | Pusat biaya | Lokasi | Anggaran awal (nominal) |
|---|---|---|---|
| GA-100 | General Affairs | Ground Floor (lobi, Reception) | Rp 150 juta |
| OPS-300 | Operations | Level 3 (Garuda, Merak, Pantry L3) | Rp 120 juta |
| HR-400 | Human Resources | Level 4 (lantai baru, dibuat modul ini) | Rp 45 juta |
| FIN-500 | Finance | Level 5 | Rp 60 juta |
| MKT-600 | Marketing | Branch Office Bandung (site) | Rp 90 juta |
| FAC-900 | Facilities | Basement (parkir), Gudang Pusat, Server room (Server room ada di Level 5 tetapi dipetakan sendiri ke Facilities) | Rp 360 juta |

Anggaran site: Head Office Jakarta Rp 600 juta, Branch Office Bandung Rp 120 juta. Anggaran kategori: Facilities & Maintenance Rp 380 juta, Cleaning & Hygiene Rp 80 juta.

### PENTING untuk pemakaian anggaran: `calibrateBudgets`
Pada saat modul ini jalan (sebelum servicedesk) belum ada biaya, jadi semua anggaran 0 % dan nominalnya sementara. Modul mengekspor `calibrateBudgets(d)` dan juga memanggilnya di akhir `run`. **Jalankan `--only stock` sekali lagi (atau panggil `calibrateBudgets(d)` dari modul terakhir) setelah servicedesk membuat biaya work order**: jumlah anggaran diubah menjadi `terpakai / target` sehingga tampil satu anggaran pusat biaya hampir habis (86 %, "near"), satu melewati anggaran (114 %, "over"), sisanya aman (22-63 %). Pusat biaya diurutkan menurut pengeluaran terbesar (karena belum diketahui tempat mana yang dapat biaya); site Jakarta 63 %, Bandung 40 %, kategori Facilities 52 %, Cleaning 70 %. Anggaran tanpa biaya dibiarkan 0 %. Sudah diuji dengan 6 work order contoh di database uji: FAC 113 % over, GA 85 % near, OPS 48 %, MKT 30 %, FIN 56 %, site Jakarta 63 %. Saran untuk servicedesk: beri biaya work order pada tiket di lokasi yang berbeda (Garuda/Merak, Reception/lobi, Server room/Basement, Bandung, Level 4/5) supaya beberapa pusat biaya terisi.

Catatan untuk servicedesk: barang stok dapat dipakai pada work order lewat `POST /api/work-orders/{id}/parts` `{ itemId, locationId, quantity }` (gerakan issue + baris biaya "part" sebesar biaya barang). Ini sudah dicoba. `d.ctx.stock = { items: { SKU: id }, places: { W, PAN, LOB, BDG } }` berisi id barang dan tempat. Barang AC/listrik yang cocok: `SPR-FLT-CST` (filter AC, stok 2), `SPR-KPS-35` (kapasitor, 7), `SPR-FRN-R32` (freon, 3), `SPR-LED-9W` (stok 0), `SPR-TL-18W` (19), `SPR-DWN-12W` (14), `SPR-STK-ARD`, `SPR-SKL-1`, `SPR-KRN-WST`. `d.ctx.costCenters = { "GA-100": id, ... }` dan `d.ctx.loc.lvl4`.

## Flows ready to demo

Semua alur memakai data pada keadaan awal; kata "Putri" = putri.anggraini, dst.

1. **Karyawan minta barang** (rizky/anita/fajar/lina/dimas): `/supplies` > "Ask for supplies", pilih tempat (Gudang Pusat bawaan), isi jumlah; perkiraan biaya muncul. Di bawah Rp 400.000 langsung "To be issued"; mulai Rp 400.000 menunggu atasan (rizky/anita/fajar/lina: Dimas; dimas: desk; agus: Putri).
2. **Dimas menyetujui/menolak** (dimas): `/supplies` > menunggu persetujuan Anda, 4 permintaan: rizky (Kertas A4 x10 + Post-it, Rp ~590 rb), anita (perlengkapan hari rekrutmen, Rp ~450 rb), fajar (perlengkapan surat-menyurat), lina (materi event peluncuran produk: spidol whiteboard x12, kertas A3, lakban, Post-it). Setuju: status jadi "To be issued" dan putri/hendra diberi tahu. Tolak: wajib alasan. Ide: setujui rizky dan lina, tolak anita/fajar dengan alasan.
3. **Putri menyerahkan penuh** (putri): permintaan siap serah: **rizky** (ordner, binder clip, lem, stabilo; Rp ~300 rb), **bayu** (pembersih lantai, Glassex, pengharum), **anita** (konsumsi rapat divisi HR, sudah disetujui Dimas kemarin). Klik "Issue", jumlah terisi otomatis, kirim: stok Gudang Pusat berkurang, peminta menerima notifikasi "ready to collect", status "Issued".
4. **Putri menyerahkan sebagian / back-order**: permintaan **lina** (spidol whiteboard x12, stok 9) setelah Dimas menyetujui: dialog Issue mengisi 9 otomatis, 3 sisanya jadi back-order, status "Partly issued". Sudah ada 2 permintaan sebagian-diserahkan yang menunggu: **fajar** (Toner HP 05A, 4 diminta, 2 diserahkan, 2 back-order, stok 0) dan **agus** (Lampu LED 9 W, 12 diminta, 8 diserahkan, 4 back-order, stok 0).
5. **Terima kiriman lalu lengkapi back-order** (hendra lalu putri): hendra `/inventory` > Toner HP 05A > "Receive" ke Gudang Pusat, jumlah 4, referensi mis. "CV Mitra Kantor Sejahtera · Faktur MKS-2611-0xxx" (baris reorder Toner HP hilang dari daftar saat stok naik di atas level 2). Lalu putri `/supplies` > permintaan fajar (toner) > Issue 2 sisanya: status "Issued".
6. **Tutup back-order dengan alasan** (putri): permintaan **agus** (Lampu LED 9 W, back-order 4, stok 0) > Close, isi alasan ("Lampu dipesan ulang ke PT Sumber Listrik Nusantara, kebutuhan basement dipenuhi dulu dari stok lain"): status "Closed". (Contoh riwayat: SR-0004 lina, Closed.)
7. **Keluarkan stok** (hendra): barang > "Issue", mis. 2 Kertas A3 untuk Rizky Pratama.
8. **Transfer antar tempat** (hendra): Gula pasir atau Air galon dari Gudang Pusat ke Pantry Level 3 (kedua baris reorder pantry hilang). Jangan transfer dari/ke **Ground Floor**: tempat itu sedang dalam stock-take terbuka.
9. **Penyesuaian dengan alasan** (putri atau hendra): barang > "Adjust", jumlah bertanda minus + alasan wajib (mis. Kertas A4 -1 "Rusak terkena air").
10. **Selesaikan stock-take** (putri): `/inventory/stock-takes` > hitungan terbuka Ground Floor: isi 4 baris yang belum dihitung (angka sama dengan "Expected" tidak menimbulkan selisih; ubah satu untuk menunjukkan selisih), lalu "Close": selisih (termasuk Pulpen -2 dan Post-it -1 yang sudah diisi) dibukukan sebagai gerakan "count". Mulai stock-take baru: pantry atau gudang (mis. Pantry Level 3).
11. **Daftar reorder** (hendra/putri): `/inventory` filter stok rendah atau tab reorder: 8 baris dengan saran jumlah beli (sesuai reorder quantity) dan perkiraan biaya.
12. **Barang baru** (hendra): `/inventory` > "New item" (SKU, nama, kategori, satuan, reorder level/quantity, biaya), lalu Receive ke Gudang Pusat.
13. **Atur aturan permintaan** (hendra): `/supplies` > Settings: batas persetujuan dan tempat bawaan.
14. **Anggaran dan pusat biaya** (hendra): `/costs/centers` (tambah pusat biaya dan pilih lokasi), `/costs/budgets` (tambah/ubah anggaran), `/costs` setelah servicedesk berjalan.
15. **Permintaan tanpa atasan** (dimas): permintaan dimas (jamuan tamu dari kantor pusat grup, Rp ~466 rb) menunggu di desk: hendra atau putri membuka `/supplies` > menunggu persetujuan Anda > Approve.
16. **Persetujuan atasan agus** (putri): permintaan agus (suku cadang AC cassette: kapasitor x4, remote AC x2, filter AC x2) menunggu Putri, bukan Dimas.

Riwayat sebagai bahan lihat-lihat (contoh): SR-0003 fajar (disetujui Dimas lalu diserahkan), SR-0004 lina (back-order spidol ditutup), SR-0006 dimas (disetujui desk), SR-0008 agus (disetujui putri, diserahkan hendra), SR-0009 rizky (ditolak Dimas dengan alasan toner HP vs Ricoh), SR-0011 fajar (dibatalkan oleh peminta), SR-0014 fajar (ordner diserahkan dua tahap: 8 lalu 4 setelah kiriman), SR-0015 rizky (ditutup desk tanpa diserahkan: status "Not approved").

## Not demonstrable / bugs / caveats

- **Kategori barang tetap** (`atk, pantry, cleaning, uniform, toiletries, it, other`): tidak ada kategori "suku cadang". Suku cadang memakai "other" (label "Other"). Tidak ada barang seragam.
- **Hak akses**: hanya `inventory.manage` (hendra, admin) yang boleh Receive/Issue/Transfer dan barang baru. Putri (supervisor) hanya boleh Adjust, stock-take dan menyerahkan permintaan (fulfil), TIDAK bisa menerima kiriman. Bayu (manajer gudang) dan agus hanya punya baca. Karena itu semua penerimaan/pengeluaran/transfer di riwayat dicatat oleh Hendra Kusuma dan penyesuaian/hitungan oleh Putri Anggraini.
- **Stock-take dan gerakan di tempat yang sama**: saat ditutup, selisih = hitungan - angka "Expected" yang dibekukan saat dimulai. Kalau ada issue/receipt/transfer di tempat itu antara mulai dan tutup, hasilnya dihitung ganda (stok tercatat salah). Hitungan terbuka ada di Ground Floor: jangan memindah/mengeluarkan stok di lobi sebelum ditutup. (Perilaku produk, tidak diubah.)
- Penyerahan (fulfil) ditolak seluruhnya kalau stok kurang; dialog UI mengisi otomatis min(outstanding, tersedia), jadi sebagian-diserahkan lancar. Tombol "Receive" memperbarui biaya satuan barang (biaya terakhir menjadi biaya barang); itu sebabnya perkiraan biaya permintaan lama memakai harga yang sedikit lebih murah.
- **Catatan audit tidak bisa digeser** (tabel audit tidak dapat diubah): kejadian stok/permintaan di log audit bertanggal hari seeding, sementara gerakan stok dan permintaan bertanggal mundur.
- Anggaran tidak dapat menunjukkan pemakaian sebelum ada biaya work order (lihat `calibrateBudgets`). `/costs` kosong sampai servicedesk membuat biaya.
- Modul membuat satu lokasi baru, floor "Level 4" (kode `TWR-A-L4`, di bawah Tower A), untuk pusat biaya HR; dicari dengan nama sehingga aman bila modul `spaces` juga membuatnya. Kalau modul spaces membuat lokasi dengan kode yang sama tetapi nama lain, akan bentrok.
- Pantry Level 3 hanya menyimpan 6 jenis barang (kopi, teh, gula, susu, galon, gelas); stock-take pantry pendek.
- Kolom "Requester" meminta barang dari "Collect from" mana saja yang punya stok; permintaan sinta (lobi) dan lina (pantry) ada di riwayat.
- Lonceng notifikasi: notifikasi lama (stok rendah, persetujuan, "siap diambil") diberi tanggal kejadian dan ditandai sudah dibaca; hanya yang berkaitan dengan permintaan yang masih berjalan yang belum dibaca (dimas 4 menunggu persetujuan, putri/hendra/admin permintaan baru).

## Backdating done

Semua lewat `d.sql`, setelah data dibuat lewat API (Receive/Issue/Adjust/Transfer/permintaan/stock-take):
- `stock_movements.at`: setiap gerakan digeser ke tanggal rencananya, 8 minggu terakhir (hari kerja, pukul 08-16). Urutan waktu sama dengan urutan eksekusi sehingga saldo (`balance_after`) berantai benar (diperiksa: 0 putus rantai, saldo = saldo akhir terakhir, tidak ada gerakan di masa depan). Gerakan `count` memakai tanggal penutupan hitungan dan referensinya diganti menjadi "Stock-take <tanggal mulai>".
- `supply_requests`: `created_at`, `updated_at`, `decided_at`, `fulfilled_at` mengikuti jadwal tiap permintaan (nomor SR tetap berurutan menurut tanggal).
- `stock_takes.started_at` / `closed_at`.
- `inventory_items.created_at` / `updated_at` = hari pengiriman pertama (~8 minggu lalu).
- `notifications.created_at` (dan `read_at`) untuk notifikasi stok/permintaan yang dikirim saat seeding, dicocokkan per langkah menurut jendela waktu.
- Tidak digeser: audit log (tidak dapat diubah), notifikasi lain, saldo `stock_balances.updated_at`.
