using Microsoft.AspNetCore.Mvc;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>VND-001/004/005. The vendor directory, contracts and documents. Reading needs <c>vendors.read</c>, changing <c>vendors.manage</c>; the work order picker needs <c>workorders.manage</c>.</summary>
public static class VendorEndpoints
{
    public static void MapVendors(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/vendors");
        g.MapGet("/", async (string? q, string? status, Guid? locationId, int? page, int? pageSize, VendorService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(q, status, locationId, page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.VendorsRead);
        g.MapGet("/summary", async (int? days, VendorService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequirePrincipal(), days, ctx.RequestAborted))).RequirePermission(Permissions.VendorsRead);
        g.MapGet("/pick", async (string? q, VendorService svc, HttpContext ctx) => Results.Ok(await svc.PickAsync(q, ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersManage);
        g.MapGet("/{id:guid}", async (Guid id, VendorService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsRead);
        g.MapPost("/", async (SaveVendorRequest body, VendorService svc, HttpContext ctx) =>
        {
            var v = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/vendors/{v.Id}", v);
        }).RequirePermission(Permissions.VendorsManage);
        g.MapPut("/{id:guid}", async (Guid id, SaveVendorRequest body, VendorService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
        g.MapPost("/{id:guid}/block", async (Guid id, BlockVendorRequest body, VendorService svc, HttpContext ctx) => Results.Ok(await svc.SetBlockedAsync(id, true, body.Reason, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
        g.MapPost("/{id:guid}/portal-users", async (Guid id, LinkPortalUserRequest body, VendorService svc, HttpContext ctx) => Results.Ok(await svc.LinkPortalUserAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
        g.MapDelete("/{id:guid}/portal-users/{userId:guid}", async (Guid id, Guid userId, VendorService svc, HttpContext ctx) => Results.Ok(await svc.UnlinkPortalUserAsync(id, userId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
        g.MapPost("/{id:guid}/unblock", async (Guid id, VendorService svc, HttpContext ctx) => Results.Ok(await svc.SetBlockedAsync(id, false, null, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);

        g.MapPost("/{id:guid}/contracts", async (Guid id, SaveContractRequest body, VendorService svc, HttpContext ctx) => Results.Ok(await svc.SaveContractAsync(id, null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
        g.MapPut("/{id:guid}/contracts/{contractId:guid}", async (Guid id, Guid contractId, SaveContractRequest body, VendorService svc, HttpContext ctx) => Results.Ok(await svc.SaveContractAsync(id, contractId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
        g.MapDelete("/{id:guid}/contracts/{contractId:guid}", async (Guid id, Guid contractId, VendorService svc, HttpContext ctx) => Results.Ok(await svc.DeleteContractAsync(id, contractId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);

        g.MapPost("/{id:guid}/documents", async (Guid id, Guid? contractId, IFormFile file, VendorService svc, HttpContext ctx) =>
        {
            await using var s = file.OpenReadStream();
            return Results.Ok(await svc.AddDocumentAsync(id, contractId, file.FileName, s, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted));
        }).RequirePermission(Permissions.VendorsManage).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024));
        g.MapGet("/{id:guid}/documents/{docId:guid}", async (Guid id, Guid docId, VendorService svc, HttpContext ctx) =>
        {
            var d = await svc.OpenDocumentAsync(id, docId, ctx.RequestAborted);
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(d.Inline ? "inline" : "attachment") { FileNameStar = d.FileName }.ToString();
            return Results.Stream(d.Content, d.ContentType);
        }).RequirePermission(Permissions.VendorsRead);
        g.MapDelete("/{id:guid}/documents/{docId:guid}", async (Guid id, Guid docId, VendorService svc, HttpContext ctx) => Results.Ok(await svc.DeleteDocumentAsync(id, docId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VendorsManage);
    }
}
