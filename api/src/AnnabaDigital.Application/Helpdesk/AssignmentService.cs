using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record AssignmentRuleItem(Guid Id, string Name, int Position, Guid? ServiceId, string? ServiceName, Guid? CategoryId, string? CategoryName, Guid? LocationId, string? LocationName,
    string? Priority, Guid TeamId, string? TeamName, string Strategy, bool Archived, int Conditions, int Version);
public sealed record SaveAssignmentRuleRequest(string? Name, Guid? ServiceId, Guid? CategoryId, Guid? LocationId, string? Priority, Guid? TeamId, string? Strategy, int? Version = null);
public sealed record RouteResult(Guid? RuleId, string? RuleName, Guid? TeamId, string? TeamName, string Strategy, Guid? AssigneeId, string? AssigneeName, IReadOnlyList<RouteCandidate> Candidates);
public sealed record RouteCandidate(Guid Id, string Name, int OpenTickets, DateTimeOffset? LastAssignedAt);

/// <summary>
/// TKT-003. Routing rules: the first live rule whose conditions match sends a new ticket to a team and, with a strategy, to one of its members
/// (round robin = longest since last given a ticket; least loaded = fewest open tickets). Only active people who may work tickets are candidates.
/// Without a matching rule the service's owning team gets the ticket and nobody is assigned.
/// </summary>
public sealed class AssignmentService(IAppDb db, IClock clock, AuditWriter audit)
{
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();
    private static Conditions Cond(AssignmentRule r) => new(r.ServiceId, r.CategoryId, r.LocationId, r.Priority);
    private static readonly TicketStatus[] Open = [TicketStatus.Submitted, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.OnHold, TicketStatus.Reopened];

