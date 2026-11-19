/**
 * The knowledge base of the service-desk seed: 5 categories, published how-to articles in Bahasa Indonesia (linked to catalog services, read and voted on),
 * a staff-only guide, drafts, one article overdue for review and one archived. Written and published by GA staff through the API; read counts are set directly.
 */
import { rows } from "./lib.mjs";

const DAY = 86_400_000;
const ymd = (offset) => new Date(Date.now() + offset * DAY).toISOString().slice(0, 10);

export const KB_CATEGORIES = ["Ruang Rapat & Acara", "Fasilitas & Perawatan", "Kebersihan & Pantri", "Tamu, Parkir & Akses", "Kebijakan & Layanan Kantor"];

/** age: days since it was published; views: reads; yes/no: "was this helpful" votes (by the first people of VOTERS); solved: "this solved it" counts. */
export const ARTICLES = [
  { cat: 0, by: "putri", age: 70, views: 412, yes: 9, no: 0, solved: 38, services: ["Meeting room booking"], title: "Cara memesan ruang rapat Garuda dan Merak",
    summary: "Langkah memesan ruang rapat, batas waktu pemesanan dan cara membatalkan.",
    body: `## Memesan ruang rapat

Ruang rapat di Tower A Level 3: **Garuda** (kapasitas 12 orang) dan **Merak** (6 orang).

1. Buka menu **Bookings** lalu pilih ruang dan jam yang kosong.
2. Isi jumlah peserta dan tujuan rapat. Pilih layout (boardroom, classroom, U-shape atau theatre).
3. Jika butuh katering, centang pilihan katering dan tulis kebutuhannya.
4. Pemesanan langsung terkonfirmasi kalau ruang kosong. Tim GA akan menyiapkan ruangan satu jam sebelum rapat.

## Aturan penting

- Pesan paling lambat **1 hari kerja** sebelumnya. Untuk acara lebih dari 20 orang, pakai layanan *Event set-up*.
- Batalkan pemesanan yang tidak jadi dipakai paling lambat 2 jam sebelum mulai. Ruang yang tidak dipakai tanpa pembatalan dicatat sebagai *no-show*.
- Satu orang boleh memesan maksimal 4 jam per hari per ruang. Perpanjangan diajukan ke GA.

Pertanyaan lain? Hubungi GA di ext. 120 atau buat permintaan lewat layanan *Meeting room booking*.` },
  { cat: 0, by: "putri", age: 64, views: 187, yes: 5, no: 0, solved: 12, services: [], title: "Tata tertib penggunaan ruang rapat",
    summary: "Yang perlu dilakukan sebelum dan sesudah memakai ruang rapat.",
    body: `## Sebelum dan sesudah rapat

- Datang tepat waktu. Ruang dilepas otomatis jika tidak ada yang hadir dalam 15 menit pertama.
- Kembalikan **layout** meja dan kursi seperti semula dan bawa pulang sampah serta gelas.
- Hapus papan tulis, matikan proyektor, layar dan lampu.
- Jangan memindahkan kabel proyektor atau remote. Jika rusak, laporkan lewat layanan *Furniture & fittings*.
- Makanan berat tidak boleh dibawa ke ruang Garuda dan Merak (karpet). Kudapan ringan dan minuman bertutup diperbolehkan.

## Jika ruang bermasalah

AC tidak dingin atau lampu mati? Laporkan lewat layanan *Air conditioning* atau *Electrical & lighting* dan sebutkan nama ruangannya.` },
  { cat: 0, by: "hendra", age: 55, views: 96, yes: 4, no: 0, solved: 6, services: ["Event set-up"], title: "Mengajukan set-up acara: kursi, panggung dan sound system",
    summary: "Alur pengajuan dan persetujuan set-up acara serta lead time yang dibutuhkan.",
    body: `## Kapan memakai Event set-up

Gunakan layanan ini untuk acara dengan peserta **lebih dari 20 orang**, atau yang butuh kursi tambahan, panggung, sound system dan dekorasi.

## Alur

1. Ajukan lewat **New request → Event set-up** paling lambat **5 hari kerja** sebelum acara.
2. Permintaan masuk ke **atasan langsung** Anda untuk disetujui, lalu ke **GA Manager** (Hendra Kusuma).
3. Setelah disetujui, tim GA membuat jadwal dan work order. Anda dapat melihat statusnya di *My requests*.
4. Tim GA menyiapkan lokasi 2 jam sebelum acara dan membongkar setelahnya.

## Yang perlu disiapkan

- Tanggal, jumlah peserta, dan susunan ruangan yang diinginkan.
- Kebutuhan sound system, mic, layar atau backdrop.
- Penanggung jawab acara di lokasi.

Biaya sewa tambahan (mic, panggung dan lain-lain) dibebankan ke cost centre departemen peminta.` },
  { cat: 1, by: "agus", age: 66, views: 356, yes: 8, no: 1, solved: 41, services: ["Air conditioning"], title: "AC tidak dingin atau menetes: yang bisa dicek sebelum membuat tiket",
    summary: "Pemeriksaan cepat yang sering menyelesaikan masalah AC tanpa menunggu teknisi.",
    body: `## Cek cepat (2 menit)

1. Pastikan mode **Cool** dan suhu diset **24–25°C**. Remote kadang tersetel ke *Fan* atau *Dry*.
2. Pastikan jendela dan pintu ruangan tertutup.
3. Lihat apakah **filter udara** tampak berdebu. Jangan dibuka sendiri, cukup laporkan.
4. Jika AC menetes, jangan menyalakan ulang. Pindahkan laptop dan dokumen dari area bawah AC.

## Kapan membuat tiket

- AC tetap tidak dingin setelah 30 menit.
- Bunyi gesekan, bau gosong atau percikan.
- Air menetes atau mengalir dari unit.

Buat permintaan lewat **Air conditioning**, pilih ruangan, jenis masalah dan lampirkan foto bila bisa. Tiket AC **prioritas tinggi** (mengganggu pekerjaan) ditargetkan selesai dalam 1 hari kerja.

## Catatan teknisi

Jika ada rapat penting, tulis jam dan nama ruangannya di deskripsi supaya kami bisa mengutamakan.` },
  { cat: 1, by: "agus", age: 60, views: 241, yes: 6, no: 0, solved: 22, services: ["Electrical & lighting"], title: "Lampu mati atau stop kontak tidak berfungsi",
    summary: "Apa yang bisa dilakukan sendiri dan kapan harus segera melapor.",
    body: `## Jangan lakukan sendiri

Jangan membuka cover stop kontak, mengganti lampu TL atau memasang kabel sendiri. Itu pekerjaan teknisi.

## Yang boleh dicek

- Pastikan colokan terpasang kuat dan perangkat lain di stop kontak yang sama juga mati.
- Cek apakah saklar lampu pada posisi *on*.
- Lihat apakah ada **MCB** (sekring) di lemari panel lantai yang turun. Jangan dinaikkan kembali bila turun berulang, laporkan.

## Wajib dilaporkan segera (prioritas urgen)

- Bau gosong, percikan api atau stop kontak panas.
- Listrik padam sebagian area.
- Lampu emergency dan exit tidak menyala.

Buat permintaan lewat **Electrical & lighting** dan centang pilihan *safety risk* bila ada bahaya. Untuk kejadian darurat di luar jam kerja, hubungi satpam di ext. 100.` },
  { cat: 1, by: "bayu", age: 50, views: 178, yes: 4, no: 0, solved: 15, services: ["Plumbing & water"], title: "Toilet tersumbat dan kebocoran air: langkah cepat",
    summary: "Yang dilakukan saat ada genangan atau toilet tersumbat sebelum teknisi datang.",
    body: `## Jika ada genangan atau kebocoran

1. **Jauhi area basah** dan beri tahu rekan sekitar. Lantai licin berbahaya.
2. Tutup **stop kran** di bawah wastafel atau toilet bila mudah dijangkau.
3. Pindahkan barang elektronik dan dokumen dari area itu.
4. Segera buat permintaan lewat **Plumbing & water** dengan prioritas tinggi jika air terus mengalir. Satpam dan tim kebersihan akan memasang tanda lantai licin.

## Toilet tersumbat

- Jangan menyiram berulang kali.
- Jangan membuang pembalut, tisu basah atau puntung rokok ke toilet.
- Laporkan lantai, bilik dan jenis sumbatan (mampet atau air tidak turun).

Tim Facilities Engineering menargetkan penanganan dalam 4 jam kerja untuk kebocoran dan 1 hari untuk sumbatan biasa.` },
  { cat: 1, by: "putri", age: 44, views: 83, yes: 3, no: 0, solved: 5, services: ["Desk or office move"], title: "Mengajukan pindah meja atau pindah ruangan",
    summary: "Alur persetujuan, lead time dan persiapan sebelum pindah.",
    body: `## Siapa yang boleh mengajukan

Atasan langsung, atau staf HR atas nama karyawan. Pindah meja **antar lantai** memerlukan persetujuan **GA Manager**.

## Langkah

1. Ajukan lewat **Desk or office move** minimal **5 hari kerja** sebelum tanggal pindah.
2. Sebutkan lokasi asal, lokasi tujuan, tanggal dan barang yang dipindah (meja, pedestal, komputer, kabel jaringan).
3. Setelah disetujui, GA membuat jadwal. Pemindahan dilakukan di luar jam kerja untuk tim lebih dari 3 orang.
4. IT akan memasang ulang jaringan dan telepon di tempat baru.

## Yang perlu diperhatikan

- Kosongkan isi pedestal dan bawa barang pribadi sendiri.
- Beri label nama pada komputer dan monitor.
- Pindah ke lantai yang berbeda mungkin mengubah cost centre biaya fasilitas.` },
  { cat: 1, by: "bayu", age: 38, views: 61, yes: 2, no: 0, solved: 3, services: ["Furniture & fittings"], title: "Melaporkan kerusakan furnitur dan perlengkapan",
    summary: "Kursi, meja, pintu, kunci dan tirai: apa yang bisa diperbaiki dan berapa lama.",
    body: `## Yang termasuk furnitur dan fittings

Kursi dan meja kerja, lemari arsip, pintu, engsel, kunci, tirai dan blinds, serta papan tulis.

## Cara melapor

Buat permintaan lewat **Furniture & fittings** dengan menyebut lokasi, nama barang dan masalahnya. Foto sangat membantu kami membawa suku cadang yang tepat.

## Berapa lama

- Perbaikan ringan (engsel, handle, roda): **1–3 hari kerja**.
- Penggantian barang (kursi, lemari): perlu persetujuan pengadaan, sekitar **2–3 minggu**.
- Barang yang rusak karena kecelakaan atau penyalahgunaan dicatat dan dapat dibebankan ke departemen.

Mohon tidak memperbaiki sendiri barang yang berisiko roboh.` },
  { cat: 2, by: "bayu", age: 62, views: 205, yes: 5, no: 0, solved: 19, services: ["Cleaning request"], title: "Jadwal kebersihan dan cara meminta pembersihan tambahan",
    summary: "Jam kerja tim kebersihan dan layanan tambahan seperti deep cleaning.",
    body: `## Jadwal rutin

| Area | Jadwal |
|---|---|
| Toilet semua lantai | 08.00, 12.00, 15.00 |
| Lobby dan koridor | 07.00 dan 13.00 |
| Ruang kerja | Setiap pagi sebelum 08.00 |
| Pantry | 10.00 dan 15.30 |

## Pembersihan tambahan

Buat permintaan lewat **Cleaning request** untuk tumpahan, toilet yang perlu dibersihkan ekstra, pembuangan sampah besar atau deep cleaning ruangan sebelum acara.

- Tumpahan dan toilet: dibersihkan dalam **1 jam kerja**.
- Deep cleaning ruang rapat: ajukan **2 hari kerja** sebelumnya.

Mohon tidak membuang makanan ke wastafel pantry dan memisahkan sampah organik di tempat yang disediakan.` },
  { cat: 2, by: "bayu", age: 48, views: 142, yes: 4, no: 0, solved: 11, services: ["Pantry supplies"], title: "Pantry: persediaan kopi, teh dan air minum",
    summary: "Kapan pantry diisi ulang dan cara meminta tambahan persediaan.",
    body: `## Jadwal pengisian

Kopi, teh, gula dan krimer dicek tiap pagi Senin dan Kamis. Galon air minum diganti setiap hari saat kosong.

## Jika persediaan habis

Buat permintaan lewat **Pantry supplies**, sebutkan pantry mana dan apa yang habis. Tim pantry mengisi ulang dalam **setengah hari kerja**.

## Catatan

- Cucilah gelas pribadi Anda dan jangan meninggalkan gelas di wastafel.
- Gunakan dispenser air panas dengan hati-hati, terutama di jam sibuk.
- Permintaan makanan ringan untuk rapat diajukan lewat *Meeting room booking* (opsi katering).` },
  { cat: 2, by: "bayu", age: 34, views: 76, yes: 3, no: 0, solved: 8, services: ["Pest control"], title: "Melaporkan hama: kecoak, tikus dan semut",
    summary: "Cara melapor, apa yang kami lakukan dan apa yang bisa Anda bantu.",
    body: `## Yang bisa Anda lakukan

- Simpan makanan dalam wadah tertutup dan buang sampah organik setiap sore.
- Jangan menaruh makanan terbuka di laci atau di atas meja semalaman.

## Cara melapor

Buat permintaan lewat **Pest control**. Tulis jenis hama dan di mana Anda melihatnya (misalnya *kecoak di bawah wastafel pantry Level 3*).

## Penanganan

Pest control dilakukan oleh vendor resmi. Untuk area kerja dilakukan **di luar jam kerja** atau akhir pekan agar aman. Anda akan diberi tahu jika area perlu dikosongkan beberapa jam. Tim akan kembali memeriksa 3 hari setelah penanganan.` },
  { cat: 3, by: "putri", age: 69, views: 388, yes: 8, no: 0, solved: 33, services: ["Visitor registration"], title: "Mendaftarkan tamu sebelum kunjungan",
    summary: "Cara mendaftarkan tamu agar resepsionis siap dan tamu tidak menunggu.",
    body: `## Daftarkan tamu minimal 1 jam sebelum tiba

1. Buka **My visitors** lalu pilih **Undang tamu**, atau pakai layanan *Visitor registration*.
2. Isi nama, perusahaan, jam kedatangan dan tujuan kunjungan.
3. Resepsionis menyiapkan **badge tamu** dan memberi tahu Anda lewat notifikasi saat tamu tiba.

## Saat tamu datang

- Tamu membawa **KTP/ID** untuk dititipkan di resepsionis.
- Tamu harus didampingi tuan rumah di area kerja. Badge dikembalikan saat pulang.
- Tamu yang datang tanpa janji akan didaftarkan resepsionis dan Anda akan dihubungi.

## Kendaraan tamu

Parkir tamu tersedia di Basement B1 (kuota 10 kendaraan). Daftarkan plat nomor di kolom tujuan.` },
  { cat: 3, by: "hendra", age: 58, views: 273, yes: 6, no: 1, solved: 17, services: [], title: "Parkir: aturan, kuota dan kendaraan tamu",
    summary: "Siapa yang boleh parkir, di mana, dan apa yang harus dilakukan bila kartu parkir hilang.",
    body: `## Aturan parkir di Basement

- Parkir karyawan: **Basement B1 dan B2**, memakai kartu akses.
- Parkir tamu: B1 sisi timur, kuota **10 kendaraan**, maksimal 8 jam. Daftarkan plat nomor lewat *Visitor registration*.
- Motor di area khusus depan gedung. Helm tidak boleh ditinggalkan di lobby.

## Hal yang dilarang

- Parkir di jalur evakuasi, depan ramp pemadam atau menutupi pintu lift barang.
- Menginap di basement tanpa izin tertulis dari GA.
- Mengisi daya kendaraan listrik di stop kontak gedung.

## Kartu parkir hilang

Laporkan lewat layanan **ID card & access**. Kartu lama diblokir dan kartu baru bisa diambil di resepsionis dalam 1 hari kerja.` },
  { cat: 3, by: "putri", age: 52, views: 321, yes: 7, no: 0, solved: 28, services: ["ID card & access"], title: "Kartu akses (ID card) hilang, rusak atau baru",
    summary: "Cara mengajukan kartu akses baru dan memblokir kartu yang hilang.",
    body: `## Karyawan baru

HR mengajukan kartu **minimal 3 hari kerja** sebelum hari pertama. Kartu diambil di resepsionis dengan membawa KTP.

## Kartu hilang

1. Segera buat permintaan lewat **ID card & access**, pilih *Replace a lost card*.
2. Centang **Blokir kartu sekarang** agar kartu lama tidak bisa dipakai.
3. Kartu pengganti dicetak dalam 1 hari kerja. Biaya penggantian **Rp 25.000**, dipotong dari uang makan bulan berikutnya.

## Kartu rusak atau retak

Bawa kartu lama ke resepsionis. Penggantian kartu rusak karena pemakaian wajar **gratis**.

## Kartu akan habis masa berlaku

Kartu tamu proyek dan kontraktor dapat diperpanjang oleh pemberi kerja lewat layanan yang sama.` },
  { cat: 3, by: "putri", age: 46, views: 164, yes: 4, no: 0, solved: 14, services: ["Pool car booking"], title: "Memesan mobil operasional dan persetujuannya",
    summary: "Syarat memesan mobil, persetujuan atasan dan aturan penggunaan.",
    body: `## Syarat

- Untuk kepentingan dinas. Perjalanan pribadi tidak diizinkan.
- Pemesanan harus disetujui **atasan langsung**. Tanpa persetujuan, permintaan tidak diproses.
- Ajukan minimal **1 hari kerja** sebelumnya (pagi hari sebelum 12.00 untuk berangkat esok hari).

## Langkah

1. Buka **New request → Pool car booking**.
2. Isi waktu jemput, waktu kembali, tujuan, jumlah penumpang dan apakah butuh sopir.
3. Atasan Anda menerima notifikasi dan email untuk menyetujui.
4. Setelah disetujui, GA menetapkan kendaraan dan sopir. Anda menerima pemberitahuan.

## Saat memakai kendaraan

Isi **odometer awal dan akhir** serta biaya BBM dan tol saat mengembalikan. Kunci diambil dan dikembalikan di GA.` },
  { cat: 4, by: "putri", age: 56, views: 298, yes: 7, no: 0, solved: 29, services: ["Stationery request (ATK)"], title: "Pengajuan ATK: apa yang boleh dipesan dan batasnya",
    summary: "Daftar ATK standar, batas bulanan dan cara meminta barang tambahan.",
    body: `## ATK standar

Kertas A4/F4, pulpen, pensil, stabilo, map, binder, staples, lakban, sticky notes, tinta stempel dan toner printer.

## Cara mengajukan

Buat permintaan lewat **Stationery request (ATK)** dan tulis satu barang per baris beserta jumlahnya. Pilih lokasi pengantaran.

## Batas

- Setiap tim boleh mengajukan **satu kali per bulan**. Kebutuhan mendadak diajukan dengan alasan.
- Permintaan dengan total di atas **Rp 500.000** memerlukan persetujuan atasan.
- Barang di luar daftar (flashdisk, headset dan sejenisnya) diajukan lewat pengadaan, bukan lewat ATK.

ATK diantar dalam **1 hari kerja**. Untuk event, ajukan 3 hari sebelumnya.` },
  { cat: 4, by: "hendra", age: 40, views: 452, yes: 11, no: 1, solved: 46, services: [], title: "Aturan WFH dan fasilitas kantor saat bekerja dari rumah",
    summary: "Ringkasan kebijakan WFH terbaru: kuota, absensi, fasilitas dan kontak.",
    body: `## Kuota WFH

- Maksimal **2 hari per minggu**, disetujui atasan langsung lewat HRIS.
- Hari WFH tidak boleh digabung dengan cuti dalam satu minggu yang sama tanpa persetujuan.

## Fasilitas

- Laptop kantor dan VPN tetap digunakan. Jangan membawa monitor atau kursi dari kantor.
- Tunjangan internet WFH **Rp 150.000/bulan** otomatis masuk di slip gaji bagi yang WFH minimal 4 hari per bulan.
- Barang inventaris yang dibawa pulang harus dicatat di GA.

## Kehadiran dan ruang kantor

- Hari non-WFH, gunakan **hot desk** yang dipesan lewat *Bookings* bila meja tetap sedang dipakai.
- Ruang rapat tetap dapat dipesan seperti biasa dan peserta online diprioritaskan di Garuda (kamera dan mic tersedia).

Pertanyaan kebijakan ditujukan ke HR (ext. 130). Fasilitas ditujukan ke GA (ext. 120).` },
  { cat: 4, by: "putri", age: 32, views: 119, yes: 3, no: 0, solved: 9, services: ["Courier & package dispatch"], title: "Pengiriman kurir dan paket: batas waktu dan biaya",
    summary: "Jenis layanan kurir, cut-off pengiriman dan cara melacak paket.",
    body: `## Pilihan layanan

| Layanan | Batas waktu permintaan | Estimasi sampai |
|---|---|---|
| Same day | 13.00 | Hari yang sama (dalam kota) |
| Next day | 15.00 | Besok |
| Regular | 15.00 | 2–4 hari |

## Cara mengajukan

Buat permintaan lewat **Courier & package dispatch**. Tulis alamat lengkap, penerima, nomor telepon dan isi paket. Dokumen penting dikirim dengan asuransi.

## Biaya

Biaya dibebankan ke cost centre departemen peminta. Untuk pengiriman pribadi, bayar langsung ke kurir.

## Pelacakan

Nomor resi dicatat pada tiket Anda dan dikirim lewat email. Jika paket tidak sampai dalam batas waktu, balas tiket yang sama.` },
  { cat: 4, by: "hendra", age: 36, views: 154, yes: 5, no: 0, solved: 0, vis: "staff", services: [], title: "Panduan internal: prioritas tiket dan target SLA GA",
    summary: "Cara menentukan prioritas, target respons dan eskalasi untuk tim GA (staf).",
    body: `## Menentukan prioritas

| Prioritas | Contoh | Respons pertama | Penyelesaian |
|---|---|---|---|
| Urgent | Server room, listrik padam, kebocoran besar, risiko keselamatan | 15 menit | 4 jam kerja |
| High | AC di ruang rapat/ruang kerja tidak dingin, stop kontak mati | 1 jam | 8 jam kerja |
| Medium | Permintaan standar (kurir, ID card, perbaikan ringan) | 4 jam | 1 hari kerja |
| Low | ATK, pantry, perabotan | 8 jam | 2 hari kerja |

## Aturan kerja

1. Balas peminta (komentar publik) sebelum target respons berakhir. Itu dihitung sebagai *first response*.
2. Pakai **catatan internal** untuk hal yang tidak perlu dibaca peminta.
3. **On hold** hanya untuk menunggu pihak luar. Tulis alasan dan estimasi waktu.
4. Work order *wajib* dibuat untuk pekerjaan yang butuh checklist, vendor atau biaya.
5. Tiket yang akan melewati SLA dilaporkan ke Putri atau Hendra sebelum target habis.` },
  { cat: 1, by: "hendra", age: 120, views: 64, yes: 1, no: 2, solved: 2, vis: "staff", review: -25, services: [], title: "Daftar kontak darurat dan vendor GA",
    summary: "Nomor satpam, PLN, damkar dan vendor. Perlu diperiksa ulang (beberapa nomor mungkin berubah).",
    body: `## Kontak darurat

- Satpam gedung: ext. 100
- Pemadam kebakaran: 113
- Ambulans: 118
- PLN gangguan: 123

## Vendor utama GA

| Layanan | Vendor | Catatan |
|---|---|---|
| AC dan pendingin presisi | PT Sejuk Abadi | PIC: Joko Susilo |
| Pest control | Lihat daftar vendor | Kontrak tahunan |
| Kontraktor listrik | Lihat daftar vendor | Panggilan darurat 24 jam |

> **Perlu ditinjau**: nomor dan PIC vendor dicek terakhir beberapa bulan lalu.` },

  // ---- drafts and one archived article
  { cat: 3, by: "putri", age: 0, status: "draft", services: [], title: "Prosedur evakuasi gedung (draf)", file: "Denah-evakuasi-Tower-A.pdf",
    summary: "Draf prosedur evakuasi kebakaran dan gempa untuk Tower A.",
    body: `## Draf

1. Dengarkan instruksi dari **floor warden** dan jangan memakai lift.
2. Menuju titik kumpul di halaman depan gedung.
3. Lapor ke floor warden bila ada rekan yang belum terlihat.

*Menunggu masukan tim K3 sebelum diterbitkan.*` },
  { cat: 1, by: "agus", age: 0, status: "draft", services: [], vis: "staff", title: "Panduan menyalakan genset saat listrik padam (draf)",
    summary: "Langkah teknisi saat PLN padam, untuk staf Facilities Engineering.",
    body: `## Draf (staf)

1. Pastikan ATS berpindah ke genset dalam 10 detik. Jika tidak, pindahkan secara manual.
2. Cek bahan bakar dan oli. Catat jam mulai di logbook.
3. Matikan beban non-esensial (AC area umum) selama genset menyala.
4. Setelah PLN kembali, tunggu 5 menit sebelum memindahkan beban.

*Perlu diverifikasi dengan vendor genset.*` },
  { cat: 3, by: "putri", age: 0, status: "draft", services: [], title: "Kebijakan parkir motor dan sepeda (draf)",
    summary: "Draf kebijakan area parkir motor dan sepeda.",
    body: `## Draf

Motor diparkir di area depan gedung dengan stiker parkir. Sepeda boleh diparkir di rak sepeda di sisi timur. Kebijakan ini masih dibahas bersama HR.` },
  { cat: 4, by: "putri", age: 160, status: "archived", services: [], title: "Formulir permintaan ATK versi lama (kertas)",
    summary: "Tidak berlaku lagi: permintaan ATK sekarang lewat layanan di aplikasi.",
    body: `Formulir kertas permintaan ATK sudah tidak dipakai. Gunakan layanan **Stationery request (ATK)** di aplikasi.` },
];

