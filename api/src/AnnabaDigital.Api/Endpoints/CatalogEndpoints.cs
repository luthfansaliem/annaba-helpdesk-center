using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>
/// Service catalog administration (<c>/api/catalog</c>, one permission per route) and the requester-facing read side (<c>/api/portal</c>, any signed-in person:
/// what they may see is decided by the service, not by a role).
/// </summary>
public static class CatalogEndpoints
{
    public static void MapCatalog(this IEndpointRouteBuilder app)
    {
        var cat = app.MapGroup("/api/catalog");

        cat.MapGet("/categories", async (bool? includeArchived, CatalogService svc, HttpContext ctx) => Results.Ok(await svc.ListCategoriesAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.CatalogRead);
        cat.MapPost("/categories", async (SaveCategoryRequest body, CatalogService svc, HttpContext ctx) =>
        {
            var c = await svc.CreateCategoryAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/catalog/categories/{c.Id}", c);
        }).RequirePermission(Permissions.CatalogManage);
        cat.MapPatch("/categories/{id:guid}", async (Guid id, SaveCategoryRequest body, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateCategoryAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);
        cat.MapPost("/categories/{id:guid}/archive", async (Guid id, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveCategoryAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);
        cat.MapPost("/categories/{id:guid}/restore", async (Guid id, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreCategoryAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);

        cat.MapGet("/services", async (bool? includeArchived, Guid? categoryId, string? q, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.ListServicesAsync(includeArchived ?? false, categoryId, q, ctx.RequestAborted))).RequirePermission(Permissions.CatalogRead);
        cat.MapGet("/services/{id:guid}", async (Guid id, CatalogService svc, HttpContext ctx) => Results.Ok(await svc.GetServiceAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.CatalogRead);
        cat.MapPost("/services", async (SaveServiceRequest body, CatalogService svc, HttpContext ctx) =>
        {
            var s = await svc.CreateServiceAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/catalog/services/{s.Id}", s);
        }).RequirePermission(Permissions.CatalogManage);
        cat.MapPatch("/services/{id:guid}", async (Guid id, SaveServiceRequest body, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateServiceAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);
        cat.MapPost("/services/{id:guid}/publish", async (Guid id, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.SetPublishedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);
        cat.MapPost("/services/{id:guid}/unpublish", async (Guid id, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.SetPublishedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);
        cat.MapPost("/services/{id:guid}/archive", async (Guid id, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveServiceAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);
        cat.MapPost("/services/{id:guid}/restore", async (Guid id, CatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreServiceAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.CatalogManage);

        cat.MapGet("/services/{id:guid}/form", async (Guid id, ServiceFormService svc, HttpContext ctx) => Results.Ok(await svc.GetCurrentAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.CatalogRead);
        cat.MapGet("/services/{id:guid}/form/versions", async (Guid id, ServiceFormService svc, HttpContext ctx) => Results.Ok(await svc.ListVersionsAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.CatalogRead);
        cat.MapGet("/services/{id:guid}/form/versions/{number:int}", async (Guid id, int number, ServiceFormService svc, HttpContext ctx) => Results.Ok(await svc.GetVersionAsync(id, number, ctx.RequestAborted))).RequirePermission(Permissions.CatalogRead);
        cat.MapPut("/services/{id:guid}/form", async (Guid id, SaveFormRequest body, ServiceFormService svc, HttpContext ctx)
            => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.FormsManage);

        // Requester side: any signed-in person. Visibility (published, live, audience) is enforced inside the service; hidden and missing both answer 404.
        var portal = app.MapGroup("/api/portal");
        portal.MapGet("/catalog", async (string? q, PortalCatalogService svc, HttpContext ctx) => Results.Ok(await svc.BrowseAsync(q, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        portal.MapGet("/services/{id:guid}", async (Guid id, PortalCatalogService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        portal.MapGet("/locations", async (PortalCatalogService svc, HttpContext ctx) => Results.Ok(await svc.LocationsAsync(ctx.RequestAborted))).RequireAuthorization();
        portal.MapGet("/people", async (string? q, PortalCatalogService svc, HttpContext ctx) => Results.Ok(await svc.PeopleAsync(q, ctx.RequestAborted))).RequireAuthorization();
        portal.MapPost("/services/{id:guid}/check", async (Guid id, CheckAnswersRequest body, PortalCatalogService svc, HttpContext ctx)
            => Results.Ok(await svc.CheckAnswersAsync(id, body, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
    }
}
