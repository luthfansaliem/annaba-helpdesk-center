using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>BKG-001/002/003/005. Bookable resources and bookings. Anyone signed in books for themselves (like making a request; vendor accounts never reach these routes);
/// maintaining resources needs <c>resources.manage</c>.
/// Seeing others' details and managing others' bookings are decided inside the service (<c>bookings.read_all</c>, <c>bookings.manage</c>).</summary>
public static class BookingEndpoints
{
    public static void MapBookings(this IEndpointRouteBuilder app)
    {
        var r = app.MapGroup("/api/resources");
        r.MapGet("/", async (string? kind, Guid? locationId, string? q, int? capacity, string? feature, bool? archived, BookingService svc, HttpContext ctx)
            => Results.Ok(await svc.ListResourcesAsync(kind, locationId, q, capacity, feature, archived ?? false, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        r.MapGet("/{id:guid}", async (Guid id, BookingService svc, HttpContext ctx) => Results.Ok(await svc.GetResourceAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        r.MapPost("/", async (SaveResourceRequest body, BookingService svc, HttpContext ctx) =>
        {
            var x = await svc.SaveResourceAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/resources/{x.Id}", x);
        }).RequirePermission(Permissions.ResourcesManage);
        r.MapPut("/{id:guid}", async (Guid id, SaveResourceRequest body, BookingService svc, HttpContext ctx) => Results.Ok(await svc.SaveResourceAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        r.MapPost("/{id:guid}/archive", async (Guid id, BookingService svc, HttpContext ctx) => Results.Ok(await svc.ArchiveResourceAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        r.MapPost("/{id:guid}/restore", async (Guid id, BookingService svc, HttpContext ctx) => Results.Ok(await svc.ArchiveResourceAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);

        var b = app.MapGroup("/api/bookings");
        b.MapGet("/", async (string? scope, DateTimeOffset? from, DateTimeOffset? to, Guid? resourceId, bool? cancelled, int? page, int? pageSize, BookingService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(scope, from, to, resourceId, cancelled ?? false, page ?? 1, pageSize ?? 25, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        b.MapGet("/availability", async (DateTimeOffset? from, DateTimeOffset? to, string? kind, Guid? locationId, int? capacity, string? feature, Guid? resourceId, BookingService svc, HttpContext ctx)
            => Results.Ok(await svc.AvailabilityAsync(from, to, kind, locationId, capacity, feature, resourceId, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        b.MapGet("/summary", async (BookingService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        b.MapGet("/{id:guid}", async (Guid id, BookingService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        b.MapPost("/", async (SaveBookingRequest body, BookingService svc, HttpContext ctx) =>
        {
            var x = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/bookings/{x.Id}", x);
        }).RequireAuthorization();
        b.MapPut("/{id:guid}", async (Guid id, SaveBookingRequest body, BookingService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        b.MapPost("/{id:guid}/cancel", async (Guid id, CancelBookingRequest body, BookingService svc, HttpContext ctx) => Results.Ok(await svc.CancelAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();

        // R3-2: repeating bookings, approval, check-in and the personal calendar feed
        b.MapPost("/series", async (SaveSeriesRequest body, BookingService svc, HttpContext ctx) => Results.Ok(await svc.SaveSeriesAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        b.MapPost("/{id:guid}/approve", async (Guid id, DecideBookingRequest body, BookingService svc, HttpContext ctx) => Results.Ok(await svc.DecideAsync(id, true, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BookingsManage);
        b.MapPost("/{id:guid}/reject", async (Guid id, DecideBookingRequest body, BookingService svc, HttpContext ctx) => Results.Ok(await svc.DecideAsync(id, false, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BookingsManage);
        b.MapPost("/{id:guid}/check-in", async (Guid id, BookingService svc, HttpContext ctx) => Results.Ok(await svc.CheckInAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        b.MapGet("/feed", async (BookingService svc, HttpContext ctx) => Results.Ok(await svc.FeedInfoAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        b.MapPost("/feed", async (BookingService svc, HttpContext ctx) => Results.Ok(await svc.NewFeedAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        b.MapDelete("/feed", async (BookingService svc, HttpContext ctx) => Results.Ok(await svc.RevokeFeedAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();

        // The calendar app fetching the feed has no session: the secret token in the link is the credential (hashed at rest, revocable, read-only).
        app.MapGet("/api/calendar/{token}", async (string token, BookingService svc, HttpContext ctx) =>
        {
            var ics = await svc.FeedAsync(token, ctx.RequestAborted);
            ctx.Response.Headers.CacheControl = "private, no-store";
            return Results.Text(ics, "text/calendar; charset=utf-8");
        }).AllowAnonymous();
    }
}
