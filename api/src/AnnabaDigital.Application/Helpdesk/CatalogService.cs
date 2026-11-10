using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record CategoryItem(Guid Id, Guid? ParentId, string Name, string? Description, string? Icon, int SortOrder, bool Archived, int Services, int Children, int Version);
public sealed record SaveCategoryRequest(string? Name, string? Description, string? Icon, Guid? ParentId, int? SortOrder, int? Version = null);

public sealed record ServiceListItem(Guid Id, Guid CategoryId, string CategoryName, string Name, string? Description, string? Icon, Guid? OwningTeamId, string? OwningTeamName,
    string DefaultPriority, string Audience, bool IsPublished, bool Archived, int CurrentFormVersion, int Version);
public sealed record SaveServiceRequest(string? Name, string? Description, string? Icon, Guid? CategoryId, Guid? OwningTeamId, string? DefaultPriority, string? Audience, int? Version = null);

/// <summary>
/// CAT-001. Categories (two levels) and services. A service starts unpublished; only published, live services in live categories reach the portal.
/// Names are unique among live siblings, and nothing can be archived while live things still sit inside it.
/// </summary>
public sealed class CatalogService(IAppDb db, IClock clock, AuditWriter audit)
{
    private static readonly Regex IconKey = new("^[a-z][a-z0-9-]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static void ValidateIcon(string? icon, Dictionary<string, string> errors)
    {
        if (Clean(icon) is { } i && !IconKey.IsMatch(i)) errors["icon"] = "Use a lower-case icon name such as wrench or car.";
    }
    private static Dictionary<string, string> Meta(string name) => new() { ["name"] = name };

    // ================================================================ categories
    public async Task<IReadOnlyList<CategoryItem>> ListCategoriesAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.ServiceCategories.AsNoTracking().Where(c => includeArchived || c.ArchivedAt == null)
            .Select(c => new
            {
                c.Id, c.ParentId, c.Name, c.Description, c.Icon, c.SortOrder, c.Version, Archived = c.ArchivedAt != null,
                Services = db.Services.Count(s => s.CategoryId == c.Id && s.ArchivedAt == null),
                Children = db.ServiceCategories.Count(x => x.ParentId == c.Id && x.ArchivedAt == null),
            }).ToListAsync(ct);
        return [.. rows.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).Select(c => new CategoryItem(c.Id, c.ParentId, c.Name, c.Description, c.Icon, c.SortOrder, c.Archived, c.Services, c.Children, c.Version))];
    }

    private async Task<CategoryItem> GetCategoryAsync(Guid id, CancellationToken ct)
        => (await ListCategoriesAsync(true, ct)).FirstOrDefault(c => c.Id == id) ?? throw DomainException.NotFound("Category");

    private async Task<(string Name, string? Desc, string? Icon, int Sort)> ValidateCategoryAsync(SaveCategoryRequest req, Guid? selfId, Dictionary<string, string> errors, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 100) errors["name"] = "Use 2–100 characters.";
        if (req.Description is { Length: > 300 }) errors["description"] = "Use at most 300 characters.";
        ValidateIcon(req.Icon, errors);
        var sort = req.SortOrder ?? 0;
        if (sort is < 0 or > 9999) errors["sortOrder"] = "Use 0–9999.";

        if (req.ParentId is { } pid)
        {
            var parent = await db.ServiceCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == pid, ct);
            if (pid == selfId) errors["parentId"] = "A category can't be inside itself.";
            else if (parent is null) errors["parentId"] = "Choose an existing category.";
            else if (parent.ArchivedAt is not null) errors["parentId"] = "That category is archived.";
            else if (parent.ParentId is not null) errors["parentId"] = "Sub-categories can't contain other categories.";
            else if (selfId is { } me && await db.ServiceCategories.AnyAsync(c => c.ParentId == me && c.ArchivedAt == null, ct)) errors["parentId"] = "A category with sub-categories must stay at the top level.";
        }
        if (!errors.ContainsKey("name") && !errors.ContainsKey("parentId")
            && await db.ServiceCategories.AnyAsync(c => c.Name == name && c.ParentId == req.ParentId && c.ArchivedAt == null && c.Id != selfId, ct))
            errors["name"] = "A category with this name already exists here.";
        return (name, Clean(req.Description), Clean(req.Icon), sort);
    }

    public async Task<CategoryItem> CreateCategoryAsync(SaveCategoryRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var (name, desc, icon, sort) = await ValidateCategoryAsync(req, null, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var now = clock.UtcNow;
        var c = new ServiceCategory { ParentId = req.ParentId, Name = name, Description = desc, Icon = icon, SortOrder = sort, CreatedAt = now, UpdatedAt = now };
        db.ServiceCategories.Add(c);
        audit.Record("catalog.category.create", "service_category", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(name));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("A category with this name already exists here."); }
        return await GetCategoryAsync(c.Id, ct);
    }

    public async Task<CategoryItem> UpdateCategoryAsync(Guid id, SaveCategoryRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ServiceCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Category");
        if (c.ArchivedAt is not null) throw DomainException.Conflict("This category is archived. Restore it before editing.");
        if (req.Version is { } v && v != c.Version) throw DomainException.Conflict("This category was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>();
        var (name, desc, icon, sort) = await ValidateCategoryAsync(req, id, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var diff = new List<AuditDiff>();
        if (c.Name != name) diff.Add(new("name", c.Name, name));
        if (c.ParentId != req.ParentId) diff.Add(new("parent", c.ParentId?.ToString() ?? "—", req.ParentId?.ToString() ?? "—"));
        if (c.Description != desc) diff.Add(new("description", c.Description ?? "—", desc ?? "—"));
        if (c.Icon != icon) diff.Add(new("icon", c.Icon ?? "—", icon ?? "—"));
        if (c.SortOrder != sort) diff.Add(new("sortOrder", c.SortOrder.ToString(), sort.ToString()));
        if (diff.Count > 0)
        {
            c.Name = name; c.ParentId = req.ParentId; c.Description = desc; c.Icon = icon; c.SortOrder = sort; c.Version++; c.UpdatedAt = clock.UtcNow;
            audit.Record("catalog.category.update", "service_category", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(name), diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This category was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("A category with this name already exists here."); }
        }
        return await GetCategoryAsync(id, ct);
    }

    public async Task<CategoryItem> ArchiveCategoryAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ServiceCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Category");
        if (c.ArchivedAt is not null) throw DomainException.Conflict("This category is already archived.");
        if (await db.ServiceCategories.AnyAsync(x => x.ParentId == id && x.ArchivedAt == null, ct)) throw DomainException.Invariant("Archive or move its sub-categories first.");
        if (await db.Services.AnyAsync(s => s.CategoryId == id && s.ArchivedAt == null, ct)) throw DomainException.Invariant("Archive or move its services first.");
        c.ArchivedAt = clock.UtcNow; c.UpdatedAt = c.ArchivedAt.Value; c.Version++;
        audit.Record("catalog.category.archive", "service_category", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(c.Name));
        await db.SaveChangesAsync(ct);
        return await GetCategoryAsync(id, ct);
    }

    public async Task<CategoryItem> RestoreCategoryAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.ServiceCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Category");
        if (c.ArchivedAt is null) throw DomainException.Conflict("This category is not archived.");
        if (c.ParentId is { } pid && !await db.ServiceCategories.AnyAsync(x => x.Id == pid && x.ArchivedAt == null, ct)) throw DomainException.Invariant("Restore its parent category first.");
        if (await db.ServiceCategories.AnyAsync(x => x.Name == c.Name && x.ParentId == c.ParentId && x.ArchivedAt == null, ct)) throw DomainException.Conflict("A category with this name exists here. Rename that one first.");
        c.ArchivedAt = null; c.UpdatedAt = clock.UtcNow; c.Version++;
        audit.Record("catalog.category.restore", "service_category", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(c.Name));
        await db.SaveChangesAsync(ct);
        return await GetCategoryAsync(id, ct);
    }

    // ================================================================ services
    public async Task<IReadOnlyList<ServiceListItem>> ListServicesAsync(bool includeArchived, Guid? categoryId, string? q, CancellationToken ct = default)
    {
        var query = db.Services.AsNoTracking().Where(s => includeArchived || s.ArchivedAt == null);
        if (categoryId is { } cid) query = query.Where(s => s.CategoryId == cid);
        if (Clean(q) is { } text) { var t = text.ToLowerInvariant(); query = query.Where(s => s.Name.ToLower().Contains(t) || (s.Description != null && s.Description.ToLower().Contains(t))); }
        var rows = await query.Select(s => new
        {
            s.Id, s.CategoryId, s.Name, s.Description, s.Icon, s.OwningTeamId, s.DefaultPriority, s.Audience, s.IsPublished, s.CurrentFormVersion, s.Version, Archived = s.ArchivedAt != null,
            Category = db.ServiceCategories.Where(c => c.Id == s.CategoryId).Select(c => c.Name).FirstOrDefault(),
            Team = db.Teams.Where(t => t.Id == s.OwningTeamId).Select(t => t.Name).FirstOrDefault(),
        }).ToListAsync(ct);
        return [.. rows.OrderBy(s => s.Category).ThenBy(s => s.Name).Select(s => new ServiceListItem(s.Id, s.CategoryId, s.Category ?? "", s.Name, s.Description, s.Icon, s.OwningTeamId, s.Team,
            Lower(s.DefaultPriority), Lower(s.Audience), s.IsPublished, s.Archived, s.CurrentFormVersion, s.Version))];
    }

    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();

    public async Task<ServiceListItem> GetServiceAsync(Guid id, CancellationToken ct = default)
        => (await ListServicesAsync(true, null, null, ct)).FirstOrDefault(s => s.Id == id) ?? throw DomainException.NotFound("Service");

    private async Task<(string Name, string? Desc, string? Icon, TicketPriority Priority, ServiceAudience Audience)> ValidateServiceAsync(SaveServiceRequest req, Guid? selfId, Dictionary<string, string> errors, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length is < 2 or > 120) errors["name"] = "Use 2–120 characters.";
        if (req.Description is { Length: > 500 }) errors["description"] = "Use at most 500 characters.";
        ValidateIcon(req.Icon, errors);

        var priority = TicketPriority.Medium;
        if (req.DefaultPriority is not null && !(Enum.TryParse(req.DefaultPriority.Trim(), true, out priority) && Enum.IsDefined(priority))) errors["defaultPriority"] = "Choose low, medium, high or urgent.";
        var audience = ServiceAudience.Everyone;
        if (req.Audience is not null && !(Enum.TryParse(req.Audience.Trim(), true, out audience) && Enum.IsDefined(audience))) errors["audience"] = "Choose everyone or staff.";

        var category = req.CategoryId is { } cid ? await db.ServiceCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
        if (category is null) errors["categoryId"] = "Choose an existing category.";
        else if (category.ArchivedAt is not null) errors["categoryId"] = "That category is archived.";

        if (req.OwningTeamId is { } tid)
        {
            var team = await db.Teams.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tid, ct);
            if (team is null) errors["owningTeamId"] = "Choose an existing team.";
            else if (team.ArchivedAt is not null) errors["owningTeamId"] = "That team is archived.";
        }
        if (!errors.ContainsKey("name") && !errors.ContainsKey("categoryId")
            && await db.Services.AnyAsync(s => s.Name == name && s.CategoryId == req.CategoryId && s.ArchivedAt == null && s.Id != selfId, ct))
            errors["name"] = "A service with this name already exists in this category.";
        return (name, Clean(req.Description), Clean(req.Icon), priority, audience);
    }

    public async Task<ServiceListItem> CreateServiceAsync(SaveServiceRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var (name, desc, icon, priority, audience) = await ValidateServiceAsync(req, null, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var now = clock.UtcNow;
        var s = new ServiceItem { CategoryId = req.CategoryId!.Value, Name = name, Description = desc, Icon = icon, OwningTeamId = req.OwningTeamId, DefaultPriority = priority, Audience = audience, CreatedAt = now, UpdatedAt = now };
        db.Services.Add(s);
        audit.Record("catalog.service.create", "service", s.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(name));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("A service with this name already exists in this category."); }
        return await GetServiceAsync(s.Id, ct);
    }

    public async Task<ServiceListItem> UpdateServiceAsync(Guid id, SaveServiceRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Service");
        if (s.ArchivedAt is not null) throw DomainException.Conflict("This service is archived. Restore it before editing.");
        if (req.Version is { } v && v != s.Version) throw DomainException.Conflict("This service was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>();
        var (name, desc, icon, priority, audience) = await ValidateServiceAsync(req, id, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var diff = new List<AuditDiff>();
        if (s.Name != name) diff.Add(new("name", s.Name, name));
        if (s.CategoryId != req.CategoryId) diff.Add(new("category", s.CategoryId.ToString(), req.CategoryId.ToString()!));
        if (s.Description != desc) diff.Add(new("description", s.Description ?? "—", desc ?? "—"));
        if (s.Icon != icon) diff.Add(new("icon", s.Icon ?? "—", icon ?? "—"));
        if (s.OwningTeamId != req.OwningTeamId) diff.Add(new("owningTeam", s.OwningTeamId?.ToString() ?? "—", req.OwningTeamId?.ToString() ?? "—"));
        if (s.DefaultPriority != priority) diff.Add(new("defaultPriority", s.DefaultPriority.ToString(), priority.ToString()));
        if (s.Audience != audience) diff.Add(new("audience", s.Audience.ToString(), audience.ToString()));
        if (diff.Count > 0)
        {
            s.Name = name; s.CategoryId = req.CategoryId!.Value; s.Description = desc; s.Icon = icon; s.OwningTeamId = req.OwningTeamId; s.DefaultPriority = priority; s.Audience = audience;
            s.Version++; s.UpdatedAt = clock.UtcNow;
            audit.Record("catalog.service.update", "service", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(name), diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This service was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("A service with this name already exists in this category."); }
        }
        return await GetServiceAsync(id, ct);
    }

    public async Task<ServiceListItem> SetPublishedAsync(Guid id, bool published, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Service");
        if (s.ArchivedAt is not null) throw DomainException.Conflict("This service is archived. Restore it first.");
        if (s.IsPublished == published) throw DomainException.Conflict(published ? "This service is already published." : "This service is not published.");
        s.IsPublished = published; s.Version++; s.UpdatedAt = clock.UtcNow;
        audit.Record(published ? "catalog.service.publish" : "catalog.service.unpublish", "service", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(s.Name));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This service was changed by someone else. Reload and try again."); }
        return await GetServiceAsync(id, ct);
    }

    /// <summary>Archiving also unpublishes, so restoring never silently puts a service back in front of people.</summary>
    public async Task<ServiceListItem> ArchiveServiceAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Service");
        if (s.ArchivedAt is not null) throw DomainException.Conflict("This service is already archived.");
        s.ArchivedAt = clock.UtcNow; s.IsPublished = false; s.UpdatedAt = s.ArchivedAt.Value; s.Version++;
        audit.Record("catalog.service.archive", "service", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(s.Name));
        await db.SaveChangesAsync(ct);
        return await GetServiceAsync(id, ct);
    }

    public async Task<ServiceListItem> RestoreServiceAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.Services.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Service");
        if (s.ArchivedAt is null) throw DomainException.Conflict("This service is not archived.");
        if (!await db.ServiceCategories.AnyAsync(c => c.Id == s.CategoryId && c.ArchivedAt == null, ct)) throw DomainException.Invariant("Restore its category first.");
        if (s.OwningTeamId is { } tid && !await db.Teams.AnyAsync(t => t.Id == tid && t.ArchivedAt == null, ct)) throw DomainException.Invariant("Its owning team is archived. Restore that team first.");
        if (await db.Services.AnyAsync(x => x.Name == s.Name && x.CategoryId == s.CategoryId && x.ArchivedAt == null, ct)) throw DomainException.Conflict("A service with this name exists in this category. Rename that one first.");
        s.ArchivedAt = null; s.UpdatedAt = clock.UtcNow; s.Version++;
        audit.Record("catalog.service.restore", "service", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, Meta(s.Name));
        await db.SaveChangesAsync(ct);
        return await GetServiceAsync(id, ct);
    }
}
