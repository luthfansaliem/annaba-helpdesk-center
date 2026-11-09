using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

public sealed record PendingApprovals(int AccessRequests, int ReviewItems);

/// <summary>
/// Tells the people who decide (those who may assign roles) that something is waiting for them: e-mail when a request or review arrives,
/// and a count for the sidebar badge. Items the caller may not decide themselves (their own request or their own access) are not counted.
/// E-mail is best-effort and goes through the outbox, so a mail problem never fails the request that triggered it.
/// </summary>
public sealed class ApprovalService(IAppDb db, IEmailQueue email, IAppLinks links)
{
    public const int MaxRecipients = 25;

    public async Task<PendingApprovals> PendingAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var requests = await db.AccessRequests.CountAsync(r => r.Status == AccessRequestStatus.Pending && r.RequesterId != actor.UserId, ct);
        var items = await db.AccessReviewItems.CountAsync(i => i.Decision == ReviewDecision.Pending && i.Review!.Status == AccessReviewStatus.Open && i.UserId != actor.UserId, ct);
        return new PendingApprovals(requests, items);
    }

    public async Task NotifyRequestSubmittedAsync(AccessRequestItem req, Guid requesterId, CancellationToken ct = default)
    {
        foreach (var a in await ApproversAsync(requesterId, ct))
            email.Enqueue(EmailTemplates.AccessRequestSubmitted(a.Email, a.Name, req.RequesterName, req.Role.Name, req.Reason, Link("/access-requests")));
    }

    public async Task NotifyReviewStartedAsync(string reviewName, DateTimeOffset dueAt, Guid startedBy, CancellationToken ct = default)
    {
        foreach (var a in await ApproversAsync(startedBy, ct))
            email.Enqueue(EmailTemplates.AccessReviewStarted(a.Email, a.Name, reviewName, dueAt, Link("/access-reviews")));
    }

    public async Task NotifyClosureRequestedAsync(string personName, Guid requesterId, CancellationToken ct = default)
    {
        foreach (var a in await ApproversAsync(requesterId, ct, Permissions.UsersSuspend))
            email.Enqueue(EmailTemplates.ClosureRequested(a.Email, a.Name, personName, Link("/account-closures")));
    }

    public void NotifyRequestDecided(string toEmail, string toName, string roleName, bool approved, string? note)
        => email.Enqueue(EmailTemplates.AccessRequestDecided(toEmail, toName, approved, roleName, note, Link("/access-requests")));

    private string Link(string path) => links.Origin.TrimEnd('/') + path;

    private async Task<List<Recipient>> ApproversAsync(Guid except, CancellationToken ct, string permission = Permissions.UsersAssignRole)
        => await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId != except && ur.User!.Status == UserStatus.Active && ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == permission))
            .Select(ur => new Recipient(ur.User!.Name, ur.User.Email)).Distinct().Take(MaxRecipients).ToListAsync(ct);

    private sealed record Recipient(string Name, string Email);
}
