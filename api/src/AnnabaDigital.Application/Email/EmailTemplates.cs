using System.Net;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Application.Email;

/// <summary>
/// Plain, escaped templates in English and Bahasa Indonesia. Emails never contain passwords or session data — only a one-time link.
/// Every template carries both wordings; the outbox picks one when the mail is queued (<see cref="EmailMessage.Localize"/>): the recipient's own
/// profile language, else the sender's hint (an invitation goes out in the inviter's language), else English.
/// </summary>
public static class EmailTemplates
{
    private static string E(string s) => WebUtility.HtmlEncode(s);

    private sealed record Copy(string Subject, string Intro, string? LinkLabel, string Outro);

    private static readonly string[] IdMonths = ["Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des"];
    private static string IdDate(DateTimeOffset d) => $"{d.Day} {IdMonths[d.Month - 1]} {d.Year}";
    private static string EnDate(DateTimeOffset d) => d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static EmailMessage Render(string to, string? name, string? link, Copy c, bool id)
    {
        var hi = name is null ? (id ? "Halo," : "Hi there,") : (id ? $"Halo {name}," : $"Hi {name},");
        var open = c.LinkLabel ?? (id ? "Buka tautan" : "Open link");
        var copyLink = id ? "Atau salin tautan ini:" : "Or copy this link:";
        var text = $"{hi}\n\n{c.Intro}\n\n{(link is null ? "" : $"{c.LinkLabel}: {link}\n\n")}{c.Outro}\n\n— Annaba Digital";
        var html = $"""
            <div style="font-family:Inter,Arial,sans-serif;max-width:520px;margin:auto;color:#1c2a4a">
              <h2 style="color:#0e1b4d">{E(c.Subject)}</h2>
              <p>{E(hi)}</p><p>{E(c.Intro)}</p>
              {(link is null ? "" : $"<p><a href=\"{E(link)}\" style=\"display:inline-block;background:#0b62e6;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none\">{E(open)}</a></p><p style=\"font-size:12px;color:#5d6b88\">{copyLink} {E(link)}</p>")}
              <p style="color:#5d6b88">{E(c.Outro)}</p>
            </div>
            """;
        return new EmailMessage(to, c.Subject, text, html);
    }

    /// <summary>The English message, plus a way to get the Indonesian one.</summary>
    private static EmailMessage Bi(string to, string? name, string? link, Copy en, Copy id)
        => Render(to, name, link, en, false) with { Localize = lang => lang == "id" ? Render(to, name, link, id, true) : null };

    public static EmailMessage VerifyEmail(string to, string name, string link) =>
        Bi(to, name, link,
            new("Verify your email", "Confirm your email address to activate your Annaba Digital account. This link works once and expires in 24 hours.", "Verify email", "If you didn't create an account, you can ignore this email."),
            new("Verifikasi email Anda", "Konfirmasi alamat email Anda untuk mengaktifkan akun Annaba Digital. Tautan ini hanya berlaku sekali dan kedaluwarsa dalam 24 jam.", "Verifikasi email", "Jika Anda tidak membuat akun, abaikan email ini."));

    public static EmailMessage AlreadyRegistered(string to, string name, string resetLink) =>
        Bi(to, name, resetLink,
            new("You already have an account", "Someone tried to register with this email address, but an account already exists.", "Reset your password", "If this was you, sign in or reset your password. If not, no action is needed."),
            new("Anda sudah memiliki akun", "Seseorang mencoba mendaftar dengan alamat email ini, tetapi akunnya sudah ada.", "Atur ulang password Anda", "Jika itu Anda, silakan masuk atau atur ulang password. Jika bukan, tidak perlu tindakan apa pun."));

    public static EmailMessage ResetPassword(string to, string name, string link) =>
        Bi(to, name, link,
            new("Reset your password", "We received a request to reset your password. This link works once and expires in 30 minutes.", "Choose a new password", "If you didn't ask for this, ignore this email — your password won't change."),
            new("Atur ulang password Anda", "Kami menerima permintaan untuk mengatur ulang password Anda. Tautan ini hanya berlaku sekali dan kedaluwarsa dalam 30 menit.", "Pilih password baru", "Jika Anda tidak memintanya, abaikan email ini — password Anda tidak akan berubah."));

