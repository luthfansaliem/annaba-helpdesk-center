using AnnabaDigital.Application.Helpdesk;

namespace AnnabaDigital.Api.Http;

/// <summary>
/// ADR-GA-07. On work-order changes that carry an <c>Idempotency-Key</c> (field mode replays): a key already applied for this person returns the work order as it
/// is now, without doing the change again; a key is remembered only when the change succeeded, so a refused change (409, 403, 422) can be tried again.
/// </summary>
public sealed class IdempotentChangeFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext c, EndpointFilterDelegate next)
    {
        var ctx = c.HttpContext;
        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method)) return await next(c);
        var key = OfflineOpService.Key(ctx.Request.Headers["Idempotency-Key"].ToString());
        if (key is null) return await next(c);
        var p = ctx.RequirePrincipal();
        var ops = ctx.RequestServices.GetRequiredService<OfflineOpService>();
        if (await ops.SeenAsync(p.UserId, key, ctx.RequestAborted))
        {
            ctx.Response.Headers["Idempotent-Replayed"] = "true";
            return ctx.Request.RouteValues.TryGetValue("id", out var raw) && Guid.TryParse(raw?.ToString(), out var id)
                ? Results.Ok(await ctx.RequestServices.GetRequiredService<WorkOrderService>().GetAsync(id, p, ctx.RequestAborted))
                : Results.NoContent();
        }
        var result = await next(c);
        if (result is not IStatusCodeHttpResult { StatusCode: >= 400 }) await ops.RememberAsync(p.UserId, key, ctx.RequestAborted);
        return result;
    }
}
