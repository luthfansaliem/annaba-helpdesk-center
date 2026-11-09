using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Http;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";
    public static string Name(string permission) => Prefix + permission;

    /// <summary>Endpoint helper: <c>.RequirePermission(Permissions.UsersRead)</c>. Unknown permission keys fail at startup, not at request time.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder b, string permission) where TBuilder : IEndpointConventionBuilder
    {
        if (!Permissions.All.Contains(permission)) throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
        return b.RequireAuthorization(Name(permission));
    }
}

/// <summary>Builds "perm:xxx" policies on demand (each requires an authenticated user + that permission).</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PermissionPolicy.Prefix)) return await base.GetPolicyAsync(policyName);
        var perm = policyName[PermissionPolicy.Prefix.Length..];
        if (!Permissions.All.Contains(perm)) return null; // unknown ⇒ no policy ⇒ endpoint fails closed
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(perm)).Build();
    }
}

/// <summary>Resolves effective permissions from the DB once per request (never from the cookie), so role changes apply immediately.</summary>
public sealed class PermissionHandler(IHttpContextAccessor accessor, AuthorizationService authz) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var http = accessor.HttpContext;
        var p = http?.Principal();
        if (http is null || p is null) return;
        if (!http.Items.TryGetValue("annabadigital.perms", out var cached))
        {
            cached = await authz.GetEffectivePermissionsAsync(p.UserId, http.RequestAborted);
            http.Items["annabadigital.perms"] = cached;
        }
        if (((IReadOnlySet<string>)cached!).Contains(requirement.Permission)) context.Succeed(requirement);
    }
}

/// <summary>Turns authorization outcomes into the JSON error envelope and audits denials (with the missing permission).</summary>
public sealed class EnvelopeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        if (result.Succeeded) { await _default.HandleAsync(next, context, policy, result); return; }

        if (result.Challenged) throw DomainException.Unauthenticated();

        var missing = policy.Requirements.OfType<PermissionRequirement>().FirstOrDefault()?.Permission ?? "(authorization)";
        var p = context.Principal();
        if (p is not null)
        {
            var audit = context.RequestServices.GetRequiredService<AuditWriter>();
            var db = context.RequestServices.GetRequiredService<IAppDb>();
            audit.Record("authz.denied", "endpoint", $"{context.Request.Method} {context.Request.Path}", Outcome.Denied, context.Client(), p.UserId, p.Email,
                new Dictionary<string, string> { ["missing"] = missing });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        throw DomainException.Denied(missing);
    }
}
