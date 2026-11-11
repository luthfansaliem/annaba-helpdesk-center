using System.Text.Json;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>INT-003 / ADM-004. Directory mappings and CSV sync (<c>directory.manage</c>), the same sync for a nightly job on the public API, and SCIM 2.0 for identity
/// providers (both with an API key holding the <c>directory.write</c> scope).</summary>
public static class DirectoryEndpoints
{
    private const string ScimJson = "application/scim+json";

    private static async Task<JsonElement> BodyAsync(HttpContext ctx)
    {
        if (ctx.Request.ContentLength is > 1_000_000) throw new ScimException(413, "The request is too large.");
        try { using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted); return doc.RootElement.Clone(); }
        catch (JsonException) { throw new ScimException(400, "The body is not valid JSON.", "invalidSyntax"); }
    }

    /// <summary>Runs a SCIM handler and writes SCIM responses and errors (RFC 7644 §3.12).</summary>
    private static async Task<IResult> Scim(HttpContext ctx, Func<Task<object?>> work, int ok = 200)
    {
        try
        {
            var r = await work();
            return r is null ? Results.NoContent() : Results.Json(r, statusCode: ok, contentType: ScimJson);
        }
        catch (ScimException e) { return Error(e.Status, e.Message, e.ScimType); }
        catch (DomainException e) { return Error(e.Code == ErrorCodes.NotFound ? 404 : e.Code == ErrorCodes.Conflict ? 409 : 400, e.Message, null); }
        static IResult Error(int status, string detail, string? type) => Results.Json(new Dictionary<string, object?>
        {
            ["schemas"] = new[] { ScimService.ErrorSchema }, ["status"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture), ["scimType"] = type, ["detail"] = detail,
        }, statusCode: status, contentType: ScimJson);
    }

    public static void MapDirectory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/directory");
        g.MapGet("/overview", async (DirectoryService svc, HttpContext ctx) => Results.Ok(await svc.OverviewAsync(ctx.RequestAborted))).RequirePermission(Permissions.DirectoryManage);
        g.MapGet("/mappings", async (DirectoryService svc, HttpContext ctx) => Results.Ok(await svc.MappingsAsync(ctx.RequestAborted))).RequirePermission(Permissions.DirectoryManage);
        g.MapPost("/mappings", async (SaveGroupMappingRequest body, DirectoryService svc, HttpContext ctx) => Results.Ok(await svc.AddMappingAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.DirectoryManage);
        g.MapDelete("/mappings/{id:guid}", async (Guid id, DirectoryService svc, HttpContext ctx) => { await svc.DeleteMappingAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.DirectoryManage);
        g.MapPost("/sync", async (DirectorySyncRequest body, DirectoryService svc, HttpContext ctx) => Results.Ok(await svc.SyncAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.DirectoryManage);

        // the public API: a nightly CSV sync, and SCIM
        var v1 = app.MapGroup("/api/v1").WithMetadata(new MachineEndpoint()).RequireAuthorization();
        v1.MapPost("/directory/sync", async (DirectorySyncRequest body, DirectoryService svc, HttpContext ctx) => Results.Ok(await svc.SyncAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted, viaApiKey: true)))
            .RequirePermission(Permissions.DirectoryManage).WithMetadata(new ApiScope(DirectoryRules.Scope));

        var s = v1.MapGroup("/scim/v2").WithMetadata(new ApiScope(DirectoryRules.Scope));
        s.MapGet("/ServiceProviderConfig", (ScimService _, HttpContext ctx) => Scim(ctx, () => Task.FromResult<object?>(ScimService.ServiceProviderConfig())));
        s.MapGet("/Users", (string? filter, int? startIndex, int? count, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.ListUsersAsync(filter, startIndex, count, ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapGet("/Users/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.GetUserAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapPost("/Users", (ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.CreateUserAsync(await BodyAsync(ctx), ctx.RequirePrincipal(), ctx.RequestAborted), 201));
        s.MapPut("/Users/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.ReplaceUserAsync(id, await BodyAsync(ctx), ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapPatch("/Users/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.PatchUserAsync(id, await BodyAsync(ctx), ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapDelete("/Users/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => { await svc.DeleteUserAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted); return null; }));
        s.MapGet("/Groups", (string? filter, int? startIndex, int? count, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.ListGroupsAsync(filter, startIndex, count, ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapGet("/Groups/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.GetGroupAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapPost("/Groups", (ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.CreateGroupAsync(await BodyAsync(ctx), ctx.RequirePrincipal(), ctx.RequestAborted), 201));
        s.MapPut("/Groups/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.ReplaceGroupAsync(id, await BodyAsync(ctx), ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapPatch("/Groups/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => await svc.PatchGroupAsync(id, await BodyAsync(ctx), ctx.RequirePrincipal(), ctx.RequestAborted)));
        s.MapDelete("/Groups/{id:guid}", (Guid id, ScimService svc, HttpContext ctx) => Scim(ctx, async () => { await svc.DeleteGroupAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted); return null; }));
    }
}
