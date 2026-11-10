using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Forms;

namespace AnnabaDigital.Application.Helpdesk;

/// <summary>
/// CAT-002. The starter General Affairs catalog every organisation begins with: published, fully editable, and valid by construction
/// (a unit test runs every template form through the same validator the admin screens use).
/// </summary>
public static class CatalogTemplates
{
    public sealed record ServiceTemplate(string Name, string Description, string Icon, TicketPriority Priority, FormField[] Fields);
    public sealed record CategoryTemplate(string Name, string Description, string Icon, ServiceTemplate[] Services);

    private static FormField Txt(string key, string label, bool req = false, int? max = null) => new(key, FieldTypes.Text, label, Required: req, MaxLength: max);
    private static FormField Long(string key, string label, bool req = false, string? help = null) => new(key, FieldTypes.LongText, label, help, req);
    private static FormField Num(string key, string label, bool req = false, decimal? min = null, decimal? max = null) => new(key, FieldTypes.Number, label, Required: req, Min: min, Max: max);
    private static FormField Day(string key, string label, bool req = false) => new(key, FieldTypes.Date, label, Required: req);
    private static FormField When(string key, string label, bool req = false) => new(key, FieldTypes.DateTime, label, Required: req);
    private static FormField Yes(string key, string label) => new(key, FieldTypes.YesNo, label);
    private static FormField Loc(string key, string label, bool req = false) => new(key, FieldTypes.Location, label, Required: req);
    private static FormField Person(string key, string label, bool req = false) => new(key, FieldTypes.User, label, Required: req);
    private static FormField Photos(string key, string label, int max = 3) => new(key, FieldTypes.File, label, "Optional. A photo helps us bring the right tools.", Max: max);
    private static FormField Pick(string key, string label, bool req, params (string Value, string Label)[] options)
        => new(key, FieldTypes.Select, label, Required: req, Options: [.. options.Select(o => new FieldOption(o.Value, o.Label))]);
    private static FormField If(FormField f, string field, string op, params string[] values) => f with { VisibleWhen = new FieldCondition(field, op, values.Length == 0 ? null : values) };

