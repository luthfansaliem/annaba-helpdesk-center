using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Profile;

/// <summary>
/// "Download my data": everything the system holds about the signed-in person, in one JSON file. It needs a fresh password check (it is a copy of their identity),
/// is recorded in the audit log, and never contains secrets (password hash, MFA seed, recovery codes, tokens) or anyone else's data.
/// </summary>
public sealed class AccountExportService(IAppDb db, IClock clock, SettingsProvider settings, AuditWriter audit)
{
    public const int MaxSessions = 200, MaxActivity = 1000, MaxTickets = 1000, MaxComments = 2000, MaxFiles = 1000;

    private static System.Text.Json.JsonElement? Answers(string json)
    {
        try { return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone(); } catch (System.Text.Json.JsonException) { return null; }
    }

    public async Task<object> ExportAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(who, await settings.GetAsync(ct), clock);
        var id = who.UserId;
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).FirstAsync(x => x.Id == id, ct);
        var org = await db.Tenants.AsNoTracking().Where(t => t.Id == u.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);
        var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserId == id).Select(r => r.Role!.Name).OrderBy(n => n).ToListAsync(ct);
        var hasMfa = await db.MfaFactors.AnyAsync(f => f.UserId == id && f.ConfirmedAt != null, ct);
        var cred = await db.Credentials.AsNoTracking().Where(c => c.UserId == id).Select(c => (DateTimeOffset?)c.PasswordChangedAt).FirstOrDefaultAsync(ct);
        var sso = await db.UserIdentities.AsNoTracking().Where(i => i.UserId == id).Select(i => i.Issuer).ToListAsync(ct);
        var sessions = await db.Sessions.AsNoTracking().Where(s => s.UserId == id).OrderByDescending(s => s.CreatedAt).Take(MaxSessions)
            .Select(s => new { s.DeviceLabel, s.BrowserLabel, s.Ip, s.CreatedAt, s.LastActiveAt, s.RevokedAt, s.RevokedReason }).ToListAsync(ct);
        var activity = await db.AuditEvents.AsNoTracking().Where(a => a.ActorId == id).OrderByDescending(a => a.Ts).Take(MaxActivity)
            .Select(a => new { At = a.Ts, a.Action, Outcome = a.Outcome.ToString(), a.TargetType, a.TargetId, a.Ip }).ToListAsync(ct);
        var requests = await db.AccessRequests.AsNoTracking().Where(r => r.RequesterId == id).OrderByDescending(r => r.CreatedAt)
            .Select(r => new { Role = r.Role!.Name, r.Reason, Status = r.Status.ToString(), r.CreatedAt, r.DecidedAt }).ToListAsync(ct);
        var closures = await db.AccountClosureRequests.AsNoTracking().Where(r => r.UserId == id).OrderByDescending(r => r.RequestedAt)
            .Select(r => new { Status = r.Status.ToString(), r.Reason, r.RequestedAt, r.ExecuteAfter, r.CompletedAt }).ToListAsync(ct);

        // the requests the person made, what they wrote on tickets (public comments only: internal notes are staff's, not theirs to take), and the files they attached
        var tickets = await db.Tickets.AsNoTracking().Where(t => t.RequesterId == id).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(MaxTickets)
            .Select(t => new { t.Number, t.ServiceName, t.Subject, t.Description, t.AnswersJson, Status = t.Status.ToString(), Priority = t.Priority.ToString(), t.CreatedAt, t.ResolvedAt, t.ClosedAt, t.ResolutionNote, t.CancelReason, t.AnonymisedAt }).ToListAsync(ct);
        var ticketCount = await db.Tickets.CountAsync(t => t.RequesterId == id, ct);
        var comments = await db.TicketEvents.AsNoTracking().Where(e => e.ActorId == id && e.Kind == TicketEventKind.Comment && !e.Internal).OrderByDescending(e => e.CreatedAt).Take(MaxComments)
            .Select(e => new { Ticket = db.Tickets.Where(t => t.Id == e.TicketId).Select(t => t.Number).FirstOrDefault(), e.CreatedAt, e.Body }).ToListAsync(ct);
        var files = await db.Attachments.AsNoTracking().Where(a => a.UploadedById == id && a.DeletedAt == null).OrderByDescending(a => a.CreatedAt).Take(MaxFiles)
            .Select(a => new { Ticket = db.Tickets.Where(t => t.Id == a.TicketId).Select(t => t.Number).FirstOrDefault(), a.FileName, a.ContentType, a.Size, a.CreatedAt }).ToListAsync(ct);

        audit.Record("account.export", "user", id.ToString(), Outcome.Success, client, id, who.Email, new Dictionary<string, string> { ["sessions"] = sessions.Count.ToString(), ["activity"] = activity.Count.ToString(), ["tickets"] = tickets.Count.ToString(), ["comments"] = comments.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return new
        {
            exportedAt = clock.UtcNow,
            note = "Everything Annaba Digital holds about you, without secrets (password hash, authenticator seed, recovery codes, tokens) and without other people's data. Security logs about your activity are kept for your organisation's retention period.",
            account = new { u.Id, u.Email, u.Name, Status = u.Status.ToString(), u.CreatedAt, u.LastLoginAt, u.EmailVerifiedAt, organisation = org },
            profile = u.Profile is null ? null : new { u.Profile.Phone, u.Profile.Department, u.Profile.JobTitle, u.Profile.Language, u.Profile.Timezone, u.Profile.NotifyEmail, u.Profile.NotifySecurity, u.Profile.NotifyProduct, u.Profile.AutoLockMinutes },
            roles,
            security = new { twoStepVerification = hasMfa, passwordChangedAt = cred, singleSignOnProviders = sso },
            sessions, activity, accessRequests = requests, closureRequests = closures,
            helpdesk = new
            {
                requestsTotal = ticketCount, requestsIncluded = tickets.Count,
                requests = tickets.Select(t => new { t.Number, service = t.ServiceName, t.Subject, t.Description, answers = Answers(t.AnswersJson), t.Status, t.Priority, t.CreatedAt, t.ResolvedAt, t.ClosedAt, t.ResolutionNote, t.CancelReason, contentRemovedAt = t.AnonymisedAt }),
                comments, files,
            },
        };
    }
}
