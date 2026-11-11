namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// NTF-003. An organisation's set-up of one outside channel (WhatsApp Cloud API, Slack, Microsoft Teams). Public settings (the WhatsApp phone number id and
/// template names) are plain JSON; credentials (access token, bot token) are encrypted with the app's secret key and never shown again.
/// </summary>
public class ChannelSetting : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary><c>whatsapp</c>, <c>slack</c> or <c>teams</c>.</summary>
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public byte[]? SecretCiphertext { get; set; }
    public byte[]? SecretNonce { get; set; }
    public int SecretKeyVersion { get; set; }
    public Guid UpdatedById { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A person's own address on a channel (opt-in): their WhatsApp number (verified with a code), Slack member id (found by their e-mail) or Teams
/// Workflows URL. The address is personal data and encrypted; <see cref="Hint"/> is what the screens show ("+62 •••• 7890").</summary>
public class UserChannel : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public byte[] AddressCiphertext { get; set; } = [];
    public byte[] AddressNonce { get; set; } = [];
    public int AddressKeyVersion { get; set; }
    public string Hint { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Send notifications here <em>instead of</em> e-mail; when every such channel fails, the e-mail goes after all.</summary>
    public bool ReplaceEmail { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? CodeHash { get; set; }
    public DateTimeOffset? CodeExpiresAt { get; set; }
    public int CodeAttempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The channel outbox: one message to one person on one channel, retried with back-off. Deliveries of the same notification share <see cref="GroupId"/>
/// (for the e-mail fallback). Title and body are cleared once the message is delivered or given up.</summary>
public class ChannelDelivery : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public string Channel { get; set; } = "";
    /// <summary><c>notify</c>, <c>verify</c> (a WhatsApp code) or <c>test</c>.</summary>
    public string Purpose { get; set; } = "notify";
    public string? NotificationKind { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? Link { get; set; }
    public bool ReplacesEmail { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? FellBackAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class ChannelRules
{
    public const string WhatsApp = "whatsapp", Slack = "slack", Teams = "teams";
    public static readonly string[] Kinds = [WhatsApp, Slack, Teams];
    public const int MaxAttempts = 5, CodeMinutes = 10, MaxCodeAttempts = 5, KeepDays = 30;
    public static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(3600, 30 * Math.Pow(3, Math.Max(0, attempt - 1))));
    /// <summary>Microsoft Teams "Workflows" (Power Automate) and legacy connector webhook hosts a person may paste.</summary>
    public static readonly string[] TeamsHosts = [".logic.azure.com", ".powerplatform.com", ".powerautomate.com", ".webhook.office.com"];

    /// <summary>E.164 from what people type: "+62 812-3456-7890", "0812 3456 7890" (Indonesia) → "+6281234567890". Null when it is not a phone number.</summary>
    public static string? Phone(string? raw)
    {
        var s = new string((raw ?? "").Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (s.StartsWith("00")) s = "+" + s[2..];
        else if (s.StartsWith('0')) s = "+62" + s[1..];
        else if (!s.StartsWith('+')) s = "+" + s;
        var digits = s[1..];
        return digits.Length is >= 8 and <= 15 && digits.All(char.IsDigit) && digits[0] != '0' ? s : null;
    }
    public static string PhoneHint(string e164) => e164.Length <= 7 ? e164 : $"{e164[..3]} •••• {e164[^4..]}";
}
