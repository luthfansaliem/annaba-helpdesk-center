using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Forms;

namespace AnnabaDigital.Domain;

/// <summary>AST-001/006. The pure rules of the asset register: what a tag may look like, which question types an asset type may ask, status names, and CSV reading.</summary>
public static partial class AssetRules
{
    public const int NameMin = 2, NameMax = 150, SerialMax = 100, DepartmentMax = 100, VendorMax = 120, NotesMax = 2000, ReasonMin = 3, ReasonMax = 300;
    public const decimal CostMax = 1_000_000_000_000m;
    public const string AutoTagPrefix = "AST-";

    [GeneratedRegex("^[A-Z0-9][A-Z0-9._/-]{0,39}$")] private static partial Regex TagShape();

    /// <summary>Upper case, trimmed; null when empty.</summary>
    public static string? NormaliseTag(string? tag) { var t = (tag ?? "").Trim().ToUpperInvariant(); return t.Length == 0 ? null : t; }
    public static bool IsValidTag(string tag) => TagShape().IsMatch(tag);
    public static string AutoTag(int n) => $"{AutoTagPrefix}{n:D6}";

    /// <summary>Asset types ask simple questions only: no files, places or people (the asset has its own place and person).</summary>
    public static readonly IReadOnlySet<string> FieldTypes = new HashSet<string> { Forms.FieldTypes.Text, Forms.FieldTypes.LongText, Forms.FieldTypes.Number, Forms.FieldTypes.Date, Forms.FieldTypes.Select, Forms.FieldTypes.YesNo };
    public const int MaxTypeFields = 20;

    public static string StatusName(AssetStatus s) => s switch { AssetStatus.InUse => "in_use", AssetStatus.InRepair => "in_repair", AssetStatus.Stored => "stored", _ => "retired" };
    public static AssetStatus? ParseStatus(string? s) => (s ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_') switch
    {
        "in_use" or "inuse" or "" => AssetStatus.InUse, "in_repair" or "inrepair" => AssetStatus.InRepair, "stored" => AssetStatus.Stored, "retired" => AssetStatus.Retired, _ => null,
    };

    /// <summary>Dates in CSV files and requests: <c>yyyy-MM-dd</c> only (no ambiguous day/month orders), between 1950 and 2100.</summary>
    public static bool TryDate(string? s, out DateOnly? d)
    {
        d = null; var t = (s ?? "").Trim(); if (t.Length == 0) return true;
        if (!DateOnly.TryParseExact(t, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) || v.Year < 1950 || v.Year > 2100) return false;
        d = v; return true;
    }

    /// <summary>A cost with at most two decimals, 0 up to a trillion; dot as decimal separator (thousands separators are not accepted, to avoid 1.000 meaning one or a thousand).</summary>
    public static bool TryCost(string? s, out decimal? c)
    {
        c = null; var t = (s ?? "").Trim(); if (t.Length == 0) return true;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) || v < 0 || v > CostMax || decimal.Round(v, 2) != v) return false;
        c = v; return true;
    }

    // ================================================================ CSV (RFC 4180)
    /// <summary>Parses CSV text: quoted fields may contain commas, quotes ("") and line breaks; CRLF or LF; a leading BOM is ignored. Throws <see cref="FormatException"/> on an unterminated quote.</summary>
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var cell = new StringBuilder();
        var i = 0; if (text.Length > 0 && text[0] == '﻿') i = 1;
        bool quoted = false, any = false;
        for (; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; any = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); any = true; break;
                case '\r': break;
                case '\n': row.Add(cell.ToString()); cell.Clear(); if (any || row.Count > 1 || row[0].Length > 0) rows.Add([.. row]); row.Clear(); any = false; break;
                default: cell.Append(ch); any = true; break;
            }
        }
        if (quoted) throw new FormatException("A quoted value is not closed.");
        if (any || cell.Length > 0) { row.Add(cell.ToString()); rows.Add([.. row]); }
        return rows;
    }
}
