using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Forms;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record FormView(Guid ServiceId, string ServiceName, int Version, IReadOnlyList<FormField> Fields, string? Note, DateTimeOffset? SavedAt, string? SavedBy);
public sealed record FormVersionItem(int Number, int FieldCount, string? Note, DateTimeOffset CreatedAt, string? CreatedBy, bool Current);
/// <summary><c>Version</c> is the service's version (optimistic concurrency); a new <i>form</i> version is created on every real change.</summary>
public sealed record SaveFormRequest(FormField[]? Fields, string? Note, int? Version = null);

/// <summary>
/// CAT-003. A service's form is a list of immutable versions. Saving a changed form appends version N+1 and points the service at it; saving an identical form changes nothing.
/// Tickets (next slice) record the version they were created with, so later edits never rewrite history.
/// </summary>
public sealed class ServiceFormService(IAppDb db, IClock clock, AuditWriter audit)
{
    private async Task<ServiceItem> ServiceAsync(Guid id, CancellationToken ct, bool tracking = false)
    {
        IQueryable<ServiceItem> q = tracking ? db.Services : db.Services.AsNoTracking();
        return await q.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound("Service");
    }

    public async Task<FormView> GetCurrentAsync(Guid serviceId, CancellationToken ct = default)
    {
        var s = await ServiceAsync(serviceId, ct);
        return await ViewAsync(s, s.CurrentFormVersion, ct);
    }

    public async Task<FormView> GetVersionAsync(Guid serviceId, int number, CancellationToken ct = default)
    {
        var s = await ServiceAsync(serviceId, ct);
        if (number != 0 && number > s.CurrentFormVersion) throw DomainException.NotFound("Form version");
        return await ViewAsync(s, number, ct);
    }

    private async Task<FormView> ViewAsync(ServiceItem s, int number, CancellationToken ct)
    {
        if (number == 0) return new FormView(s.Id, s.Name, 0, [], null, null, null);
        var v = await db.ServiceFormVersions.AsNoTracking().FirstOrDefaultAsync(x => x.ServiceId == s.Id && x.Number == number, ct) ?? throw DomainException.NotFound("Form version");
        var by = v.CreatedByUserId is { } uid ? await db.Users.Where(u => u.Id == uid).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
        return new FormView(s.Id, s.Name, v.Number, FormEngine.Deserialize(v.FieldsJson), v.Note, v.CreatedAt, by);
    }

    public async Task<IReadOnlyList<FormVersionItem>> ListVersionsAsync(Guid serviceId, CancellationToken ct = default)
    {
        var s = await ServiceAsync(serviceId, ct);
        var rows = await db.ServiceFormVersions.AsNoTracking().Where(v => v.ServiceId == serviceId)
            .Select(v => new { v.Number, v.FieldCount, v.Note, v.CreatedAt, By = db.Users.Where(u => u.Id == v.CreatedByUserId).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.OrderByDescending(v => v.Number).Select(v => new FormVersionItem(v.Number, v.FieldCount, v.Note, v.CreatedAt, v.By, v.Number == s.CurrentFormVersion))];
    }

    public async Task<FormView> SaveAsync(Guid serviceId, SaveFormRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await ServiceAsync(serviceId, ct, tracking: true);
        if (s.ArchivedAt is not null) throw DomainException.Conflict("This service is archived. Restore it before editing its form.");
        if (req.Version is { } rv && rv != s.Version) throw DomainException.Conflict("This service was changed by someone else. Reload and try again.");
        if (req.Note is { Length: > 200 }) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["note"] = "Use at most 200 characters." });
        var errors = FormEngine.ValidateDefinition(req.Fields);
        if (errors.Count > 0) throw DomainException.Validation("The form has problems. Please correct the highlighted questions.", errors);

        var fields = FormEngine.Normalize(req.Fields ?? []);
        var json = FormEngine.Serialize(fields);
        if (s.CurrentFormVersion > 0)
        {
            var current = await db.ServiceFormVersions.AsNoTracking().FirstAsync(v => v.ServiceId == serviceId && v.Number == s.CurrentFormVersion, ct);
            if (current.FieldsJson == json || JsonSerializer.Serialize(FormEngine.Deserialize(current.FieldsJson), FormEngine.Json) == json) return await ViewAsync(s, s.CurrentFormVersion, ct); // nothing changed
        }
        else if (fields.Count == 0) return await ViewAsync(s, 0, ct);

