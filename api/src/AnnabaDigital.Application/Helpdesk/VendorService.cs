using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record VendorListItem(Guid Id, string Name, string? Services, string? Phone, string? Email, bool Blocked, int Sites, int OpenWorkOrders, DateOnly? NextContractEnd);
public sealed record VendorContactItem(Guid Id, string Name, string? Role, string? Email, string? Phone);
public sealed record VendorSiteItem(Guid LocationId, string Path);
public sealed record VendorContractItem(Guid Id, string Title, string? Reference, DateOnly StartsOn, DateOnly? EndsOn, decimal? Value, string? Notes, string State);
public sealed record VendorDocumentItem(Guid Id, string FileName, string ContentType, long Size, Guid? ContractId, DateTimeOffset CreatedAt, string? UploadedBy);
/// <summary>VND-004 (first part). How the vendor's work orders went: all of them, done, open, done by their due date or late, and their cost (only for people who may see money).</summary>
/// <summary>R2-6: <c>Rating</c> is the average requester rating (1–5) of tickets that had work by this vendor, from <c>Ratings</c> ratings.</summary>
public sealed record VendorScore(int WorkOrders, int Done, int Open, int OnTime, int Late, decimal? Cost, double? Rating = null, int Ratings = 0);
public sealed record VendorDetail(Guid Id, string Name, string? Services, string? Email, string? Phone, string? Website, string? Address, string? TaxId, string? Notes, bool Blocked, string? BlockReason, DateTimeOffset? BlockedAt,
    IReadOnlyList<VendorContactItem> Contacts, IReadOnlyList<VendorSiteItem> Sites, IReadOnlyList<VendorContractItem> Contracts, IReadOnlyList<VendorDocumentItem> Documents, VendorScore Score,
    IReadOnlyList<WorkOrderListItem> WorkOrders, bool CanManage, int Version, IReadOnlyList<VendorPortalUser>? PortalUsers = null);
/// <summary>R2-11. A vendor's person who signs in to work the vendor's work orders (shown to people who manage vendors).</summary>
public sealed record VendorPortalUser(Guid UserId, string Name, string Email, DateTimeOffset LinkedAt);
public sealed record LinkPortalUserRequest(string? Email);
public sealed record ContactInput(string? Name, string? Role, string? Email, string? Phone);
public sealed record SaveVendorRequest(string? Name, string? Services, string? Email, string? Phone, string? Website, string? Address, string? TaxId, string? Notes, ContactInput[]? Contacts = null, Guid[]? LocationIds = null, int? Version = null);
public sealed record BlockVendorRequest(string? Reason);
public sealed record SaveContractRequest(string? Title, string? Reference, DateOnly? StartsOn, DateOnly? EndsOn, decimal? Value, string? Notes);
public sealed record VendorPick(Guid Id, string Name, string? Services, string? Phone);
public sealed record VendorDocumentDownload(Stream Content, string FileName, string ContentType, bool Inline);

