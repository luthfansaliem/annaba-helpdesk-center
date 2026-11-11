namespace AnnabaDigital.Domain.Entities;

public enum ResourceKind { Room = 0, Vehicle = 1, Parking = 2, Equipment = 3 }
/// <summary>Pending and Confirmed hold the slot; Cancelled, Rejected (not approved) and Released (nobody checked in) free it.</summary>
public enum BookingStatus { Confirmed = 0, Cancelled = 1, Pending = 2, Rejected = 3, Released = 4 }

/// <summary>
/// BKG-001. Something people book by the hour: a meeting room, a pool vehicle, a parking slot or shared equipment. Opening hours are in the place's time
/// zone; a buffer keeps the resource free after each booking (cleaning, handover). Only the roles listed in <see cref="AllowedRoleIds"/> may book it
/// when the list is not empty. Archived resources keep their history and cannot be booked.
/// </summary>
public class BookableResource : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public ResourceKind Kind { get; set; } = ResourceKind.Room;
    public string Name { get; set; } = "";
    /// <summary>Short code shown on screens and calendars, e.g. <c>R-301</c> or a plate number.</summary>
    public string? Code { get; set; }
    public string? Description { get; set; }
    public Guid? LocationId { get; set; }
    /// <summary>Seats in a room or a vehicle; empty for parking and equipment.</summary>
    public int? Capacity { get; set; }
    /// <summary>Lower-case tags such as <c>projector</c>, <c>video_call</c>, <c>whiteboard</c>.</summary>
    public List<string> Features { get; set; } = [];
    /// <summary>Opening hours (local time). Both empty means bookable around the clock.</summary>
    public TimeOnly? OpenFrom { get; set; }
    public TimeOnly? OpenUntil { get; set; }
    /// <summary>Days it can be booked, Monday = bit 0 … Sunday = bit 6. 127 = every day.</summary>
    public int OpenDays { get; set; } = 127;
    public int BufferMinutes { get; set; }
    /// <summary>The longest single booking in minutes; empty means up to <see cref="BookingRules.MaxDays"/> days.</summary>
    public int? MaxMinutes { get; set; }
    /// <summary>BKG-005 (R3-2): bookings wait for someone with <c>bookings.manage</c> to approve them; until then they hold the slot.</summary>
    public bool NeedsApproval { get; set; }
    /// <summary>BKG-005 (R3-2): a booking nobody checked in to within this many minutes of its start is released. Empty: no check-in needed.</summary>
    public int? CheckInMinutes { get; set; }
    /// <summary>Roles that may book it; empty means everyone who may book.</summary>
    public List<Guid> AllowedRoleIds { get; set; } = [];
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// BKG-002. One booking of one resource. <see cref="BlockedUntil"/> is the end plus the resource's buffer at booking time; the database refuses two
/// live (pending or confirmed) bookings of the same resource whose [start, blocked-until) ranges overlap, so concurrent requests cannot double-book.
/// Bookings made together as a repeating series share a <see cref="SeriesId"/>; each one can still be moved or cancelled on its own.
/// </summary>
public class Booking : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ResourceId { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Who the booking is for (and who is reminded); differs from <see cref="CreatedById"/> when someone books for a colleague.</summary>
    public Guid BookedForId { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset BlockedUntil { get; set; }
    public int? Attendees { get; set; }
    public string? Note { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public Guid? SeriesId { get; set; }
    /// <summary>How the series repeats, e.g. <c>week</c>, <c>weekday</c> or <c>day</c> (shown on every booking of it).</summary>
    public string? Repeat { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public DateTimeOffset? RemindedAt { get; set; }
    /// <summary>Who approved or rejected a booking that needed approval, when, and why (a rejection needs a reason).</summary>
    public Guid? DecidedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledById { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public bool HoldsSlot => Status is BookingStatus.Confirmed or BookingStatus.Pending;
}

/// <summary>BKG-005. A person's private calendar feed (ICS): the secret link is shown once and stored only as a hash; making a new one retires the old.</summary>
public class BookingFeed : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
}

public static class BookingRules
{
    public const int MaxDays = 14;
    public const int TitleMax = 150;
    public const int NoteMax = 1000;
    /// <summary>A booking may start this long ago (a meeting already under way is booked on the spot).</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);
    /// <summary>How far ahead people can book.</summary>
    public const int HorizonDays = 365;
    public const int StepMinutes = 5;
    /// <summary>The reminder goes this long before the start; check-in opens this long before the start too.</summary>
    public const int ReminderMinutes = 30;
    public const int CheckInEarlyMinutes = 15;
    public const int MaxOccurrences = 52;
    public static readonly int[] CheckInChoices = [10, 15, 20, 30, 60];
    public static readonly string[] KnownFeatures = ["projector", "screen", "video_call", "whiteboard", "phone", "accessible", "air_conditioning", "ev_charger", "four_wheel_drive"];

    public static bool OpenOn(int openDays, DayOfWeek d) => (openDays & (1 << (((int)d + 6) % 7))) != 0;
}
