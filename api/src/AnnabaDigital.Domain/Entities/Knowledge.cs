namespace AnnabaDigital.Domain.Entities;

/// <summary>Who may read a published article: everyone signed in to the organisation, or staff only (<c>kb.read</c>). Stored as text.</summary>
public enum KbVisibility { Everyone = 0, Staff = 1 }

/// <summary>An article is a draft until first published; archived articles are hidden from readers. Stored as text.</summary>
public enum KbStatus { Draft = 0, Published = 1, Archived = 2 }

/// <summary>KB-001. A shelf of the knowledge base (for example "Printers", "Building rules").</summary>
public class KbCategory : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// KB-001/002/003. A knowledge-base article. Its text lives in numbered <see cref="KbArticleVersion"/>s: writers save new versions, and publishing copies one into
/// <see cref="Title"/>, <see cref="Summary"/> and <see cref="Body"/> — what readers see and search. So a published article can be improved in a draft without readers seeing
/// half-finished text. Category, visibility, review date and linked services apply at once.
/// </summary>
public class KbArticle : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? CategoryId { get; set; }
    public KbVisibility Visibility { get; set; } = KbVisibility.Everyone;
    public KbStatus Status { get; set; } = KbStatus.Draft;
    /// <summary>The published text (empty until first published).</summary>
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string Body { get; set; } = "";
    public int PublishedVersion { get; set; }
    public int LatestVersion { get; set; }
    /// <summary>When the content should be checked again (shown as "review due" to writers).</summary>
    public DateOnly? ReviewBy { get; set; }
    public int Views { get; set; }
    /// <summary>People who said a suggested article solved their problem, so they did not file the request (KB-002).</summary>
    public int Deflections { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>One saved text of an article. Never changed once written.</summary>
public class KbArticleVersion : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ArticleId { get; set; }
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    /// <summary>Markdown (headings, lists, emphasis, links, images of the article's files).</summary>
    public string Body { get; set; } = "";
    public string? Note { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>KB-003. An article shown with a catalog service ("before you ask").</summary>
public class KbArticleService : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid ArticleId { get; set; }
    public Guid ServiceId { get; set; }
}

/// <summary>KB-001. One reader's "was this helpful?" answer (one per person, changeable).</summary>
public class KbFeedback : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid ArticleId { get; set; }
    public Guid UserId { get; set; }
    public bool Helpful { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>KB-001/003. A file of an article: an image used in its text, or a policy or SOP document.</summary>
public class KbFile : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ArticleId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public Guid UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
