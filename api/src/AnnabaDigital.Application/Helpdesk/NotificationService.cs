using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record NotificationItem(Guid Id, string Kind, string Title, string? Body, string? Link, Guid? TicketId, DateTimeOffset CreatedAt, bool Read);
public sealed record UnreadCount(int Unread);
public sealed record NotificationPrefItem(string Kind, string Group, bool Locked, bool InApp, bool Email, bool DefaultEmail, bool Custom);
public sealed record NotificationPrefs(bool EmailMaster, IReadOnlyList<NotificationPrefItem> Items);
public sealed record NotificationPrefChange(string? Kind, bool? InApp, bool? Email);
public sealed record SaveNotificationPrefsRequest(NotificationPrefChange[]? Items);

/// <summary>
/// NTF-001/002. In-app notifications for the events people care about, with an e-mail copy for the few that need attention outside the app
/// (assigned to you, resolved, a reply, approval needed or decided, SLA breached). E-mail goes through the outbox in the recipient's language and
/// respects their "e-mail notifications" preference — except approval requests, which are actionable and always mailed. Nobody is notified about
/// their own action. Sending is part of the caller's unit of work: nothing is announced for a change that did not save.
/// </summary>
public sealed class NotificationService(IAppDb db, IClock clock, IEmailQueue email, IAppLinks links, AuditWriter audit, ChannelService channels)
{
    public const int MaxSupervisors = 10;
    private string Url(string path) => links.Origin.TrimEnd('/') + path;

    // ================================================================ reading (the bell and the list)
    public async Task<PagedResult<NotificationItem>> ListAsync(Guid userId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<NotificationItem>([.. rows.Select(n => new NotificationItem(n.Id, n.Kind, n.Title, n.Body, n.Link, n.TicketId, n.CreatedAt, n.ReadAt is not null))], page, pageSize, total);
    }