        var before = s.CurrentFormVersion;
        var number = before + 1;
        db.ServiceFormVersions.Add(new ServiceFormVersion
        {
            ServiceId = serviceId, Number = number, FieldsJson = json, FieldCount = fields.Count, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            CreatedByUserId = actor.UserId, CreatedAt = clock.UtcNow,
        });
        s.CurrentFormVersion = number; s.Version++; s.UpdatedAt = clock.UtcNow;
        audit.Record("catalog.form.save", "service", serviceId.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["name"] = s.Name, ["version"] = number.ToString(), ["questions"] = fields.Count.ToString() },
            [new AuditDiff("formVersion", before == 0 ? "—" : $"v{before}", $"v{number} ({fields.Count} questions)")]);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("This form was changed by someone else. Reload and try again."); } // concurrency token or the (service, number) unique index
        return await ViewAsync(s, number, ct);
    }
}

// ============================================================================== requester portal (CAT-004, read side)
public sealed record PortalCategory(Guid Id, Guid? ParentId, string Name, string? Description, string? Icon);
public sealed record PortalServiceItem(Guid Id, Guid CategoryId, string Name, string? Description, string? Icon);
public sealed record PortalCatalog(IReadOnlyList<PortalCategory> Categories, IReadOnlyList<PortalServiceItem> Services);
public sealed record PortalServiceDetail(PortalServiceItem Service, string CategoryName, string DefaultPriority, int FormVersion, IReadOnlyList<FormField> Fields);
public sealed record PortalLocation(Guid Id, string Name, string Type, string Path);
public sealed record PortalPerson(Guid Id, string Name, string? Department);
public sealed record CheckAnswersRequest(Dictionary<string, JsonElement>? Answers);
public sealed record AnswerCheck(Guid ServiceId, int FormVersion, IReadOnlyDictionary<string, object?> Values, IReadOnlyList<Guid> FileIds);

/// <summary>
/// What a signed-in person sees when asking for something: published services in live categories, narrowed to their audience.
/// "Staff" services are visible to people who hold <c>catalog.read</c>. <see cref="CheckAnswersAsync"/> is the one place answers are validated; ticket creation will call it.
/// </summary>
public sealed class PortalCatalogService(IAppDb db, AuthorizationService authz, AttachmentService attachments)
{
    private async Task<bool> IsStaffAsync(SessionPrincipal who, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(who.UserId, ct)).Contains(Permissions.CatalogRead);

