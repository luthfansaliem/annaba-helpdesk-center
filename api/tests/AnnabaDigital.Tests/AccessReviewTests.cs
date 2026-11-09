using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AccessReviewTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private async Task<(TestBrowser b, User u)> Actor(string role, bool reauth = true)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    /// <summary>Only one review may be open, so every test starts from none.</summary>
    private async Task CloseOpenReviews() => await _seed.Db(async db => await db.AccessReviews.Where(r => r.Status == AccessReviewStatus.Open).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, AccessReviewStatus.Closed)));
    private async Task<Guid> Start(TestBrowser b, string name = "Quarterly review")
    {
        await CloseOpenReviews();
        var res = await b.Post("/api/access-reviews", new { name, dueInDays = 14 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode); return (await res.Body()).GetProperty("id").GetGuid();
    }
    private async Task<Guid> ItemFor(Guid reviewId, Guid userId, string roleKey) => await _seed.Db(db => db.AccessReviewItems.Where(i => i.ReviewId == reviewId && i.UserId == userId && i.Role!.Key == roleKey).Select(i => i.Id).SingleAsync());
    private async Task<string[]> RoleKeys(Guid userId) => (await _seed.Db(db => db.UserRoles.Where(r => r.UserId == userId).Select(r => r.Role!.Key).ToListAsync())).ToArray();

    [Fact]
    public async Task A_review_snapshots_only_privileged_assignments_of_active_users_and_only_one_can_be_open()
    {
        var (b, _) = await Actor("super_admin");
        var holder = await _seed.UserAsync(role: "user_admin"); var plain = await _seed.UserAsync(); var gone = await _seed.UserAsync(role: "user_admin", status: UserStatus.Suspended);
        var id = await Start(b);
        Assert.NotEmpty(await _seed.Db(db => db.AccessReviewItems.Where(i => i.ReviewId == id && i.UserId == holder.Id && i.Role!.Key == "user_admin").ToListAsync()));
        Assert.Empty(await _seed.Db(db => db.AccessReviewItems.Where(i => i.ReviewId == id && (i.UserId == plain.Id || i.UserId == gone.Id)).ToListAsync())); // "user" has no permissions; suspended users are out
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post("/api/access-reviews", new { name = "Another one", dueInDays = 7 })).StatusCode);
        var list = await (await b.Get("/api/access-reviews")).Body();
        var mine = list.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal("open", mine.GetProperty("status").GetString()); Assert.True(mine.GetProperty("pending").GetInt32() > 0);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/access-reviews", new { name = "x", dueInDays = 7 })).StatusCode);
    }

    [Fact]
    public async Task Keep_leaves_access_alone_and_revoke_removes_the_role_through_the_normal_path_and_is_audited()
    {
        var (b, _) = await Actor("super_admin");
        var keeper = await _seed.UserAsync(role: "user_admin"); var loser = await _seed.UserAsync(role: "user_admin");
        var id = await Start(b);
        var keepItem = await ItemFor(id, keeper.Id, "user_admin"); var revokeItem = await ItemFor(id, loser.Id, "user_admin");

        Assert.Equal(HttpStatusCode.NoContent, (await b.Post($"/api/access-reviews/{id}/items/{keepItem}/decision", new { decision = "keep", note = "Still on the team." })).StatusCode);
        Assert.Contains("user_admin", await RoleKeys(keeper.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post($"/api/access-reviews/{id}/items/{revokeItem}/decision", new { decision = "revoke" })).StatusCode);
        var after = await RoleKeys(loser.Id); Assert.DoesNotContain("user_admin", after); Assert.Contains("user", after); // never left without a role
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "access.review.revoke" && a.TargetId == loser.Id.ToString()).ToListAsync()));

        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/access-reviews/{id}/items/{revokeItem}/decision", new { decision = "keep" })).StatusCode); // already decided
        Assert.Equal((HttpStatusCode)422, (await b.Post($"/api/access-reviews/{id}/items/{revokeItem}/decision", new { decision = "maybe" })).StatusCode);
        var items = await (await b.Get($"/api/access-reviews/{id}/items?decision=revoke&pageSize=100")).Body();
        Assert.Contains(items.GetProperty("data").EnumerateArray(), x => x.GetProperty("userId").GetGuid() == loser.Id && x.GetProperty("decision").GetString() == "revoke");
    }

    [Fact]
    public async Task Nobody_reviews_their_own_access_and_revoking_needs_recent_reauthentication()
    {
        var (b, me) = await Actor("super_admin");
        var target = await _seed.UserAsync(role: "user_admin");
        var id = await Start(b);
        var own = await ItemFor(id, me.Id, "super_admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post($"/api/access-reviews/{id}/items/{own}/decision", new { decision = "keep" })).StatusCode);

        var (stale, _) = await Actor("super_admin", reauth: false);
        var item = await ItemFor(id, target.Id, "user_admin");
        var denied = await stale.Post($"/api/access-reviews/{id}/items/{item}/decision", new { decision = "revoke" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
        Assert.Contains("user_admin", await RoleKeys(target.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await stale.Post($"/api/access-reviews/{id}/items/{item}/decision", new { decision = "keep" })).StatusCode); // keeping changes nothing, so no re-auth
    }

    [Fact]
    public async Task Closing_ends_decisions_and_the_whole_feature_needs_the_assign_role_permission()
    {
        var (b, _) = await Actor("super_admin"); var target = await _seed.UserAsync(role: "user_admin");
        var id = await Start(b); var item = await ItemFor(id, target.Id, "user_admin");
        var (plain, _) = await Actor("user", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/access-reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Post("/api/access-reviews", new { name = "Sneaky", dueInDays = 7 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Post($"/api/access-reviews/{id}/items/{item}/decision", new { decision = "revoke" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await b.Post($"/api/access-reviews/{id}/close", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/access-reviews/{id}/close", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/access-reviews/{id}/items/{item}/decision", new { decision = "keep" })).StatusCode);
        Assert.Contains("user_admin", await RoleKeys(target.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/access-reviews/{Guid.NewGuid()}/items")).StatusCode);
    }
}
