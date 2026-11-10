namespace AnnabaDigital.Domain.Entities;

/// <summary>CST-001. The organisation's currency (a label: amounts are never converted) and the hourly labour rate that turns logged time into cost.</summary>
public class CostSettings : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    /// <summary>ISO 4217 code, IDR by default.</summary>
    public string Currency { get; set; } = "IDR";
    /// <summary>Per hour; null = logged time has no cost.</summary>
    public decimal? LabourRatePerHour { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>CST-002. Who pays: a cost centre covers the places mapped to it (and the places below them, unless one of those is mapped elsewhere).</summary>
public class CostCenter : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>A place paid by a cost centre. A place belongs to at most one cost centre.</summary>
public class CostCenterPlace : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid LocationId { get; set; }
    public Guid CostCenterId { get; set; }
}

/// <summary>What a budget covers. Stored as text.</summary>
public enum BudgetScope { CostCenter = 0, Site = 1, Category = 2 }

/// <summary>CST-003. A year's budget for a cost centre, a site (top-level place) or a service category; consumption is the costs recorded in that year in its scope.</summary>
public class Budget : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public BudgetScope Scope { get; set; }
    public Guid ScopeId { get; set; }
    public int Year { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    /// <summary>0 none, 1 the 80 % alert went out, 2 the 100 % alert went out.</summary>
    public int AlertLevel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