    public static EmailMessage PasswordChanged(string to, string name) =>
        Bi(to, name, null,
            new("Your password was changed", "The password for your Annaba Digital account was just changed and all other sessions were signed out.", null, "If this wasn't you, contact your administrator immediately."),
            new("Password Anda diubah", "Password akun Annaba Digital Anda baru saja diubah dan semua sesi lainnya telah dikeluarkan.", null, "Jika ini bukan Anda, segera hubungi administrator Anda."));

    public static EmailMessage MfaEnabled(string to, string name) =>
        Bi(to, name, null,
            new("Two-step verification turned on", "Two-step verification was enabled for your Annaba Digital account. You'll need your authenticator app (or a recovery code) when you sign in.", null, "If this wasn't you, contact your administrator immediately."),
            new("Verifikasi dua langkah diaktifkan", "Verifikasi dua langkah diaktifkan untuk akun Annaba Digital Anda. Anda akan memerlukan aplikasi authenticator (atau kode pemulihan) saat masuk.", null, "Jika ini bukan Anda, segera hubungi administrator Anda."));

    public static EmailMessage MfaDisabled(string to, string name) =>
        Bi(to, name, null,
            new("Two-step verification turned off", "Two-step verification was disabled for your Annaba Digital account and your other sessions were signed out.", null, "If this wasn't you, contact your administrator immediately and reset your password."),
            new("Verifikasi dua langkah dimatikan", "Verifikasi dua langkah dinonaktifkan untuk akun Annaba Digital Anda dan sesi Anda yang lain telah dikeluarkan.", null, "Jika ini bukan Anda, segera hubungi administrator Anda dan atur ulang password Anda."));

    public static EmailMessage RecoveryCodeUsed(string to, string name, int remaining) =>
        Bi(to, name, null,
            new("A recovery code was used", $"A recovery code was just used to sign in to your account. You have {remaining} recovery code(s) left.", null, "If this wasn't you, change your password and regenerate your recovery codes."),
            new("Kode pemulihan digunakan", $"Sebuah kode pemulihan baru saja dipakai untuk masuk ke akun Anda. Anda memiliki {remaining} kode pemulihan tersisa.", null, "Jika ini bukan Anda, ubah password Anda dan buat ulang kode pemulihan."));

    public static EmailMessage ConfirmEmailChange(string to, string name, string link) =>
        Bi(to, name, link,
            new("Confirm your new email address", "Confirm this address to use it for your Annaba Digital account. This link works once and expires in 24 hours.", "Confirm new email", "If you didn't request this, ignore this email — nothing will change."),
            new("Konfirmasi alamat email baru Anda", "Konfirmasi alamat ini untuk dipakai pada akun Annaba Digital Anda. Tautan ini hanya berlaku sekali dan kedaluwarsa dalam 24 jam.", "Konfirmasi email baru", "Jika Anda tidak memintanya, abaikan email ini — tidak ada yang akan berubah."));

    public static EmailMessage EmailChangeRequested(string to, string name, string newEmail) =>
        Bi(to, name, null,
            new("Email change requested", $"A request was made to change your account email to {newEmail}. Nothing changes until that address is confirmed.", null, "If this wasn't you, change your password and tell your administrator."),
            new("Permintaan perubahan email", $"Ada permintaan untuk mengubah email akun Anda menjadi {newEmail}. Tidak ada yang berubah sampai alamat itu dikonfirmasi.", null, "Jika ini bukan Anda, ubah password Anda dan beri tahu administrator Anda."));

