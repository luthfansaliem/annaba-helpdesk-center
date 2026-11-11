using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>BKG-004 and RPT-005. Vehicle trips (the people involved in the booking, or <c>bookings.manage</c>, decided in the service), vehicle documents and
/// driver licences (<c>resources.manage</c>), the driver picker (<c>bookings.manage</c>) and the utilisation report (<c>bookings.read_all</c>).</summary>
public static class VehicleEndpoints
{
    public static void MapVehicles(this IEndpointRouteBuilder app)
    {
        var t = app.MapGroup("/api/bookings/{id:guid}/trip");
        t.MapGet("/", async (Guid id, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.GetTripAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        t.MapPut("/", async (Guid id, SaveTripRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.SaveTripAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        t.MapPost("/pick-up", async (Guid id, PickUpRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.PickUpAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        t.MapPost("/return", async (Guid id, ReturnRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.ReturnAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();

        var d = app.MapGroup("/api/resources/{id:guid}/documents");
        d.MapGet("/", async (Guid id, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.DocumentsAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        d.MapPost("/", async (Guid id, SaveResourceDocumentRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.SaveDocumentAsync(id, null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        d.MapPut("/{docId:guid}", async (Guid id, Guid docId, SaveResourceDocumentRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.SaveDocumentAsync(id, docId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        d.MapDelete("/{docId:guid}", async (Guid id, Guid docId, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.DeleteDocumentAsync(id, docId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);

        var dr = app.MapGroup("/api/drivers");
        dr.MapGet("/", async (VehicleService svc, HttpContext ctx) => Results.Ok(await svc.DriversAsync(ctx.RequestAborted))).RequirePermission(Permissions.BookingsManage);
        dr.MapGet("/licences", async (VehicleService svc, HttpContext ctx) => Results.Ok(await svc.LicencesAsync(ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        dr.MapPost("/licences", async (AddLicenceRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.AddLicenceAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        dr.MapPut("/{userId:guid}/licence", async (Guid userId, SaveLicenceRequest body, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.SaveLicenceAsync(userId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);
        dr.MapDelete("/{userId:guid}/licence", async (Guid userId, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.DeleteLicenceAsync(userId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ResourcesManage);

        app.MapGet("/api/reports/utilisation", async (int? days, string? kind, VehicleService svc, HttpContext ctx) => Results.Ok(await svc.UtilisationAsync(days, kind, ctx.RequestAborted))).RequirePermission(Permissions.BookingsReadAll);
    }
}
