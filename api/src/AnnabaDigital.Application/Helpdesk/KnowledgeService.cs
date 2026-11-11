using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record KbCategoryItem(Guid Id, string Name, int Articles, bool Archived);
public sealed record SaveKbCategoryRequest(string? Name);
public sealed record KbListItem(Guid Id, string Title, string? Summary, string? CategoryName, string Visibility, string Status, bool HasDraft, DateOnly? ReviewBy, bool ReviewDue,
    DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, int Views, int Helpful, int NotHelpful, int Deflections);
public sealed record KbServiceRef(Guid Id, string Name);
public sealed record KbFileItem(Guid Id, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt);
public sealed record KbVersionItem(int Number, string Title, string? Note, string? By, DateTimeOffset At, bool Published);
public sealed record KbArticleDetail(Guid Id, string Title, string? Summary, string Body, Guid? CategoryId, string? CategoryName, string Visibility, string Status, int PublishedVersion, int LatestVersion,
    int ShownVersion, bool HasDraft, DateOnly? ReviewBy, bool ReviewDue, IReadOnlyList<KbServiceRef> Services, IReadOnlyList<KbFileItem> Files, int Views, int Helpful, int NotHelpful, int Deflections,
    bool? MyFeedback, IReadOnlyList<KbVersionItem>? Versions, bool CanWrite, bool CanPublish, DateTimeOffset UpdatedAt, DateTimeOffset? PublishedAt, int Version);
public sealed record SaveKbArticleRequest(string? Title, string? Summary, string? Body, Guid? CategoryId, string? Visibility, DateOnly? ReviewBy, Guid[]? ServiceIds, string? Note, int? Version = null);
public sealed record KbFeedbackRequest(bool Helpful);
public sealed record KbSuggestion(Guid Id, string Title, string? Summary, bool ForService);
public sealed record KbFileDownload(Stream Content, string FileName, string ContentType, bool Inline);

/// <summary>
/// KB-001/002/003. The knowledge base. Everyone signed in reads published articles meant for everyone; <c>kb.read</c> also reads staff-only ones; <c>kb.write</c> writes drafts
/// (new versions), adds files and links services; <c>kb.publish</c> publishes, archives and manages categories. Readers only ever see the published version.
/// Suggestions while making a request search the published articles the person may read (the request's service's own articles first); "this solved it" is counted as a deflection.
/// </summary>
public sealed class KnowledgeService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, IFileStorage storage, AttachmentService attachments)
{
    public const int TitleMin = 3, TitleMax = 150, SummaryMax = 300, BodyMax = 50_000, NoteMax = 200, MaxServices = 50, MaxFiles = 20;
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();

    private sealed record Reader(Guid Id, bool Staff, bool Write, bool Publish);
    private async Task<Reader> ReaderAsync(SessionPrincipal p, CancellationToken ct)
    {
        var perms = await authz.GetEffectivePermissionsAsync(p.UserId, ct);
        var write = perms.Contains(Permissions.KbWrite); var publish = perms.Contains(Permissions.KbPublish);
        return new Reader(p.UserId, perms.Contains(Permissions.KbRead) || write || publish, write || publish, publish);
    }

    /// <summary>Published articles the reader may see.</summary>
    private static IQueryable<KbArticle> Readable(IQueryable<KbArticle> q, Reader r)
        => r.Staff ? q.Where(a => a.Status == KbStatus.Published) : q.Where(a => a.Status == KbStatus.Published && a.Visibility == KbVisibility.Everyone);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
    private static string Pattern(string word) => "%" + word.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    private static IQueryable<KbArticle> Matching(IQueryable<KbArticle> q, string? text)
    {
        foreach (var w in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(w => w.Length >= 2).Take(6))
        {
            var pat = Pattern(w);
            q = q.Where(a => EF.Functions.Like(a.Title.ToLower(), pat, "\\") || (a.Summary != null && EF.Functions.Like(a.Summary.ToLower(), pat, "\\")) || EF.Functions.Like(a.Body.ToLower(), pat, "\\"));
        }
        return q;
    }

