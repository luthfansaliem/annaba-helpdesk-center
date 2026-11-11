using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AnnabaDigital.Application.Common;

/// <summary>A report as a table. Cells are strings, numbers (int, long, double, decimal) or null. <see cref="Truncated"/>: more rows existed than the limit.</summary>
public sealed record ReportTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<object?[]> Rows, bool Truncated = false);

/// <summary>Writes a <see cref="ReportTable"/> as CSV (UTF-8 with BOM, so Excel opens it with the right characters) or as a one-sheet XLSX workbook
/// (Office Open XML written by hand: no third-party library). Text that a spreadsheet would run as a formula is prefixed with an apostrophe.</summary>
public static class Tabular
{
    public static (byte[] Content, string ContentType, string Extension) Write(ReportTable t, string format)
        => format == "xlsx" ? (Xlsx(t), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx") : (Csv(t), "text/csv; charset=utf-8", "csv");

    /// <summary>Formula injection guard (CWE-1236): text starting with = + - @ tab or carriage return is kept as text.</summary>
    public static string Safe(string s) => s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + s : s;

    private static string Cell(object? v) => v switch
    {
        null => "",
        string s => Safe(s),
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        float f => f.ToString("0.##", CultureInfo.InvariantCulture),
        bool b => b ? "yes" : "no",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Safe(v.ToString() ?? ""),
    };

    public static byte[] Csv(ReportTable t)
    {
        var sb = new StringBuilder();
        static string Quote(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        sb.Append(string.Join(",", t.Headers.Select(h => Quote(Safe(h))))).Append("\r\n");
        foreach (var r in t.Rows) sb.Append(string.Join(",", r.Select(c => Quote(Cell(c))))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Col(int i) { var s = ""; for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s; return s; }
    private static string Xml(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c < 0x20 && c is not ('\t' or '\n' or '\r')) continue;   // not allowed in XML 1.0
            sb.Append(c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => c.ToString() });
        }
        return sb.ToString();
    }

    public static byte[] Xlsx(ReportTable t)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string path, string xml)
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Put("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Put("_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            var sheet = new string(Xml(t.Title).Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).Take(31).ToArray()).Trim();
            Put("xl/workbook.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="{ns}" xmlns:r="{rel}"><sheets><sheet name="{(sheet.Length == 0 ? "Report" : sheet)}" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Put("xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
            Put("xl/styles.xml", $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="{ns}"><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs></styleSheet>""");

            var sb = new StringBuilder();
            sb.Append($"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="{ns}"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetData>""");
            void Row(int n, IEnumerable<object?> cells, bool head)
            {
                sb.Append($"<row r=\"{n}\">");
                var i = 0;
                foreach (var c in cells)
                {
                    var r = Col(i++) + n; var s = head ? " s=\"1\"" : "";
                    switch (c)
                    {
                        case null: break;
                        case int or long or decimal or double or float:
                            sb.Append($"<c r=\"{r}\"{s}><v>{Convert.ToString(c, CultureInfo.InvariantCulture)}</v></c>"); break;
                        default:
                            sb.Append($"<c r=\"{r}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Xml(c is bool b ? (b ? "yes" : "no") : Safe(Convert.ToString(c, CultureInfo.InvariantCulture) ?? ""))}</t></is></c>"); break;
                    }
                }
                sb.Append("</row>");
            }
            Row(1, t.Headers, true);
            var line = 2;
            foreach (var r in t.Rows) Row(line++, r, false);
            sb.Append("</sheetData></worksheet>");
            Put("xl/worksheets/sheet1.xml", sb.ToString());
        }
        return ms.ToArray();
    }
}
