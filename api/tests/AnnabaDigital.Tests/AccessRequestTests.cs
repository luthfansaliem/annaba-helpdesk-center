using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AccessRequestTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string Why = "I need to review the user list for onboarding.";

    private async Task<(TestBrowser b, User u)> Actor(string role = "user", bool reauth = false)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    private async Task<Guid> Request(TestBrowser b, string roleKey, string reason = Why)
    {
        var res = await b.Post("/api/access-requests", new { roleId = await _seed.RoleId(roleKey), reason });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode); return (await res.Body()).GetProperty("id").GetGuid();
    }
    private async Task<string[]> RoleKeys(Guid userId) => (await _seed.Db(db => db.UserRoles.Where(r => r.UserId == userId).Select(r => r.Role!.Key).ToListAsync())).ToArray();

    [Fact]
    public async Task A_user_can_request_a_role_but_not_super_admin_one_they_hold_or_a_duplicate_and_can_cancel()
    {
        var (b, u) = await Actor();
        var roles = (await (await b.Get("/api/access-requests/roles")).Body()).EnumerateArray().Select(r => r.GetProperty("key").GetString()).ToList();
        Assert.Contains("user_admin", roles); Assert.DoesNotContain("super_admin", roles); Assert.DoesNotContain("user", roles); // protected and held roles aren't offered

        var id = await Request(b, "user_admin");
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post("/api/access-requests", new { roleId = await _seed.RoleId("user_admin"), reason = Why })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/access-requests", new { roleId = await _seed.RoleId("super_admin"), reason = Why })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post("/api/access-requests", new { roleId = await _seed.RoleId("user"), reason = Why })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/access-requests", new { roleId = await _seed.RoleId("security_auditor"), reason = "short" })).StatusCode);

        var mine = await (await b.Get("/api/access-requests/mine")).Body();
        Assert.Equal("pending", mine[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post($"/api/access-requests/{id}/cancel", new { })).StatusCode);
        Assert.Equal("cancelled", (await (await b.Get("/api/access-requests/mine")).Body())[0].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/access-requests/{id}/cancel", new { })).StatusCode);
        await Request(b, "user_admin"); // a cancelled request frees the slot
    }

    [Fact]
    public async Task Approval_grants_the_role_through_the_normal_path_and_is_audited()
    {
        var (rb, requester) = await Actor(); var id = await Request(rb, "security_auditor");
        var (ab, _) = await Actor("super_admin", reauth: true);
        Assert.Equal(HttpStatusCode.NoContent, (await ab.Post($"/api/access-requests/{id}/approve", new { note = "Approved for the audit." })).StatusCode);
        Assert.Contains("security_auditor", await RoleKeys(requester.Id)); Assert.Contains("user", await RoleKeys(requester.Id));
        var item = (await (await rb.Get("/api/access-requests/mine")).Body())[0];
        Assert.Equal("approved", item.GetProperty("status").GetString()); Assert.Equal("Approved for the audit.", item.GetProperty("decisionNote").GetString());
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "access.request.approve").ToListAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await ab.Post($"/api/access-requests/{id}/approve", new { })).StatusCode); // already decided
    }

    [Fact]
    public async Task Rejection_grants_nothing()
    {
        var (rb, requester) = await Actor(); var id = await Request(rb, "user_admin");
        var (ab, _) = await Actor("user_admin", reauth: true);
        Assert.Equal(HttpStatusCode.NoContent, (await ab.Post($"/api/access-requests/{id}/reject", new { note = "Not needed." })).StatusCode);
        Assert.DoesNotContain("user_admin", await RoleKeys(requester.Id));
        Assert.Equal("rejected", (await (await rb.Get("/api/access-requests/mine")).Body())[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Nobody_decides_their_own_request_and_approving_cannot_escalate_beyond_the_approver()
    {
        var (ab, _) = await Actor("user_admin", reauth: true);
        var own = await Request(ab, "security_auditor");
        Assert.Equal(HttpStatusCode.Forbidden, (await ab.Post($"/api/access-requests/{own}/approve", new { })).StatusCode); // separation of duties

        var (rb, requester) = await Actor(); var big = await Request(rb, "security_auditor"); // includes audit.read, which a user admin lacks
        Assert.Equal(HttpStatusCode.Forbidden, (await ab.Post($"/api/access-requests/{big}/approve", new { })).StatusCode);
        Assert.DoesNotContain("security_auditor", await RoleKeys(requester.Id));
    }

    [Fact]
    public async Task Approving_needs_recent_reauthentication_and_the_review_queue_needs_the_assign_role_permission()
    {
        var (rb, requester) = await Actor(); var id = await Request(rb, "user_admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await rb.Get("/api/access-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rb.Post($"/api/access-requests/{id}/approve", new { })).StatusCode);
        var (stale, _) = await Actor("super_admin", reauth: false);
        var denied = await stale.Post($"/api/access-requests/{id}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
        Assert.DoesNotContain("user_admin", await RoleKeys(requester.Id));

        var (ab, _) = await Actor("user_admin");
        var list = await (await ab.Get("/api/access-requests?status=pending")).Body();
        Assert.Contains(list.GetProperty("data").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal((HttpStatusCode)422, (await ab.Get("/api/access-requests?status=nope")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/access-requests/mine")).StatusCode);
    }
}