    public async Task<UnreadCount> UnreadAsync(Guid userId, CancellationToken ct = default) => new(await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct));

    /// <summary>Only the owner can mark a notification read; anyone else's id looks like it doesn't exist.</summary>
    public async Task MarkReadAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct) ?? throw DomainException.NotFound("Notification");
        if (n.ReadAt is null) { n.ReadAt = clock.UtcNow; await db.SaveChangesAsync(ct); }
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
        => await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, clock.UtcNow), ct);

    // ================================================================ sending
    /// <summary>
    /// Adds the in-app notification and, when <paramref name="mail"/> is given, queues the e-mail (unless the person turned e-mail off and <paramref name="alwaysMail"/> is false).
    /// The caller saves.
    /// </summary>
    public async Task SendAsync(Guid userId, string kind, string title, string? body, string? link, Guid? ticketId, Func<string, string, EmailMessage>? mail = null, bool alwaysMail = false, CancellationToken ct = default)
    {
        var def = NotificationCatalog.Find(kind); var locked = def?.Locked == true;
        var pref = def is null ? null : await db.NotificationPreferences.AsNoTracking().Where(x => x.UserId == userId && x.Kind == kind).Select(x => new { x.InApp, x.Email }).FirstOrDefaultAsync(ct);
        if (locked || pref?.InApp != false) db.Notifications.Add(new Notification { UserId = userId, Kind = kind, Title = Clip(title, 200), Body = body is null ? null : Clip(body, 500), Link = link, TicketId = ticketId, CreatedAt = clock.UtcNow });
        // e-mail: what the person chose, else the usual (mailed when the event comes with its own message); the "e-mail notifications" switch and `alwaysMail` still apply
        var force = alwaysMail || locked;
        if (!force && !(pref?.Email ?? mail is not null)) return;
        var u = await db.Users.AsNoTracking().Where(x => x.Id == userId && x.Status == UserStatus.Active)
            .Select(x => new { x.Name, x.Email, Notify = db.Profiles.Where(p => p.UserId == x.Id).Select(p => (bool?)p.NotifyEmail).FirstOrDefault() }).FirstOrDefaultAsync(ct);
        if (u is null) return;
        // NTF-003: the person's channels get what would be e-mailed; one that replaces e-mail holds the e-mail back (it is sent if every such channel fails)
        if (await channels.QueueAsync(userId, kind, title, body, link, ct)) return;
        if (!force && u.Notify == false) return;
        email.Enqueue(mail is not null ? mail(u.Email, u.Name) : EmailTemplates.Notification(u.Email, u.Name, title, body, Url(link ?? "/notifications")));
    }

    // ================================================================ preferences (NTF-002)
    public async Task<NotificationPrefs> PreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.NotificationPreferences.AsNoTracking().Where(x => x.UserId == userId).ToDictionaryAsync(x => x.Kind, ct);
        var master = await db.Profiles.AsNoTracking().Where(x => x.UserId == userId).Select(x => (bool?)x.NotifyEmail).FirstOrDefaultAsync(ct) ?? true;
        return new NotificationPrefs(master, [.. NotificationCatalog.All.Select(k =>
        {
            rows.TryGetValue(k.Key, out var r);
            return new NotificationPrefItem(k.Key, k.Group, k.Locked, k.Locked || (r?.InApp ?? true), k.Locked || (r?.Email ?? k.DefaultEmail), k.DefaultEmail, !k.Locked && (r?.InApp != null || r?.Email != null));
        })]);
    }

    /// <summary>Saves the person's choices. Each item states both switches; a switch equal to the default is stored as "as usual". A locked kind cannot be switched off.</summary>
    public async Task<NotificationPrefs> SetPreferencesAsync(Guid userId, string who, SaveNotificationPrefsRequest req, ClientInfo client, CancellationToken ct = default)
    {
        var items = req.Items ?? []; var errors = new Dictionary<string, string>();
        if (items.Length is 0 or > 50) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["items"] = "Send between 1 and 50 choices." });
        for (var i = 0; i < items.Length; i++)
        {
            var def = NotificationCatalog.Find(items[i].Kind);
            if (def is null) errors[$"items[{i}].kind"] = "Unknown kind of notification.";
            else if (def.Locked && (items[i].InApp == false || items[i].Email == false)) errors[$"items[{i}].kind"] = "This notification cannot be switched off: work waits for your answer.";
            else if (items.Take(i).Any(x => x.Kind == items[i].Kind)) errors[$"items[{i}].kind"] = "Listed twice.";
        }
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var existing = await db.NotificationPreferences.Where(x => x.UserId == userId).ToDictionaryAsync(x => x.Kind, ct);
        var now = clock.UtcNow; var changed = 0;
        foreach (var it in items)
        {
            var def = NotificationCatalog.Find(it.Kind)!;
            if (def.Locked) continue;
            bool? inApp = it.InApp == true ? null : it.InApp;             // on is the default for the app
            bool? mail = it.Email is { } e && e != def.DefaultEmail ? e : null;
            existing.TryGetValue(def.Key, out var row);
            if (row is null && inApp is null && mail is null) continue;
            if (row is not null && row.InApp == inApp && row.Email == mail) continue;
            changed++;
            if (inApp is null && mail is null) { db.NotificationPreferences.Remove(row!); continue; }
            if (row is null) db.NotificationPreferences.Add(new NotificationPreference { UserId = userId, Kind = def.Key, InApp = inApp, Email = mail, UpdatedAt = now });
            else { row.InApp = inApp; row.Email = mail; row.UpdatedAt = now; }
        }
        if (changed > 0) { audit.Record("notification.prefs", "user", userId.ToString(), Outcome.Success, client, userId, who, new Dictionary<string, string> { ["changed"] = changed.ToString() }); await db.SaveChangesAsync(ct); }
        return await PreferencesAsync(userId, ct);
    }

    public async Task<NotificationPrefs> ResetPreferencesAsync(Guid userId, string who, ClientInfo client, CancellationToken ct = default)
    {
        var n = await db.NotificationPreferences.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        if (n > 0) { audit.Record("notification.prefs", "user", userId.ToString(), Outcome.Success, client, userId, who, new Dictionary<string, string> { ["reset"] = n.ToString() }); await db.SaveChangesAsync(ct); }
        return await PreferencesAsync(userId, ct);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
    private static string TicketPath(Ticket t) => $"/tickets/{t.Id}";

    /// <summary>People who supervise tickets (may assign them): who to tell when something is at risk. Capped so a large organisation is not flooded.</summary>
    private async Task<List<Guid>> SupervisorsAsync(Guid? except, CancellationToken ct)
        => await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Id != except
            && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.TicketsAssign)))
            .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxSupervisors).ToListAsync(ct);

    // ================================================================ ticket events
    public async Task AssignedAsync(Ticket t, Guid assigneeId, Guid? actorId, CancellationToken ct = default)
    {
        if (assigneeId == actorId) return;
        await SendAsync(assigneeId, "ticket.assigned", $"{t.Number} was assigned to you", t.Subject, TicketPath(t), t.Id,
            (to, name) => EmailTemplates.TicketAssigned(to, name, t.Number, t.Subject, Url(TicketPath(t))), ct: ct);
    }

    /// <summary>Tells the requester how their request is going; a reopened ticket goes to the person who has it.</summary>
    public async Task StatusAsync(Ticket t, TicketStatus to, Guid? actorId, CancellationToken ct = default)
    {
        var path = TicketPath(t);
        switch (to)
        {
            case TicketStatus.InProgress when t.RequesterId != actorId:
                await SendAsync(t.RequesterId, "ticket.started", $"Work has started on {t.Number}", t.Subject, path, t.Id, ct: ct); break;
            case TicketStatus.OnHold when t.RequesterId != actorId:
                await SendAsync(t.RequesterId, "ticket.hold", $"{t.Number} is on hold", t.HoldReason ?? t.Subject, path, t.Id, ct: ct); break;
            case TicketStatus.Resolved when t.RequesterId != actorId:
                await SendAsync(t.RequesterId, "ticket.resolved", $"{t.Number} was resolved", t.ResolutionNote ?? t.Subject, path, t.Id, (mailTo, name) => EmailTemplates.TicketResolved(mailTo, name, t.Number, t.Subject, Url(path)), ct: ct); break;
            case TicketStatus.Closed when t.RequesterId != actorId:
                await SendAsync(t.RequesterId, "ticket.closed", $"{t.Number} was closed", t.Subject, path, t.Id, ct: ct); break;
            case TicketStatus.Cancelled when t.RequesterId != actorId:
                await SendAsync(t.RequesterId, "ticket.cancelled", $"{t.Number} was cancelled", t.CancelReason ?? t.Subject, path, t.Id, ct: ct); break;
            case TicketStatus.Reopened when t.AssigneeId is { } who && who != actorId:
                await SendAsync(who, "ticket.reopened", $"{t.Number} was reopened", t.Subject, path, t.Id, ct: ct); break;
        }
    }

    /// <summary>A public reply goes to the other side (staff → requester with an e-mail, requester → the assignee); an internal note only to the assignee.</summary>
    public async Task CommentAsync(Ticket t, Guid actorId, string actorName, bool isInternal, string body, CancellationToken ct = default)
    {
        var path = TicketPath(t);
        if (isInternal) { if (t.AssigneeId is { } a && a != actorId) await SendAsync(a, "ticket.note", $"Internal note on {t.Number}", body, path, t.Id, ct: ct); return; }
        if (actorId != t.RequesterId) await SendAsync(t.RequesterId, "ticket.reply", $"{actorName} replied on {t.Number}", body, path, t.Id, (to, name) => EmailTemplates.TicketReplied(to, name, t.Number, t.Subject, actorName, Url(path)), ct: ct);
        else if (t.AssigneeId is { } a2 && a2 != actorId) await SendAsync(a2, "ticket.reply", $"{actorName} replied on {t.Number}", body, path, t.Id, ct: ct);
    }

    /// <summary>SLA at risk: the person who has the ticket, its team lead(s) and the supervisors, in the app. A breach is also mailed to them.</summary>
    public async Task EscalationAsync(Ticket t, int level, CancellationToken ct = default)
    {
        if (level < 2) return;
        var path = TicketPath(t);
        // the person who has it, the lead(s) of its team, and (capped) people who supervise tickets
        var recipients = new HashSet<Guid>(await SupervisorsAsync(null, ct)); if (t.AssigneeId is { } a) recipients.Add(a);
        if (t.TeamId is { } team) foreach (var lead in await db.TeamMembers.AsNoTracking().Where(m => m.TeamId == team && m.IsLead && m.User!.Status == UserStatus.Active).Select(m => m.UserId).ToListAsync(ct)) recipients.Add(lead);
        var title = level >= 3 ? $"SLA breached: {t.Number}" : $"SLA at risk: {t.Number}";
        foreach (var id in recipients)
            await SendAsync(id, level >= 3 ? "sla.breached" : "sla.at_risk", title, t.Subject, path, t.Id,
                level >= 3 ? (to, name) => EmailTemplates.SlaBreached(to, name, t.Number, t.Subject, Url(path)) : null, ct: ct);
    }
}
