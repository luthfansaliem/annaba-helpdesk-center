using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AnnabaDigital.Domain.Forms;

/// <summary>The kinds of question a service form can ask. A <c>file</c> question takes uploaded files (for it <c>Max</c> is the number of files, 1–10, default 5); the asset picker arrives with the asset register.</summary>
public static class FieldTypes
{
    public const string Text = "text", LongText = "long_text", Number = "number", Date = "date", DateTime = "datetime",
        Select = "select", MultiSelect = "multi_select", YesNo = "yes_no", Location = "location", User = "user", File = "file";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Text, LongText, Number, Date, DateTime, Select, MultiSelect, YesNo, Location, User, File };
}

public static class ConditionOps
{
    public const string IsEqual = "equals", IsNotEqual = "not_equals", In = "in", Filled = "filled", Empty = "empty";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { IsEqual, IsNotEqual, In, Filled, Empty };
}

public sealed record FieldOption(string Value, string Label);

/// <summary>Show a field only when an earlier field has a given answer. <c>Values</c> is empty for filled/empty.</summary>
public sealed record FieldCondition(string Field, string Op, string[]? Values = null);

public sealed record FormField(
    string Key, string Type, string Label, string? Help = null, bool Required = false, FieldOption[]? Options = null,
    decimal? Min = null, decimal? Max = null, int? MaxLength = null, string? Pattern = null, FieldCondition? VisibleWhen = null);