const VOTERS = ["rizky", "anita", "fajar", "lina", "dimas", "bayu", "agus", "putri", "hendra"];

/** Creates the knowledge base. Idempotent: categories and articles are found by name/title. */
export async function seedKb(d, desk) {
  const hendra = await d.as("hendra"), putri = await d.as("putri");
  const existing = rows(d.must(await hendra.get("/api/kb/articles?status=all&pageSize=100"), "kb articles"));
  const have = new Set(existing.map((a) => a.title));
  if (ARTICLES.every((a) => have.has(a.title))) { d.log(`  knowledge base: already there (${existing.length} articles)`); return; }
  const cats = rows(d.must(await hendra.get("/api/kb/categories"), "kb categories"));
  const catId = {};
  for (const [i, n] of KB_CATEGORIES.entries()) { let c = cats.find((x) => x.name === n); if (!c) c = d.must(await hendra.post("/api/kb/categories", { name: n }), `category ${n}`); catId[i] = c.id; }
  const svcId = (n) => desk?.svc?.get(n)?.id ?? (d.ctx.services ?? []).find((s) => s.name === n)?.id;

  const made = [];
  for (const a of ARTICLES) {
    if (have.has(a.title)) continue;
    const author = await d.as(a.by);
    const body = { title: a.title, summary: a.summary, body: a.body, categoryId: catId[a.cat], visibility: a.vis ?? "everyone", reviewBy: a.review != null ? ymd(a.review) : a.status === "draft" || a.status === "archived" ? null : ymd(90 + (a.age % 40)), serviceIds: a.services.map(svcId).filter(Boolean), note: a.status === "draft" ? "Draf awal" : "Versi pertama" };
    const r = await author.post("/api/kb/articles", body);
    const art = d.must(r, `article ${a.title}`);
    if (a.status !== "draft") d.must(await putri.post(`/api/kb/articles/${art.id}/publish`), `publish ${a.title}`);
    if (a.file) await d.soft("kb file", async () => d.must(await author.upload(`/api/kb/articles/${art.id}/files`, [{ name: a.file, type: "application/pdf", data: d.pdf("Denah evakuasi Tower A") }]), "kb file"));
    if (a.status === "archived") d.must(await putri.post(`/api/kb/articles/${art.id}/archive`), `archive ${a.title}`);
    made.push({ a, id: art.id });
  }
  // a published article improved after it was published: a newer draft version waiting (shows "has unpublished changes")
  const wfh = made.find((m) => /WFH/.test(m.a.title));
  if (wfh) await d.soft("wfh draft", async () => { const au = await d.as("hendra"); const cur = d.must(await au.get(`/api/kb/articles/${wfh.id}`), "get"); d.must(await au.put(`/api/kb/articles/${wfh.id}`, { title: cur.title, summary: cur.summary, body: cur.body + "\n\n## Pembaruan (draf)\n\nKuota WFH akan menjadi 3 hari per minggu untuk tim non-operasional mulai bulan depan, menunggu persetujuan direksi.", categoryId: cur.categoryId, visibility: cur.visibility, reviewBy: cur.reviewBy, serviceIds: cur.services.map((s) => s.id), note: "Rencana kuota 3 hari", version: cur.version }), "update"); });

  // readers' answers: "was this helpful" (one per person) and "this solved it"
  let votes = 0;
  for (const m of made) {
    if (m.a.status === "draft" || m.a.status === "archived") continue;
    const voters = m.a.vis === "staff" ? ["bayu", "agus", "putri", "hendra"] : VOTERS;
    const total = m.a.yes + m.a.no; let no = m.a.no;
    for (let i = 0; i < Math.min(total, voters.length); i++) {
      const helpful = !(no > 0 && i >= m.a.yes); if (!helpful) no--;
      await d.soft("kb vote", async () => { const r = await (await d.as(voters[i])).post(`/api/kb/articles/${m.id}/feedback`, { helpful }); if (r.ok) votes++; });
    }
  }
  // reads and requests avoided, and the dates: spread over the ten weeks (the API stamps "now")
  const sql = ["begin;"];
  for (const m of made) {
    const a = m.a, t = new Date(Date.now() - a.age * DAY - (m.id.charCodeAt(2) % 8) * 3_600_000).toISOString();
    sql.push(`update kb_articles set views = ${a.status === "draft" ? 0 : a.views ?? 0}, deflections = ${a.solved ?? 0}, created_at = '${t}'::timestamptz - interval '${2 + (a.age % 5)} days', updated_at = '${t}', published_at = ${a.status === "draft" ? "null" : `'${t}'`} where id = '${m.id}';`);
    sql.push(`update kb_article_versions set created_at = '${t}'::timestamptz - interval '1 day' where article_id = '${m.id}' and number = 1;`);
  }
  sql.push("commit;");
  d.sql(sql.join("\n"));
  d.log(`  knowledge base: ${KB_CATEGORIES.length} categories, ${made.length} articles (${made.filter((m) => !m.a.status).length} published, ${made.filter((m) => m.a.status === "draft").length} drafts, 1 archived, 1 due for review), ${votes} votes`);
}
