using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Forms;

namespace AnnabaDigital.Application.Helpdesk;

/// <summary>A ticket's form answers as people read them. Shared by the ticket page and the approver's inbox, which shows the key answers beside each decision.</summary>
public static class TicketAnswers
{
    /// <summary>The answers labelled with the questions of the form version the ticket was made with; places and people are shown by name.</summary>
    public static async Task<IReadOnlyList<TicketAnswer>> BuildAsync(IAppDb db, Guid serviceId, int formVersion, string answersJson, CancellationToken ct = default)
    {
        if (formVersion == 0) return [];
        var json = await db.ServiceFormVersions.AsNoTracking().Where(x => x.ServiceId == serviceId && x.Number == formVersion).Select(x => x.FieldsJson).FirstOrDefaultAsync(ct);
        if (json is null) return [];
        var fields = FormEngine.Deserialize(json);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(answersJson) ?? [];

        var locIds = fields.Where(f => f.Type == FieldTypes.Location && values.ContainsKey(f.Key)).Select(f => Guid.TryParse(values[f.Key].GetString(), out var g) ? g : Guid.Empty).ToList();
        var userIds = fields.Where(f => f.Type == FieldTypes.User && values.ContainsKey(f.Key)).Select(f => Guid.TryParse(values[f.Key].GetString(), out var g) ? g : Guid.Empty).ToList();
        var locs = locIds.Count == 0 ? [] : await db.Locations.AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var users = userIds.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var fileIds = fields.Where(f => f.Type == FieldTypes.File && values.ContainsKey(f.Key)).SelectMany(f => values[f.Key].EnumerateArray().Select(x => Guid.TryParse(x.GetString(), out var g) ? g : Guid.Empty)).ToList();
        var files = fileIds.Count == 0 ? [] : await db.Attachments.AsNoTracking().Where(a => fileIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FileName, ct);

        var list = new List<TicketAnswer>();
        foreach (var f in fields)
        {
            if (!values.TryGetValue(f.Key, out var val)) continue;
            string? display = null;
            switch (f.Type)
            {
                case FieldTypes.Select: display = f.Options?.FirstOrDefault(o => o.Value == val.GetString())?.Label ?? val.GetString(); break;
                case FieldTypes.MultiSelect: display = string.Join(", ", val.EnumerateArray().Select(x => f.Options?.FirstOrDefault(o => o.Value == x.GetString())?.Label ?? x.GetString())); break;
                case FieldTypes.Location: display = Guid.TryParse(val.GetString(), out var lg) && locs.TryGetValue(lg, out var ln) ? ln : null; break;
                case FieldTypes.User: display = Guid.TryParse(val.GetString(), out var ug) && users.TryGetValue(ug, out var un) ? un : null; break;
                case FieldTypes.File: display = string.Join(", ", val.EnumerateArray().Select(x => Guid.TryParse(x.GetString(), out var fg) && files.TryGetValue(fg, out var fn) ? fn : "—")); break;
            }
            list.Add(new TicketAnswer(f.Key, f.Label, f.Type, val.Clone(), display));
        }
        return list;
    }
}
