using System.Text.Json;
using AnnabaDigital.Domain.Forms;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>CAT-002: the starter catalog must be valid by construction, using the very validator the admin screens use.</summary>
public class CatalogTemplateTests
{
    [Fact]
    public void Every_starter_form_passes_the_form_validator()
    {
        foreach (var svc in AnnabaDigital.Application.Helpdesk.CatalogTemplates.All.SelectMany(c => c.Services))
            Assert.True(FormEngine.ValidateDefinition(svc.Fields).Count == 0, $"{svc.Name}: {string.Join("; ", FormEngine.ValidateDefinition(svc.Fields).Select(e => $"{e.Key} {e.Value}"))}");
    }

    [Fact]
    public void Starter_names_are_unique_and_within_the_limits_the_admin_screens_enforce()
    {
        var cats = AnnabaDigital.Application.Helpdesk.CatalogTemplates.All;
        Assert.Equal(cats.Count, cats.Select(c => c.Name).Distinct().Count());
        Assert.All(cats, c =>
        {
            Assert.InRange(c.Name.Length, 2, 100); Assert.InRange(c.Description.Length, 1, 300);
            Assert.Equal(c.Services.Length, c.Services.Select(s => s.Name).Distinct().Count());
            Assert.All(c.Services, s => { Assert.InRange(s.Name.Length, 2, 120); Assert.InRange(s.Description.Length, 1, 500); Assert.Matches("^[a-z][a-z0-9-]{0,39}$", s.Icon); });
        });
        Assert.Contains(cats.SelectMany(c => c.Services), s => s.Fields.Any(f => f.VisibleWhen is not null)); // the templates exercise conditional questions
    }
}

/// <summary>CAT-003: the dynamic-form engine is pure, so it is tested without a database.</summary>
public class FormEngineTests
{
    private static FormField F(string key, string type = FieldTypes.Text, bool required = false, FieldOption[]? options = null, FieldCondition? when = null,
        decimal? min = null, decimal? max = null, int? maxLength = null, string? pattern = null)
        => new(key, type, key.ToUpperInvariant(), null, required, options, min, max, maxLength, pattern, when);

    private static readonly FieldOption[] Colours = [new("red", "Red"), new("blue", "Blue")];

