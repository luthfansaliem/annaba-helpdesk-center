using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Infrastructure.Security;

namespace AnnabaDigital.Api;

/// <summary>Refuses to boot in Production with insecure defaults (design: docs/10 T24).</summary>
public static class StartupChecks
{
    public static void Validate(IServiceProvider sp, IHostEnvironment env)
    {
        if (!env.IsProduction()) return;
        var api = sp.GetRequiredService<IOptions<ApiOptions>>().Value;
        var sec = sp.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var problems = new List<string>();
        if (!api.Origin.StartsWith("https://")) problems.Add("Api:Origin must be https.");
        if (string.IsNullOrWhiteSpace(api.ProxyKey) || api.ProxyKey.Length < 32) problems.Add("Api:ProxyKey must be set (>= 32 chars).");
        if (sec.CsrfKey.Length < 32 || sec.CsrfKey.StartsWith("dev-only")) problems.Add("Security:CsrfKey must be set to a strong secret.");
        if (string.IsNullOrWhiteSpace(sec.PasswordPepper) || sec.PasswordPepper.Length < 32) problems.Add("Security:PasswordPepper must be set (>= 32 chars).");
        if (!sec.EncryptionKeys.TryGetValue(sec.CurrentKeyVersion.ToString(), out var key) || key == SecurityOptions.DevEncryptionKey) problems.Add("Security:EncryptionKeys:<CurrentKeyVersion> must be a real 32-byte base64 key (the built-in development key is not allowed).");
        else { try { if (Convert.FromBase64String(key).Length != 32) problems.Add("Security:EncryptionKeys must be 32 bytes (base64)."); } catch (FormatException) { problems.Add("Security:EncryptionKeys must be valid base64."); } }
        var mail = sp.GetRequiredService<IOptions<AnnabaDigital.Infrastructure.Email.EmailOptions>>().Value;
        var provider = mail.Provider.Trim().ToLowerInvariant();
        if (provider == "resend") { if (string.IsNullOrWhiteSpace(mail.ResendApiKey)) problems.Add("Email:ResendApiKey must be set when Email:Provider is 'resend'."); }
        else if (provider == "smtp")
        {
            if (string.IsNullOrWhiteSpace(mail.SmtpHost)) problems.Add("Email:SmtpHost must be set when Email:Provider is 'smtp'.");
            if (mail.SmtpPort is < 1 or > 65535) problems.Add("Email:SmtpPort must be a valid port.");
            if (mail.SmtpSecurity.Trim().ToLowerInvariant() is not ("starttls" or "ssl")) problems.Add("Email:SmtpSecurity must be 'starttls' or 'ssl' in Production (plain SMTP would expose credentials and one-time links).");
            if (!string.IsNullOrEmpty(mail.SmtpUsername) && string.IsNullOrEmpty(mail.SmtpPassword)) problems.Add("Email:SmtpPassword must be set when Email:SmtpUsername is.");
        }
        else problems.Add("Email:Provider must be 'smtp' or 'resend' (the console sender does not deliver mail).");
        if (mail.LogBodies) problems.Add("Email:LogBodies must be false (it would write one-time links to the logs).");
        if (api.SeedDevUsers) problems.Add("Api:SeedDevUsers must be false in Production.");
        if (problems.Count > 0) throw new InvalidOperationException("Insecure production configuration: " + string.Join(" ", problems));
    }
}
