using Microsoft.AspNetCore.Mvc;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>
/// Tickets. Requester actions (create, my tickets, comment, cancel/confirm/reopen) need only a signed-in session: the service decides, per ticket, what the person may see and do.
/// Staff-only routes carry their own permission. A ticket the person may not see answers 404, exactly like a missing one.
/// </summary>
public static class TicketEndpoints
{
    public static void MapTickets(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/portal/services/{id:guid}/requests", async (Guid id, CreateTicketRequest body, TicketService svc, HttpContext ctx) =>
        {
            var t = await svc.CreateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/tickets/{t.Id}", t);
        }).RequireAuthorization();

        // files for a request form that is not submitted yet; the ticket claims them when it is created, unclaimed ones are purged after a day
        app.MapPost("/api/uploads", async (IFormFile file, AttachmentService svc, HttpContext ctx) =>
        {
            await using var s = file.OpenReadStream();
            var a = await svc.StoreAsync(null, file.FileName, s, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/uploads/{a.Id}", new { a.Id, a.FileName, a.ContentType, a.Size, inline = a.ContentType.StartsWith("image/") });
        }).RequireAuthorization().DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024));

        var g = app.MapGroup("/api/tickets");
        g.MapGet("/mine", async ([AsParameters] TicketQuery q, TicketService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(q, ctx.RequirePrincipal(), true, ctx.RequestAborted))).RequireAuthorization();
        // the queue: the service refuses anyone without tickets.read_team or tickets.read_all (403) and narrows the rest to what they may see
        g.MapGet("/", async ([AsParameters] TicketQuery q, TicketService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(q, ctx.RequirePrincipal(), false, ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/assignees", async (Guid? teamId, TicketService svc, HttpContext ctx) => Results.Ok(await svc.AssigneesAsync(teamId, ctx.RequestAborted))).RequirePermission(Permissions.TicketsAssign);
        g.MapGet("/{id:guid}", async (Guid id, TicketService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        // one change for many tickets; each ticket is judged on its own (see TicketService.BulkAsync) and the answer lists every outcome
        g.MapPost("/bulk", async (BulkRequest body, TicketService svc, HttpContext ctx) => Results.Ok(await svc.BulkAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/{id:guid}/transition", async (Guid id, TransitionRequest body, TicketService svc, HttpContext ctx)
            => Results.Ok(await svc.TransitionAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/{id:guid}/comments", async (Guid id, CommentRequest body, TicketService svc, HttpContext ctx)
            => Results.Ok(await svc.CommentAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/{id:guid}/assign", async (Guid id, AssignRequest body, TicketService svc, HttpContext ctx)
            => Results.Ok(await svc.AssignAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TicketsAssign);
        g.MapPost("/{id:guid}/attachments", async (Guid id, IFormFile file, TicketService svc, HttpContext ctx) =>
        {
            await using var s = file.OpenReadStream();
            var a = await svc.AttachAsync(id, file.FileName, s, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/tickets/{id}/attachments/{a.Id}", a);
        }).RequireAuthorization().DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024));
        g.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, TicketService svc, HttpContext ctx) =>
        {
            var d = await svc.OpenAttachmentAsync(id, attachmentId, ctx.RequirePrincipal(), ctx.RequestAborted);
            // never let a browser run what it downloads: our own content type, a sandboxing policy, no sniffing, and only images are shown inline
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(d.Inline ? "inline" : "attachment") { FileNameStar = d.FileName }.ToString();   // RFC 5987, so any name survives
            return Results.Stream(d.Content, d.ContentType);
        }).RequireAuthorization();
        g.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, TicketService svc, HttpContext ctx) =>
        {
            await svc.RemoveAttachmentAsync(id, attachmentId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();
        g.MapPatch("/{id:guid}", async (Guid id, UpdateTicketRequest body, TicketService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TicketsUpdate);
    }
}