    private static Dictionary<string, JsonElement> A(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    // ---------------------------------------------------------------- definition
    [Fact]
    public void A_sound_definition_has_no_errors()
    {
        var fields = new[]
        {
            F("what", required: true), F("kind", FieldTypes.Select, options: Colours),
            F("why", FieldTypes.LongText, when: new FieldCondition("kind", ConditionOps.IsEqual, ["red"])),
            F("qty", FieldTypes.Number, min: 1, max: 10), F("urgent", FieldTypes.YesNo), F("where", FieldTypes.Location),
        };
        Assert.Empty(FormEngine.ValidateDefinition(fields));
        Assert.Empty(FormEngine.ValidateDefinition([]));
    }

    [Theory]
    [InlineData("Bad Key")] [InlineData("1abc")] [InlineData("")] [InlineData("UPPER")] [InlineData("a-b")]
    public void Keys_are_lower_snake_case_starting_with_a_letter(string key)
        => Assert.Contains("fields[0].key", FormEngine.ValidateDefinition([F(key)]).Keys);

    [Fact]
    public void Keys_must_be_unique_and_the_type_known()
    {
        var errors = FormEngine.ValidateDefinition([F("a"), F("a"), F("b", "teleport")]);
        Assert.Contains("fields[1].key", errors.Keys); Assert.Contains("fields[2].type", errors.Keys);
    }

    [Fact]
    public void Choice_questions_need_distinct_choices_and_others_must_not_have_any()
    {
        Assert.Contains("fields[0].options", FormEngine.ValidateDefinition([F("a", FieldTypes.Select)]).Keys);
        Assert.Contains("fields[0].options", FormEngine.ValidateDefinition([F("a", FieldTypes.Select, options: [new("x", "X"), new("x", "Y")])]).Keys);
        Assert.Contains("fields[0].options", FormEngine.ValidateDefinition([F("a", FieldTypes.Text, options: Colours)]).Keys);
        Assert.Empty(FormEngine.ValidateDefinition([F("a", FieldTypes.MultiSelect, options: Colours)]));
    }

    [Fact]
    public void Limits_and_patterns_apply_only_to_the_types_they_make_sense_for()
    {
        Assert.Contains("fields[0].min", FormEngine.ValidateDefinition([F("a", min: 1)]).Keys);
        Assert.Contains("fields[0].min", FormEngine.ValidateDefinition([F("a", FieldTypes.Number, min: 5, max: 1)]).Keys);
        Assert.Contains("fields[0].maxLength", FormEngine.ValidateDefinition([F("a", FieldTypes.Number, maxLength: 5)]).Keys);
        Assert.Contains("fields[0].maxLength", FormEngine.ValidateDefinition([F("a", maxLength: 99999)]).Keys);
        Assert.Contains("fields[0].pattern", FormEngine.ValidateDefinition([F("a", FieldTypes.LongText, pattern: "^x$")]).Keys);
        Assert.Contains("fields[0].pattern", FormEngine.ValidateDefinition([F("a", pattern: "([")]).Keys);
    }

    [Fact]
    public void A_condition_can_only_look_backwards_at_a_compatible_value()
    {
        var self = F("a", when: new FieldCondition("a", ConditionOps.Filled));
        var later = new[] { F("a", when: new FieldCondition("b", ConditionOps.Filled)), F("b") };
        var unknownChoice = new[] { F("k", FieldTypes.Select, options: Colours), F("a", when: new FieldCondition("k", ConditionOps.IsEqual, ["green"])) };
        var badBool = new[] { F("y", FieldTypes.YesNo), F("a", when: new FieldCondition("y", ConditionOps.IsEqual, ["maybe"])) };
        var noValue = new[] { F("k", FieldTypes.Select, options: Colours), F("a", when: new FieldCondition("k", ConditionOps.IsEqual)) };
        var badOp = new[] { F("k"), F("a", when: new FieldCondition("k", "kinda")) };
        var extra = new[] { F("k"), F("a", when: new FieldCondition("k", ConditionOps.Filled, ["x"])) };
        foreach (var bad in new[] { new[] { self }, later, unknownChoice, badBool, noValue, badOp, extra })
            Assert.Contains(FormEngine.ValidateDefinition(bad).Keys, k => k.EndsWith(".visibleWhen"));
        Assert.Empty(FormEngine.ValidateDefinition([F("y", FieldTypes.YesNo), F("a", when: new FieldCondition("y", ConditionOps.IsEqual, ["true"]))]));
    }

    [Fact]
    public void A_form_has_a_size_limit()
        => Assert.Contains("fields", FormEngine.ValidateDefinition([.. Enumerable.Range(0, FormEngine.MaxFields + 1).Select(i => F($"f{i}"))]).Keys);

    [Fact]
    public void Normalising_trims_text_and_makes_identical_forms_serialise_identically()
    {
        var a = FormEngine.Normalize([new FormField("a", FieldTypes.Text, "  What?  ", "  ", true, null, null, null, 50)]);
        var b = FormEngine.Normalize([new FormField("a", FieldTypes.Text, "What?", null, true, null, null, null, 50)]);
        Assert.Equal(FormEngine.Serialize(a), FormEngine.Serialize(b));
        Assert.Equal("What?", a[0].Label); Assert.Null(a[0].Help);
        Assert.Equal(a.Count, FormEngine.Deserialize(FormEngine.Serialize(a)).Count); // round-trips
    }

    // ---------------------------------------------------------------- answers
    [Fact]
    public void Required_visible_questions_must_be_answered_and_unknown_answers_are_refused()
    {
        var r = FormEngine.Evaluate([F("a", required: true), F("b")], A("""{ "a": "  ", "zzz": 1 }"""));
        Assert.False(r.IsValid);
        Assert.Equal("This is required.", r.Errors["a"]); Assert.True(r.Errors.ContainsKey("zzz")); Assert.False(r.Errors.ContainsKey("b"));
    }

    [Fact]
    public void Text_is_trimmed_and_checked_against_length_and_pattern()
    {
        var f = new[] { F("code", maxLength: 5, pattern: "^[A-Z]{3}$") };
        Assert.Equal("ABC", FormEngine.Evaluate(f, A("""{ "code": "  ABC " }""")).Values["code"]);
        Assert.False(FormEngine.Evaluate(f, A("""{ "code": "abc" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "code": "ABCDEF" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "code": 5 }""")).IsValid);
        Assert.False(FormEngine.Evaluate([F("t")], A($$"""{ "t": "{{new string('x', FormEngine.DefaultTextLength + 1)}}" }""")).IsValid); // short text default cap
    }

    [Fact]
    public void Numbers_must_be_numbers_within_limits()
    {
        var f = new[] { F("n", FieldTypes.Number, min: 1, max: 10) };
        Assert.Equal(2.5m, FormEngine.Evaluate(f, A("""{ "n": 2.5 }""")).Values["n"]);
        Assert.False(FormEngine.Evaluate(f, A("""{ "n": 0 }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "n": 11 }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "n": "5" }""")).IsValid); // a string is not a number
    }

    [Fact]
    public void Dates_and_date_times_are_strict_and_date_times_are_normalised_to_utc()
    {
        var f = new[] { F("d", FieldTypes.Date), F("t", FieldTypes.DateTime) };
        var ok = FormEngine.Evaluate(f, A("""{ "d": "2026-12-25", "t": "2026-12-25T09:00:00+07:00" }"""));
        Assert.True(ok.IsValid); Assert.Equal("2026-12-25", ok.Values["d"]); Assert.Equal("2026-12-25T02:00:00Z", ok.Values["t"]);
        Assert.False(FormEngine.Evaluate(f, A("""{ "d": "25-12-2026" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "d": "2026-02-30" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "t": "2026-12-25T09:00:00" }""")).IsValid); // no zone = ambiguous
    }

    [Fact]
    public void Choices_yes_no_and_ids_are_checked()
    {
        var f = new[] { F("c", FieldTypes.Select, options: Colours), F("m", FieldTypes.MultiSelect, options: Colours), F("y", FieldTypes.YesNo), F("l", FieldTypes.Location), F("u", FieldTypes.User) };
        var id = Guid.NewGuid();
        var ok = FormEngine.Evaluate(f, A($$"""{ "c": "red", "m": ["red","blue","red"], "y": false, "l": "{{id}}", "u": "{{id}}" }"""));
        Assert.True(ok.IsValid);
        Assert.Equal(["red", "blue"], (string[])ok.Values["m"]!); // de-duplicated, in the order given
        Assert.Equal(false, ok.Values["y"]); Assert.Equal(id.ToString("D"), ok.Values["l"]);
        Assert.False(FormEngine.Evaluate(f, A("""{ "c": "green" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "m": ["red","green"] }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "y": "yes" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("""{ "l": "not-a-guid" }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A($$"""{ "u": "{{Guid.Empty}}" }""")).IsValid);
    }

    [Fact]
    public void Yes_no_required_means_it_must_be_answered_but_false_counts()
    {
        var f = new[] { F("y", FieldTypes.YesNo, required: true) };
        Assert.True(FormEngine.Evaluate(f, A("""{ "y": false }""")).IsValid);
        Assert.False(FormEngine.Evaluate(f, A("{}")).IsValid);
    }

    [Fact]
    public void Hidden_questions_are_not_required_and_their_answers_are_dropped()
    {
        var f = new[]
        {
            F("kind", FieldTypes.Select, required: true, options: Colours),
            F("detail", required: true, when: new FieldCondition("kind", ConditionOps.IsEqual, ["red"])),
        };
        var blue = FormEngine.Evaluate(f, A("""{ "kind": "blue", "detail": "ignored" }"""));
        Assert.True(blue.IsValid); Assert.False(blue.Values.ContainsKey("detail"));
        var red = FormEngine.Evaluate(f, A("""{ "kind": "red" }"""));
        Assert.Equal("This is required.", red.Errors["detail"]);
        Assert.True(FormEngine.Evaluate(f, A("""{ "kind": "red", "detail": "leak" }""")).IsValid);
    }

    [Fact]
    public void Conditions_chain_and_every_operator_works()
    {
        var f = new[]
        {
            F("a", FieldTypes.YesNo), F("b", when: new FieldCondition("a", ConditionOps.IsEqual, ["true"])),
            F("c", when: new FieldCondition("b", ConditionOps.Filled)),            // hidden whenever b is hidden or empty
            F("d", when: new FieldCondition("a", ConditionOps.IsNotEqual, ["true"])),
            F("e", FieldTypes.MultiSelect, options: Colours), F("g", when: new FieldCondition("e", ConditionOps.In, ["blue", "zzz"])),
            F("h", when: new FieldCondition("b", ConditionOps.Empty)),
        };
        var yes = FormEngine.Evaluate(f, A("""{ "a": true, "b": "x", "c": "y", "d": "no", "e": ["blue"], "g": "ok", "h": "no" }"""));
        Assert.Equal(["a", "b", "c", "e", "g"], yes.Values.Keys.OrderBy(k => k).ToArray()); // d (not_equals) and h (empty) are hidden here
        var no = FormEngine.Evaluate(f, A("""{ "a": false, "d": "yes", "h": "yes" }"""));
        Assert.Equal(["a", "d", "h"], no.Values.Keys.OrderBy(k => k).ToArray());
    }
}
