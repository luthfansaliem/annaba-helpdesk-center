using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Forms;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Service catalog, versioned forms, starter templates and the requester portal: rules, permissions, visibility, tenant isolation and audit.</summary>
[Collection("db")]
public sealed class CatalogTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static readonly HttpMethod Patch = HttpMethod.Patch;
    private static readonly HttpMethod Put = HttpMethod.Put;
    private const HttpStatusCode Invalid = (HttpStatusCode)422;

    private async Task<TestBrowser> Login(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b;
    }

    private async Task<(Guid TenantId, TestBrowser Admin)> NewTenant()
    {
        var pu = await _seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(fx.Factory); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "c" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Body()).GetProperty("id").GetGuid();
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, StrongPw)).StatusCode);
        return (id, b);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static async Task<List<JsonElement>> List(HttpResponseMessage r) => (await Ok(r)).EnumerateArray().ToList();
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static int VersionOf(JsonElement e) => e.GetProperty("version").GetInt32();

    private static object Cat(string name, Guid? parent = null, int? version = null) => new { name, parentId = parent, description = (string?)null, icon = "wrench", sortOrder = 0, version };
    private static object Svc(string name, Guid category, Guid? team = null, string priority = "medium", string audience = "everyone", int? version = null)
        => new { name, categoryId = category, description = "d", icon = (string?)null, owningTeamId = team, defaultPriority = priority, audience, version };
    private static object Field(string key, string type = "text", bool required = false, object? options = null, object? visibleWhen = null) => new { key, type, label = key.ToUpperInvariant(), required, options, visibleWhen };

    private static Task<HttpResponseMessage> SaveForm(TestBrowser b, Guid svc, object[] fields, int? version = null, string? note = null)
        => b.SendAsync(Put, $"/api/catalog/services/{svc}/form", new { fields, note, version });

    private async Task<JsonElement> CategoryAsync(TestBrowser b, string? name = null, Guid? parent = null) => await Ok(await b.Post("/api/catalog/categories", Cat(name ?? $"Cat {Uniq()}", parent)));
    private async Task<JsonElement> ServiceAsync(TestBrowser b, Guid cat, string? name = null, string audience = "everyone", Guid? team = null) => await Ok(await b.Post("/api/catalog/services", Svc(name ?? $"Svc {Uniq()}", cat, team, audience: audience)));

    // ============================================================ starter catalog
    [Fact]
    public async Task A_new_organisation_starts_with_the_published_starter_catalog()
    {
        var (_, admin) = await NewTenant();
        var cats = await List(await admin.Get("/api/catalog/categories"));
        var svcs = await List(await admin.Get("/api/catalog/services"));
        Assert.Equal(CatalogTemplates.All.Count, cats.Count);
        Assert.Equal(CatalogTemplates.All.Sum(c => c.Services.Length), svcs.Count);
        Assert.All(svcs, s => { Assert.True(s.GetProperty("isPublished").GetBoolean()); Assert.Equal(1, s.GetProperty("currentFormVersion").GetInt32()); });

        var any = svcs.First(s => s.GetProperty("name").GetString() == "Meeting room booking");
        var form = await Ok(await admin.Get($"/api/catalog/services/{IdOf(any)}/form"));
        Assert.Equal(1, form.GetProperty("version").GetInt32()); Assert.True(form.GetProperty("fields").GetArrayLength() >= 6);
        Assert.Equal("Starter template", form.GetProperty("note").GetString());

        // the installed templates are really usable by a requester
        var portal = await Ok(await admin.Get("/api/portal/catalog"));
        Assert.Equal(svcs.Count, portal.GetProperty("services").GetArrayLength());
    }

    // ============================================================ permissions
    [Fact]
    public async Task Catalog_endpoints_need_their_own_permissions_but_the_portal_needs_only_a_sign_in()
    {
        var anon = TestBrowser.Create(fx.Factory);
        foreach (var path in new[] { "/api/catalog/categories", "/api/catalog/services", "/api/portal/catalog" }) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get(path)).StatusCode);

        var plain = await Login("user");
        foreach (var path in new[] { "/api/catalog/categories", "/api/catalog/services" }) Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Post("/api/catalog/categories", Cat("Nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.Get("/api/portal/catalog")).StatusCode);

        await _seed.RoleAsync("catalog_reader", "catalog.read");
        var reader = await Login("catalog_reader");
        Assert.Equal(HttpStatusCode.OK, (await reader.Get("/api/catalog/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.Post("/api/catalog/categories", Cat("Nope"))).StatusCode);

        // forms.manage is separate from catalog.manage: a form editor can change questions but not create services
        await _seed.RoleAsync("form_editor", "catalog.read", "forms.manage");
        var editor = await Login("form_editor"); var admin = await Login("super_admin");
        var svc = IdOf(await ServiceAsync(admin, IdOf(await CategoryAsync(admin))));
        Assert.Equal(HttpStatusCode.OK, (await SaveForm(editor, svc, [Field("what")])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Post("/api/catalog/services", Svc("Nope", Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.SendAsync(Put, $"/api/catalog/services/{svc}/form", new { fields = Array.Empty<object>() })).StatusCode);
    }

    // ============================================================ categories
    [Fact]
    public async Task Categories_have_two_levels_unique_sibling_names_and_cannot_be_archived_while_in_use()
    {
        var (_, a) = await NewTenant(); var n = Uniq();
        var root = await Ok(await a.Post("/api/catalog/categories", Cat($"Root {n}")));
        var child = await Ok(await a.Post("/api/catalog/categories", Cat($"Child {n}", IdOf(root))));
        Assert.Equal(IdOf(root), child.GetProperty("parentId").GetGuid());

        Assert.Equal(Invalid, (await a.Post("/api/catalog/categories", Cat($"Grandchild {n}", IdOf(child)))).StatusCode);           // only two levels
        Assert.Equal(Invalid, (await a.Post("/api/catalog/categories", Cat($"Root {n}"))).StatusCode);                            // duplicate root name
        Assert.Equal(Invalid, (await a.Post("/api/catalog/categories", Cat($"Child {n}", IdOf(root)))).StatusCode);               // duplicate sibling name
        var tooShort = await a.Post("/api/catalog/categories", Cat("x"));                                                         // too short
        Assert.Equal(Invalid, tooShort.StatusCode);
        Assert.Equal("Use 2–100 characters.", (await tooShort.Body()).GetProperty("error").GetProperty("fields").GetProperty("name").GetString()); // text must survive encoding intact
        Assert.Equal(Invalid, (await a.Post("/api/catalog/categories", Cat("Fine name", Guid.NewGuid()))).StatusCode);            // unknown parent
        Assert.Equal(Invalid, (await a.Post("/api/catalog/categories", new { name = "Bad icon", icon = "<script>" })).StatusCode);

        // same name is fine under a different parent
        var other = await Ok(await a.Post("/api/catalog/categories", Cat($"Other {n}")));
        Assert.Equal(HttpStatusCode.Created, (await a.Post("/api/catalog/categories", Cat($"Child {n}", IdOf(other)))).StatusCode);

        // a category that has sub-categories can't be moved under another one; it can't be archived either
        Assert.Equal(Invalid, (await a.SendAsync(Patch, $"/api/catalog/categories/{IdOf(root)}", Cat($"Root {n}", IdOf(other), VersionOf(root)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/categories/{IdOf(root)}/archive")).StatusCode);

        // nor while it still holds a live service
        var svc = await ServiceAsync(a, IdOf(child));
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/categories/{IdOf(child)}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Post($"/api/catalog/services/{IdOf(svc)}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.Post($"/api/catalog/categories/{IdOf(child)}/archive")).StatusCode);

        // restore: the parent must be live, and the name must be free
        Assert.Equal(HttpStatusCode.OK, (await a.Post($"/api/catalog/categories/{IdOf(child)}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/categories/{IdOf(child)}/restore")).StatusCode); // no longer archived
    }

    [Fact]
    public async Task Updating_a_category_is_audited_and_detects_stale_versions()
    {
        var (_, a) = await NewTenant();
        var c = await CategoryAsync(a); var id = IdOf(c);
        var res = await a.SendAsync(Patch, $"/api/catalog/categories/{id}", new { name = "Renamed " + Uniq(), description = "New text", icon = "car", sortOrder = 5, version = VersionOf(c) });
        var updated = await Ok(res);
        Assert.Equal(VersionOf(c) + 1, VersionOf(updated)); Assert.Equal(5, updated.GetProperty("sortOrder").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await a.SendAsync(Patch, $"/api/catalog/categories/{id}", new { name = "Again", version = VersionOf(c) })).StatusCode); // stale
        Assert.Contains("catalog.category.update", await _seed.Db(db => db.AuditEvents.Where(e => e.TargetId == id.ToString()).Select(e => e.Action).ToListAsync()));

        var same = await Ok(await a.SendAsync(Patch, $"/api/catalog/categories/{id}", new { name = updated.GetProperty("name").GetString(), description = "New text", icon = "car", sortOrder = 5, version = VersionOf(updated) }));
        Assert.Equal(VersionOf(updated), VersionOf(same)); // a no-op changes nothing
    }

    // ============================================================ services
    [Fact]
    public async Task Services_validate_their_references_and_start_unpublished_with_no_form()
    {
        var (_, a) = await NewTenant(); var n = Uniq();
        var cat = IdOf(await CategoryAsync(a));
        var team = await Ok(await a.Post("/api/teams", new { name = $"Team {n}" })); var teamId = IdOf(team);

        var s = await Ok(await a.Post("/api/catalog/services", Svc($"AC {n}", cat, teamId, "high", "staff")));
        Assert.False(s.GetProperty("isPublished").GetBoolean()); Assert.Equal(0, s.GetProperty("currentFormVersion").GetInt32());
        Assert.Equal("high", s.GetProperty("defaultPriority").GetString()); Assert.Equal("staff", s.GetProperty("audience").GetString());
        Assert.Equal($"Team {n}", s.GetProperty("owningTeamName").GetString());

        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc($"AC {n}", cat))).StatusCode);                              // duplicate name in the category
        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc("No category", Guid.NewGuid()))).StatusCode);
        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc("Bad team", cat, Guid.NewGuid()))).StatusCode);
        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc("Bad priority", cat, priority: "asap"))).StatusCode);
        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc("Bad audience", cat, audience: "friends"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await a.Post("/api/catalog/services", Svc($"AC {n}", IdOf(await CategoryAsync(a))))).StatusCode); // same name elsewhere is fine

        // a team in use can't be archived; an archived team can't be chosen
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/teams/{teamId}/archive")).StatusCode);
        var other = await Ok(await a.Post("/api/teams", new { name = $"Other {n}" }));
        Assert.Equal(HttpStatusCode.OK, (await a.Post($"/api/teams/{IdOf(other)}/archive")).StatusCode);
        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc("Archived team", cat, IdOf(other)))).StatusCode);

        // an archived category can't receive services
        var dead = await CategoryAsync(a); await a.Post($"/api/catalog/categories/{IdOf(dead)}/archive");
        Assert.Equal(Invalid, (await a.Post("/api/catalog/services", Svc("In archived", IdOf(dead)))).StatusCode);
    }

    [Fact]
    public async Task Updating_publishing_and_archiving_a_service_are_audited_and_restoring_never_republishes()
    {
        var (_, a) = await NewTenant();
        var cat = IdOf(await CategoryAsync(a)); var s = await ServiceAsync(a, cat); var id = IdOf(s);

        var upd = await Ok(await a.SendAsync(Patch, $"/api/catalog/services/{id}", Svc("Renamed " + Uniq(), cat, priority: "urgent", version: VersionOf(s))));
        Assert.Equal("urgent", upd.GetProperty("defaultPriority").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await a.SendAsync(Patch, $"/api/catalog/services/{id}", Svc("Stale", cat, version: VersionOf(s)))).StatusCode);

        var pub = await Ok(await a.Post($"/api/catalog/services/{id}/publish")); Assert.True(pub.GetProperty("isPublished").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/services/{id}/publish")).StatusCode);       // already published
        var archived = await Ok(await a.Post($"/api/catalog/services/{id}/archive"));
        Assert.True(archived.GetProperty("archived").GetBoolean()); Assert.False(archived.GetProperty("isPublished").GetBoolean()); // archiving unpublishes
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/services/{id}/publish")).StatusCode);       // archived: restore first
        Assert.Equal(HttpStatusCode.Conflict, (await a.SendAsync(Patch, $"/api/catalog/services/{id}", Svc("Edit archived", cat))).StatusCode);
        var restored = await Ok(await a.Post($"/api/catalog/services/{id}/restore"));
        Assert.False(restored.GetProperty("archived").GetBoolean()); Assert.False(restored.GetProperty("isPublished").GetBoolean());

        var actions = await _seed.Db(db => db.AuditEvents.Where(e => e.TargetId == id.ToString()).Select(e => e.Action).ToListAsync());
        Assert.Contains("catalog.service.update", actions); Assert.Contains("catalog.service.publish", actions); Assert.Contains("catalog.service.archive", actions); Assert.Contains("catalog.service.restore", actions);
    }

    [Fact]
    public async Task Restoring_a_service_needs_a_live_category_and_a_free_name()
    {
        var (_, a) = await NewTenant();
        var cat = await CategoryAsync(a); var s = await ServiceAsync(a, IdOf(cat), "Unique name");
        await a.Post($"/api/catalog/services/{IdOf(s)}/archive");
        await a.Post($"/api/catalog/categories/{IdOf(cat)}/archive");
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/services/{IdOf(s)}/restore")).StatusCode);      // its category is archived
        await a.Post($"/api/catalog/categories/{IdOf(cat)}/restore");
        await ServiceAsync(a, IdOf(cat), "Unique name");                                                                    // the name is taken meanwhile
        Assert.Equal(HttpStatusCode.Conflict, (await a.Post($"/api/catalog/services/{IdOf(s)}/restore")).StatusCode);
    }

    // ============================================================ forms
    [Fact]
    public async Task Saving_a_form_creates_immutable_versions_and_an_identical_save_changes_nothing()
    {
        var (_, a) = await NewTenant();
        var s = await ServiceAsync(a, IdOf(await CategoryAsync(a))); var id = IdOf(s);

        var v1 = await Ok(await SaveForm(a, id, [Field("what", required: true)], VersionOf(s), "first"));
        Assert.Equal(1, v1.GetProperty("version").GetInt32()); Assert.Equal("first", v1.GetProperty("note").GetString());
        var svcAfter = await Ok(await a.Get($"/api/catalog/services/{id}")); Assert.Equal(1, svcAfter.GetProperty("currentFormVersion").GetInt32());

        var same = await Ok(await SaveForm(a, id, [Field("what", required: true)], VersionOf(svcAfter)));
        Assert.Equal(1, same.GetProperty("version").GetInt32());                                  // identical â‡’ no new version
        Assert.Equal(VersionOf(svcAfter), VersionOf(await Ok(await a.Get($"/api/catalog/services/{id}"))));

        var v2 = await Ok(await SaveForm(a, id, [Field("what", required: true), Field("when", "datetime")], VersionOf(svcAfter), "added when"));
        Assert.Equal(2, v2.GetProperty("version").GetInt32()); Assert.Equal(2, v2.GetProperty("fields").GetArrayLength());

        // history keeps the old version exactly as it was
        var old = await Ok(await a.Get($"/api/catalog/services/{id}/form/versions/1"));
        Assert.Equal(1, old.GetProperty("fields").GetArrayLength()); Assert.Equal("first", old.GetProperty("note").GetString());
        var versions = await List(await a.Get($"/api/catalog/services/{id}/form/versions"));
        Assert.Equal([2, 1], versions.Select(v => v.GetProperty("number").GetInt32()).ToArray());
        Assert.True(versions[0].GetProperty("current").GetBoolean()); Assert.False(versions[1].GetProperty("current").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await a.Get($"/api/catalog/services/{id}/form/versions/9")).StatusCode);

        // the form can be cleared (a new version), and an empty form on a service that never had one is a no-op
        var cleared = await Ok(await SaveForm(a, id, []));
        Assert.Equal(3, cleared.GetProperty("version").GetInt32()); Assert.Equal(0, cleared.GetProperty("fields").GetArrayLength());
        var fresh = await ServiceAsync(a, IdOf(await CategoryAsync(a)));
        Assert.Equal(0, (await Ok(await SaveForm(a, IdOf(fresh), []))).GetProperty("version").GetInt32());

        var actions = await _seed.Db(db => db.AuditEvents.Where(e => e.TargetId == id.ToString()).Select(e => e.Action).ToListAsync());
        Assert.Equal(3, actions.Count(x => x == "catalog.form.save"));
    }

    [Fact]
    public async Task A_broken_form_is_refused_with_per_question_errors_and_stale_editors_conflict()
    {
        var (_, a) = await NewTenant();
        var s = await ServiceAsync(a, IdOf(await CategoryAsync(a))); var id = IdOf(s);

        var bad = await SaveForm(a, id, [Field("Bad Key"), Field("pick", "select"), Field("x", "teleport")]);
        Assert.Equal(Invalid, bad.StatusCode);
        var fields = (await bad.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("fields[0].key", out _)); Assert.True(fields.TryGetProperty("fields[1].options", out _)); Assert.True(fields.TryGetProperty("fields[2].type", out _));
        Assert.Equal(0, (await Ok(await a.Get($"/api/catalog/services/{id}"))).GetProperty("currentFormVersion").GetInt32()); // nothing was saved

        Assert.Equal(HttpStatusCode.OK, (await SaveForm(a, id, [Field("what")], VersionOf(s))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SaveForm(a, id, [Field("other")], VersionOf(s))).StatusCode);          // saved from a stale copy

        await a.Post($"/api/catalog/services/{id}/archive");
        Assert.Equal(HttpStatusCode.Conflict, (await SaveForm(a, id, [Field("late")])).StatusCode);                         // archived services are frozen
        Assert.Equal(HttpStatusCode.NotFound, (await SaveForm(a, Guid.NewGuid(), [Field("x")])).StatusCode);
    }

    // ============================================================ portal
    [Fact]
    public async Task The_portal_shows_only_published_live_services_for_the_persons_audience()
    {
        var (_, a) = await NewTenant();
        var user = await InviteUser(a);                                                                                       // a plain requester of this organisation
        var cat = await CategoryAsync(a, "Visible things"); var empty = await CategoryAsync(a, "Nothing inside");
        var shown = await ServiceAsync(a, IdOf(cat), "Shown"); await a.Post($"/api/catalog/services/{IdOf(shown)}/publish");
        var draft = await ServiceAsync(a, IdOf(cat), "Draft");
        var staff = await ServiceAsync(a, IdOf(cat), "Staff only", "staff"); await a.Post($"/api/catalog/services/{IdOf(staff)}/publish");
        var gone = await ServiceAsync(a, IdOf(cat), "Gone"); await a.Post($"/api/catalog/services/{IdOf(gone)}/publish"); await a.Post($"/api/catalog/services/{IdOf(gone)}/archive");

        // every organisation also has the starter catalog, so compare only the things this test created
        var mine = new[] { "Shown", "Draft", "Staff only", "Gone", "In sub" };
        var mineCats = new[] { "Visible things", "Nothing inside", "Sub" };
        static string[] Svcs(JsonElement p, string[] only) => [.. p.GetProperty("services").EnumerateArray().Select(s => s.GetProperty("name").GetString()!).Where(only.Contains).Order()];
        static string[] Cats(JsonElement p, string[] only) => [.. p.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).Where(only.Contains).Order()];

        var asUser = await Ok(await user.Get("/api/portal/catalog"));
        Assert.Equal(["Shown"], Svcs(asUser, mine));
        Assert.Equal(["Visible things"], Cats(asUser, mineCats));                                                          // the empty category is not shown

        var asAdmin = await Ok(await a.Get("/api/portal/catalog"));
        Assert.Equal(["Shown", "Staff only"], Svcs(asAdmin, mine));

        Assert.Equal(HttpStatusCode.OK, (await user.Get($"/api/portal/services/{IdOf(shown)}")).StatusCode);
        foreach (var hidden in new[] { draft, staff, gone }) Assert.Equal(HttpStatusCode.NotFound, (await user.Get($"/api/portal/services/{IdOf(hidden)}")).StatusCode); // hidden looks like missing
        Assert.Equal(HttpStatusCode.NotFound, (await user.Get($"/api/portal/services/{Guid.NewGuid()}")).StatusCode);

        // search by name or description, case-insensitively; wildcard characters are literal
        Assert.Equal(["Shown"], Svcs(await Ok(await user.Get("/api/portal/catalog?q=sHoWn")), mine));
        Assert.Empty((await Ok(await user.Get("/api/portal/catalog?q=zzz"))).GetProperty("services").EnumerateArray());
        Assert.Empty((await Ok(await user.Get("/api/portal/catalog?q=%25"))).GetProperty("services").EnumerateArray());

        // a sub-category's services show; if the category chain is archived underneath them they vanish (defence in depth: archiving is normally blocked while services are live)
        var sub = await CategoryAsync(a, "Sub", IdOf(cat)); var inSub = await ServiceAsync(a, IdOf(sub), "In sub"); await a.Post($"/api/catalog/services/{IdOf(inSub)}/publish");
        Assert.Equal(["In sub", "Shown"], Svcs(await Ok(await user.Get("/api/portal/catalog")), mine));
        Assert.Equal(["Sub", "Visible things"], Cats(await Ok(await user.Get("/api/portal/catalog")), mineCats));
        await _seed.Db(async db => { await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE service_categories SET archived_at = now() WHERE id = {IdOf(cat)}"); return 0; });
        Assert.Empty(Svcs(await Ok(await user.Get("/api/portal/catalog")), mine));
        Assert.Empty(Cats(await Ok(await user.Get("/api/portal/catalog")), mineCats));
    }
    [Fact]
    public async Task Answers_are_checked_against_the_published_form_including_places_and_people()
    {
        var (_, a) = await NewTenant();
        var user = await InviteUser(a);
        var site = await Ok(await a.Post("/api/locations", new { type = "site", code = "HQ-" + Uniq(), name = "HQ" }));
        var gone = await Ok(await a.Post("/api/locations", new { type = "site", code = "OLD-" + Uniq(), name = "Old" })); await a.Post($"/api/locations/{IdOf(gone)}/archive");
        var s = await ServiceAsync(a, IdOf(await CategoryAsync(a)), "Repair");
        var kind = new[] { new { value = "ac", label = "AC" }, new { value = "light", label = "Light" } };
        Assert.Equal(HttpStatusCode.OK, (await SaveForm(a, IdOf(s), [
            Field("where", "location", true), Field("kind", "select", true, kind),
            Field("why", "long_text", true, visibleWhen: new { field = "kind", op = "equals", values = new[] { "ac" } }),
        ])).StatusCode);
        await a.Post($"/api/catalog/services/{IdOf(s)}/publish");
        var url = $"/api/portal/services/{IdOf(s)}/check";

        var detail = await Ok(await user.Get($"/api/portal/services/{IdOf(s)}"));
        Assert.Equal(1, detail.GetProperty("formVersion").GetInt32()); Assert.Equal(3, detail.GetProperty("fields").GetArrayLength());

        var good = await Ok(await user.Post(url, new { answers = new { where = IdOf(site), kind = "light", why = "ignored because hidden" } }));
        Assert.Equal(1, good.GetProperty("formVersion").GetInt32());
        Assert.Equal(IdOf(site).ToString("D"), good.GetProperty("values").GetProperty("where").GetString());
        Assert.False(good.GetProperty("values").TryGetProperty("why", out _));                                           // hidden answers are dropped

        var missing = await user.Post(url, new { answers = new { kind = "ac" } });
        Assert.Equal(Invalid, missing.StatusCode);
        var errs = (await missing.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(errs.TryGetProperty("where", out _)); Assert.True(errs.TryGetProperty("why", out _));                 // `why` is required once `kind` is ac

        Assert.Equal(Invalid, (await user.Post(url, new { answers = new { where = IdOf(gone), kind = "light" } })).StatusCode);         // archived location
        Assert.Equal(Invalid, (await user.Post(url, new { answers = new { where = Guid.NewGuid(), kind = "light" } })).StatusCode);     // unknown location
        Assert.Equal(Invalid, (await user.Post(url, new { answers = new { where = IdOf(site), kind = "heater" } })).StatusCode);       // not an option
        Assert.Equal(Invalid, (await user.Post(url, new { answers = new { where = IdOf(site), kind = "light", extra = 1 } })).StatusCode); // unknown question

        // an edit creates version 2; the portal then validates against it
        Assert.Equal(HttpStatusCode.OK, (await SaveForm(a, IdOf(s), [Field("where", "location", true)])).StatusCode);
        var v2 = await Ok(await user.Post(url, new { answers = new { where = IdOf(site) } }));
        Assert.Equal(2, v2.GetProperty("formVersion").GetInt32());
    }

    // ============================================================ tenant isolation
    [Fact]
    public async Task One_organisations_catalog_is_invisible_to_another()
    {
        var (_, a) = await NewTenant(); var (_, b) = await NewTenant();
        var catA = await CategoryAsync(a, "Only A"); var sA = await ServiceAsync(a, IdOf(catA), "A service"); await a.Post($"/api/catalog/services/{IdOf(sA)}/publish");
        var id = IdOf(sA);

        Assert.DoesNotContain(await List(await b.Get("/api/catalog/categories?includeArchived=true")), c => IdOf(c) == IdOf(catA));
        Assert.DoesNotContain(await List(await b.Get("/api/catalog/services?includeArchived=true")), s => IdOf(s) == id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/catalog/services/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/catalog/services/{id}/form")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/portal/services/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveForm(b, id, [Field("x")])).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/catalog/services/{id}/archive")).StatusCode);
        Assert.Equal(Invalid, (await b.Post("/api/catalog/services", Svc("Steal", IdOf(catA)))).StatusCode);              // can't file a service under another organisation's category
        Assert.Equal(Invalid, (await b.Post("/api/catalog/categories", Cat("Steal", IdOf(catA)))).StatusCode);
    }

    /// <summary>A plain requester in the same organisation as <paramref name="admin"/>, created through the normal invitation flow.</summary>
    private async Task<TestBrowser> InviteUser(TestBrowser admin)
    {
        var email = $"req{Uniq()}@test.local";
        await admin.Post("/api/auth/reauth", new { password = StrongPw });
        var userRole = (await List(await admin.Get("/api/roles"))).Single(r => r.GetProperty("key").GetString() == "user").GetProperty("id").GetGuid();
        var res = await admin.Post("/api/invitations", new { email, roleIds = new[] { userRole } });
        Assert.True(res.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Requester", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, StrongPw)).StatusCode);
        return b;
    }
}