    public static readonly IReadOnlyList<CategoryTemplate> All =
    [
        new("Facilities & Maintenance", "Repairs and upkeep of the building and its equipment.", "wrench",
        [
            new("Air conditioning", "Cooling that is too weak, noisy or leaking.", "wind", TicketPriority.Medium,
            [
                Loc("location", "Where is the problem?", true),
                Pick("problem", "What is wrong?", true, ("not_cooling", "Not cooling"), ("noisy", "Noisy"), ("leaking", "Leaking water"), ("other", "Something else")),
                Long("details", "Tell us more", true),
                Yes("disruptive", "Is it stopping people from working?"),
                Photos("photos", "Photo of the problem"),
            ]),
            new("Electrical & lighting", "Lights, sockets and power problems.", "zap", TicketPriority.Medium,
            [
                Loc("location", "Where is the problem?", true),
                Pick("problem", "What is wrong?", true, ("light", "Light not working"), ("socket", "Socket not working"), ("outage", "No power"), ("other", "Something else")),
                Long("details", "Tell us more", true),
                Yes("unsafe", "Is there a safety risk (sparks, burning smell)?"),
                Photos("photos", "Photo of the problem"),
            ]),
            new("Plumbing & water", "Leaks, blockages and water supply.", "droplet", TicketPriority.Medium,
            [
                Loc("location", "Where is the problem?", true),
                Pick("problem", "What is wrong?", true, ("leak", "Leak"), ("blocked", "Blocked drain or toilet"), ("no_water", "No water"), ("other", "Something else")),
                Long("details", "Tell us more", true),
                Photos("photos", "Photo of the problem"),
            ]),
            new("Furniture & fittings", "Broken or missing furniture, doors, locks and blinds.", "armchair", TicketPriority.Low,
            [
                Loc("location", "Where is it?", true),
                Txt("item", "What needs attention?", true, 100),
                Long("details", "Tell us more"),
                Photos("photos", "Photo of the item"),
            ]),
        ]),
        new("Cleaning & Hygiene", "Cleaning, waste and pest control.", "sparkles",
        [
            new("Cleaning request", "Spills, restrooms, waste and extra cleaning.", "spray-can", TicketPriority.Medium,
            [
                Loc("location", "Where?", true),
                Pick("type", "What is needed?", true, ("spill", "Spill or stain"), ("restroom", "Restroom"), ("waste", "Waste removal"), ("deep_clean", "Deep clean"), ("other", "Something else")),
                Long("details", "Tell us more"),
            ]),
            new("Pest control", "Insects, rodents and other pests.", "bug", TicketPriority.Medium,
            [
                Loc("location", "Where did you see it?", true),
                Txt("pest", "What kind of pest?", true, 100),
                Long("details", "Tell us more"),
            ]),
        ]),
        new("Office Supplies", "Stationery and pantry items.", "package",
        [
            new("Stationery request (ATK)", "Pens, paper, folders and other office stationery.", "pencil", TicketPriority.Low,
            [
                Long("items", "What do you need?", true, "One item per line, with the quantity."),
                Num("quantity", "Total number of items", false, 1, 1000),
                Day("needed_by", "Needed by"),
                Loc("deliver_to", "Deliver to", true),
            ]),
            new("Pantry supplies", "Coffee, tea, water and snacks.", "coffee", TicketPriority.Low,
            [
                Loc("location", "Which pantry?", true),
                Long("items", "What is running low?", true),
            ]),
        ]),
        new("Transport & Vehicles", "Company cars and drivers.", "car",
        [
            new("Pool car booking", "Book a company car, with or without a driver.", "car", TicketPriority.Medium,
            [
                When("start", "Pick-up time", true),
                When("end", "Return time", true),
                Txt("destination", "Destination", true, 150),
                Num("passengers", "Number of passengers", true, 1, 20),
                Yes("driver", "Do you need a driver?"),
                Long("purpose", "Purpose of the trip", true),
            ]),
        ]),
        new("Rooms & Events", "Meeting rooms and event set-up.", "calendar",
        [
            new("Meeting room booking", "Reserve a room and its set-up.", "door-open", TicketPriority.Low,
            [
                Loc("room", "Which room?", true),
                When("start", "Starts", true),
                When("end", "Ends", true),
                Num("attendees", "Number of attendees", true, 1, 500),
                Pick("layout", "Room layout", false, ("boardroom", "Boardroom"), ("classroom", "Classroom"), ("u_shape", "U-shape"), ("theatre", "Theatre")),
                Yes("catering", "Do you need catering?"),
                If(Long("catering_details", "What catering?", true), "catering", ConditionOps.IsEqual, "true"),
            ]),
            new("Event set-up", "Chairs, stage, sound and decorations for an event.", "party-popper", TicketPriority.Medium,
            [
                Loc("location", "Where is the event?", true),
                Day("event_date", "Event date", true),
                Num("attendees", "Expected attendees", true, 1, 5000),
                Long("requirements", "What do you need set up?", true),
            ]),
        ]),
        new("Visitors & Access", "Guests, ID cards and building access.", "id-card",
        [
            new("Visitor registration", "Announce a visitor so reception is ready.", "user-plus", TicketPriority.Low,
            [
                Txt("visitor_name", "Visitor's name", true, 100),
                Txt("company", "Visitor's company", false, 100),
                When("arrival", "Expected arrival", true),
                Person("host", "Who are they visiting?", true),
                Long("purpose", "Purpose of the visit"),
            ]),
            new("ID card & access", "New, lost, damaged or expiring ID cards.", "badge", TicketPriority.Medium,
            [
                Pick("kind", "What do you need?", true, ("new", "New card"), ("lost", "Replace a lost card"), ("damaged", "Replace a damaged card"), ("renew", "Renew")),
                Person("employee", "Card holder", true),
                If(Yes("blocked", "Should the lost card be blocked now?"), "kind", ConditionOps.IsEqual, "lost"),
                Long("details", "Anything else we should know?"),
            ]),
        ]),
        new("Mail & Courier", "Outgoing packages and documents.", "mail",
        [
            new("Courier & package dispatch", "Send a document or parcel.", "truck", TicketPriority.Medium,
            [
                Long("address", "Delivery address", true),
                Txt("recipient", "Recipient", true, 100),
                Pick("speed", "How fast?", true, ("same_day", "Same day"), ("next_day", "Next day"), ("regular", "Regular")),
                Long("contents", "What are you sending?", true),
            ]),
        ]),
        new("Moves & Relocation", "Moving people, desks and equipment.", "move",
        [
            new("Desk or office move", "Relocate a person, desk or small team.", "move", TicketPriority.Medium,
            [
                Loc("from", "Moving from", true),
                Loc("to", "Moving to", true),
                Day("move_date", "Preferred date", true),
                Long("items", "What needs to move?", true),
            ]),
        ]),
    ];

    /// <summary>Idempotent: installs the starter catalog only for an organisation that has no categories at all (so it never overwrites or duplicates).</summary>
    public static async Task SeedAsync(IAppDb db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await db.ServiceCategories.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId, ct)) return;
        var order = 0;
        foreach (var cat in All)
        {
            var c = new ServiceCategory { TenantId = tenantId, Name = cat.Name, Description = cat.Description, Icon = cat.Icon, SortOrder = order += 10, CreatedAt = now, UpdatedAt = now };
            db.ServiceCategories.Add(c);
            foreach (var svc in cat.Services)
            {
                var s = new ServiceItem
                {
                    TenantId = tenantId, CategoryId = c.Id, Name = svc.Name, Description = svc.Description, Icon = svc.Icon, DefaultPriority = svc.Priority,
                    IsPublished = true, CurrentFormVersion = 1, CreatedAt = now, UpdatedAt = now,
                };
                db.Services.Add(s);
                var fields = FormEngine.Normalize(svc.Fields);
                db.ServiceFormVersions.Add(new ServiceFormVersion
                {
                    TenantId = tenantId, ServiceId = s.Id, Number = 1, FieldsJson = FormEngine.Serialize(fields), FieldCount = fields.Count, Note = "Starter template", CreatedAt = now,
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