    public static EmailMessage EmailChanged(string to, string name, string newEmail) =>
        Bi(to, name, null,
            new("Your email address was changed", $"The email address for your Annaba Digital account is now {newEmail}.", null, "If this wasn't you, contact your administrator immediately."),
            new("Alamat email Anda diubah", $"Alamat email akun Annaba Digital Anda sekarang adalah {newEmail}.", null, "Jika ini bukan Anda, segera hubungi administrator Anda."));

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public static EmailMessage AccessRequestSubmitted(string to, string name, string requester, string role, string reason, string link) =>
        Bi(to, name, link,
            new("Access request waiting for your decision", $"{requester} asked for the \"{role}\" role. Reason: {Clip(reason, 300)}", "Review the request", "You receive this because you can assign roles. Requests from you are decided by another administrator."),
            new("Permintaan akses menunggu keputusan Anda", $"{requester} meminta peran \"{role}\". Alasan: {Clip(reason, 300)}", "Tinjau permintaan", "Anda menerima ini karena Anda dapat memberikan peran. Permintaan dari Anda diputuskan oleh administrator lain."));

    public static EmailMessage AccessRequestDecided(string to, string name, bool approved, string role, string? note, string link) =>
        Bi(to, name, link,
            new(approved ? "Your access request was approved" : "Your access request was rejected",
                $"Your request for the \"{role}\" role was {(approved ? "approved. The role is active now" : "rejected")}.{(string.IsNullOrWhiteSpace(note) ? "" : $" Note: {Clip(note, 300)}")}", "See your requests", "If you have questions, ask the administrator who decided."),
            new(approved ? "Permintaan akses Anda disetujui" : "Permintaan akses Anda ditolak",
                $"Permintaan Anda untuk peran \"{role}\" {(approved ? "disetujui. Peran itu sudah aktif sekarang" : "ditolak")}.{(string.IsNullOrWhiteSpace(note) ? "" : $" Catatan: {Clip(note, 300)}")}", "Lihat permintaan Anda", "Jika ada pertanyaan, tanyakan kepada administrator yang memutuskan."));

    public static EmailMessage AccessReviewStarted(string to, string name, string review, DateTimeOffset dueAt, string link) =>
        Bi(to, name, link,
            new("An access review needs your attention", $"The access review \"{Clip(review, 120)}\" was opened. Please keep or revoke each privileged role by {dueAt:yyyy-MM-dd}.", "Open the review", "You receive this because you can assign roles. You can't review your own access."),
            new("Tinjauan akses memerlukan perhatian Anda", $"Tinjauan akses \"{Clip(review, 120)}\" telah dibuka. Mohon pertahankan atau cabut setiap peran berhak sebelum {dueAt:yyyy-MM-dd}.", "Buka tinjauan", "Anda menerima ini karena Anda dapat memberikan peran. Anda tidak dapat meninjau akses Anda sendiri."));

    public static EmailMessage ClosureRequested(string to, string name, string person, string link) =>
        Bi(to, name, link,
            new("Account closure request waiting for your decision", $"{person} asked to close their account and have their personal data erased.", "Review the request", "You receive this because you can suspend accounts. Approving starts a 14-day waiting period in which they can still cancel."),
            new("Permintaan penutupan akun menunggu keputusan Anda", $"{person} meminta akunnya ditutup dan data pribadinya dihapus.", "Tinjau permintaan", "Anda menerima ini karena Anda dapat menangguhkan akun. Menyetujui memulai masa tunggu 14 hari di mana mereka masih bisa membatalkan."));

    public static EmailMessage ClosureApproved(string to, string name, DateTimeOffset when, string link) =>
        Bi(to, name, link,
            new("Your account closure was approved", $"Your request was approved. Your account will be closed and your personal data erased on {EnDate(when)}. Until then you can still sign in, and you can cancel at any time.", "Cancel the closure", "If you didn't expect this, cancel it now and change your password."),
            new("Penutupan akun Anda disetujui", $"Permintaan Anda disetujui. Akun Anda akan ditutup dan data pribadi Anda dihapus pada {IdDate(when)}. Sampai saat itu Anda masih bisa masuk, dan Anda dapat membatalkannya kapan saja.", "Batalkan penutupan", "Jika Anda tidak mengharapkan ini, batalkan sekarang dan ubah password Anda."));