    // ================================================================ admin
    public async Task<IReadOnlyList<AssignmentRuleItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.AssignmentRules.AsNoTracking().Where(r => includeArchived || r.ArchivedAt == null)
            .Select(r => new
            {
                r, Service = db.Services.Where(x => x.Id == r.ServiceId).Select(x => x.Name).FirstOrDefault(), Category = db.ServiceCategories.Where(x => x.Id == r.CategoryId).Select(x => x.Name).FirstOrDefault(),
                Location = db.Locations.Where(x => x.Id == r.LocationId).Select(x => x.Name).FirstOrDefault(), Team = db.Teams.Where(x => x.Id == r.TeamId).Select(x => x.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        return [.. rows.OrderBy(x => x.r.ArchivedAt != null).ThenBy(x => x.r.Position).ThenBy(x => x.r.Name).Select(x => new AssignmentRuleItem(x.r.Id, x.r.Name, x.r.Position, x.r.ServiceId, x.Service, x.r.CategoryId, x.Category,
            x.r.LocationId, x.Location, x.r.Priority is { } p ? Lower(p) : null, x.r.TeamId, x.Team, Lower(x.r.Strategy), x.r.ArchivedAt is not null, Cond(x.r).Count, x.r.Version))];
    }

    private async Task<AssignmentRuleItem> GetAsync(Guid id, CancellationToken ct) => (await ListAsync(true, ct)).FirstOrDefault(r => r.Id == id) ?? throw DomainException.NotFound("Assignment rule");

    private async Task<(string Name, TicketPriority? Priority, AssignStrategy Strategy)> ValidateAsync(SaveAssignmentRuleRequest r, Guid? selfId, Dictionary<string, string> errors, CancellationToken ct)
    {
        var name = (r.Name ?? "").Trim();
        if (name.Length is < 2 or > 100) errors["name"] = "Use 2–100 characters.";
        TicketPriority? priority = null;
        if (Clean(r.Priority) is { } ps) { if (Enum.TryParse<TicketPriority>(ps, true, out var pr) && Enum.IsDefined(pr)) priority = pr; else errors["priority"] = "Choose low, medium, high or urgent."; }
        var strategy = AssignStrategy.None;
        if (Clean(r.Strategy) is { } ss && !(Enum.TryParse(ss.Replace("_", ""), true, out strategy) && Enum.IsDefined(strategy))) errors["strategy"] = "Choose none, round robin or least loaded.";
        if (r.ServiceId is { } sid && !await db.Services.AnyAsync(x => x.Id == sid, ct)) errors["serviceId"] = "Choose an existing service.";
        if (r.CategoryId is { } cid && !await db.ServiceCategories.AnyAsync(x => x.Id == cid, ct)) errors["categoryId"] = "Choose an existing category.";
        if (r.LocationId is { } lid && !await db.Locations.AnyAsync(x => x.Id == lid && x.ArchivedAt == null, ct)) errors["locationId"] = "Choose an active location.";
        if (r.TeamId is not { } tid) errors["teamId"] = "Choose the team that receives these tickets.";
        else if (!await db.Teams.AnyAsync(x => x.Id == tid && x.ArchivedAt == null, ct)) errors["teamId"] = "Choose an active team.";
        if (!errors.ContainsKey("name") && await db.AssignmentRules.AnyAsync(x => x.Name == name && x.ArchivedAt == null && x.Id != selfId, ct)) errors["name"] = "A rule with this name already exists.";
        return (name, priority, strategy);
    }

    public async Task<AssignmentRuleItem> CreateAsync(SaveAssignmentRuleRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>(); var (name, priority, strategy) = await ValidateAsync(r, null, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var now = clock.UtcNow;
        var last = await db.AssignmentRules.Where(x => x.ArchivedAt == null).Select(x => (int?)x.Position).MaxAsync(ct) ?? 0;
        var rule = new AssignmentRule { Name = name, Position = last + 10, ServiceId = r.ServiceId, CategoryId = r.CategoryId, LocationId = r.LocationId, Priority = priority, TeamId = r.TeamId!.Value, Strategy = strategy, CreatedAt = now, UpdatedAt = now };
        db.AssignmentRules.Add(rule);
        audit.Record("rule.create", "assignment_rule", rule.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("A rule with this name already exists."); }
        return await GetAsync(rule.Id, ct);
    }

    public async Task<AssignmentRuleItem> UpdateAsync(Guid id, SaveAssignmentRuleRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Assignment rule");
        if (rule.ArchivedAt is not null) throw DomainException.Conflict("This rule is archived. Restore it before editing.");
        if (r.Version is { } v && v != rule.Version) throw DomainException.Conflict("This rule was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>(); var (name, priority, strategy) = await ValidateAsync(r, id, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var diff = new List<AuditDiff>();
        void D(string f, object? a, object? b) { if (!Equals(a, b)) diff.Add(new(f, a?.ToString() ?? "—", b?.ToString() ?? "—")); }
        D("name", rule.Name, name); D("service", rule.ServiceId, r.ServiceId); D("category", rule.CategoryId, r.CategoryId); D("location", rule.LocationId, r.LocationId);
        D("priority", rule.Priority, priority); D("team", rule.TeamId, r.TeamId); D("strategy", rule.Strategy, strategy);
        if (diff.Count > 0)
        {
            rule.Name = name; rule.ServiceId = r.ServiceId; rule.CategoryId = r.CategoryId; rule.LocationId = r.LocationId; rule.Priority = priority; rule.TeamId = r.TeamId!.Value; rule.Strategy = strategy;
            rule.Version++; rule.UpdatedAt = clock.UtcNow;
            audit.Record("rule.update", "assignment_rule", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name }, diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This rule was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("A rule with this name already exists."); }
        }
        return await GetAsync(id, ct);
    }

    public async Task<AssignmentRuleItem> ArchiveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Assignment rule");
        if (rule.ArchivedAt is not null) throw DomainException.Conflict("This rule is already archived.");
        rule.ArchivedAt = clock.UtcNow; rule.UpdatedAt = rule.ArchivedAt.Value; rule.Version++;
        audit.Record("rule.archive", "assignment_rule", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = rule.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AssignmentRuleItem> RestoreAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var rule = await db.AssignmentRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Assignment rule");
        if (rule.ArchivedAt is null) throw DomainException.Conflict("This rule is not archived.");
        if (!await db.Teams.AnyAsync(t => t.Id == rule.TeamId && t.ArchivedAt == null, ct)) throw DomainException.Invariant("Its team is archived. Restore the team first.");
        if (await db.AssignmentRules.AnyAsync(x => x.Name == rule.Name && x.ArchivedAt == null, ct)) throw DomainException.Conflict("A rule with this name exists. Rename that one first.");
        rule.Position = (await db.AssignmentRules.Where(x => x.ArchivedAt == null).Select(x => (int?)x.Position).MaxAsync(ct) ?? 0) + 10; rule.ArchivedAt = null; rule.UpdatedAt = clock.UtcNow; rule.Version++;
        audit.Record("rule.restore", "assignment_rule", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = rule.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<AssignmentRuleItem>> ReorderAsync(ReorderRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var live = await db.AssignmentRules.Where(x => x.ArchivedAt == null).ToListAsync(ct);
        var ids = r.Ids ?? [];
        if (ids.Length != live.Count || ids.Distinct().Count() != ids.Length || ids.Any(i => live.All(x => x.Id != i)))
            throw DomainException.Validation("The order must list every active rule once.", new Dictionary<string, string> { ["ids"] = "List every active rule exactly once." });
        var now = clock.UtcNow;
        for (var i = 0; i < ids.Length; i++) { var x = live.First(y => y.Id == ids[i]); if (x.Position != (i + 1) * 10) { x.Position = (i + 1) * 10; x.UpdatedAt = now; } }
        audit.Record("rule.reorder", "assignment_rule", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["count"] = ids.Length.ToString() });
        await db.SaveChangesAsync(ct);
        return await ListAsync(false, ct);
    }

    // ================================================================ routing
    /// <summary>People of a team who are active and may work tickets, with their current load.</summary>
    private async Task<List<Candidate>> CandidatesAsync(Guid teamId, CancellationToken ct)
    {
        var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
            && db.TeamMembers.Any(m => m.TeamId == teamId && m.UserId == u.Id)
            && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.TicketsTransition)))
            .Select(u => u.Id).ToListAsync(ct);
        if (people.Count == 0) return [];
        var load = await db.Tickets.AsNoTracking().Where(t => t.AssigneeId != null && people.Contains(t.AssigneeId.Value))
            .GroupBy(t => t.AssigneeId!.Value).Select(g => new { Id = g.Key, Open = g.Count(t => Open.Contains(t.Status)), Last = g.Max(t => t.AssignedAt) }).ToDictionaryAsync(x => x.Id, ct);
        return [.. people.Select(id => load.TryGetValue(id, out var l) ? new Candidate(id, l.Open, l.Last) : new Candidate(id, 0, null))];
    }

    /// <summary>Where would a ticket with these facts go? Used when a ticket is created and by the admin "test a ticket" panel; assigns nothing itself.</summary>
    public async Task<RouteResult> RouteAsync(MatchFacts facts, Guid? serviceTeam, CancellationToken ct = default)
    {
        var rules = await db.AssignmentRules.AsNoTracking().Where(r => r.ArchivedAt == null).OrderBy(r => r.Position).ThenBy(r => r.Id).ToListAsync(ct);
        var rule = Routing.FirstMatch(rules, Cond, facts);
        var teamId = rule?.TeamId ?? serviceTeam;
        if (teamId is null) return new RouteResult(rule?.Id, rule?.Name, null, null, "none", null, null, []);
        var team = await db.Teams.AsNoTracking().Where(t => t.Id == teamId && t.ArchivedAt == null).Select(t => t.Name).FirstOrDefaultAsync(ct);
        if (team is null) return new RouteResult(rule?.Id, rule?.Name, null, null, "none", null, null, []);
        var strategy = rule?.Strategy ?? AssignStrategy.None;
        var cands = strategy == AssignStrategy.None ? [] : await CandidatesAsync(teamId.Value, ct);
        var pick = Routing.Pick(strategy, cands);
        var candidateIds = cands.Select(c => c.UserId).ToList();
        var names = candidateIds.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => candidateIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return new RouteResult(rule?.Id, rule?.Name, teamId, team, Lower(strategy), pick, pick is { } p ? names.GetValueOrDefault(p) : null,
            [.. cands.Select(c => new RouteCandidate(c.UserId, names.GetValueOrDefault(c.UserId) ?? "", c.OpenCount, c.LastAssignedAt)).OrderBy(c => c.Name)]);
    }

    /// <summary>
    /// Routes a ticket for real: sets its team and, when a strategy picks someone, its assignee, status (Assigned), assignment time and an "assigned" timeline entry.
    /// Used when a ticket is created, and when an approved request is released to the team. Returns the decision so the caller can audit it.
    /// </summary>
    public async Task<RouteResult> ApplyRoutingAsync(Ticket t, Guid? serviceTeam, DateTimeOffset now, CancellationToken ct = default)
    {
        var routed = await RouteAsync(await MatchFactsBuilder.BuildAsync(db, t.ServiceId, t.LocationId, t.Priority, ct), serviceTeam, ct);
        if (routed.TeamId is { } team) t.TeamId = team;
        if (routed.AssigneeId is { } who)
        {
            var from = t.Status;
            t.AssigneeId = who; t.Status = TicketStatus.Assigned; t.AssignedAt = now;
            db.TicketEvents.Add(new TicketEvent { TenantId = t.TenantId, TicketId = t.Id, Kind = TicketEventKind.Assigned, ActorLabel = "System", Body = $"Assigned to {routed.AssigneeName} ({routed.TeamName}) by rule “{routed.RuleName}”.", FromStatus = from, ToStatus = TicketStatus.Assigned, CreatedAt = now });
        }
        return routed;
    }

    public async Task<RouteResult> PreviewAsync(RulePreviewRequest r, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var service = r.ServiceId is { } sid ? await db.Services.AsNoTracking().Where(s => s.Id == sid).Select(s => new { s.OwningTeamId, s.DefaultPriority }).FirstOrDefaultAsync(ct) : null;
        if (service is null) errors["serviceId"] = "Choose a service.";
        var priority = service?.DefaultPriority ?? TicketPriority.Medium;
        if (Clean(r.Priority) is { } ps && !(Enum.TryParse(ps, true, out priority) && Enum.IsDefined(priority))) errors["priority"] = "Choose low, medium, high or urgent.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        return await RouteAsync(await MatchFactsBuilder.BuildAsync(db, r.ServiceId!.Value, r.LocationId, priority, ct), service!.OwningTeamId, ct);
    }
}