/// <summary>
/// VND-001/004/005. The vendor directory: contacts, the places a vendor covers, contracts with their dates and value, documents, block/unblock, a first scorecard from
/// its work orders, and contract expiry reminders. Reading needs <c>vendors.read</c>, changing <c>vendors.manage</c>.
/// </summary>
public sealed partial class VendorService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, ITenantContext tenant, IFileStorage storage, AttachmentService attachments,
    WorkOrderService workOrders, NotificationService notifications, IAppLinks links)
{
    public const int MaxContacts = 20, MaxSites = 200, MaxDocuments = 50;
    public const int ContractNoticeDays = 30, ContractDigestAbove = 5, MaxReminderRecipients = 20;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailShape();
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);

    private sealed record Place(Guid Id, Guid? ParentId, string Name);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }

    private string ContractState(VendorContract c)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return c.StartsOn > today ? "upcoming" : c.EndsOn is { } e && e < today ? "ended" : "active";
    }

    // ================================================================ reading
    /// <summary><paramref name="status"/>: <c>active</c> (default), <c>blocked</c> or <c>all</c>. <paramref name="locationId"/>: vendors covering that place (directly or through a place above it).</summary>
    public async Task<PagedResult<VendorListItem>> ListAsync(string? q, string? status, Guid? locationId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Vendors.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pat = "%" + q.Trim().ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(v => EF.Functions.Like(v.Name.ToLower(), pat, "\\") || (v.Services != null && EF.Functions.Like(v.Services.ToLower(), pat, "\\")));
        }
        query = (status ?? "active").Trim().ToLowerInvariant() switch
        {
            "active" or "" => query.Where(v => v.BlockedAt == null),
            "blocked" => query.Where(v => v.BlockedAt != null),
            "all" => query,
            _ => throw Invalid(new() { ["status"] = "Use active, blocked or all." }),
        };
        if (locationId is { } lid)
        {
            var places = await PlacesAsync(ct); var up = new List<Guid>();
            for (Guid? c = lid; c is { } k && places.TryGetValue(k, out var p) && up.Count < 10; c = p.ParentId) up.Add(k);
            query = query.Where(v => db.VendorSites.Any(s => s.VendorId == v.Id && up.Contains(s.LocationId)));
        }
        var total = await query.CountAsync(ct);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var rows = await query.OrderBy(v => v.Name).Skip((page - 1) * pageSize).Take(pageSize).Select(v => new VendorListItem(v.Id, v.Name, v.Services, v.Phone, v.Email, v.BlockedAt != null,
            db.VendorSites.Count(s => s.VendorId == v.Id),
            db.WorkOrders.Count(w => w.VendorId == v.Id && w.Status != WorkOrderStatus.Done && w.Status != WorkOrderStatus.Cancelled),
            db.VendorContracts.Where(c => c.VendorId == v.Id && c.EndsOn != null && c.EndsOn >= today).OrderBy(c => c.EndsOn).Select(c => c.EndsOn).FirstOrDefault())).ToListAsync(ct);
        return new PagedResult<VendorListItem>(rows, page, pageSize, total);
    }

    /// <summary>Active vendors for the work order picker.</summary>
    public async Task<IReadOnlyList<VendorPick>> PickAsync(string? q, CancellationToken ct = default)
        => [.. (await ListAsync(q, "active", null, 1, 50, ct)).Data.Select(v => new VendorPick(v.Id, v.Name, v.Services, v.Phone))];

    public async Task<VendorDetail> GetAsync(Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var v = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Vendor");
        var perms = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        var places = await PlacesAsync(ct);
        var contacts = await db.VendorContacts.AsNoTracking().Where(c => c.VendorId == id).OrderBy(c => c.Position).ToListAsync(ct);
        var sites = await db.VendorSites.AsNoTracking().Where(s => s.VendorId == id).Select(s => s.LocationId).ToListAsync(ct);
        var contracts = await db.VendorContracts.AsNoTracking().Where(c => c.VendorId == id).OrderByDescending(c => c.StartsOn).ToListAsync(ct);
        var docs = await db.VendorDocuments.AsNoTracking().Where(d => d.VendorId == id && d.DeletedAt == null).OrderByDescending(d => d.CreatedAt)
            .Select(d => new VendorDocumentItem(d.Id, d.FileName, d.ContentType, d.Size, d.ContractId, d.CreatedAt, db.Users.Where(u => u.Id == d.UploadedById).Select(u => u.Name).FirstOrDefault())).ToListAsync(ct);
        var wos = await db.WorkOrders.AsNoTracking().Where(w => w.VendorId == id).Select(w => new { w.Status, w.DueAt, w.CompletedAt }).ToListAsync(ct);
        var rated = await db.TicketRatings.AsNoTracking().Where(r => db.WorkOrders.Any(w => w.VendorId == id && w.TicketId == r.TicketId)).Select(r => r.Score).ToListAsync(ct);
        decimal? cost = perms.Contains(Permissions.WorkOrdersCosts)
            ? await db.WorkOrderCosts.Where(c => db.WorkOrders.Any(w => w.Id == c.WorkOrderId && w.VendorId == id && w.Status != WorkOrderStatus.Cancelled)).SumAsync(c => (decimal?)(c.Quantity * c.UnitCost), ct) ?? 0
            : null;
        var done = wos.Where(w => w.Status == WorkOrderStatus.Done).ToList();
        var score = new VendorScore(wos.Count, done.Count, wos.Count(w => !WorkOrderRules.IsFinished(w.Status)),
            done.Count(w => w.DueAt is { } d && w.CompletedAt <= d), done.Count(w => w.DueAt is { } d && w.CompletedAt > d), cost,
            rated.Count == 0 ? null : Math.Round(rated.Average(), 2), rated.Count);
        var list = perms.Contains(Permissions.WorkOrdersRead) ? await workOrders.ForVendorAsync(id, who, ct) : [];
        return new VendorDetail(v.Id, v.Name, v.Services, v.Email, v.Phone, v.Website, v.Address, v.TaxId, v.Notes, v.BlockedAt != null, v.BlockReason, v.BlockedAt,
            [.. contacts.Select(c => new VendorContactItem(c.Id, c.Name, c.Role, c.Email, c.Phone))],
            [.. sites.Where(places.ContainsKey).Select(s => new VendorSiteItem(s, PathOf(places, s))).OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)],
            [.. contracts.Select(c => new VendorContractItem(c.Id, c.Title, c.Reference, c.StartsOn, c.EndsOn, c.Value, c.Notes, ContractState(c)))], docs, score, list,
            perms.Contains(Permissions.VendorsManage), v.Version,
            perms.Contains(Permissions.VendorsManage)
                ? (await (from x in db.VendorUsers.AsNoTracking() where x.VendorId == id join u in db.Users.AsNoTracking() on x.UserId equals u.Id orderby u.Name select new { u.Id, u.Name, u.Email, x.CreatedAt }).ToListAsync(ct))
                    .Select(x => new VendorPortalUser(x.Id, x.Name, x.Email, x.CreatedAt)).ToList()
                : null);
    }

    // ================================================================ portal users (R2-11)
    /// <summary>Permissions a vendor's person may hold: only doing work orders. Anyone with more is staff and can't be made a vendor's person (no mixing of the two).</summary>
    private static readonly HashSet<string> PortalPermissions = [Permissions.WorkOrdersRead, Permissions.WorkOrdersComplete];

    public async Task<VendorDetail> LinkPortalUserAsync(Guid vendorId, LinkPortalUserRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.Vendors.AnyAsync(v => v.Id == vendorId, ct)) throw DomainException.NotFound("Vendor");
        var email = (r.Email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
        if (user is null) throw Invalid(new() { ["email"] = "Invite this person first (Users → Invite, role Vendor), then link them here." });
        if (await db.VendorUsers.AnyAsync(x => x.UserId == user.Id, ct)) throw Invalid(new() { ["email"] = "This person is already linked to a vendor." });
        var perms = await authz.GetEffectivePermissionsAsync(user.Id, ct);
        if (perms.Any(p => !PortalPermissions.Contains(p))) throw Invalid(new() { ["email"] = "This person has staff rights. A vendor's person may only hold the Vendor role." });
        if (!perms.Contains(Permissions.WorkOrdersRead)) throw Invalid(new() { ["email"] = "Give this person the Vendor role first." });
        db.VendorUsers.Add(new VendorUser { UserId = user.Id, VendorId = vendorId, CreatedById = actor.UserId, CreatedAt = clock.UtcNow });
        audit.Record("vendor.portal_user.link", "vendor", vendorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["user"] = user.Email });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw Invalid(new() { ["email"] = "This person is already linked to a vendor." }); }
        return await GetAsync(vendorId, actor, ct);
    }

    public async Task<VendorDetail> UnlinkPortalUserAsync(Guid vendorId, Guid userId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var link = await db.VendorUsers.FirstOrDefaultAsync(x => x.VendorId == vendorId && x.UserId == userId, ct) ?? throw DomainException.NotFound("Portal user");
        db.VendorUsers.Remove(link);
        audit.Record("vendor.portal_user.unlink", "vendor", vendorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["user"] = userId.ToString() });
        await db.SaveChangesAsync(ct);
        return await GetAsync(vendorId, actor, ct);
    }

    // ================================================================ writing
    private static void CheckEmail(string key, string? v, Dictionary<string, string> e) { if (v is not null && (v.Length > 200 || !EmailShape().IsMatch(v))) e[key] = "Use an e-mail address like name@example.com."; }

    public async Task<VendorDetail> SaveAsync(Guid? id, SaveVendorRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var name = (r.Name ?? "").Trim(); var services = Clean(r.Services); var email = Clean(r.Email)?.ToLowerInvariant(); var phone = Clean(r.Phone); var web = Clean(r.Website); var address = Clean(r.Address); var tax = Clean(r.TaxId); var notes = Clean(r.Notes);
        if (name.Length is < 2 or > 150) e["name"] = "Use 2–150 characters.";
        if (services is { Length: > 300 }) e["services"] = "Use at most 300 characters.";
        CheckEmail("email", email, e);
        if (phone is { Length: > 40 }) e["phone"] = "Use at most 40 characters.";
        if (web is not null && (web.Length > 200 || !(Uri.TryCreate(web, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")))) e["website"] = "Use a web address starting with https://.";
        if (address is { Length: > 300 }) e["address"] = "Use at most 300 characters.";
        if (tax is { Length: > 40 }) e["taxId"] = "Use at most 40 characters.";
        if (notes is { Length: > 2000 }) e["notes"] = "Use at most 2000 characters.";
        var contacts = (r.Contacts ?? []).Select(c => new { Name = (c.Name ?? "").Trim(), Role = Clean(c.Role), Email = Clean(c.Email)?.ToLowerInvariant(), Phone = Clean(c.Phone) }).ToList();
        if (contacts.Count > MaxContacts) e["contacts"] = $"At most {MaxContacts} contacts.";
        for (var i = 0; i < contacts.Count; i++)
        {
            var c = contacts[i];
            if (c.Name.Length is < 2 or > 100) e[$"contacts[{i}].name"] = "Use 2–100 characters.";
            if (c.Role is { Length: > 100 }) e[$"contacts[{i}].role"] = "Use at most 100 characters.";
            CheckEmail($"contacts[{i}].email", c.Email, e);
            if (c.Phone is { Length: > 40 }) e[$"contacts[{i}].phone"] = "Use at most 40 characters.";
        }
        var sites = (r.LocationIds ?? []).Distinct().ToList();
        if (sites.Count > MaxSites) e["locationIds"] = $"At most {MaxSites} places.";
        else if (sites.Count > 0 && await db.Locations.CountAsync(l => sites.Contains(l.Id) && l.ArchivedAt == null, ct) != sites.Count) e["locationIds"] = "Choose active places.";
        if (!e.ContainsKey("name") && await db.Vendors.AnyAsync(v => v.Id != id && v.Name.ToLower() == name.ToLower(), ct)) e["name"] = "A vendor with this name already exists.";
        if (e.Count > 0) throw Invalid(e);

        var now = clock.UtcNow; Vendor v;
        if (id is { } vid)
        {
            v = await db.Vendors.FirstOrDefaultAsync(x => x.Id == vid, ct) ?? throw DomainException.NotFound("Vendor");
            if (r.Version is { } ver && ver != v.Version) throw DomainException.Conflict("This vendor was changed by someone else. Reload and try again.");
            await db.VendorContacts.Where(c => c.VendorId == vid).ExecuteDeleteAsync(ct);
            await db.VendorSites.Where(s => s.VendorId == vid).ExecuteDeleteAsync(ct);
            v.Version++;
        }
        else { v = new Vendor { CreatedAt = now }; db.Vendors.Add(v); }
        v.Name = name; v.Services = services; v.Email = email; v.Phone = phone; v.Website = web; v.Address = address; v.TaxId = tax; v.Notes = notes; v.UpdatedAt = now;
        for (var i = 0; i < contacts.Count; i++) db.VendorContacts.Add(new VendorContact { VendorId = v.Id, Position = i + 1, Name = contacts[i].Name, Role = contacts[i].Role, Email = contacts[i].Email, Phone = contacts[i].Phone });
        foreach (var s in sites) db.VendorSites.Add(new VendorSite { TenantId = tenant.TenantId, VendorId = v.Id, LocationId = s });
        audit.Record(id is null ? "vendor.create" : "vendor.update", "vendor", v.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name, ["contacts"] = contacts.Count.ToString(), ["places"] = sites.Count.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This vendor was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("A vendor with this name already exists."); }
        return await GetAsync(v.Id, actor, ct);
    }

    /// <summary>A blocked vendor gets no new work orders; what it has keeps going (or is cancelled by a manager).</summary>
    public async Task<VendorDetail> SetBlockedAsync(Guid id, bool blocked, string? reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var v = await db.Vendors.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Vendor");
        if (blocked == (v.BlockedAt is not null)) throw DomainException.Conflict(blocked ? "This vendor is already blocked." : "This vendor is not blocked.");
        var why = Clean(reason);
        if (blocked && (why is null || why.Length is < 3 or > 300)) throw Invalid(new() { ["reason"] = "Give a reason of 3–300 characters." });
        v.BlockedAt = blocked ? clock.UtcNow : null; v.BlockReason = blocked ? why : null; v.UpdatedAt = clock.UtcNow; v.Version++;
        audit.Record(blocked ? "vendor.block" : "vendor.unblock", "vendor", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = v.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    // ================================================================ contracts
    public async Task<VendorDetail> SaveContractAsync(Guid vendorId, Guid? contractId, SaveContractRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.Vendors.AnyAsync(v => v.Id == vendorId, ct)) throw DomainException.NotFound("Vendor");
        var e = new Dictionary<string, string>();
        var title = (r.Title ?? "").Trim(); var reference = Clean(r.Reference); var notes = Clean(r.Notes);
        if (title.Length is < 2 or > 150) e["title"] = "Use 2–150 characters.";
        if (reference is { Length: > 60 }) e["reference"] = "Use at most 60 characters.";
        if (r.StartsOn is null) e["startsOn"] = "Give the start date.";
        else if (r.StartsOn.Value.Year is < 1990 or > 2100) e["startsOn"] = "Use a date between 1990 and 2100.";
        if (r.EndsOn is { } end && r.StartsOn is { } start && end < start) e["endsOn"] = "The contract can't end before it starts.";
        if (r.Value is { } val && (val < 0 || val > 1_000_000_000_000m || decimal.Round(val, 2) != val)) e["value"] = "Use an amount of 0 or more with at most two decimals.";
        if (notes is { Length: > 2000 }) e["notes"] = "Use at most 2000 characters.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow; VendorContract c;
        if (contractId is { } cid)
        {
            c = await db.VendorContracts.FirstOrDefaultAsync(x => x.Id == cid && x.VendorId == vendorId, ct) ?? throw DomainException.NotFound("Contract");
            if (c.EndsOn != r.EndsOn) c.ExpiryNotice = 0;             // a new end date starts the reminders over
        }
        else { c = new VendorContract { VendorId = vendorId, CreatedAt = now }; db.VendorContracts.Add(c); }
        c.Title = title; c.Reference = reference; c.StartsOn = r.StartsOn!.Value; c.EndsOn = r.EndsOn; c.Value = r.Value; c.Notes = notes; c.UpdatedAt = now;
        audit.Record(contractId is null ? "vendor.contract.create" : "vendor.contract.update", "vendor", vendorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["contract"] = title, ["endsOn"] = r.EndsOn?.ToString("yyyy-MM-dd") ?? "" });
        await db.SaveChangesAsync(ct);
        return await GetAsync(vendorId, actor, ct);
    }

    public async Task<VendorDetail> DeleteContractAsync(Guid vendorId, Guid contractId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.VendorContracts.FirstOrDefaultAsync(x => x.Id == contractId && x.VendorId == vendorId, ct) ?? throw DomainException.NotFound("Contract");
        db.VendorContracts.Remove(c);
        audit.Record("vendor.contract.delete", "vendor", vendorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["contract"] = c.Title });
        await db.SaveChangesAsync(ct);
        return await GetAsync(vendorId, actor, ct);
    }

    // ================================================================ documents
    public async Task<VendorDetail> AddDocumentAsync(Guid vendorId, Guid? contractId, string? fileName, Stream content, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.Vendors.AnyAsync(v => v.Id == vendorId, ct)) throw DomainException.NotFound("Vendor");
        if (contractId is { } cid && !await db.VendorContracts.AnyAsync(c => c.Id == cid && c.VendorId == vendorId, ct)) throw Invalid(new() { ["contractId"] = "Choose one of this vendor's contracts." });
        if (await db.VendorDocuments.CountAsync(d => d.VendorId == vendorId && d.DeletedAt == null, ct) >= MaxDocuments) throw Invalid(new() { ["file"] = $"A vendor can have at most {MaxDocuments} documents." });
        using var file = await attachments.CheckAsync(fileName, content, "vendor", vendorId.ToString(), actor, client, ct);
        var d = new VendorDocument { VendorId = vendorId, ContractId = contractId, FileName = file.Name, ContentType = file.ContentType, Size = file.Size, Sha256 = file.Sha256, UploadedById = actor.UserId, CreatedAt = clock.UtcNow };
        d.StorageKey = attachments.NewKey(d.Id);
        await storage.SaveAsync(d.StorageKey, file.Content, ct);
        db.VendorDocuments.Add(d);
        audit.Record("vendor.document.add", "vendor", vendorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["file"] = d.FileName, ["sha256"] = d.Sha256 });
        try { await db.SaveChangesAsync(ct); }
        catch { await storage.DeleteAsync(d.StorageKey, CancellationToken.None); throw; }
        return await GetAsync(vendorId, actor, ct);
    }

    public async Task<VendorDocumentDownload> OpenDocumentAsync(Guid vendorId, Guid docId, CancellationToken ct = default)
    {
        var d = await db.VendorDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == docId && x.VendorId == vendorId && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("Document");
        var s = await storage.OpenAsync(d.StorageKey, ct) ?? throw DomainException.NotFound("Document");
        return new VendorDocumentDownload(s, d.FileName, d.ContentType, d.ContentType.StartsWith("image/"));
    }

    public async Task<VendorDetail> DeleteDocumentAsync(Guid vendorId, Guid docId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var d = await db.VendorDocuments.FirstOrDefaultAsync(x => x.Id == docId && x.VendorId == vendorId && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("Document");
        d.DeletedAt = clock.UtcNow;
        audit.Record("vendor.document.delete", "vendor", vendorId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["file"] = d.FileName });
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(d.StorageKey, ct);
        return await GetAsync(vendorId, actor, ct);
    }

    // ================================================================ contract reminders (VND-005)
    /// <summary>Like warranty reminders: people who manage vendors hear 30 days before a contract ends and when it ends, once per end date; more than a few become one summary.</summary>
    public async Task<int> RunContractRemindersAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime); var soon = today.AddDays(ContractNoticeDays); var stale = today.AddDays(-ContractNoticeDays);
        var due = await db.VendorContracts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EndsOn != null && ((c.ExpiryNotice < 1 && c.EndsOn <= soon && c.EndsOn > today) || (c.ExpiryNotice < 2 && c.EndsOn <= today)))
            .Select(c => new { c.Id, c.TenantId, c.VendorId, c.Title, End = c.EndsOn!.Value, Vendor = db.Vendors.IgnoreQueryFilters().Where(v => v.Id == c.VendorId).Select(v => v.Name).FirstOrDefault() }).ToListAsync(ct);
        var sent = 0;
        foreach (var org in due.GroupBy(d => d.TenantId))
        {
            using (tenant.Switch(org.Key))
            {
                db.ResetTracking();
                var claimed = new List<(Guid VendorId, string Vendor, string Title, DateOnly End, bool Ended)>();
                foreach (var c in org)
                {
                    var level = c.End <= today ? 2 : 1; var end = c.End;
                    var n = await db.VendorContracts.Where(x => x.Id == c.Id && x.ExpiryNotice < level && x.EndsOn == end).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExpiryNotice, level), ct);
                    if (n == 1 && !(level == 2 && c.End < stale)) claimed.Add((c.VendorId, c.Vendor ?? "", c.Title, c.End, level == 2));
                }
                if (claimed.Count == 0) continue;
                var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
                        && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.VendorsManage)))
                    .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxReminderRecipients).ToListAsync(ct);
                string Url(string path) => links.Origin.TrimEnd('/') + path;
                foreach (var uid in people)
                {
                    if (claimed.Count > ContractDigestAbove)
                    {
                        var title = $"{claimed.Count} vendor contracts end soon or have ended"; var body = $"Ending within {ContractNoticeDays} days: {claimed.Count(c => !c.Ended)}. Ended: {claimed.Count(c => c.Ended)}.";
                        await notifications.SendAsync(uid, "vendor.contract", title, body, "/vendors", null, (to, name) => Email.EmailTemplates.Notification(to, name, title, body, Url("/vendors")), ct: ct);
                    }
                    else foreach (var c in claimed)
                    {
                        var title = c.Ended ? $"Contract with {c.Vendor} has ended" : $"Contract with {c.Vendor} ends on {c.End:yyyy-MM-dd}";
                        await notifications.SendAsync(uid, "vendor.contract", title, c.Title, $"/vendors/{c.VendorId}", null, (to, name) => Email.EmailTemplates.Notification(to, name, title, c.Title, Url($"/vendors/{c.VendorId}")), ct: ct);
                    }
                }
                await db.SaveChangesAsync(ct);
                sent += claimed.Count;
            }
        }
        return sent;
    }
}
