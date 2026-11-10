using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record TeamMemberDto(Guid UserId, string Name, string Email, bool IsLead);
public sealed record TeamItem(Guid Id, string Name, string? Description, bool Archived, int Members, int Version);
public sealed record TeamDetail(Guid Id, string Name, string? Description, bool Archived, int Version, IReadOnlyList<TeamMemberDto> Members);
public sealed record SaveTeamRequest(string? Name, string? Description, int? Version = null);
public sealed record MemberRef(Guid UserId, bool IsLead);
public sealed record SetMembersRequest(MemberRef[]? Members, int? Version = null);

/// <summary>
/// ADM-003. Teams group agents. Members must be active people of the same organisation; the member list is replaced as a whole (all-or-nothing). Archived teams keep their history
/// but are not offered for assignment.
/// </summary>
public sealed class TeamService(IAppDb db, IClock clock, AuditWriter audit)
{
    public async Task<IReadOnlyList<TeamItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.Teams.AsNoTracking().Where(t => includeArchived || t.ArchivedAt == null)
            .Select(t => new { t.Id, t.Name, t.Description, Archived = t.ArchivedAt != null, Members = t.Members.Count, t.Version }).ToListAsync(ct);
        return [.. rows.OrderBy(t => t.Name).Select(t => new TeamItem(t.Id, t.Name, t.Description, t.Archived, t.Members, t.Version))];
    }

    public async Task<TeamDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.Teams.AsNoTracking().Include(x => x.Members).ThenInclude(m => m.User).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Team");
        return new TeamDetail(t.Id, t.Name, t.Description, t.ArchivedAt is not null, t.Version,
            [.. t.Members.OrderByDescending(m => m.IsLead).ThenBy(m => m.User!.Name).Select(m => new TeamMemberDto(m.UserId, m.User!.Name, m.User.Email, m.IsLead))]);
    }

    private static void ValidateText(SaveTeamRequest req, Dictionary<string, string> errors)
    {
        var n = (req.Name ?? "").Trim();
        if (n.Length < 2 || n.Length > 100) errors["name"] = "Use 2–100 characters.";
        if (req.Description is { Length: > 300 }) errors["description"] = "Use at most 300 characters.";
    }

    public async Task<TeamDetail> CreateAsync(SaveTeamRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>(); ValidateText(req, errors);
        var name = (req.Name ?? "").Trim();
        if (errors.Count == 0 && await db.Teams.AnyAsync(t => t.Name == name && t.ArchivedAt == null, ct)) errors["name"] = "A team with this name already exists.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var now = clock.UtcNow;
        var t = new Team { Name = name, Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(), CreatedAt = now, UpdatedAt = now };
        db.Teams.Add(t);
        audit.Record("team.create", "team", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("A team with this name already exists."); }
        return await GetAsync(t.Id, ct);
    }

    public async Task<TeamDetail> UpdateAsync(Guid id, SaveTeamRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Team");
        if (t.ArchivedAt is not null) throw DomainException.Conflict("This team is archived. Restore it before editing.");
        if (req.Version is { } v && v != t.Version) throw DomainException.Conflict("This team was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>(); ValidateText(req, errors);
        var name = (req.Name ?? "").Trim();
        if (errors.Count == 0 && await db.Teams.AnyAsync(x => x.Name == name && x.ArchivedAt == null && x.Id != id, ct)) errors["name"] = "A team with this name already exists.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var desc = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        var diff = new List<AuditDiff>();
        if (t.Name != name) diff.Add(new("name", t.Name, name));
        if (t.Description != desc) diff.Add(new("description", t.Description ?? "—", desc ?? "—"));
        if (diff.Count > 0)
        {
            t.Name = name; t.Description = desc; t.Version++; t.UpdatedAt = clock.UtcNow;
            audit.Record("team.update", "team", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name }, diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This team was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("A team with this name already exists."); }
        }
        return await GetAsync(id, ct);
    }

    public async Task<TeamDetail> SetMembersAsync(Guid id, SetMembersRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.Teams.Include(x => x.Members).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Team");
        if (t.ArchivedAt is not null) throw DomainException.Conflict("This team is archived. Restore it before changing its members.");
        if (req.Version is { } v && v != t.Version) throw DomainException.Conflict("This team was changed by someone else. Reload and try again.");
        var wanted = (req.Members ?? []).GroupBy(m => m.UserId).Select(g => new MemberRef(g.Key, g.Any(x => x.IsLead))).ToList();
        if (wanted.Count > 200) throw DomainException.Validation("Too many members.", new Dictionary<string, string> { ["members"] = "A team can have at most 200 members." });

        var ids = wanted.Select(w => w.UserId).ToList();
        var ok = await db.Users.Where(u => ids.Contains(u.Id) && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
        var bad = ids.Except(ok).ToList();
        if (bad.Count > 0) throw DomainException.Validation("Some people can't be added.", new Dictionary<string, string> { ["members"] = "Only active people of your organisation can join a team." });

        var now = clock.UtcNow;
        var before = t.Members.Select(m => m.UserId).ToHashSet();
        db.TeamMembers.RemoveRange(t.Members.Where(m => !ids.Contains(m.UserId)));
        foreach (var w in wanted)
        {
            var existing = t.Members.FirstOrDefault(m => m.UserId == w.UserId);
            if (existing is null) db.TeamMembers.Add(new TeamMember { TeamId = id, UserId = w.UserId, IsLead = w.IsLead, AddedAt = now });
            else existing.IsLead = w.IsLead;
        }
        t.Version++; t.UpdatedAt = now;
        audit.Record("team.members", "team", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["name"] = t.Name, ["added"] = ids.Except(before).Count().ToString(), ["removed"] = before.Except(ids).Count().ToString(), ["leads"] = wanted.Count(w => w.IsLead).ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This team was changed by someone else. Reload and try again."); }
        return await GetAsync(id, ct);
    }

    public async Task<TeamDetail> ArchiveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Team");
        if (t.ArchivedAt is not null) throw DomainException.Conflict("This team is already archived.");
        if (await db.Services.AnyAsync(s => s.OwningTeamId == id && s.ArchivedAt == null, ct)) throw DomainException.Invariant("Live services still use this team. Change their owning team first.");
        if (await db.AssignmentRules.AnyAsync(r => r.TeamId == id && r.ArchivedAt == null, ct)) throw DomainException.Invariant("Active assignment rules still send tickets to this team. Change or archive them first.");
        t.ArchivedAt = clock.UtcNow; t.UpdatedAt = t.ArchivedAt.Value; t.Version++;
        audit.Record("team.archive", "team", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = t.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<TeamDetail> RestoreAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.Teams.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Team");
        if (t.ArchivedAt is null) throw DomainException.Conflict("This team is not archived.");
        if (await db.Teams.AnyAsync(x => x.Name == t.Name && x.ArchivedAt == null, ct)) throw DomainException.Conflict("A team with this name exists. Rename that one first.");
        t.ArchivedAt = null; t.UpdatedAt = clock.UtcNow; t.Version++;
        audit.Record("team.restore", "team", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = t.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }
}
