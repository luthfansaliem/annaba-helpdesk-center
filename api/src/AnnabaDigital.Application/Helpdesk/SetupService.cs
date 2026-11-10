using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

/// <summary>One line of the go-live checklist. <c>Key</c> is stable (the screen words it); <c>Link</c> is where the missing piece is configured.</summary>
public sealed record SetupStep(string Key, bool Done, bool Required, int Count, string Link);
public sealed record SetupStatus(IReadOnlyList<SetupStep> Steps, bool Ready, int Done, int Total);
public sealed record QuickStartRequest(string? SiteName, string? TeamName, Guid[]? MemberIds, Guid? LeadId);
public sealed record QuickStartResult(IReadOnlyList<string> Created, IReadOnlyList<string> Skipped, SetupStatus Status);

/// <summary>
/// ADM-001. Tells an administrator what is still missing before the helpdesk can take real requests, and fills the gaps with sensible defaults in one go.
/// It never changes what already exists: each part (first site, first team, SLA policies, routing) is created only when the organisation has none.
/// It composes the ordinary services, so every rule, audit entry and uniqueness check of the setup screens applies unchanged.
/// </summary>
public sealed partial class SetupService(IAppDb db, AuthorizationService authz, LocationService locations, TeamService teams, SlaService sla, AssignmentService routing, IAttachmentScanner scanner)
{
    /// <summary>Same targets as <see cref="SlaDefaults"/> (working minutes: first response, resolution); used when an organisation has archived every policy it started with.</summary>
    public static readonly IReadOnlyList<(string Priority, string Name, int First, int Resolve)> DefaultTargets =
    [
        ("urgent", "Urgent requests", 15, 240), ("high", "High priority requests", 60, 480), ("medium", "Standard requests", 240, 1440), ("low", "Low priority requests", 480, 2880),
    ];

    private static readonly string[] Needed = [Permissions.LocationsManage, Permissions.TeamsManage, Permissions.SlaManage, Permissions.RulesManage];
    private static readonly string[] AnyOf = [.. Needed, Permissions.CalendarsManage, Permissions.CatalogManage, Permissions.FormsManage, Permissions.ApprovalsManage];

    [GeneratedRegex("[^A-Z0-9]+")] private static partial Regex NonCode();

    public async Task<SetupStatus> StatusAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        if (!AnyOf.Any(perms.Contains)) throw DomainException.DeniedBecause("Only people who configure the helpdesk can see its setup status.");
        return await BuildAsync(ct);
    }

    private async Task<SetupStatus> BuildAsync(CancellationToken ct)
    {
        var sites = await db.Locations.AsNoTracking().CountAsync(l => l.ArchivedAt == null, ct);
        var calendars = await db.Calendars.AsNoTracking().CountAsync(c => c.ArchivedAt == null, ct);
        var staffed = await db.Teams.AsNoTracking().CountAsync(t => t.ArchivedAt == null && t.Members.Any(), ct);
        var published = await db.Services.AsNoTracking().CountAsync(s => s.IsPublished && s.ArchivedAt == null, ct);
        var policies = await db.SlaPolicies.AsNoTracking().CountAsync(p => p.ArchivedAt == null, ct);
        var rules = await db.AssignmentRules.AsNoTracking().CountAsync(r => r.ArchivedAt == null, ct);
        var approvals = await db.ApprovalPolicies.AsNoTracking().CountAsync(p => p.ArchivedAt == null, ct);
        var tickets = await db.Tickets.AsNoTracking().CountAsync(ct);
        SetupStep S(string key, int n, bool required, string link) => new(key, n > 0, required, n, link);
        var steps = new List<SetupStep>
        {
            S("site", sites, true, "/setup/locations"), S("calendar", calendars, true, "/setup/calendars"), S("team", staffed, true, "/setup/teams"), S("catalog", published, true, "/setup/catalog"),
            S("sla", policies, true, "/setup/sla"), S("routing", rules, true, "/setup/assignment-rules"),
            S("approvals", approvals, false, "/setup/approvals"), S("scanner", scanner.Active ? 1 : 0, false, "/setup/start"), S("test", tickets, false, "/requests/new"),
        };
        var required = steps.Where(s => s.Required).ToList();
        var done = required.Count(s => s.Done);
        return new SetupStatus(steps, done == required.Count, done, required.Count);
    }

    public async Task<QuickStartResult> QuickStartAsync(QuickStartRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        foreach (var p in Needed) if (!perms.Contains(p)) throw DomainException.Denied(p);

        var hasSite = await db.Locations.AnyAsync(l => l.ArchivedAt == null, ct);
        var team = await db.Teams.Where(t => t.ArchivedAt == null).OrderByDescending(t => t.Members.Any()).ThenBy(t => t.CreatedAt).Select(t => new { t.Id, t.Name, Staffed = t.Members.Any() }).FirstOrDefaultAsync(ct);
        var hasPolicies = await db.SlaPolicies.AnyAsync(p => p.ArchivedAt == null, ct);
        var hasRules = await db.AssignmentRules.AnyAsync(r => r.ArchivedAt == null, ct);

        var errors = new Dictionary<string, string>();
        var siteName = (req.SiteName ?? "").Trim(); var teamName = (req.TeamName ?? "").Trim(); var members = (req.MemberIds ?? []).Distinct().ToList();
        if (!hasSite && (siteName.Length < 2 || siteName.Length > 120)) errors["siteName"] = "Name your first site (2–120 characters).";
        if (team is null && (teamName.Length < 2 || teamName.Length > 100)) errors["teamName"] = "Name your first team (2–100 characters).";
        if ((team is null || !team.Staffed) && members.Count == 0) errors["memberIds"] = "Choose at least one person who will work the tickets.";
        if (req.LeadId is { } lead && !members.Contains(lead)) errors["leadId"] = "The team lead must be one of the chosen people.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var created = new List<string>(); var skipped = new List<string>();
        await db.InTransactionAsync(async () =>
        {
            if (hasSite) skipped.Add("site");
            else
            {
                var code = NonCode().Replace(siteName.ToUpperInvariant(), "");
                if (code.Length == 0) code = "HQ"; else if (code.Length > 12) code = code[..12];
                await locations.CreateAsync(new SaveLocationRequest("site", null, code, siteName, null, null, null, null, null), who, client, ct);
                created.Add("site");
            }

            Guid teamId;
            var refs = members.Select(m => new MemberRef(m, m == req.LeadId)).ToArray();
            if (team is null)
            {
                teamId = (await teams.CreateAsync(new SaveTeamRequest(teamName, null), who, client, ct)).Id;
                await teams.SetMembersAsync(teamId, new SetMembersRequest(refs), who, client, ct);
                created.Add("team");
            }
            else if (!team.Staffed)
            {
                teamId = team.Id;
                await teams.SetMembersAsync(teamId, new SetMembersRequest(refs), who, client, ct);
                created.Add("team");
            }
            else { teamId = team.Id; skipped.Add("team"); }

            if (hasPolicies) skipped.Add("sla");
            else
            {
                foreach (var (priority, name, first, resolve) in DefaultTargets)
                    await sla.CreateAsync(new SaveSlaPolicyRequest(name, null, null, null, priority, null, first, resolve, null, null), who, client, ct);
                created.Add("sla");
            }

            if (hasRules) skipped.Add("routing");
            else
            {
                await routing.CreateAsync(new SaveAssignmentRuleRequest("All requests", null, null, null, null, teamId, "least_loaded"), who, client, ct);
                created.Add("routing");
            }
            return true;
        }, ct);
        return new QuickStartResult(created, skipped, await BuildAsync(ct));
    }
}
