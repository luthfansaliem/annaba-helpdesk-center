using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class ApprovalNotificationTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string Why = "I need to review the user list for onboarding.";

    private async Task<(TestBrowser b, User u)> Actor(string role, bool reauth = false)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    private async Task<int> Pending(TestBrowser b, string field) => (await (await b.Get("/api/access-requests/pending-count")).Body()).GetProperty(field).GetInt32();

    private async Task<AnnabaDigital.Application.Abstractions.EmailMessage> WaitForText(string subject, string text)
    {
        for (var i = 0; i < 200; i++)
        {
            var m = _seed.Mail.Sent.LastOrDefault(x => x.Subject.Contains(subject, StringComparison.OrdinalIgnoreCase) && x.Text.Contains(text));
            if (m is not null) return m;
            await Task.Delay(25);
        }
        throw new TimeoutException($"No '{subject}' email mentioning '{text}'.");
    }

    [Fact]
    public async Task A_new_request_mails_the_approvers_but_not_the_requester_and_the_decision_mails_the_requester()
    {
        var (admin, _) = await Actor("super_admin", reauth: true);
        var (rb, requester) = await Actor("user_admin"); // can assign roles too, but must not be told about their own request
        var res = await rb.Post("/api/access-requests", new { roleId = await _seed.RoleId("security_auditor"), reason = Why });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode); var id = (await res.Body()).GetProperty("id").GetGuid();

        // The shared test database holds many approvers and a request mails at most MaxRecipients of them, so look for any mail about this request.
        var mail = await WaitForText("Access request waiting", requester.Name);
        Assert.Contains("security", mail.Text, StringComparison.OrdinalIgnoreCase); Assert.Contains("/access-requests", mail.Text);
        await _seed.Mail.AssertNone(requester.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/access-requests/{id}/reject", new { note = "Not needed right now." })).StatusCode);
        var decided = await _seed.Mail.WaitFor(requester.Email, "was rejected");
        Assert.Contains("Not needed right now.", decided.Text);
    }

    [Fact]
    public async Task Starting_a_review_mails_the_other_reviewers()
    {
        await _seed.Db(async db => await db.AccessReviews.Where(r => r.Status == AccessReviewStatus.Open).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, AccessReviewStatus.Closed)));
        var (starter, startedBy) = await Actor("super_admin");
        var name = "Quarterly review " + Guid.NewGuid().ToString("N")[..6];
        var res = await starter.Post("/api/access-reviews", new { name, dueInDays = 7 });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        await WaitForText("access review", name);
        await _seed.Mail.AssertNone(startedBy.Email);
    }

    [Fact]
    public async Task The_pending_count_skips_own_requests_and_needs_the_assign_role_permission()
    {
        var (admin, _) = await Actor("super_admin");
        var (plain, _) = await Actor("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/access-requests/pending-count")).StatusCode);

        var before = await Pending(admin, "accessRequests");
        Assert.Equal(HttpStatusCode.Created, (await plain.Post("/api/access-requests", new { roleId = await _seed.RoleId("security_auditor"), reason = Why })).StatusCode);
        Assert.Equal(before + 1, await Pending(admin, "accessRequests"));

        var (other, _) = await Actor("user_admin");
        var own = await other.Post("/api/access-requests", new { roleId = await _seed.RoleId("security_auditor"), reason = Why });
        Assert.Equal(HttpStatusCode.Created, own.StatusCode);
        Assert.Equal(before + 2, await Pending(admin, "accessRequests")); // seen by others
        Assert.Equal(before + 1, await Pending(other, "accessRequests")); // not counting their own
    }
}
