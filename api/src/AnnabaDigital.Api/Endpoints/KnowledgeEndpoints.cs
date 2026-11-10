using Microsoft.AspNetCore.Mvc;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>KB-001/002/003. The knowledge base. Reading needs only a session (the service decides which articles); writing <c>kb.write</c>; publishing and categories <c>kb.publish</c>.</summary>
public static class KnowledgeEndpoints
{
    public static void MapKnowledge(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/kb");
        g.MapGet("/categories", async (KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.CategoriesAsync(ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/categories", async (SaveKbCategoryRequest body, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.SaveCategoryAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbPublish);
        g.MapPut("/categories/{id:guid}", async (Guid id, SaveKbCategoryRequest body, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.SaveCategoryAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbPublish);
        g.MapPost("/categories/{id:guid}/archive", async (Guid id, KnowledgeService svc, HttpContext ctx) => { await svc.SetCategoryArchivedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.KbPublish);
        g.MapPost("/categories/{id:guid}/restore", async (Guid id, KnowledgeService svc, HttpContext ctx) => { await svc.SetCategoryArchivedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.KbPublish);

        g.MapGet("/articles", async (string? q, Guid? categoryId, string? status, int? page, int? pageSize, KnowledgeService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(q, categoryId, status, page ?? 1, pageSize ?? 25, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/suggest", async (string? q, Guid? serviceId, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.SuggestAsync(q, serviceId, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/articles/{id:guid}", async (Guid id, int? version, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, version, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/articles", async (SaveKbArticleRequest body, KnowledgeService svc, HttpContext ctx) =>
        {
            var a = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/kb/articles/{a.Id}", a);
        }).RequirePermission(Permissions.KbWrite);
        g.MapPut("/articles/{id:guid}", async (Guid id, SaveKbArticleRequest body, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbWrite);
        g.MapPost("/articles/{id:guid}/publish", async (Guid id, int? version, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.PublishAsync(id, version, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbPublish);
        g.MapPost("/articles/{id:guid}/archive", async (Guid id, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.SetArchivedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbPublish);
        g.MapPost("/articles/{id:guid}/restore", async (Guid id, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.SetArchivedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbPublish);
        g.MapPost("/articles/{id:guid}/feedback", async (Guid id, KbFeedbackRequest body, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.FeedbackAsync(id, body, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/articles/{id:guid}/solved", async (Guid id, KnowledgeService svc, HttpContext ctx) => { await svc.DeflectAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted); return Results.NoContent(); }).RequireAuthorization();

        g.MapPost("/articles/{id:guid}/files", async (Guid id, IFormFile file, KnowledgeService svc, HttpContext ctx) =>
        {
            await using var s = file.OpenReadStream();
            return Results.Ok(await svc.AddFileAsync(id, file.FileName, s, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted));
        }).RequirePermission(Permissions.KbWrite).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024));
        g.MapGet("/articles/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, KnowledgeService svc, HttpContext ctx) =>
        {
            var f = await svc.OpenFileAsync(id, fileId, ctx.RequirePrincipal(), ctx.RequestAborted);
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(f.Inline ? "inline" : "attachment") { FileNameStar = f.FileName }.ToString();
            return Results.Stream(f.Content, f.ContentType);
        }).RequireAuthorization();
        g.MapDelete("/articles/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, KnowledgeService svc, HttpContext ctx) => Results.Ok(await svc.DeleteFileAsync(id, fileId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.KbWrite);
    }
}
