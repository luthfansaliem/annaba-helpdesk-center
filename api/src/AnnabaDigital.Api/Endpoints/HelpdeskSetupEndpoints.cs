using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Helpdesk setup: locations, business calendars, teams. Every route declares exactly one permission (the deny-by-default coverage test enforces it).</summary>
public static class HelpdeskSetupEndpoints
{
    public static void MapHelpdeskSetup(this IEndpointRouteBuilder app)
    {
        var loc = app.MapGroup("/api/locations");
        loc.MapGet("/", async (bool? includeArchived, LocationService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.LocationsRead);
        loc.MapGet("/{id:guid}", async (Guid id, LocationService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.LocationsRead);
        loc.MapPost("/", async (SaveLocationRequest body, LocationService svc, HttpContext ctx) =>
        {
            var l = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/locations/{l.Id}", l);
        }).RequirePermission(Permissions.LocationsManage);
        loc.MapPatch("/{id:guid}", async (Guid id, SaveLocationRequest body, LocationService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LocationsManage);
        loc.MapPost("/{id:guid}/archive", async (Guid id, LocationService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LocationsManage);
        loc.MapPost("/{id:guid}/restore", async (Guid id, LocationService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.LocationsManage);

        var cal = app.MapGroup("/api/calendars");
        cal.MapGet("/", async (bool? includeArchived, CalendarService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.CalendarsRead);
        cal.MapGet("/{id:guid}", async (Guid id, CalendarService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.CalendarsRead);
        cal.MapPost("/", async (SaveCalendarRequest body, CalendarService svc, HttpContext ctx) =>
        {
            var c = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/calendars/{c.Id}", c);
        }).RequirePermission(Permissions.CalendarsManage);
        cal.MapPut("/{id:guid}", async (Guid id, SaveCalendarRequest body, CalendarService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CalendarsManage);
        cal.MapPost("/{id:guid}/default", async (Guid id, CalendarService svc, HttpContext ctx)
            => Results.Ok(await svc.SetDefaultAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CalendarsManage);
        cal.MapPost("/{id:guid}/archive", async (Guid id, CalendarService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CalendarsManage);
        cal.MapPost("/{id:guid}/restore", async (Guid id, CalendarService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CalendarsManage);

        var team = app.MapGroup("/api/teams");
        team.MapGet("/", async (bool? includeArchived, TeamService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.TeamsRead);
        team.MapGet("/{id:guid}", async (Guid id, TeamService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.TeamsRead);
        team.MapPost("/", async (SaveTeamRequest body, TeamService svc, HttpContext ctx) =>
        {
            var t = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/teams/{t.Id}", t);
        }).RequirePermission(Permissions.TeamsManage);
        team.MapPatch("/{id:guid}", async (Guid id, SaveTeamRequest body, TeamService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TeamsManage);
        team.MapPut("/{id:guid}/members", async (Guid id, SetMembersRequest body, TeamService svc, HttpContext ctx)
            => Results.Ok(await svc.SetMembersAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TeamsManage);
        team.MapPost("/{id:guid}/archive", async (Guid id, TeamService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TeamsManage);
        team.MapPost("/{id:guid}/restore", async (Guid id, TeamService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TeamsManage);
    }
}
