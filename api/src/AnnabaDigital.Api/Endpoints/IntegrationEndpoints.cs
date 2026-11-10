using System.Text.Json;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Application.Integrations;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Security;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Api.Endpoints;

public sealed record V1CreateTicket(Guid? ServiceId, string? Subject, string? Description, string? RequesterEmail, Guid? LocationId, Guid? AssetId, Dictionary<string, JsonElement>? Answers);
public sealed record V1Comment(string? Body, bool Internal = false);

/// <summary>
/// INT-001/002. The public API (<c>/api/v1</c>, API keys only; each key acts as the person who made it, within its scopes) and the screens' endpoints for keys and webhooks
/// (<c>integrations.manage</c>).
/// </summary>
public static class IntegrationEndpoints
{
    public static void MapIntegrations(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1").WithMetadata(new MachineEndpoint()).RequireAuthorization();
        v1.MapGet("/tickets", async (string? q, string? status, int? page, int? pageSize, TicketService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(new TicketQuery(q, status, null, null, null, null, Page: page ?? 1, PageSize: pageSize ?? 50), ctx.RequirePrincipal(), false, ctx.RequestAborted)))
            .WithMetadata(new ApiScope("tickets.read"));
        v1.MapGet("/tickets/{id:guid}", async (Guid id, TicketService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).WithMetadata(new ApiScope("tickets.read"));
        v1.MapPost("/tickets", async (V1CreateTicket body, TicketService svc, IntegrationService keys, IAppDb db, HttpContext ctx) =>
        {
            var key = ctx.ApiKey()!; var who = ctx.RequirePrincipal();
            var idem = ctx.Request.Headers["Idempotency-Key"].ToString().Trim();
            if (idem.Length > 100) throw DomainException.Validation("Idempotency-Key is too long.", new Dictionary<string, string> { ["Idempotency-Key"] = "Use at most 100 characters." });
            if (idem.Length > 0 && await keys.IdempotentTicketAsync(key.KeyId, idem, ctx.RequestAborted) is { } earlier) return Results.Ok(await svc.GetAsync(earlier, who, ctx.RequestAborted));
            if (body.ServiceId is not { } sid) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["serviceId"] = "Give the service." });
            Guid? behalf = null;
            if (!string.IsNullOrWhiteSpace(body.RequesterEmail))
            {
                var mail = body.RequesterEmail.Trim().ToLowerInvariant();
                behalf = await db.Users.Where(u => u.Email.ToLower() == mail).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ctx.RequestAborted)
                    ?? throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["requesterEmail"] = "No person with this e-mail in the organisation." });
            }
            var t = await svc.CreateAsync(sid, new CreateTicketRequest(body.Subject, body.Description, body.Answers ?? [], body.LocationId, behalf, body.AssetId), who, ctx.Client(), ctx.RequestAborted);
            if (idem.Length > 0) await keys.RememberIdempotentAsync(key.KeyId, idem, t.Id, ctx.RequestAborted);
            return Results.Created($"/api/v1/tickets/{t.Id}", t);
        }).WithMetadata(new ApiScope("tickets.write"));
        v1.MapPost("/tickets/{id:guid}/comments", async (Guid id, V1Comment body, TicketService svc, HttpContext ctx)
            => Results.Ok(await svc.CommentAsync(id, new CommentRequest(body.Body, body.Internal), ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).WithMetadata(new ApiScope("tickets.write"));
        v1.MapGet("/services", async (CatalogService svc, HttpContext ctx) => Results.Ok(await svc.ListServicesAsync(false, null, null, ctx.RequestAborted))).WithMetadata(new ApiScope("catalog.read"));

        var g = app.MapGroup("/api/integrations");
        g.MapGet("/keys", async (IntegrationService svc, HttpContext ctx) => Results.Ok(await svc.KeysAsync(ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapPost("/keys", async (CreateApiKeyRequest body, IntegrationService svc, HttpContext ctx) => Results.Ok(await svc.CreateKeyAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapPost("/keys/{id:guid}/revoke", async (Guid id, IntegrationService svc, HttpContext ctx) => { await svc.RevokeKeyAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.IntegrationsManage);
        g.MapGet("/webhooks", async (IntegrationService svc, HttpContext ctx) => Results.Ok(await svc.WebhooksAsync(ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapPost("/webhooks", async (SaveWebhookRequest body, IntegrationService svc, HttpContext ctx) => Results.Ok(await svc.CreateWebhookAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapPut("/webhooks/{id:guid}", async (Guid id, SaveWebhookRequest body, IntegrationService svc, HttpContext ctx) => Results.Ok(await svc.UpdateWebhookAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapDelete("/webhooks/{id:guid}", async (Guid id, IntegrationService svc, HttpContext ctx) => { await svc.DeleteWebhookAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.IntegrationsManage);
        g.MapGet("/webhooks/{id:guid}/deliveries", async (Guid id, IntegrationService svc, HttpContext ctx) => Results.Ok(await svc.DeliveriesAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
    }
}
