using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// SPIKE H00: can this application be entered from Annaba Workspace as it is? One journey: Core delivers an entitlement, a member arrives with a launch
/// code, and the ordinary screens' API works for them in their Workspace and in no other.
/// </summary>
[Collection("db")]
public sealed class SpikeWorkspaceBridgeTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Key() => "ws_" + Guid.NewGuid().ToString("N")[..16];

    private async Task<HttpResponseMessage> Entitle(string ws, int revision, string? secret = null, long? at = null)
    {
        var raw = JsonSerializer.Serialize(new { workspaceKey = ws, appKey = "helpdesk", revision, features = Array.Empty<string>(), operationId = Guid.NewGuid().ToString() });
        var ts = (at ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret ?? ApiFactory.CoreAdapterSecret), Encoding.UTF8.GetBytes(ts + "." + raw))).ToLowerInvariant();
        var req = new HttpRequestMessage(HttpMethod.Post, "/platform/v1/entitlements") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
        req.Headers.Add("X-Timestamp", ts); req.Headers.Add("X-Signature", sig); req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
        return await fx.Factory.CreateClient().SendAsync(req);
    }

    private async Task<(TestBrowser B, HttpStatusCode Status)> Launch(string code, string? state = null)
    {
        var b = TestBrowser.Create(fx.Factory); b.Origin = null; // the application's web server calls this, not a browser
        var res = await b.Post("/api/launch", state is null ? new { code } : new { code, state });
        b.Origin = ApiFactory.Origin;
        if (res.IsSuccessStatusCode) b.CsrfToken = (await (await b.Get("/api/auth/csrf")).Body()).GetProperty("token").GetString();
        return (b, res.StatusCode);
    }

    [Fact]
    public async Task A_member_enters_from_Workspace_and_works_in_their_own_Workspace_only()
    {
        var a = Key(); var other = Key();
        // Entitlement: signed by Core, applied once, never lowered, refused when the signature or the clock is wrong.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Entitle(a, 1, secret: "another-secret-0123456789abcdef0123456789")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Entitle(a, 1, at: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Launch(fx.Factory.Core.Issue(a, "early", "app_admin"))).Status);       // a grant alone creates nothing here
        Assert.Equal(2, (await (await Entitle(a, 2)).Body()).GetProperty("appliedRevision").GetInt32());
        Assert.Equal(2, (await (await Entitle(a, 1)).Body()).GetProperty("appliedRevision").GetInt32());                   // late and older: not lowered
        Assert.Equal(HttpStatusCode.OK, (await Entitle(other, 1)).StatusCode);
        Assert.Equal(1, await _seed.Db(db => db.Tenants.CountAsync(t => t.WorkspaceKey == a)));

        // Launch: once, with its state, for this application.
        var state = new string('s', 43);
        Assert.Equal(HttpStatusCode.Forbidden, (await Launch(fx.Factory.Core.Issue(a, "rina", "app_admin", state))).Status);            // the code without its state
        Assert.Equal(HttpStatusCode.Forbidden, (await Launch(fx.Factory.Core.Issue(a, "rina", "app_admin", app: "asset"))).Status);    // issued for another application
        var code = fx.Factory.Core.Issue(a, "rina", "app_admin", state, workspaceName: "PT Uji Helpdesk");
        var (admin, ok) = await Launch(code, state); Assert.Equal(HttpStatusCode.OK, ok);
        Assert.Equal(HttpStatusCode.Forbidden, (await Launch(code, state)).Status);                                                    // single use

        // Behind the session nothing knows the difference: the person is who Core said, in the Workspace Core said, with the role Workspace granted.
        var me = await (await admin.Get("/api/auth/me")).Body();
        Assert.Equal("rina@example.test", me.GetProperty("email").GetString());
        Assert.Equal("PT Uji Helpdesk", await _seed.Db(db => db.Tenants.Where(t => t.WorkspaceKey == a).Select(t => t.Name).SingleAsync()));
        // The starter catalogue is there, and the whole request flow works: the administrator publishes nothing new, an employee raises a request, an agent sees it.
        var (employee, _) = await Launch(fx.Factory.Core.Issue(a, "budi", "viewer")); var (agent, _) = await Launch(fx.Factory.Core.Issue(a, "sari", "operator"));
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.Get("/api/users?pageSize=5")).StatusCode);                              // an employee is not an administrator here either
        var cat = (await (await admin.Post("/api/catalog/categories", new { name = "Spike" })).Body()).GetProperty("id").GetGuid();
        var svc = (await (await admin.Post("/api/catalog/services", new { name = "Spike service", categoryId = cat, defaultPriority = "high" })).Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/catalog/services/{svc}/publish")).StatusCode);
        var raised = await employee.Post($"/api/portal/services/{svc}/requests", new { subject = "AC ruang rapat mati", description = "Tolong dicek", answers = new { } });
        Assert.True(raised.IsSuccessStatusCode, await raised.Content.ReadAsStringAsync()); var ticket = (await raised.Body()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Get($"/api/tickets/{ticket}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await employee.Get($"/api/tickets/{ticket}")).StatusCode);                                     // the requester sees their own request
        Assert.Equal(HttpStatusCode.NotFound, (await agent.Get($"/api/tickets/{ticket}")).StatusCode);                                  // an agent outside the owning team does not: the application's own rule, untouched

        // The same person (same account, same address) is a member of a second Workspace: a different user there, and neither Workspace sees the other's ticket.
        var (elsewhere, okOther) = await Launch(fx.Factory.Core.Issue(other, "rina", "app_admin")); Assert.Equal(HttpStatusCode.OK, okOther);
        Assert.Equal(HttpStatusCode.NotFound, (await elsewhere.Get($"/api/tickets/{ticket}")).StatusCode);
        Assert.Equal(2, await _seed.Db(db => db.Users.IgnoreQueryFilters().CountAsync(u => u.AccountRef == "acct_rina")));
        Assert.Equal(2, await _seed.Db(db => db.Users.IgnoreQueryFilters().Where(u => u.Email == "rina@example.test").Select(u => u.TenantId).Distinct().CountAsync()));

        // A second launch is the same user, with the role and the address Workspace has now.
        var (again, _) = await Launch(fx.Factory.Core.Issue(a, "rina", "viewer", email: "rina.baru@example.test"));
        Assert.Equal("rina.baru@example.test", (await (await again.Get("/api/auth/me")).Body()).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await again.Get("/api/users?pageSize=5")).StatusCode);                                  // demoted in Workspace: demoted here
        Assert.Equal(1, await _seed.Db(db => db.Users.IgnoreQueryFilters().CountAsync(u => u.AccountRef == "acct_rina" && u.TenantId != Guid.Empty && db.Tenants.Any(t => t.Id == u.TenantId && t.WorkspaceKey == a))));
    }
}