    // ================================================================ categories
    public async Task<IReadOnlyList<KbCategoryItem>> CategoriesAsync(CancellationToken ct = default)
        => (await db.KbCategories.AsNoTracking().Select(c => new KbCategoryItem(c.Id, c.Name, db.KbArticles.Count(a => a.CategoryId == c.Id && a.Status != KbStatus.Archived), c.ArchivedAt != null)).ToListAsync(ct))
            .OrderBy(c => c.Archived).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public async Task<KbCategoryItem> SaveCategoryAsync(Guid? id, SaveKbCategoryRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var name = (r.Name ?? "").Trim();
        if (name.Length is < 2 or > 60) throw Invalid(new() { ["name"] = "Use 2–60 characters." });
        if (await db.KbCategories.AnyAsync(c => c.Id != id && c.ArchivedAt == null && c.Name.ToLower() == name.ToLower(), ct)) throw Invalid(new() { ["name"] = "Another category has this name." });
        var c = id is { } cid ? await db.KbCategories.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw DomainException.NotFound("Category") : null;
        if (c is null) { c = new KbCategory { CreatedAt = clock.UtcNow }; db.KbCategories.Add(c); }
        c.Name = name;
        audit.Record(id is null ? "kb.category.create" : "kb.category.update", "kb_category", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return (await CategoriesAsync(ct)).First(x => x.Id == c.Id);
    }

    public async Task SetCategoryArchivedAsync(Guid id, bool archived, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.KbCategories.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Category");
        if (!archived && await db.KbCategories.AnyAsync(x => x.Id != id && x.ArchivedAt == null && x.Name.ToLower() == c.Name.ToLower(), ct)) throw DomainException.Conflict("Another active category has this name.");
        c.ArchivedAt = archived ? clock.UtcNow : null;
        audit.Record(archived ? "kb.category.archive" : "kb.category.restore", "kb_category", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = c.Name });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ reading
    /// <summary>
    /// <paramref name="status"/> (writers only): <c>published</c> (default for readers), <c>draft</c> (never published, or with unpublished changes), <c>archived</c>, <c>review</c>
    /// (review date passed), <c>all</c>.
    /// </summary>
    public async Task<PagedResult<KbListItem>> ListAsync(string? q, Guid? categoryId, string? status, int page, int pageSize, SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await ReaderAsync(who, ct);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var today = Today;
        IQueryable<KbArticle> query = db.KbArticles.AsNoTracking();
        var st = (status ?? "published").Trim().ToLowerInvariant();
        if (!r.Write || st == "published") query = Readable(query, r);
        else query = st switch
        {
            "draft" => query.Where(a => a.Status == KbStatus.Draft || (a.Status == KbStatus.Published && a.LatestVersion > a.PublishedVersion)),
            "archived" => query.Where(a => a.Status == KbStatus.Archived),
            "review" => query.Where(a => a.Status != KbStatus.Archived && a.ReviewBy != null && a.ReviewBy <= today),
            "all" => query,
            _ => throw Invalid(new() { ["status"] = "Use published, draft, archived, review or all." }),
        };
        if (categoryId is { } cid) query = query.Where(a => a.CategoryId == cid);
        if (Clean(q) is { } text)
        {
            // writers search drafts too: their latest version's title
            if (r.Write && st != "published")
            {
                var pat = Pattern(text);
                query = query.Where(a => EF.Functions.Like(a.Title.ToLower(), pat, "\\") || db.KbArticleVersions.Any(v => v.ArticleId == a.Id && v.Number == a.LatestVersion && EF.Functions.Like(v.Title.ToLower(), pat, "\\")));
            }
            else query = Matching(query, text);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(a => new
        {
            a.Id, a.Title, a.Summary, a.Visibility, a.Status, a.LatestVersion, a.PublishedVersion, a.ReviewBy, a.UpdatedAt, a.PublishedAt, a.Views, a.Deflections,
            Category = db.KbCategories.Where(c => c.Id == a.CategoryId).Select(c => c.Name).FirstOrDefault(),
            Draft = db.KbArticleVersions.Where(v => v.ArticleId == a.Id && v.Number == a.LatestVersion).Select(v => new { v.Title, v.Summary }).FirstOrDefault(),
            Yes = db.KbFeedback.Count(f => f.ArticleId == a.Id && f.Helpful), No = db.KbFeedback.Count(f => f.ArticleId == a.Id && !f.Helpful),
        }).ToListAsync(ct);
        return new PagedResult<KbListItem>([.. rows.Select(a =>
        {
            var useDraft = r.Write && (a.PublishedVersion == 0 || st is "draft");
            return new KbListItem(a.Id, useDraft ? a.Draft?.Title ?? a.Title : a.Title, useDraft ? a.Draft?.Summary : a.Summary, a.Category, Lower(a.Visibility), Lower(a.Status),
                a.LatestVersion > a.PublishedVersion, r.Write ? a.ReviewBy : null, r.Write && a.ReviewBy is { } rb && rb <= today, a.UpdatedAt, a.PublishedAt,
                r.Write ? a.Views : 0, r.Write ? a.Yes : 0, r.Write ? a.No : 0, r.Write ? a.Deflections : 0);
        })], page, pageSize, total);
    }

    /// <summary>Readers get the published version (and it counts as a view); writers may ask for any version (<paramref name="version"/>, or 0 for the latest).</summary>
    public async Task<KbArticleDetail> GetAsync(Guid id, int? version, SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await ReaderAsync(who, ct);
        var a = r.Write ? await db.KbArticles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) : await Readable(db.KbArticles.AsNoTracking(), r).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) throw DomainException.NotFound("Article");
        string title = a.Title; string? summary = a.Summary; string body = a.Body; var shown = a.PublishedVersion;
        if (r.Write && (version is not null || a.PublishedVersion == 0))
        {
            var n = version is > 0 ? version.Value : a.LatestVersion;
            var v = await db.KbArticleVersions.AsNoTracking().FirstOrDefaultAsync(x => x.ArticleId == id && x.Number == n, ct) ?? throw DomainException.NotFound("Version");
            title = v.Title; summary = v.Summary; body = v.Body; shown = v.Number;
        }
        else if (!r.Write || version is null) await db.KbArticles.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Views, x => x.Views + 1), ct);
        return await BuildAsync(a, r, title, summary, body, shown, ct);
    }

    private async Task<KbArticleDetail> BuildAsync(KbArticle a, Reader r, string title, string? summary, string body, int shown, CancellationToken ct)
    {
        var category = a.CategoryId is { } cid ? await db.KbCategories.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var services = await db.KbArticleServices.AsNoTracking().Where(s => s.ArticleId == a.Id).Join(db.Services.AsNoTracking(), s => s.ServiceId, s => s.Id, (_, s) => new KbServiceRef(s.Id, s.Name)).ToListAsync(ct);
        var files = await db.KbFiles.AsNoTracking().Where(f => f.ArticleId == a.Id && f.DeletedAt == null).OrderBy(f => f.CreatedAt).Select(f => new KbFileItem(f.Id, f.FileName, f.ContentType, f.Size, f.CreatedAt)).ToListAsync(ct);
        var yes = await db.KbFeedback.CountAsync(f => f.ArticleId == a.Id && f.Helpful, ct); var no = await db.KbFeedback.CountAsync(f => f.ArticleId == a.Id && !f.Helpful, ct);
        var mine = await db.KbFeedback.AsNoTracking().Where(f => f.ArticleId == a.Id && f.UserId == r.Id).Select(f => (bool?)f.Helpful).FirstOrDefaultAsync(ct);
        IReadOnlyList<KbVersionItem>? versions = null;
        if (r.Write)
            versions = (await db.KbArticleVersions.AsNoTracking().Where(v => v.ArticleId == a.Id).OrderByDescending(v => v.Number).Take(50)
                .Select(v => new { v.Number, v.Title, v.Note, v.CreatedAt, By = db.Users.Where(u => u.Id == v.CreatedById).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct))
                .Select(v => new KbVersionItem(v.Number, v.Title, v.Note, v.By, v.CreatedAt, v.Number == a.PublishedVersion)).ToList();
        var today = Today;
        return new KbArticleDetail(a.Id, title, summary, body, a.CategoryId, category, Lower(a.Visibility), Lower(a.Status), a.PublishedVersion, a.LatestVersion, shown, a.LatestVersion > a.PublishedVersion,
            a.ReviewBy, r.Write && a.ReviewBy is { } rb && rb <= today, services, files, a.Views, r.Write ? yes : 0, r.Write ? no : 0, r.Write ? a.Deflections : 0, mine, versions,
            r.Write, r.Publish, a.UpdatedAt, a.PublishedAt, a.Version);
    }

    // ================================================================ writing
    public async Task<KbArticleDetail> SaveAsync(Guid? id, SaveKbArticleRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var r = await ReaderAsync(actor, ct);
        var e = new Dictionary<string, string>();
        var title = (req.Title ?? "").Trim(); var summary = Clean(req.Summary); var body = (req.Body ?? "").Replace("\r\n", "\n").Trim(); var note = Clean(req.Note);
        if (title.Length is < TitleMin or > TitleMax) e["title"] = $"Use {TitleMin}–{TitleMax} characters.";
        if (summary is { Length: > SummaryMax }) e["summary"] = $"Use at most {SummaryMax} characters.";
        if (body.Length == 0) e["body"] = "Write the article.";
        else if (body.Length > BodyMax) e["body"] = $"Use at most {BodyMax:N0} characters.";
        if (note is { Length: > NoteMax }) e["note"] = $"Use at most {NoteMax} characters.";
        var visibility = KbVisibility.Everyone;
        if (!Enum.TryParse(Clean(req.Visibility) ?? "everyone", true, out visibility) || !Enum.IsDefined(visibility) || int.TryParse(req.Visibility, out _)) e["visibility"] = "Use everyone or staff.";
        if (req.CategoryId is { } cid && !await db.KbCategories.AnyAsync(c => c.Id == cid && c.ArchivedAt == null, ct)) e["categoryId"] = "Choose an active category.";
        var services = (req.ServiceIds ?? []).Distinct().ToList();
        if (services.Count > MaxServices) e["serviceIds"] = $"At most {MaxServices} services.";
        else if (services.Count > 0 && await db.Services.CountAsync(s => services.Contains(s.Id) && s.ArchivedAt == null, ct) != services.Count) e["serviceIds"] = "Choose active services.";
        if (e.Count > 0) throw Invalid(e);

        var now = clock.UtcNow;
        KbArticle a;
        if (id is { } aid)
        {
            a = await db.KbArticles.FirstOrDefaultAsync(x => x.Id == aid, ct) ?? throw DomainException.NotFound("Article");
            if (req.Version is { } ver && ver != a.Version) throw DomainException.Conflict("This article was changed by someone else. Reload and try again.");
            a.Version++;
        }
        else
        {
            a = new KbArticle { CreatedById = actor.UserId, CreatedAt = now };
            db.KbArticles.Add(a);
        }
        // a new version only when the text changed (a metadata-only save keeps the history short)
        var last = a.LatestVersion == 0 ? null : await db.KbArticleVersions.AsNoTracking().FirstOrDefaultAsync(v => v.ArticleId == a.Id && v.Number == a.LatestVersion, ct);
        if (last is null || last.Title != title || last.Summary != summary || last.Body != body)
        {
            a.LatestVersion++;
            db.KbArticleVersions.Add(new KbArticleVersion { ArticleId = a.Id, Number = a.LatestVersion, Title = title, Summary = summary, Body = body, Note = note, CreatedById = actor.UserId, CreatedAt = now });
        }
        a.CategoryId = req.CategoryId; a.Visibility = visibility; a.ReviewBy = req.ReviewBy; a.UpdatedAt = now;
        if (a.PublishedVersion == 0) a.Title = title;          // lists show something sensible before the first publish
        var existing = await db.KbArticleServices.Where(s => s.ArticleId == a.Id).ToListAsync(ct);
        db.KbArticleServices.RemoveRange(existing.Where(s => !services.Contains(s.ServiceId)));
        foreach (var sid in services.Where(s => existing.All(x => x.ServiceId != s))) db.KbArticleServices.Add(new KbArticleService { ArticleId = a.Id, ServiceId = sid });
        audit.Record(id is null ? "kb.article.create" : "kb.article.update", "kb_article", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["title"] = title, ["version"] = a.LatestVersion.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This article was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("Another version was saved at the same moment. Reload and try again."); }
        return await GetAsync(a.Id, 0, actor, ct);
    }

    /// <summary>Publishes the latest version (or the one given): readers see it from now on.</summary>
    public async Task<KbArticleDetail> PublishAsync(Guid id, int? number, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.KbArticles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Article");
        var n = number is > 0 ? number.Value : a.LatestVersion;
        var v = await db.KbArticleVersions.AsNoTracking().FirstOrDefaultAsync(x => x.ArticleId == id && x.Number == n, ct) ?? throw DomainException.NotFound("Version");
        var now = clock.UtcNow;
        a.Title = v.Title; a.Summary = v.Summary; a.Body = v.Body; a.PublishedVersion = v.Number; a.Status = KbStatus.Published; a.PublishedAt = now; a.UpdatedAt = now; a.Version++;
        audit.Record("kb.article.publish", "kb_article", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = v.Title, ["version"] = v.Number.ToString() });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, 0, actor, ct);
    }

    public async Task<KbArticleDetail> SetArchivedAsync(Guid id, bool archived, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.KbArticles.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Article");
        a.Status = archived ? KbStatus.Archived : a.PublishedVersion > 0 ? KbStatus.Published : KbStatus.Draft;
        a.UpdatedAt = clock.UtcNow; a.Version++;
        audit.Record(archived ? "kb.article.archive" : "kb.article.restore", "kb_article", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = a.Title });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, 0, actor, ct);
    }

    // ================================================================ readers' answers
    public async Task<KbArticleDetail> FeedbackAsync(Guid id, KbFeedbackRequest req, SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await ReaderAsync(who, ct);
        if (!await Readable(db.KbArticles, r).AnyAsync(a => a.Id == id, ct)) throw DomainException.NotFound("Article");
        var f = await db.KbFeedback.FirstOrDefaultAsync(x => x.ArticleId == id && x.UserId == who.UserId, ct);
        if (f is null) db.KbFeedback.Add(f = new KbFeedback { ArticleId = id, UserId = who.UserId });
        f.Helpful = req.Helpful; f.UpdatedAt = clock.UtcNow;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("Your answer was saved at the same moment from somewhere else. Reload and try again."); }
        var a = await db.KbArticles.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return await BuildAsync(a, r, a.Title, a.Summary, a.Body, a.PublishedVersion, ct);
    }

    /// <summary>KB-002. Published articles the person may read that match what they are typing; the service's own articles first.</summary>
    public async Task<IReadOnlyList<KbSuggestion>> SuggestAsync(string? q, Guid? serviceId, SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await ReaderAsync(who, ct);
        var readable = Readable(db.KbArticles.AsNoTracking(), r);
        var linked = serviceId is { } sid
            ? await readable.Where(a => db.KbArticleServices.Any(s => s.ArticleId == a.Id && s.ServiceId == sid)).OrderBy(a => a.Title).Take(5).Select(a => new KbSuggestion(a.Id, a.Title, a.Summary, true)).ToListAsync(ct)
            : [];
        var text = Clean(q);
        if (text is null || text.Length < 3) return linked;
        var ids = linked.Select(l => l.Id).ToList();
        var found = await Matching(readable, text).Where(a => !ids.Contains(a.Id)).OrderByDescending(a => a.PublishedAt).Take(5 - Math.Min(5, linked.Count) + 3)
            .Select(a => new KbSuggestion(a.Id, a.Title, a.Summary, false)).ToListAsync(ct);
        return [.. linked.Concat(found).Take(8)];
    }

    /// <summary>KB-002. The person read a suggested article and did not need to ask after all.</summary>
    public async Task DeflectAsync(Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await ReaderAsync(who, ct);
        if (!await Readable(db.KbArticles, r).AnyAsync(a => a.Id == id, ct)) throw DomainException.NotFound("Article");
        await db.KbArticles.Where(a => a.Id == id).ExecuteUpdateAsync(u => u.SetProperty(a => a.Deflections, a => a.Deflections + 1), ct);
    }

    // ================================================================ files
    public async Task<KbArticleDetail> AddFileAsync(Guid id, string? fileName, Stream content, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.KbArticles.AnyAsync(a => a.Id == id, ct)) throw DomainException.NotFound("Article");
        if (await db.KbFiles.CountAsync(f => f.ArticleId == id && f.DeletedAt == null, ct) >= MaxFiles) throw Invalid(new() { ["file"] = $"An article can have at most {MaxFiles} files." });
        using var file = await attachments.CheckAsync(fileName, content, "kb_article", id.ToString(), actor, client, ct);
        var f = new KbFile { ArticleId = id, FileName = file.Name, ContentType = file.ContentType, Size = file.Size, Sha256 = file.Sha256, UploadedById = actor.UserId, CreatedAt = clock.UtcNow };
        f.StorageKey = attachments.NewKey(f.Id);
        await storage.SaveAsync(f.StorageKey, file.Content, ct);
        db.KbFiles.Add(f);
        audit.Record("kb.file.add", "kb_article", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["file"] = f.FileName, ["sha256"] = f.Sha256 });
        try { await db.SaveChangesAsync(ct); }
        catch { await storage.DeleteAsync(f.StorageKey, CancellationToken.None); throw; }
        return await GetAsync(id, 0, actor, ct);
    }

    /// <summary>A file is readable by whoever can read its article (writers: any article).</summary>
    public async Task<KbFileDownload> OpenFileAsync(Guid id, Guid fileId, SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await ReaderAsync(who, ct);
        var visible = r.Write ? await db.KbArticles.AnyAsync(a => a.Id == id, ct) : await Readable(db.KbArticles, r).AnyAsync(a => a.Id == id, ct);
        if (!visible) throw DomainException.NotFound("File");
        var f = await db.KbFiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fileId && x.ArticleId == id && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("File");
        var s = await storage.OpenAsync(f.StorageKey, ct) ?? throw DomainException.NotFound("File");
        return new KbFileDownload(s, f.FileName, f.ContentType, f.ContentType.StartsWith("image/"));
    }

    public async Task<KbArticleDetail> DeleteFileAsync(Guid id, Guid fileId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var f = await db.KbFiles.FirstOrDefaultAsync(x => x.Id == fileId && x.ArticleId == id && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("File");
        f.DeletedAt = clock.UtcNow;
        audit.Record("kb.file.delete", "kb_article", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["file"] = f.FileName });
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(f.StorageKey, ct);
        return await GetAsync(id, 0, actor, ct);
    }
    // ================================================================ summary (module analytics, docs/52)
    /// <summary>For the people who write the knowledge base (<c>kb.write</c>): articles by state, the ones due for review, reads, requests avoided
    /// ("this solved my problem"), helpful votes, the most-read articles and published articles per category.</summary>
    public async Task<KbSummary> SummaryAsync(CancellationToken ct = default)
    {
        var today = Today;
        var states = await db.KbArticles.AsNoTracking().GroupBy(a => a.Status).Select(g => new
        {
            g.Key, N = g.Count(), Review = g.Count(a => a.ReviewBy != null && a.ReviewBy <= today), Views = g.Sum(a => a.Views), Deflections = g.Sum(a => a.Deflections),
        }).ToListAsync(ct);
        var votes = await db.KbFeedback.AsNoTracking().GroupBy(f => f.Helpful).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var top = await db.KbArticles.AsNoTracking().Where(a => a.Status == KbStatus.Published).OrderByDescending(a => a.Views).ThenBy(a => a.Title).Take(5)
            .Select(a => new { a.Id, a.Title, a.Views, a.Deflections, Yes = db.KbFeedback.Count(f => f.ArticleId == a.Id && f.Helpful), No = db.KbFeedback.Count(f => f.ArticleId == a.Id && !f.Helpful) }).ToListAsync(ct);
        var perCategory = await db.KbArticles.AsNoTracking().Where(a => a.Status == KbStatus.Published).GroupBy(a => a.CategoryId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var names = await db.KbCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        int Of(KbStatus s) => states.Where(x => x.Key == s).Sum(x => x.N);
        return new KbSummary(Of(KbStatus.Published), Of(KbStatus.Draft), Of(KbStatus.Archived), states.Where(x => x.Key != KbStatus.Archived).Sum(x => x.Review),
            states.Sum(x => x.Views), states.Sum(x => x.Deflections), votes.Where(x => x.Key).Sum(x => x.N), votes.Where(x => !x.Key).Sum(x => x.N),
            [.. top.Select(a => new KbTop(a.Id, a.Title, a.Views, a.Deflections, a.Yes + a.No == 0 ? null : (int)Math.Round(100.0 * a.Yes / (a.Yes + a.No))))],
            [.. perCategory.OrderByDescending(x => x.N).Select(x => new CountItem(x.Key?.ToString() ?? "none", x.Key is { } id && names.TryGetValue(id, out var n) ? n : "", x.N))]);
    }
}