    public static EmailMessage ClosureRejected(string to, string name, string? note) =>
        Bi(to, name, null,
            new("Your account closure request was declined", $"Your request to close your account was declined.{(string.IsNullOrWhiteSpace(note) ? "" : $" Note: {Clip(note, 300)}")}", null, "Your account stays as it is. Ask your administrator if you have questions."),
            new("Permintaan penutupan akun Anda ditolak", $"Permintaan Anda untuk menutup akun ditolak.{(string.IsNullOrWhiteSpace(note) ? "" : $" Catatan: {Clip(note, 300)}")}", null, "Akun Anda tetap seperti semula. Tanyakan kepada administrator Anda jika ada pertanyaan."));

    public static EmailMessage AccountClosed(string to, string name) =>
        Bi(to, name, null,
            new("Your account was closed", "Your Annaba Digital account was closed and your personal data was erased. Security logs are kept for the period required by your organisation's retention policy.", null, "Thank you for using Annaba Digital."),
            new("Akun Anda ditutup", "Akun Annaba Digital Anda telah ditutup dan data pribadi Anda dihapus. Log keamanan disimpan selama periode yang diwajibkan oleh kebijakan retensi organisasi Anda.", null, "Terima kasih telah menggunakan Annaba Digital."));

    public static EmailMessage NewSignIn(string to, string name, string device, string browser, string ip, string when, string link) =>
        Bi(to, name, link,
            new("New sign-in to your account", $"We noticed a sign-in from a device we haven't seen lately: {device}, {browser}, IP address {ip}, {when}.", "Review your account security", "If this was you, there's nothing to do. If it wasn't, change your password and sign out your other sessions right away. You can turn these alerts off under Profile → Notifications."),
            new("Login baru ke akun Anda", $"Kami melihat login dari perangkat yang belum terlihat belakangan ini: {device}, {browser}, alamat IP {ip}, {when}.", "Tinjau keamanan akun Anda", "Jika itu Anda, tidak ada yang perlu dilakukan. Jika bukan, segera ubah password dan keluarkan sesi Anda yang lain. Anda dapat mematikan peringatan ini di Profil → Notifikasi."));

    public static EmailMessage MfaResetByAdmin(string to, string name, string admin) =>
        Bi(to, name, null,
            new("Two-step verification was reset", $"{admin} reset two-step verification on your Annaba Digital account and signed you out everywhere. You can sign in with your password and set it up again.", null, "If you didn't ask for this, tell your administrator and change your password."),
            new("Verifikasi dua langkah diatur ulang", $"{admin} mengatur ulang verifikasi dua langkah pada akun Annaba Digital Anda dan mengeluarkan Anda dari semua sesi. Anda bisa masuk dengan password dan menyiapkannya lagi.", null, "Jika Anda tidak memintanya, beri tahu administrator Anda dan ubah password Anda."));

    public static EmailMessage PasswordResetRequiredByAdmin(string to, string name, string admin, string link) =>
        Bi(to, name, link,
            new("Choose a new password", $"{admin} asked you to choose a new password for your Annaba Digital account. You were signed out everywhere and can't sign in with the old password. This link works once and expires in 30 minutes.", "Choose a new password", "If the link has expired, use \"Forgot password\" on the sign-in page."),
            new("Pilih password baru", $"{admin} meminta Anda memilih password baru untuk akun Annaba Digital Anda. Anda dikeluarkan dari semua sesi dan tidak bisa masuk dengan password lama. Tautan ini hanya berlaku sekali dan kedaluwarsa dalam 30 menit.", "Pilih password baru", "Jika tautannya sudah kedaluwarsa, gunakan \"Lupa password\" di halaman masuk."));

    /// <summary>The invitee has no account yet, so there is no profile language: callers set <see cref="EmailMessage.Lang"/> to the inviter's language.</summary>
    public static EmailMessage Invitation(string to, string inviter, string link, int expiryDays) =>
        Bi(to, null, link,
            new("You're invited to Annaba Digital", $"{inviter} invited you to join Annaba Digital. This invitation works once and expires in {expiryDays} days.", "Accept invitation", "If you weren't expecting this, you can ignore it."),
            new("Anda diundang ke Annaba Digital", $"{inviter} mengundang Anda bergabung di Annaba Digital. Undangan ini hanya berlaku sekali dan kedaluwarsa dalam {expiryDays} hari.", "Terima undangan", "Jika Anda tidak mengharapkan ini, abaikan saja."));
}