/// <summary>The outcome of checking a set of answers against a form: normalised values of the visible fields, or per-field errors.</summary>
public sealed record FormEvaluation(IReadOnlyDictionary<string, object?> Values, IReadOnlyDictionary<string, string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The dynamic-form engine (CAT-003). Pure: no database, no clock. It validates a form <i>definition</i> (so a broken form can never be published)
/// and evaluates <i>answers</i> against one (conditional visibility, required rules, type checks). Whether a chosen location or person exists is the caller's job.
/// </summary>
public static class FormEngine
{
    public const int MaxFields = 60, MaxOptions = 50, MaxTextLength = 4000, DefaultTextLength = 200, MaxFiles = 10, DefaultFiles = 5;
    private static readonly Regex KeyPattern = new("^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OffsetSuffix = new(@"(Z|[+-]\d{2}:?\d{2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static string Serialize(IReadOnlyList<FormField> fields) => JsonSerializer.Serialize(fields, Json);
    public static IReadOnlyList<FormField> Deserialize(string json) => JsonSerializer.Deserialize<List<FormField>>(json, Json) ?? [];

    private static bool IsChoice(string? t) => t is FieldTypes.Select or FieldTypes.MultiSelect;

    // =============================================================== definition
    /// <summary>Errors keyed like <c>fields[2].options</c>; empty when the definition is sound.</summary>
    public static Dictionary<string, string> ValidateDefinition(IReadOnlyList<FormField>? fields)
    {
        var errors = new Dictionary<string, string>();
        fields ??= [];
        if (fields.Count > MaxFields) { errors["fields"] = $"A form can have at most {MaxFields} questions."; return errors; }

        var earlier = new Dictionary<string, FormField>();
        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i]; var p = $"fields[{i}]";
            if (f is null) { errors[p] = "Empty question."; continue; }

            var keyOk = f.Key is not null && KeyPattern.IsMatch(f.Key);
            if (!keyOk) errors[$"{p}.key"] = "Use lower-case letters, digits and underscores, starting with a letter (max 40).";
            else if (earlier.ContainsKey(f.Key!)) errors[$"{p}.key"] = "Another question already uses this key.";

            var typeOk = f.Type is not null && FieldTypes.All.Contains(f.Type);
            if (!typeOk) errors[$"{p}.type"] = "Choose a supported question type.";

            var label = (f.Label ?? "").Trim();
            if (label.Length is < 1 or > 120) errors[$"{p}.label"] = "Use 1–120 characters.";
            if (f.Help is { Length: > 300 }) errors[$"{p}.help"] = "Use at most 300 characters.";

            if (typeOk)
            {
                if (IsChoice(f.Type))
                {
                    var opts = f.Options ?? [];
                    if (opts.Length is < 1 or > MaxOptions) errors[$"{p}.options"] = $"Give 1–{MaxOptions} choices.";
                    else if (opts.Any(o => o is null || string.IsNullOrWhiteSpace(o.Value) || o.Value.Trim().Length > 60 || string.IsNullOrWhiteSpace(o.Label) || o.Label.Trim().Length > 120))
                        errors[$"{p}.options"] = "Each choice needs a value (max 60) and a label (max 120).";
                    else if (opts.Select(o => o.Value.Trim()).Distinct(StringComparer.Ordinal).Count() != opts.Length) errors[$"{p}.options"] = "Choice values must be different.";
                }
                else if (f.Options is { Length: > 0 }) errors[$"{p}.options"] = "Only choice questions have choices.";

                if (f.Type == FieldTypes.File)
                {
                    if (f.Min is not null) errors[$"{p}.min"] = "A file question has a most-files limit only.";
                    else if (f.Max is { } mf && (mf < 1 || mf > MaxFiles || mf != decimal.Truncate(mf))) errors[$"{p}.max"] = $"Use a whole number of files from 1 to {MaxFiles}.";
                }
                else if (f.Type != FieldTypes.Number && (f.Min is not null || f.Max is not null)) errors[$"{p}.min"] = "Limits apply to number questions only.";
                else if (f.Min is not null && f.Max is not null && f.Min > f.Max) errors[$"{p}.min"] = "The minimum can't be above the maximum.";

                var texty = f.Type is FieldTypes.Text or FieldTypes.LongText;
                if (!texty && f.MaxLength is not null) errors[$"{p}.maxLength"] = "A length limit applies to text questions only.";
                else if (f.MaxLength is < 1 or > MaxTextLength) errors[$"{p}.maxLength"] = $"Use 1–{MaxTextLength}.";

                if (f.Pattern is not null)
                {
                    if (f.Type != FieldTypes.Text) errors[$"{p}.pattern"] = "A pattern applies to short text questions only.";
                    else if (f.Pattern.Length is 0 or > 200) errors[$"{p}.pattern"] = "Use 1–200 characters.";
                    else { try { _ = new Regex(f.Pattern, RegexOptions.CultureInvariant, RegexTimeout); } catch (ArgumentException) { errors[$"{p}.pattern"] = "This isn't a valid pattern."; } }
                }
            }

            if (f.VisibleWhen is { } c) ValidateCondition(c, f, earlier, p, errors);

            if (keyOk && !earlier.ContainsKey(f.Key!)) earlier[f.Key!] = f;
        }
        return errors;
    }

    private static void ValidateCondition(FieldCondition c, FormField self, Dictionary<string, FormField> earlier, string p, Dictionary<string, string> errors)
    {
        if (c.Op is null || !ConditionOps.All.Contains(c.Op)) { errors[$"{p}.visibleWhen"] = "Choose a valid condition."; return; }
        if (c.Field == self.Key || c.Field is null || !earlier.TryGetValue(c.Field, out var source)) { errors[$"{p}.visibleWhen"] = "A condition can only look at a question placed before this one."; return; }
        var values = c.Values ?? [];
        var needsValues = c.Op is ConditionOps.IsEqual or ConditionOps.IsNotEqual or ConditionOps.In;
        if (!needsValues) { if (values.Length > 0) errors[$"{p}.visibleWhen"] = "This condition takes no value."; return; }
        var max = c.Op == ConditionOps.In ? 20 : 1;
        if (values.Length < 1 || values.Length > max || values.Any(string.IsNullOrWhiteSpace)) { errors[$"{p}.visibleWhen"] = max == 1 ? "Give exactly one value." : "Give 1–20 values."; return; }
        if (IsChoice(source.Type) && source.Options is { } opts && values.Any(v => opts.All(o => o.Value.Trim() != v))) errors[$"{p}.visibleWhen"] = "The value isn't one of that question's choices.";
        else if (source.Type == FieldTypes.YesNo && values.Any(v => v is not ("true" or "false"))) errors[$"{p}.visibleWhen"] = "Use true or false.";
    }

    /// <summary>Trims text and drops values that don't apply to the type, so identical forms serialise identically. Call only on a valid definition.</summary>
    public static List<FormField> Normalize(IReadOnlyList<FormField> fields) => [.. fields.Select(f =>
    {
        var help = string.IsNullOrWhiteSpace(f.Help) ? null : f.Help.Trim();
        var opts = IsChoice(f.Type) ? f.Options!.Select(o => new FieldOption(o.Value.Trim(), o.Label.Trim())).ToArray() : null;
        var cond = f.VisibleWhen is { } c ? new FieldCondition(c.Field, c.Op, c.Values is { Length: > 0 } ? [.. c.Values.Select(v => v.Trim())] : null) : null;
        return new FormField(f.Key, f.Type, f.Label.Trim(), help, f.Required, opts, f.Min, f.Max, f.MaxLength, f.Pattern, cond);
    })];

    // =============================================================== answers
    public static FormEvaluation Evaluate(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, JsonElement>? answers)
    {
        answers ??= new Dictionary<string, JsonElement>();
        var values = new Dictionary<string, object?>();
        var errors = new Dictionary<string, string>();

        var known = fields.Select(f => f.Key).ToHashSet();
        foreach (var k in answers.Keys.Where(k => !known.Contains(k))) errors[k] = "This question isn't part of the form.";

        foreach (var f in fields)
        {
            if (!IsVisible(f, values)) continue; // hidden ⇒ not asked, not required, its answer (if any) is dropped
            answers.TryGetValue(f.Key, out var raw);
            if (IsEmpty(raw))
            {
                if (f.Required) errors[f.Key] = "This is required.";
                continue;
            }
            var (ok, value, error) = Parse(f, raw);
            if (!ok) errors[f.Key] = error!; else values[f.Key] = value;
        }
        return new FormEvaluation(values, errors);
    }

    private static bool IsEmpty(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(e.GetString()),
        JsonValueKind.Array => e.GetArrayLength() == 0,
        _ => false,
    };

    public static bool IsVisible(FormField f, IReadOnlyDictionary<string, object?> values)
    {
        if (f.VisibleWhen is not { } c) return true;
        values.TryGetValue(c.Field, out var actual);
        var answers = Texts(actual);
        var wanted = c.Values ?? [];
        return c.Op switch
        {
            ConditionOps.Filled => answers.Count > 0,
            ConditionOps.Empty => answers.Count == 0,
            ConditionOps.IsEqual => answers.Count > 0 && wanted.Length > 0 && answers.Contains(wanted[0]),
            ConditionOps.IsNotEqual => wanted.Length == 0 || !answers.Contains(wanted[0]),
            ConditionOps.In => answers.Any(a => wanted.Contains(a)),
            _ => false,
        };
    }

    private static List<string> Texts(object? v) => v switch
    {
        null => [],
        string s => [s],
        bool b => [b ? "true" : "false"],
        decimal d => [d.ToString(CultureInfo.InvariantCulture)],
        string[] arr => [.. arr],
        _ => [Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""],
    };

    private static (bool Ok, object? Value, string? Error) Parse(FormField f, JsonElement e)
    {
        switch (f.Type)
        {
            case FieldTypes.Text or FieldTypes.LongText:
            {
                if (e.ValueKind != JsonValueKind.String) return Fail("Enter text.");
                var s = e.GetString()!.Trim();
                var max = f.MaxLength ?? (f.Type == FieldTypes.Text ? DefaultTextLength : MaxTextLength);
                if (s.Length > max) return Fail($"Use at most {max} characters.");
                if (f.Pattern is not null)
                {
                    try { if (!Regex.IsMatch(s, f.Pattern, RegexOptions.CultureInvariant, RegexTimeout)) return Fail("This doesn't match the expected format."); }
                    catch (RegexMatchTimeoutException) { return Fail("This doesn't match the expected format."); }
                }
                return Ok(s);
            }
            case FieldTypes.Number:
            {
                if (e.ValueKind != JsonValueKind.Number || !e.TryGetDecimal(out var n)) return Fail("Enter a number.");
                if (f.Min is { } lo && n < lo) return Fail($"Must be at least {lo.ToString(CultureInfo.InvariantCulture)}.");
                if (f.Max is { } hi && n > hi) return Fail($"Must be at most {hi.ToString(CultureInfo.InvariantCulture)}.");
                return Ok(n);
            }
            case FieldTypes.Date:
                return e.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(e.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? Ok(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : Fail("Enter a date as yyyy-mm-dd.");
            case FieldTypes.DateTime:
            {
                var s = e.ValueKind == JsonValueKind.String ? e.GetString()!.Trim() : "";
                if (OffsetSuffix.IsMatch(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                    return Ok(dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                return Fail("Enter a date and time with its time zone.");
            }
            case FieldTypes.Select:
            {
                var s = e.ValueKind == JsonValueKind.String ? e.GetString()!.Trim() : null;
                return s is not null && f.Options!.Any(o => o.Value == s) ? Ok(s) : Fail("Choose one of the options.");
            }
            case FieldTypes.MultiSelect:
            {
                if (e.ValueKind != JsonValueKind.Array) return Fail("Choose from the options.");
                var picked = new List<string>();
                foreach (var item in e.EnumerateArray())
                {
                    var s = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : null;
                    if (s is null || f.Options!.All(o => o.Value != s)) return Fail("Choose from the options.");
                    if (!picked.Contains(s)) picked.Add(s);
                }
                return Ok(picked.ToArray());
            }
            case FieldTypes.YesNo:
                return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? Ok(e.GetBoolean()) : Fail("Choose yes or no.");
            case FieldTypes.Location or FieldTypes.User:
            {
                var ok = e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var g) && g != Guid.Empty;
                return ok ? Ok(Guid.Parse(e.GetString()!).ToString("D")) : Fail(f.Type == FieldTypes.Location ? "Choose a location." : "Choose a person.");
            }
            case FieldTypes.File:
            {
                if (e.ValueKind != JsonValueKind.Array) return Fail("Attach files.");
                var ids = new List<string>(); var limit = f.Max is { } m ? (int)m : DefaultFiles;
                foreach (var item in e.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var g) || g == Guid.Empty) return Fail("Attach files.");
                    var id = g.ToString("D"); if (!ids.Contains(id)) ids.Add(id);
                }
                return ids.Count > limit ? Fail($"Attach at most {limit} file{(limit == 1 ? "" : "s")}.") : Ok(ids.ToArray());
            }
            default:
                return Fail("Unsupported question type.");
        }

        static (bool, object?, string?) Ok(object value) => (true, value, null);
        static (bool, object?, string?) Fail(string error) => (false, null, error);
    }
}
