/**
 * In-app help (written for the three kinds of people who use the helpdesk). The text is kept in both languages here, next to the facts it describes, rather than in the
 * UI dictionary: a topic is read as a whole, so it must not be half-translated. Button and menu names are the ones on screen in each language (see i18n/id.ts).
 * When a screen changes, change its topic here; `e2e/help.spec.ts` checks that every link points to a page that exists.
 */
export type Persona = "ask" | "work" | "run";
export type Bi<T> = { en: T; id: T };
export type HelpTopic = {
  id: string;
  persona: Persona;
  title: Bi<string>;
  summary: Bi<string>;
  steps: Bi<string[]>;
  tips?: Bi<string[]>;
  /** A screen the topic is about. */
  go?: { href: string; label: Bi<string> };
};

export const PERSONAS: { key: Persona; title: Bi<string>; blurb: Bi<string> }[] = [
  { key: "ask", title: { en: "I need something", id: "Saya butuh bantuan" }, blurb: { en: "Making requests, following them and what happens to your data.", id: "Membuat permintaan, memantaunya, dan apa yang terjadi pada data Anda." } },
  { key: "work", title: { en: "I work the tickets", id: "Saya mengerjakan tiket" }, blurb: { en: "The queue, working a ticket, deadlines, approvals and reports.", id: "Antrean, mengerjakan tiket, tenggat, persetujuan, dan laporan." } },
  { key: "run", title: { en: "I set up the helpdesk", id: "Saya mengatur helpdesk" }, blurb: { en: "Going live, services, deadlines, people and keeping data safe.", id: "Mulai beroperasi, layanan, tenggat, orang, dan menjaga data tetap aman." } },
];

