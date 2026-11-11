namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// BKG-004. The trip of a vehicle booking: who drives (empty = the person it is booked for), where to and why; the odometer when the car is picked up and
/// returned; fuel, toll and parking costs (they feed the cost report); damage noticed and the return checklist. One per booking.
/// </summary>
public class VehicleTrip : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid BookingId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid? DriverId { get; set; }
    /// <summary>The person booking asked General Affairs for a driver (cleared when one is assigned).</summary>
    public bool NeedsDriver { get; set; }
    public string? Destination { get; set; }
    public string? Purpose { get; set; }
    public int? OdometerOut { get; set; }
    public DateTimeOffset? PickedUpAt { get; set; }
    public Guid? PickedUpById { get; set; }
    public int? OdometerIn { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public Guid? ReturnedById { get; set; }
    public decimal? FuelCost { get; set; }
    public decimal? TollCost { get; set; }
    public decimal? ParkingCost { get; set; }
    public string? DamageNotes { get; set; }
    /// <summary>The return checklist as answered: JSON <c>[{ "item": "fuel", "ok": true }]</c> over <see cref="VehicleRules.ReturnChecklist"/>.</summary>
    public string ChecklistJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum ResourceDocumentKind { Registration = 0, Insurance = 1, Inspection = 2, Service = 3, Other = 4 }

/// <summary>BKG-004. A dated document of a resource, mostly vehicles: STNK (registration), insurance, KIR (roadworthiness inspection), the service due date.
/// People who maintain resources are reminded 30 days before it expires and on the day it does.</summary>
public class ResourceDocument : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ResourceId { get; set; }
    public ResourceDocumentKind Kind { get; set; }
    public string? Number { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public string? Note { get; set; }
    /// <summary>The last reminder sent: <c>soon</c> (30 days ahead) or <c>expired</c>; reset when the date changes.</summary>
    public string? Reminded { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>BKG-004. A driver's licence (SIM): a trip refuses a driver whose recorded licence has expired by the start. Reminders as for documents, to the driver too.</summary>
public class DriverLicence : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid UserId { get; set; }
    public string Number { get; set; } = "";
    /// <summary>The licence class, e.g. A, B1, B2 (SIM A, B1 umum).</summary>
    public string? Class { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public string? Reminded { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class VehicleRules
{
    public static readonly string[] ReturnChecklist = ["fuel", "clean", "no_new_damage", "keys", "documents"];
    public const int NoticeDays = 30;
    public const int MaxOdometer = 2_000_000;
    public const decimal MaxCost = 100_000_000m;
}
