using System.Net;
using System.Text;
using System.Text.Json;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Knowledge base (KB-001/002/003, R2 slice 7): drafts and versions, publishing, who reads what, feedback, suggestions and deflection, service links, files and categories.</summary>
[Collection("db")]
public sealed class KnowledgeTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(Seed Seed, TestBrowser Supervisor, TestBrowser Agent, TestBrowser Requester, Guid ServiceId);

    private async Task<World> Build()
    {
        var seed = new Seed(fx.Factory, fx.Clock);
        async Task<TestBrowser> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b; }
        var sup = await Person("ga_supervisor"); var agent = await Person("ga_agent"); var req = await Person("user"); var admin = await Person("super_admin");
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Printer help {Uniq()}", categoryId = cat })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(seed, sup, agent, req, svc);
    }

    private static async Task<JsonElement> Article(TestBrowser b, string title, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["title"] = title, ["summary"] = "Short answer", ["body"] = "## Steps\n\n1. Open the tray\n2. Remove the paper", ["visibility"] = "everyone" };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return await Ok(await b.Post("/api/kb/articles", body));
    }

    [Fact]
    public async Task Writers_draft_and_publishers_publish_readers_only_ever_see_the_published_version()
    {
        var w = await Build();
        var bad = await Fields(await w.Agent.Post("/api/kb/articles", new { title = "x", body = "", visibility = "public" }));
        foreach (var k in new[] { "title", "body", "visibility" }) Assert.True(bad.TryGetProperty(k, out _), k);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Post("/api/kb/articles", new { title = "Mine", body = "x" })).StatusCode);

        var word = "Jam" + Uniq();
        var a = await Article(w.Agent, $"Clear a paper {word}", new { serviceIds = new[] { w.ServiceId }, note = "First draft" });
        var id = IdOf(a);
        Assert.Equal("draft", Str(a, "status")); Assert.Equal(1, a.GetProperty("latestVersion").GetInt32()); Assert.Equal(0, a.GetProperty("publishedVersion").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/kb/articles/{id}")).StatusCode);                  // drafts are invisible to readers
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post($"/api/kb/articles/{id}/publish")).StatusCode);            // agents write, supervisors publish

        var pub = await Ok(await w.Supervisor.Post($"/api/kb/articles/{id}/publish"));
        Assert.Equal("published", Str(pub, "status")); Assert.Equal(1, pub.GetProperty("publishedVersion").GetInt32());
        var read = await Ok(await w.Requester.Get($"/api/kb/articles/{id}"));
        Assert.Equal($"Clear a paper {word}", Str(read, "title")); Assert.Contains("Open the tray", Str(read, "body"));
        Assert.Equal(JsonValueKind.Null, read.GetProperty("versions").ValueKind); Assert.False(read.GetProperty("canWrite").GetBoolean());

        // an improvement is a draft: readers keep the published text until it is published
        var v2 = await Ok(await w.Agent.SendAsync(HttpMethod.Put, $"/api/kb/articles/{id}", new { title = $"Clear a paper {word} (updated)", body = "Pull the green lever.", visibility = "everyone", serviceIds = new[] { w.ServiceId }, version = pub.GetProperty("version").GetInt32() }));
        Assert.True(v2.GetProperty("hasDraft").GetBoolean()); Assert.Equal(2, v2.GetProperty("latestVersion").GetInt32());
        Assert.Contains("Open the tray", Str(await Ok(await w.Requester.Get($"/api/kb/articles/{id}")), "body"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Agent.SendAsync(HttpMethod.Put, $"/api/kb/articles/{id}", new { title = "Stale", body = "x", visibility = "everyone", version = a.GetProperty("version").GetInt32() })).StatusCode);
        var first = await Ok(await w.Agent.Get($"/api/kb/articles/{id}?version=1"));
        Assert.Contains("Open the tray", Str(first, "body")); Assert.Equal(2, first.GetProperty("versions").GetArrayLength());
        // a save that changes nothing in the text keeps the version
        var same = await Ok(await w.Agent.SendAsync(HttpMethod.Put, $"/api/kb/articles/{id}", new { title = $"Clear a paper {word} (updated)", body = "Pull the green lever.", visibility = "everyone", version = v2.GetProperty("version").GetInt32() }));
        Assert.Equal(2, same.GetProperty("latestVersion").GetInt32()); Assert.Empty(same.GetProperty("services").EnumerateArray());
        await Ok(await w.Supervisor.Post($"/api/kb/articles/{id}/publish"));
        Assert.Equal("Pull the green lever.", Str(await Ok(await w.Requester.Get($"/api/kb/articles/{id}")), "body"));

        // archived: gone for readers, kept for writers
        await Ok(await w.Supervisor.Post($"/api/kb/articles/{id}/archive"));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/kb/articles/{id}")).StatusCode);
        Assert.Contains(id, (await Ok(await w.Agent.Get("/api/kb/articles?status=archived"))).GetProperty("data").EnumerateArray().Select(IdOf));
        Assert.Equal("published", Str(await Ok(await w.Supervisor.Post($"/api/kb/articles/{id}/restore")), "status"));
    }

    [Fact]
    public async Task Staff_only_articles_stay_with_staff_readers_answer_was_it_helpful_and_suggestions_count_deflections()
    {
        var w = await Build();
        var word = "Toner" + Uniq();
        var open = IdOf(await Article(w.Agent, $"Change the {word} cartridge", new { serviceIds = new[] { w.ServiceId } }));
        var staff = IdOf(await Article(w.Agent, $"Reset the {word} counter (admin)", new { visibility = "staff" }));
        foreach (var x in new[] { open, staff }) await Ok(await w.Supervisor.Post($"/api/kb/articles/{x}/publish"));

        async Task<Guid[]> Found(TestBrowser b, string q) => [.. (await Ok(await b.Get($"/api/kb/articles?q={Uri.EscapeDataString(q)}"))).GetProperty("data").EnumerateArray().Select(IdOf)];
        Assert.Equal([open], await Found(w.Requester, word));
        Assert.Equal(new[] { open, staff }.OrderBy(g => g), (await Found(w.Agent, word)).OrderBy(g => g));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/kb/articles/{staff}")).StatusCode);

        // helpful? one answer per person, changeable; counts are for writers
        await Ok(await w.Requester.Get($"/api/kb/articles/{open}"));                                                    // a view
        await Ok(await w.Requester.Post($"/api/kb/articles/{open}/feedback", new { helpful = false }));
        var mine = await Ok(await w.Requester.Post($"/api/kb/articles/{open}/feedback", new { helpful = true }));
        Assert.True(mine.GetProperty("myFeedback").GetBoolean()); Assert.Equal(0, mine.GetProperty("helpful").GetInt32());
        var counts = await Ok(await w.Agent.Get($"/api/kb/articles/{open}"));
        Assert.Equal(1, counts.GetProperty("helpful").GetInt32()); Assert.Equal(0, counts.GetProperty("notHelpful").GetInt32());
        Assert.True(counts.GetProperty("views").GetInt32() >= 1);

        // suggestions while asking: the service's own articles first, then text matches the person may read
        var forService = await Ok(await w.Requester.Get($"/api/kb/suggest?serviceId={w.ServiceId}"));
        Assert.Contains(forService.EnumerateArray(), s => IdOf(s) == open && s.GetProperty("forService").GetBoolean());
        var typed = await Ok(await w.Requester.Get($"/api/kb/suggest?q={Uri.EscapeDataString(word + " counter")}"));
        Assert.DoesNotContain(typed.EnumerateArray(), s => IdOf(s) == staff);
        Assert.Contains((await Ok(await w.Agent.Get($"/api/kb/suggest?q={Uri.EscapeDataString(word + " counter")}"))).EnumerateArray(), s => IdOf(s) == staff);

        Assert.Equal(HttpStatusCode.NoContent, (await w.Requester.Post($"/api/kb/articles/{open}/solved")).StatusCode);
        Assert.Equal(1, (await Ok(await w.Agent.Get($"/api/kb/articles/{open}"))).GetProperty("deflections").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Post($"/api/kb/articles/{staff}/solved")).StatusCode);
    }

    [Fact]
    public async Task Files_and_categories_belong_to_writers_and_publishers_and_files_follow_their_article()
    {
        var w = await Build();
        var catName = $"Printers {Uniq()}";
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post("/api/kb/categories", new { name = catName })).StatusCode);
        var cat = await Ok(await w.Supervisor.Post("/api/kb/categories", new { name = catName }));
        Assert.True((await Fields(await w.Supervisor.Post("/api/kb/categories", new { name = catName.ToUpperInvariant() }))).TryGetProperty("name", out _));
        var id = IdOf(await Article(w.Agent, $"Printer SOP {Uniq()}", new { categoryId = IdOf(cat), reviewBy = "2020-01-01" }));
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");
        var withFile = await Ok(await w.Agent.UploadAsync($"/api/kb/articles/{id}/files", "diagram.png", png, "image/png"));
        var file = withFile.GetProperty("files")[0];
        Assert.True(withFile.GetProperty("reviewDue").GetBoolean());
        Assert.Contains(id, (await Ok(await w.Agent.Get("/api/kb/articles?status=review"))).GetProperty("data").EnumerateArray().Select(IdOf));
        Assert.Contains(id, (await Ok(await w.Agent.Get($"/api/kb/articles?status=draft&categoryId={IdOf(cat)}"))).GetProperty("data").EnumerateArray().Select(IdOf));
        Assert.Equal(Invalid, (await w.Agent.UploadAsync($"/api/kb/articles/{id}/files", "run.exe", png)).StatusCode);
        Assert.Equal(Invalid, (await w.Agent.UploadAsync($"/api/kb/articles/{id}/files", "notes.txt", Encoding.ASCII.GetBytes("x " + MarkerScanner.Marker))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/kb/articles/{id}/files/{IdOf(file)}")).StatusCode);    // its article is a draft
        await Ok(await w.Supervisor.Post($"/api/kb/articles/{id}/publish"));
        var dl = await w.Requester.Get($"/api/kb/articles/{id}/files/{IdOf(file)}");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode); Assert.Equal(png, await dl.Content.ReadAsByteArrayAsync());
        Assert.Contains("sandbox", dl.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.SendAsync(HttpMethod.Delete, $"/api/kb/articles/{id}/files/{IdOf(file)}")).StatusCode);
        Assert.Empty((await Ok(await w.Agent.SendAsync(HttpMethod.Delete, $"/api/kb/articles/{id}/files/{IdOf(file)}"))).GetProperty("files").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await w.Supervisor.Post($"/api/kb/categories/{IdOf(cat)}/archive")).StatusCode);
        Assert.True((await Fields(await w.Agent.SendAsync(HttpMethod.Put, $"/api/kb/articles/{id}", new { title = "Printer SOP", body = "x", visibility = "everyone", categoryId = IdOf(cat) }))).TryGetProperty("categoryId", out _));
        Assert.Contains((await Ok(await w.Requester.Get("/api/kb/categories"))).EnumerateArray(), c => IdOf(c) == IdOf(cat) && c.GetProperty("archived").GetBoolean());
    }
}