    /// <summary>Services the person may ask for: published, live, in a live category chain, and for their audience.</summary>
    private async Task<List<ServiceItem>> VisibleServicesAsync(SessionPrincipal who, string? q, CancellationToken ct)
    {
        var staff = await IsStaffAsync(who, ct);
        var query = db.Services.AsNoTracking().Where(s => s.IsPublished && s.ArchivedAt == null && (staff || s.Audience == ServiceAudience.Everyone));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim().ToLowerInvariant();
            query = query.Where(s => s.Name.ToLower().Contains(t) || (s.Description != null && s.Description.ToLower().Contains(t)));
        }
        var services = await query.ToListAsync(ct);
        var live = await LiveCategoriesAsync(ct);
        return [.. services.Where(s => live.ContainsKey(s.CategoryId))];
    }

    /// <summary>Live categories whose whole parent chain is live.</summary>
    private async Task<Dictionary<Guid, ServiceCategory>> LiveCategoriesAsync(CancellationToken ct)
    {
        var all = await db.ServiceCategories.AsNoTracking().Where(c => c.ArchivedAt == null).ToListAsync(ct);
        var ids = all.Select(c => c.Id).ToHashSet();
        return all.Where(c => c.ParentId is null || ids.Contains(c.ParentId.Value)).ToDictionary(c => c.Id);
    }

    private static PortalServiceItem Item(ServiceItem s) => new(s.Id, s.CategoryId, s.Name, s.Description, s.Icon);

    public async Task<PortalCatalog> BrowseAsync(string? q, SessionPrincipal who, CancellationToken ct = default)
    {
        var services = await VisibleServicesAsync(who, q, ct);
        var live = await LiveCategoriesAsync(ct);
        var used = new HashSet<Guid>();
        foreach (var s in services) for (var id = (Guid?)s.CategoryId; id is { } c && used.Add(c); id = live[c].ParentId) { } // a category appears when it, or something under it, has a visible service
        var cats = live.Values.Where(c => used.Contains(c.Id)).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new PortalCategory(c.Id, c.ParentId, c.Name, c.Description, c.Icon)).ToList();
        return new PortalCatalog(cats, [.. services.OrderBy(s => s.Name).Select(Item)]);
    }

    private async Task<(ServiceItem Service, string CategoryName)> VisibleAsync(Guid id, SessionPrincipal who, CancellationToken ct)
    {
        var s = (await VisibleServicesAsync(who, null, ct)).FirstOrDefault(x => x.Id == id) ?? throw DomainException.NotFound("Service"); // hidden and missing look the same
        var cat = await db.ServiceCategories.AsNoTracking().Where(c => c.Id == s.CategoryId).Select(c => c.Name).FirstAsync(ct);
        return (s, cat);
    }

    public async Task<PortalServiceDetail> GetAsync(Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var (s, cat) = await VisibleAsync(id, who, ct);
        var fields = await FieldsAsync(s, ct);
        return new PortalServiceDetail(Item(s), cat, s.DefaultPriority.ToString().ToLowerInvariant(), s.CurrentFormVersion, fields);
    }

    private async Task<IReadOnlyList<FormField>> FieldsAsync(ServiceItem s, CancellationToken ct)
    {
        if (s.CurrentFormVersion == 0) return [];
        var json = await db.ServiceFormVersions.AsNoTracking().Where(v => v.ServiceId == s.Id && v.Number == s.CurrentFormVersion).Select(v => v.FieldsJson).FirstAsync(ct);
        return FormEngine.Deserialize(json);
    }

    /// <summary>Places a requester may pick (live ones), each with its path, e.g. "Jakarta HQ › Tower A › Level 3".</summary>
    public async Task<IReadOnlyList<PortalLocation>> LocationsAsync(CancellationToken ct = default)
    {
        var all = await db.Locations.AsNoTracking().Where(l => l.ArchivedAt == null).Select(l => new { l.Id, l.ParentId, l.Name, l.Type }).ToListAsync(ct);
        var byId = all.ToDictionary(l => l.Id);
        string Path(Guid id) { var parts = new List<string>(); for (Guid? c = id; c is { } k && byId.TryGetValue(k, out var l) && parts.Count < 6; c = l.ParentId) parts.Insert(0, l.Name); return string.Join(" › ", parts); }
        return [.. all.Select(l => new PortalLocation(l.Id, l.Name, l.Type.ToString().ToLowerInvariant(), Path(l.Id))).OrderBy(l => l.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>A small directory lookup for "who is this for" questions: at least two characters, at most 20 active people, name and department only (never e-mail or phone).</summary>
    public async Task<IReadOnlyList<PortalPerson>> PeopleAsync(string? q, CancellationToken ct = default)
    {
        var t = (q ?? "").Trim().ToLowerInvariant();
        if (t.Length < 2) return [];
        var rows = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && (u.Name.ToLower().Contains(t) || u.Email.Contains(t)))
            .OrderBy(u => u.Name).Take(20)
            .Select(u => new { u.Id, u.Name, Department = db.Profiles.Where(p => p.UserId == u.Id).Select(p => p.Department).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.Select(r => new PortalPerson(r.Id, r.Name, r.Department))];
    }

    /// <summary>Validates answers against the form currently in force. Throws 422 with one message per question; returns the normalised values otherwise.</summary>
    public async Task<AnswerCheck> CheckAnswersAsync(Guid id, CheckAnswersRequest req, SessionPrincipal who, CancellationToken ct = default)
    {
        var (s, _) = await VisibleAsync(id, who, ct);
        var fields = await FieldsAsync(s, ct);
        var result = FormEngine.Evaluate(fields, req.Answers);
        var errors = new Dictionary<string, string>(result.Errors);

        // the engine checks shape; whether the chosen place/person exists and is usable is checked here
        foreach (var (kind, label) in new[] { (FieldTypes.Location, "Choose an active location."), (FieldTypes.User, "Choose an active person.") })
        {
            var keys = fields.Where(f => f.Type == kind && result.Values.ContainsKey(f.Key)).Select(f => f.Key).ToList();
            if (keys.Count == 0) continue;
            var ids = keys.Select(k => Guid.Parse((string)result.Values[k]!)).Distinct().ToList();
            var ok = kind == FieldTypes.Location
                ? await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id) && l.ArchivedAt == null).Select(l => l.Id).ToListAsync(ct)
                : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
            foreach (var k in keys.Where(k => !ok.Contains(Guid.Parse((string)result.Values[k]!)))) errors[k] = label;
        }
        // uploaded files must be this person's own, unclaimed and still there
        var fileIds = new List<Guid>();
        foreach (var f in fields.Where(f => f.Type == FieldTypes.File && result.Values.ContainsKey(f.Key)))
        {
            var ids = ((string[])result.Values[f.Key]!).Select(Guid.Parse).ToList();
            if ((await attachments.UnusableAsync(ids, who.UserId, ct)).Count > 0) errors[f.Key] = "A file is no longer available. Please upload it again.";
            fileIds.AddRange(ids);
        }
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted answers.", errors);
        return new AnswerCheck(s.Id, s.CurrentFormVersion, result.Values, fileIds);
    }
}
