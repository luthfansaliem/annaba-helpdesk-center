using System.Text;
using System.Text.Json;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Forms;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>TKT-005: which files are accepted is decided by pure rules, so every allowed and refused case is checked without a database or a disk.</summary>
public class FileRulesTests
{
    private static byte[] Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static byte[] Jpeg => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10];
    private static byte[] Pdf => Encoding.ASCII.GetBytes("%PDF-1.7\n...");
    private static byte[] Zip => [0x50, 0x4B, 0x03, 0x04, 0x14, 0];
    private static byte[] Ole => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0];
    private static byte[] Exe => Encoding.ASCII.GetBytes("MZ\u0090\u0000\u0003");
    private static byte[] Webp => [.. Encoding.ASCII.GetBytes("RIFF"), 1, 2, 3, 4, .. Encoding.ASCII.GetBytes("WEBP")];

    [Theory]
    [InlineData("photo.png", "image/png", true)] [InlineData("PHOTO.JPG", "image/jpeg", true)] [InlineData("a.jpeg", "image/jpeg", true)] [InlineData("a.gif", "image/gif", true)] [InlineData("a.webp", "image/webp", true)]
    [InlineData("report.pdf", "application/pdf", false)] [InlineData("sheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false)]
    [InlineData("letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)] [InlineData("old.doc", "application/msword", false)]
    [InlineData("notes.txt", "text/plain; charset=utf-8", false)] [InlineData("data.csv", "text/csv; charset=utf-8", false)]
    public void Allowed_files_are_served_under_our_content_type_and_only_images_inline(string name, string type, bool inline)
    {
        var bytes = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".png" => Png, ".jpg" or ".jpeg" => Jpeg, ".gif" => Encoding.ASCII.GetBytes("GIF89a.."), ".webp" => Webp, ".pdf" => Pdf, ".docx" or ".xlsx" or ".pptx" => Zip,
            ".doc" or ".xls" or ".ppt" => Ole, _ => Encoding.UTF8.GetBytes("plain, text\nline 2\t"),
        };
        var v = FileRules.Check(name, bytes.Length, bytes);
        Assert.True(v.Ok, v.Error); Assert.Equal(type, v.ContentType); Assert.Equal(inline, v.Inline);
    }

    [Theory]
    [InlineData("setup.exe")] [InlineData("run.bat")] [InlineData("page.html")] [InlineData("logo.svg")] [InlineData("macro.docm")] [InlineData("archive.zip")] [InlineData("noextension")] [InlineData(".png")] [InlineData("evil.png.exe")]
    public void Types_that_are_not_on_the_list_are_refused_whatever_they_contain(string name)
        => Assert.False(FileRules.Check(name, 100, Png).Ok);

    [Fact]
    public void A_program_renamed_to_an_allowed_type_is_refused_because_its_content_does_not_match()
    {
        foreach (var name in new[] { "photo.png", "photo.jpg", "photo.gif", "photo.webp", "doc.pdf", "doc.docx", "doc.xls" })
        {
            var v = FileRules.Check(name, Exe.Length, Exe);
            Assert.False(v.Ok, name); Assert.Contains("does not match", v.Error);
        }
        Assert.False(FileRules.Check("note.txt", 10, [0x4D, 0x5A, 0, 0, 0, 1]).Ok);                // a NUL byte is not plain text
        Assert.False(FileRules.Check("photo.png", Jpeg.Length, Jpeg).Ok);                             // a JPEG named .png
    }

    [Fact]
    public void Empty_and_oversized_files_are_refused_and_the_limit_is_inclusive()
    {
        Assert.False(FileRules.Check("a.png", 0, Png).Ok);
        Assert.True(FileRules.Check("a.png", FileRules.MaxBytes, Png).Ok);
        var over = FileRules.Check("a.png", FileRules.MaxBytes + 1, Png);
        Assert.False(over.Ok); Assert.Contains("larger", over.Error);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Desktop\photo.png", "photo.png")] [InlineData("../../etc/passwd.txt", "passwd.txt")] [InlineData("a/b/c.pdf", "c.pdf")] [InlineData("we<ird>:na|me?.png", "weirdname.png")]
    [InlineData("  spaced name .png ", "spaced name .png")] [InlineData("", "file")] [InlineData(null, "file")] [InlineData("...", "file")] [InlineData("tab\there.txt", "tabhere.txt")]
    public void Names_are_stripped_of_folders_and_unsafe_characters(string? raw, string expected) => Assert.Equal(expected, FileRules.SafeName(raw));

    [Fact]
    public void A_very_long_name_is_shortened_but_keeps_its_extension()
    {
        var name = FileRules.SafeName(new string('x', 400) + ".pdf");
        Assert.Equal(FileRules.MaxFileNameLength, name.Length); Assert.EndsWith(".pdf", name);
    }

    // ---- the file question
    private static Dictionary<string, JsonElement> A(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    private static FormField File(bool required = false, decimal? max = null) => new("photos", FieldTypes.File, "Photos", null, required, null, null, max);

    [Fact]
    public void A_file_question_takes_a_limited_list_of_upload_ids()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var ok = FormEngine.Evaluate([File(max: 2)], A($$"""{ "photos": ["{{a}}", "{{b}}", "{{a}}"] }"""));
        Assert.True(ok.IsValid); Assert.Equal([a.ToString("D"), b.ToString("D")], (string[])ok.Values["photos"]!);               // duplicates collapse
        Assert.False(FormEngine.Evaluate([File(max: 1)], A($$"""{ "photos": ["{{a}}", "{{b}}"] }""")).IsValid);                 // over the question's limit
        Assert.False(FormEngine.Evaluate([File()], A($$"""{ "photos": [{{string.Join(",", Enumerable.Range(0, 6).Select(_ => $"\"{Guid.NewGuid()}\""))}}] }""")).IsValid);   // default limit is 5
        Assert.False(FormEngine.Evaluate([File()], A("""{ "photos": ["not-a-guid"] }""")).IsValid);
        Assert.False(FormEngine.Evaluate([File()], A("""{ "photos": "x" }""")).IsValid);
        Assert.False(FormEngine.Evaluate([File(required: true)], A("""{ "photos": [] }""")).IsValid);                            // required and empty
        Assert.True(FormEngine.Evaluate([File()], A("{}")).IsValid);
    }

    [Fact]
    public void A_file_question_definition_allows_only_a_sensible_file_count()
    {
        Assert.Empty(FormEngine.ValidateDefinition([File(max: 3)]));
        Assert.Empty(FormEngine.ValidateDefinition([File()]));
        Assert.Contains("fields[0].max", FormEngine.ValidateDefinition([File(max: 0)]).Keys);
        Assert.Contains("fields[0].max", FormEngine.ValidateDefinition([File(max: 11)]).Keys);
        Assert.Contains("fields[0].max", FormEngine.ValidateDefinition([File(max: 2.5m)]).Keys);
        Assert.Contains("fields[0].min", FormEngine.ValidateDefinition([new FormField("photos", FieldTypes.File, "Photos", null, false, null, 1, null)]).Keys);
        Assert.Contains("fields[0].min", FormEngine.ValidateDefinition([new FormField("n", FieldTypes.Text, "N", null, false, null, null, 3)]).Keys);   // limits are still refused on other types
    }
}