export const TOPICS: HelpTopic[] = [
  // ---------------------------------------------------------------- people who ask
  {
    id: "make-a-request", persona: "ask",
    title: { en: "Make a request", id: "Membuat permintaan" },
    summary: { en: "Ask General Affairs for something by choosing a service and filling in its short form.", id: "Minta bantuan General Affairs dengan memilih layanan dan mengisi formulir singkatnya." },
    steps: {
      en: ["Open **New request** and search for what you need (for example “air conditioning” or “visitor”), or browse the categories.", "Choose the service. Answer the questions; fields marked as required must be filled in.", "Add a subject that says what is wrong, and the place if you are asked for one. Attach photos or documents if the form has a file question (up to 8 MB each, 20 per request).", "Send it. You get a number like GA-2026-000123 and can follow it under **My requests**."],
      id: ["Buka **Permintaan baru** dan cari yang Anda butuhkan (misalnya “AC” atau “tamu”), atau telusuri kategorinya.", "Pilih layanannya. Jawab pertanyaannya; isian yang wajib harus diisi.", "Tulis subjek yang menjelaskan masalahnya, dan lokasinya bila diminta. Lampirkan foto atau dokumen jika formulir meminta file (maksimal 8 MB per file, 20 per permintaan).", "Kirim. Anda mendapat nomor seperti GA-2026-000123 dan dapat memantaunya di **Permintaan saya**."],
    },
    tips: { en: ["A clear subject and a photo save the team a round of questions.", "Some services need a manager's approval first; see “When your request needs approval”."], id: ["Subjek yang jelas dan sebuah foto menghemat satu putaran pertanyaan dari tim.", "Beberapa layanan memerlukan persetujuan atasan lebih dulu; lihat “Saat permintaan Anda perlu persetujuan”."] },
    go: { href: "/requests/new", label: { en: "New request", id: "Permintaan baru" } },
  },
  {
    id: "follow-your-requests", persona: "ask",
    title: { en: "Follow your requests", id: "Memantau permintaan Anda" },
    summary: { en: "See where each request stands and talk to the team.", id: "Lihat posisi setiap permintaan dan berkomunikasi dengan tim." },
    steps: {
      en: ["Open **My requests**. Search by number or subject, or filter by status.", "Open a request to see its status, who works on it, the date by which you can expect it, and the whole conversation.", "Write a comment to add information or ask a question; the team is told. You can add files while the request is still open."],
      id: ["Buka **Permintaan saya**. Cari berdasarkan nomor atau subjek, atau saring menurut status.", "Buka sebuah permintaan untuk melihat status, siapa yang mengerjakan, tanggal yang bisa Anda harapkan, dan seluruh percakapannya.", "Tulis komentar untuk menambah informasi atau bertanya; tim akan diberi tahu. Anda dapat menambah file selama permintaan masih terbuka."],
    },
    tips: { en: ["Statuses: Submitted → Assigned → In progress → Resolved → Closed. “On hold” means the team is waiting for something, and the reason is shown.", "Internal notes between staff are never shown to you."], id: ["Status: Diajukan → Ditugaskan → Sedang dikerjakan → Selesai → Ditutup. “Ditunda” berarti tim sedang menunggu sesuatu, dan alasannya ditampilkan.", "Catatan internal antar staf tidak pernah ditampilkan kepada Anda."] },
    go: { href: "/requests", label: { en: "My requests", id: "Permintaan saya" } },
  },
  {
    id: "confirm-reopen-cancel", persona: "ask",
    title: { en: "Confirm, reopen or cancel", id: "Mengonfirmasi, membuka kembali, atau membatalkan" },
    summary: { en: "What to do when your request is solved, is not really solved, or is no longer needed.", id: "Apa yang dilakukan saat permintaan Anda selesai, ternyata belum selesai, atau tidak lagi diperlukan." },
    steps: {
      en: ["When the status becomes **Resolved**, read what was done. If it is fine, choose **Confirm it is solved**.", "If it is not solved, choose **Reopen** and say why; the team picks it up again.", "If you no longer need it, choose **Cancel request** and give a reason."],
      id: ["Saat status menjadi **Selesai**, baca apa yang sudah dikerjakan. Jika sudah baik, pilih **Konfirmasi sudah beres**.", "Jika belum beres, pilih **Buka kembali** dan jelaskan alasannya; tim akan mengerjakannya lagi.", "Jika tidak lagi diperlukan, pilih **Batalkan permintaan** dan beri alasan."],
    },
    tips: { en: ["If you do nothing, a resolved request closes by itself after 3 days.", "After it is closed you can still reopen it for 7 days; after that, make a new request."], id: ["Jika Anda tidak melakukan apa pun, permintaan yang selesai menutup sendiri setelah 3 hari.", "Setelah ditutup Anda masih dapat membukanya kembali selama 7 hari; setelah itu, buat permintaan baru."] },
  },
  {
    id: "needs-approval", persona: "ask",
    title: { en: "When your request needs approval", id: "Saat permintaan Anda perlu persetujuan" },
    summary: { en: "Some services need a manager to agree before the team starts.", id: "Beberapa layanan memerlukan persetujuan atasan sebelum tim mulai bekerja." },
    steps: {
      en: ["Your request shows **Pending approval** and lists who has to decide and in which order.", "The approvers are told by e-mail and in the app. Nothing starts until every step is approved.", "If it is approved it goes to the team; if it is rejected you see the reason and the request ends. You can make a new one."],
      id: ["Permintaan Anda menampilkan **Menunggu persetujuan** dan daftar siapa yang harus memutuskan serta urutannya.", "Para penyetuju diberi tahu lewat email dan di aplikasi. Tidak ada yang dimulai sebelum semua langkah disetujui.", "Jika disetujui, permintaan diteruskan ke tim; jika ditolak, Anda melihat alasannya dan permintaan berakhir. Anda dapat membuat yang baru."],
    },
    tips: { en: ["Nobody can approve their own request."], id: ["Tidak seorang pun dapat menyetujui permintaannya sendiri."] },
  },
  {
    id: "notifications", persona: "ask",
    title: { en: "Notifications and e-mail", id: "Notifikasi dan email" },
    summary: { en: "Choose what you are told about and where.", id: "Pilih apa yang diberitahukan kepada Anda dan lewat mana." },
    steps: {
      en: ["The bell shows what is new; open **Notifications** to read and clear them.", "Choose **Notification settings** to decide, for each kind of event, whether it appears in the app, by e-mail, both or neither.", "E-mail can also be switched off completely in **My Profile**."],
      id: ["Lonceng menunjukkan yang baru; buka **Notifikasi** untuk membaca dan membersihkannya.", "Pilih **Pengaturan notifikasi** untuk menentukan, untuk setiap jenis kejadian, apakah muncul di aplikasi, lewat email, keduanya, atau tidak sama sekali.", "Email juga dapat dimatikan seluruhnya di **Profil Saya**."],
    },
    tips: { en: ["A request for your approval is always sent: work cannot start without your decision."], id: ["Permintaan persetujuan untuk Anda selalu dikirim: pekerjaan tidak dapat dimulai tanpa keputusan Anda."] },
    go: { href: "/notifications/settings", label: { en: "Notification settings", id: "Pengaturan notifikasi" } },
  },
  {
    id: "your-data", persona: "ask",
    title: { en: "Your data and privacy", id: "Data dan privasi Anda" },
    summary: { en: "See how long your data is kept, download a copy, or ask to close your account.", id: "Lihat berapa lama data Anda disimpan, unduh salinannya, atau minta penutupan akun." },
    steps: {
      en: ["Open **My Profile → Privacy**. The first box says how long your organisation keeps your requests, files and notifications.", "**Download my data** gives you one file with your profile, activity, requests, your own comments and the names of files you attached. You are asked for your password first.", "**Ask to close my account** sends a request to an administrator. If approved there is a 14-day waiting period in which you can still cancel; then your personal data is erased."],
      id: ["Buka **Profil Saya → Privasi**. Kotak pertama menjelaskan berapa lama organisasi Anda menyimpan permintaan, file, dan notifikasi Anda.", "**Unduh data saya** memberi Anda satu file berisi profil, aktivitas, permintaan, komentar Anda sendiri, dan nama file yang Anda lampirkan. Anda diminta memasukkan password lebih dulu.", "**Ajukan penutupan akun saya** mengirim permintaan ke administrator. Jika disetujui ada masa tunggu 14 hari di mana Anda masih dapat membatalkan; setelah itu data pribadi Anda dihapus."],
    },
    go: { href: "/profile?tab=privacy", label: { en: "Privacy", id: "Privasi" } },
  },

  // ---------------------------------------------------------------- people who work tickets
  {
    id: "work-the-queue", persona: "work",
    title: { en: "The ticket queue", id: "Antrean tiket" },
    summary: { en: "Find the tickets you should look at, and keep your favourite filters.", id: "Temukan tiket yang perlu Anda tangani, dan simpan filter favorit Anda." },
    steps: {
      en: ["Open **Ticket Queue**. You see the tickets of your teams, those assigned to you, and everything if you supervise.", "Filter by status, priority, deadline state, place, dates, “Unassigned only” or “Assigned to me”. Sort by last update or by the soonest deadline.", "When a set of filters is useful, choose **Save as view**. Your views are private; supervisors can share one with everyone who uses the queue."],
      id: ["Buka **Antrean Tiket**. Anda melihat tiket tim Anda, yang ditugaskan kepada Anda, dan semuanya jika Anda supervisor.", "Saring menurut status, prioritas, status tenggat, lokasi, tanggal, “Hanya yang belum ditugaskan”, atau “Ditugaskan ke saya”. Urutkan menurut pembaruan terakhir atau tenggat terdekat.", "Jika kombinasi filter berguna, pilih **Simpan sebagai tampilan**. Tampilan Anda bersifat pribadi; supervisor dapat membagikannya ke semua pengguna antrean."],
    },
    tips: { en: ["Search finds text in the number, subject, details and requester name.", "Past 10,000 results the count shows “10,000+”: narrow the filter instead of paging."], id: ["Pencarian menemukan teks pada nomor, subjek, rincian, dan nama pemohon.", "Di atas 10.000 hasil jumlahnya tampil “10,000+”: persempit filter, jangan menelusuri halaman."] },
    go: { href: "/tickets", label: { en: "Ticket Queue", id: "Antrean Tiket" } },
  },
  {
    id: "work-a-ticket", persona: "work",
    title: { en: "Work a ticket", id: "Mengerjakan tiket" },
    summary: { en: "From picking it up to resolving it.", id: "Dari mengambilnya sampai menyelesaikannya." },
    steps: {
      en: ["Open the ticket. Choose **Start work** (or **Assign** to give it to someone else).", "Talk to the requester with a comment. Use an internal note for what only staff should see.", "If you are waiting for something, **Put on hold** and give the reason: the deadline clock stops while it is on hold. **Resume** when you can continue.", "When it is done choose **Resolve**, pick how it was resolved and say what was done. The requester then confirms or reopens it."],
      id: ["Buka tiketnya. Pilih **Mulai kerjakan** (atau **Tugaskan** untuk memberikannya kepada orang lain).", "Berkomunikasi dengan pemohon lewat komentar. Gunakan catatan internal untuk hal yang hanya boleh dilihat staf.", "Jika menunggu sesuatu, **Tunda** dan beri alasannya: jam tenggat berhenti selama ditunda. **Lanjutkan** saat bisa meneruskan.", "Setelah selesai pilih **Selesaikan**, pilih cara penyelesaiannya dan tuliskan apa yang dikerjakan. Pemohon lalu mengonfirmasi atau membukanya kembali."],
    },
    tips: { en: ["The first public answer from staff counts as the first response for the deadline.", "You can change priority, subject and place with **Edit**, and attach files while the ticket is open."], id: ["Jawaban publik pertama dari staf dihitung sebagai respons pertama untuk tenggat.", "Anda dapat mengubah prioritas, subjek, dan lokasi lewat **Ubah**, dan melampirkan file selama tiket terbuka."] },
  },
  {
    id: "bulk-actions", persona: "work",
    title: { en: "Change many tickets at once", id: "Mengubah banyak tiket sekaligus" },
    summary: { en: "Select rows in the queue and apply one change to all of them.", id: "Pilih baris di antrean dan terapkan satu perubahan untuk semuanya." },
    steps: {
      en: ["In **Ticket Queue** tick the tickets (up to 100).", "A bar appears: assign, set priority, start work, put on hold or cancel.", "Confirm. Each ticket is checked on its own; those that cannot take the change are skipped and listed afterwards with the reason."],
      id: ["Di **Antrean Tiket** centang tiketnya (maksimal 100).", "Sebuah bilah muncul: tugaskan, atur prioritas, mulai kerjakan, tunda, atau batalkan.", "Konfirmasi. Setiap tiket diperiksa sendiri-sendiri; yang tidak dapat menerima perubahan dilewati dan dicantumkan kemudian beserta alasannya."],
    },
    tips: { en: ["Assigning needs the permission to assign; changing priority needs the permission to edit tickets."], id: ["Menugaskan memerlukan izin menugaskan; mengubah prioritas memerlukan izin mengubah tiket."] },
  },
  {
    id: "service-levels", persona: "work",
    title: { en: "Deadlines (service levels)", id: "Tenggat (tingkat layanan)" },
    summary: { en: "What “On track”, “At risk” and “Breached” mean.", id: "Arti “Sesuai target”, “Berisiko”, dan “Terlampaui”." },
    steps: {
      en: ["Every ticket gets a first-response deadline and a resolution deadline from the SLA policy that matches it. Only working hours of the policy's calendar count.", "**On track** is fine; **At risk** means a set share of the time is used up (you and supervisors are told); **Breached** means a deadline was missed. **Paused** is shown while the ticket is on hold; **Met** once it was resolved in time.", "Use the queue's deadline filter and “Deadline (soonest first)” sort to see what needs you now."],
      id: ["Setiap tiket mendapat tenggat respons pertama dan tenggat penyelesaian dari kebijakan SLA yang cocok. Hanya jam kerja pada kalender kebijakan yang dihitung.", "**Sesuai target** artinya aman; **Berisiko** berarti sebagian waktu yang ditetapkan sudah terpakai (Anda dan supervisor diberi tahu); **Terlampaui** berarti tenggat terlewat. **Dijeda** tampil saat tiket ditunda; **Terpenuhi** setelah selesai tepat waktu.", "Gunakan filter tenggat dan urutan “Tenggat (terdekat dulu)” di antrean untuk melihat apa yang perlu Anda tangani sekarang."],
    },
    tips: { en: ["A breach is a permanent fact on the ticket, even if it is resolved later."], id: ["Pelanggaran tenggat adalah catatan permanen pada tiket, meskipun diselesaikan kemudian."] },
  },
  {
    id: "approvals-and-cover", persona: "work",
    title: { en: "Approving, and being away", id: "Menyetujui, dan saat tidak di kantor" },
    summary: { en: "Decide requests waiting for you, and name a cover when you are out of office.", id: "Putuskan permintaan yang menunggu Anda, dan tunjuk pengganti saat tidak di kantor." },
    steps: {
      en: ["Open **Approvals**. Each request waiting for you shows what it is; approve or reject it (a reason is asked when rejecting). The e-mail link works too.", "Going away? In **Out of office** on the same page choose who covers for you and until when. Approvals that would go to you go to them instead, marked as being for you.", "Cannot decide one? **Hand over** that approval to someone else."],
      id: ["Buka **Persetujuan**. Setiap permintaan yang menunggu Anda tampil di sana; setujui atau tolak (alasan diminta saat menolak). Tautan di email juga dapat dipakai.", "Akan pergi? Di **Tidak di kantor** pada halaman yang sama pilih siapa yang menggantikan dan sampai kapan. Persetujuan yang seharusnya ke Anda diteruskan kepada mereka, ditandai sebagai untuk Anda.", "Tidak bisa memutuskan satu permintaan? **Alihkan** persetujuan itu kepada orang lain."],
    },
    go: { href: "/approvals", label: { en: "Approvals", id: "Persetujuan" } },
  },
  {
    id: "reports", persona: "work",
    title: { en: "Reports", id: "Laporan" },
    summary: { en: "See the load on the team and how well deadlines are met.", id: "Lihat beban tim dan seberapa baik tenggat terpenuhi." },
    steps: {
      en: ["**Operations** shows what is open, what needs attention, tickets per day and by status, priority and team. Choose the period at the top (up to 90 days).", "**SLA Report** shows how many tickets met their deadlines by priority, team and service, the average first response and resolution time, and the list of breaches."],
      id: ["**Operasional** menampilkan apa yang terbuka, apa yang perlu perhatian, tiket per hari dan menurut status, prioritas, dan tim. Pilih periodenya di atas (hingga 90 hari).", "**Laporan SLA** menampilkan berapa banyak tiket yang memenuhi tenggat menurut prioritas, tim, dan layanan, rata-rata waktu respons pertama dan penyelesaian, serta daftar pelanggaran."],
    },
    tips: { en: ["You only see numbers for tickets you may see in the queue."], id: ["Anda hanya melihat angka untuk tiket yang boleh Anda lihat di antrean."] },
    go: { href: "/reports", label: { en: "Operations", id: "Operasional" } },
  },

  // ---------------------------------------------------------------- people who set up the helpdesk
  {
    id: "go-live", persona: "run",
    title: { en: "Going live: the checklist", id: "Mulai beroperasi: daftar periksa" },
    summary: { en: "Everything needed for a working helpdesk, in one place.", id: "Semua yang diperlukan untuk helpdesk yang berjalan, di satu tempat." },
    steps: {
      en: ["Open **Helpdesk Setup → Get started**. It lists the required steps (a site, working hours, a team with people, a published service, deadlines, a routing rule) and advice (approvals, virus scanning, a test request).", "Use **Quick start** to fill in what is missing with sensible defaults: a first site, a first team with the people you choose, four starter deadline policies and a rule that sends every request to that team. It never changes what you already set up.", "Invite the people first and give them the **GA Agent** role; then file a test request from a normal account and follow it through the queue before you tell everyone."],
      id: ["Buka **Pengaturan Helpdesk → Mulai**. Halaman ini mendaftar langkah wajib (sebuah lokasi, jam kerja, tim berisi orang, layanan terbit, tenggat, aturan penugasan) dan saran (persetujuan, pemindaian virus, permintaan uji).", "Gunakan **Mulai cepat** untuk mengisi yang kurang dengan nilai bawaan yang wajar: lokasi pertama, tim pertama berisi orang yang Anda pilih, empat kebijakan tenggat awal, dan aturan yang mengarahkan semua permintaan ke tim itu. Ini tidak pernah mengubah yang sudah Anda atur.", "Undang orangnya lebih dulu dan beri peran **GA Agent**; lalu ajukan permintaan uji dari akun biasa dan ikuti lewat antrean sebelum Anda memberi tahu semua orang."],
    },
    go: { href: "/setup/start", label: { en: "Get started", id: "Mulai" } },
  },
  {
    id: "places-teams-hours", persona: "run",
    title: { en: "Places, teams and working hours", id: "Lokasi, tim, dan jam kerja" },
    summary: { en: "Where people are, who works on what, and when the clock runs.", id: "Di mana orang berada, siapa mengerjakan apa, dan kapan jam berjalan." },
    steps: {
      en: ["**Locations**: build the tree (site → building → floor → room). Requests can point to a place.", "**Teams**: create a team, add its members and choose a lead. People are added to a team here; the role decides what they may do.", "**Business Calendars**: set working days and hours and add holidays. Deadlines only count working time of the calendar a policy uses; one calendar is the default."],
      id: ["**Lokasi**: susun pohonnya (situs → gedung → lantai → ruangan). Permintaan dapat menunjuk sebuah lokasi.", "**Tim**: buat tim, tambahkan anggota, dan pilih ketua. Orang ditambahkan ke tim di sini; peran menentukan apa yang boleh mereka lakukan.", "**Kalender Kerja**: atur hari dan jam kerja serta tambahkan hari libur. Tenggat hanya menghitung waktu kerja pada kalender yang dipakai kebijakan; satu kalender menjadi bawaan."],
    },
    go: { href: "/setup/teams", label: { en: "Teams", id: "Tim" } },
  },
  {
    id: "catalog-and-forms", persona: "run",
    title: { en: "The service catalog and forms", id: "Katalog layanan dan formulir" },
    summary: { en: "Decide what people can ask for and what you ask them.", id: "Tentukan apa yang dapat diminta orang dan apa yang Anda tanyakan." },
    steps: {
      en: ["Open **Service Catalog**. Make categories, then services; give a service an owning team, an audience (everyone or staff only) and a description.", "Edit its **form**: add questions (text, number, date, choice, yes/no, place, person, file). Required questions, hints and conditions that show a question only after an earlier answer are supported. Every change makes a new form version; tickets keep the version they were made with.", "**Publish** the service when it is ready; archive it to retire it."],
      id: ["Buka **Katalog Layanan**. Buat kategori, lalu layanan; beri layanan tim pemilik, audiens (semua orang atau hanya staf), dan deskripsi.", "Ubah **formulirnya**: tambahkan pertanyaan (teks, angka, tanggal, pilihan, ya/tidak, lokasi, orang, file). Pertanyaan wajib, petunjuk, dan kondisi yang menampilkan pertanyaan hanya setelah jawaban sebelumnya didukung. Setiap perubahan membuat versi formulir baru; tiket menyimpan versi saat dibuat.", "**Terbitkan** layanan saat sudah siap; arsipkan untuk menghentikannya."],
    },
    go: { href: "/setup/catalog", label: { en: "Service Catalog", id: "Katalog Layanan" } },
  },
  {
    id: "deadlines-and-routing", persona: "run",
    title: { en: "Deadlines and who gets new tickets", id: "Tenggat dan siapa yang menerima tiket baru" },
    summary: { en: "SLA policies set the targets; assignment rules decide where tickets go.", id: "Kebijakan SLA menetapkan target; aturan penugasan menentukan ke mana tiket pergi." },
    steps: {
      en: ["**SLA Policies**: each policy sets first-response and resolution targets and two warning levels. A policy can be limited to a service, category, place or priority. The **first matching policy in the list wins**, so put specific ones above general ones; use the test box to see which one applies.", "**Assignment Rules**: the first matching rule decides the team and how a person is chosen (team queue only, round robin, or least loaded). Without a matching rule new tickets wait unassigned.", "Editing a policy never moves the deadlines of tickets that already exist."],
      id: ["**Kebijakan SLA**: setiap kebijakan menetapkan target respons pertama dan penyelesaian serta dua tingkat peringatan. Kebijakan dapat dibatasi pada layanan, kategori, lokasi, atau prioritas. **Kebijakan pertama yang cocok dalam daftar yang berlaku**, jadi letakkan yang spesifik di atas yang umum; gunakan kotak uji untuk melihat mana yang berlaku.", "**Aturan Penugasan**: aturan pertama yang cocok menentukan tim dan cara memilih orangnya (hanya antrean tim, bergiliran, atau beban paling sedikit). Tanpa aturan yang cocok, tiket baru menunggu tanpa penugas.", "Mengubah kebijakan tidak pernah menggeser tenggat tiket yang sudah ada."],
    },
    go: { href: "/setup/sla", label: { en: "SLA Policies", id: "Kebijakan SLA" } },
  },
  {
    id: "approval-policies", persona: "run",
    title: { en: "Approval policies", id: "Kebijakan persetujuan" },
    summary: { en: "Require a decision before the team starts, for services that spend money or need authorisation.", id: "Wajibkan keputusan sebelum tim mulai, untuk layanan yang mengeluarkan uang atau perlu otorisasi." },
    steps: {
      en: ["Open **Approval Policies** and choose the service.", "Add steps in order. Each step names people, roles and/or “the requester's manager”, and a rule: any one of them, or all of them.", "Set who covers if nobody can approve. Save. New requests for the service now wait for approval; existing tickets are not changed."],
      id: ["Buka **Kebijakan Persetujuan** dan pilih layanannya.", "Tambahkan langkah berurutan. Setiap langkah menyebut orang, peran, dan/atau “atasan pemohon”, serta aturan: salah satu dari mereka, atau semuanya.", "Tentukan siapa yang menggantikan bila tidak ada yang bisa menyetujui. Simpan. Permintaan baru untuk layanan itu kini menunggu persetujuan; tiket yang sudah ada tidak berubah."],
    },
    tips: { en: ["The requester's manager comes from the “Manager” field on the person's page under Users."], id: ["Atasan pemohon diambil dari isian “Manager” pada halaman orang tersebut di Pengguna."] },
    go: { href: "/setup/approvals", label: { en: "Approval Policies", id: "Kebijakan Persetujuan" } },
  },
  {
    id: "people-and-roles", persona: "run",
    title: { en: "People and roles", id: "Orang dan peran" },
    summary: { en: "Invite people and give them only what they need.", id: "Undang orang dan beri mereka hanya yang mereka perlukan." },
    steps: {
      en: ["Under **Users → Invitations** invite by e-mail and choose a role; the person sets their own password.", "Everyday roles: **User** (asks for things, needs no permission), **GA Agent** (works tickets of their teams), **GA Supervisor** (also sees all tickets and assigns), **GA Manager** (also runs the helpdesk's configuration). **Super Admin** can do everything.", "Add agents to a team under **Teams** so they see its tickets. Set a person's manager on their page if approvals use “the requester's manager”."],
      id: ["Di **Pengguna → Undangan** undang lewat email dan pilih peran; orangnya menetapkan sendiri password-nya.", "Peran sehari-hari: **User** (meminta bantuan, tanpa izin khusus), **GA Agent** (mengerjakan tiket timnya), **GA Supervisor** (juga melihat semua tiket dan menugaskan), **GA Manager** (juga mengelola konfigurasi helpdesk). **Super Admin** dapat melakukan semuanya.", "Tambahkan agen ke tim di **Tim** agar mereka melihat tiketnya. Atur atasan seseorang pada halamannya bila persetujuan memakai “atasan pemohon”."],
    },
    tips: { en: ["Roles that were tuned for your organisation are never overwritten by an upgrade."], id: ["Peran yang sudah Anda sesuaikan untuk organisasi tidak pernah ditimpa oleh pembaruan."] },
    go: { href: "/users/invitations", label: { en: "Invitations", id: "Undangan" } },
  },
  {
    id: "keeping-data", persona: "run",
    title: { en: "How long data is kept, and legal holds", id: "Berapa lama data disimpan, dan penahanan hukum" },
    summary: { en: "Set retention for finished tickets, files and notifications, and protect what you must keep.", id: "Atur retensi tiket selesai, file, dan notifikasi, dan lindungi yang wajib disimpan." },
    steps: {
      en: ["Open **Data Retention** (needs the retention permission). Nothing is ever removed until you switch the policy on.", "Choose how many days after a ticket finished to act, and whether to keep it as a statistic (what people wrote is removed) or delete it completely; optionally shorter periods for files and for notifications.", "Use **Preview** to see what a run would remove, then **Save policy**. The clean-up then runs every day; **Run now** does it at once.", "To protect a ticket, open it and choose **Place legal hold** with a reason; held tickets and their files are never touched until you release the hold."],
      id: ["Buka **Retensi Data** (memerlukan izin retensi). Tidak ada yang dihapus sebelum Anda mengaktifkan kebijakannya.", "Pilih berapa hari setelah tiket selesai untuk bertindak, dan apakah disimpan sebagai statistik (tulisan orang dihapus) atau dihapus seluruhnya; secara opsional periode lebih pendek untuk file dan notifikasi.", "Gunakan **Pratinjau** untuk melihat apa yang akan dihapus, lalu **Simpan kebijakan**. Pembersihan kemudian berjalan setiap hari; **Jalankan sekarang** melakukannya seketika.", "Untuk melindungi sebuah tiket, buka tiketnya dan pilih **Pasang penahanan hukum** beserta alasan; tiket yang ditahan dan filenya tidak disentuh sampai penahanan dilepas."],
    },
    tips: { en: ["People can read what you decided under My Profile → Privacy.", "Audit and security logs follow the platform's own period and are not part of this policy."], id: ["Orang dapat membaca keputusan Anda di Profil Saya → Privasi.", "Log audit dan keamanan mengikuti periode platform sendiri dan bukan bagian dari kebijakan ini."] },
    go: { href: "/admin/retention", label: { en: "Data Retention", id: "Retensi Data" } },
  },
  {
    id: "files-and-backups", persona: "run",
    title: { en: "Files, backups and restoring", id: "File, backup, dan pemulihan" },
    summary: { en: "Make sure a restored system has its attachments.", id: "Pastikan sistem yang dipulihkan memiliki lampirannya." },
    steps: {
      en: ["Each database backup is paired with an archive of the files volume taken right after it. Keep both together, off the server too.", "After restoring both, open **Data Retention → Stored files → Check stored files**. It lists files missing from the volume (with their tickets) and bytes nothing refers to; **Remove unreferenced files** clears the latter.", "Rehearse a restore before you rely on it: your administrator can run the restore drill script (see the runbook)."],
      id: ["Setiap backup database dipasangkan dengan arsip volume file yang diambil tepat setelahnya. Simpan keduanya bersama, juga di luar server.", "Setelah memulihkan keduanya, buka **Retensi Data → File tersimpan → Periksa file tersimpan**. Ini mendaftar file yang hilang dari volume (beserta tiketnya) dan byte yang tidak dirujuk; **Hapus file yang tidak terpakai** membersihkan yang terakhir.", "Latih pemulihan sebelum mengandalkannya: administrator Anda dapat menjalankan script restore drill (lihat runbook)."],
    },
    go: { href: "/admin/retention", label: { en: "Data Retention", id: "Retensi Data" } },
  },
];
