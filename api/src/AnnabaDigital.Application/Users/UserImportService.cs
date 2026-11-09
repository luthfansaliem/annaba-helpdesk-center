using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Users;

/// <summary>
/// USR-006. Bulk-invite people from a CSV (name, email, optional phone, department, jobTitle). Every row goes through the same rules as a single
/// create — nobody gets a password, each person receives an invitation — and one role set (chosen by the administrator) applies to the whole file.
/// A dry run validates and reports per row without writing anything; a real run keeps going past bad rows and reports them.
/// </summary>
public sealed class UserImportService(UserAdminService users, InvitationAdminService invitations, IAppDb db, IClock clock, SettingsProvider settings, AuditWriter audit)
{
    public const int MaxRows = 200;
    public const int MaxChars = 200_000;

    public async Task<ImportUsersResult> ImportAsync(ImportUsersRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var csv = req.Csv ?? "";
        if (csv.Length > MaxChars) throw DomainException.Validation("The file is too large.", new Dictionary<string, string> { ["csv"] = $"Use at most {MaxChars / 1000} KB." });
        var table = ParseCsv(csv);
        if (table.Count == 0) throw DomainException.Validation("The file is empty.", new Dictionary<string, string> { ["csv"] = "Add a header row and at least one person." });
        var header = table[0].Select(h => new string(h.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        int cName = Col("name", "fullname"), cEmail = Col("email", "emailaddress"), cPhone = Col("phone"), cDept = Col("department"), cTitle = Col("jobtitle", "title");
        if (cName < 0 || cEmail < 0) throw DomainException.Validation("Missing columns.", new Dictionary<string, string> { ["csv"] = "The first row must contain at least the columns name and email." });
        var data = table.Skip(1).Select((r, i) => (Line: i + 2, Row: r)).Where(x => x.Row.Any(c => c.Trim().Length > 0)).ToList();
        if (data.Count == 0) throw DomainException.Validation("No people found.", new Dictionary<string, string> { ["csv"] = "Add at least one row under the header." });
        if (data.Count > MaxRows) throw DomainException.Validation("Too many rows.", new Dictionary<string, string> { ["csv"] = $"Import at most {MaxRows} people at a time." });

        string? Cell(List<string> r, int c) => c >= 0 && c < r.Count && r[c].Trim().Length > 0 ? r[c].Trim() : null;
        var s = await settings.GetAsync(ct);
        if (!req.DryRun) Guards.RequireRecentReauthForCritical(actor, s, clock); // assigns roles ⇒ critical (checked once, up front)
        var roleIds = req.RoleIds ?? [];
        await invitations.ResolveGrantableRolesAsync(roleIds, actor, "user.import", client, ct); // anti-escalation once, up front

        var emails = data.Select(x => Cell(x.Row, cEmail)?.Trim().ToLowerInvariant()).Where(e => e != null).Select(e => e!).Distinct().ToList();
        var existing = (await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => emails.Contains(u.Email)).Select(u => u.Email).ToListAsync(ct)).ToHashSet();
        var pending = (await db.Invitations.AsNoTracking().Where(i => emails.Contains(i.Email) && i.Status == InvitationStatus.Pending && i.ExpiresAt > clock.UtcNow).Select(i => i.Email).ToListAsync(ct)).ToHashSet();

        var seen = new HashSet<string>(); var rows = new List<ImportRowResult>(); int created = 0;
        foreach (var (line, r) in data)
        {
            var body = new CreateUserRequest(Cell(r, cName), Cell(r, cEmail), Cell(r, cPhone), Cell(r, cDept), Cell(r, cTitle), roleIds, null);
            var fields = UserAdminService.ValidateNewUser(body, out _, out var addr);
            if (fields.Count > 0) { rows.Add(new(line, addr, "error", string.Join(" ", fields.Values))); continue; }
            if (!seen.Add(addr)) { rows.Add(new(line, addr, "error", "Duplicate of an earlier row in this file.")); continue; }
            if (existing.Contains(addr)) { rows.Add(new(line, addr, "error", "A user with this email already exists.")); continue; }
            if (pending.Contains(addr)) { rows.Add(new(line, addr, "error", "A pending invitation already exists for this email.")); continue; }
            if (req.DryRun) { rows.Add(new(line, addr, "ok", null)); continue; }
            try { await users.CreateAsync(body, actor, client, ct); created++; rows.Add(new(line, addr, "ok", null)); }
            catch (DomainException e) when (e.Code != ErrorCodes.ReauthRequired) { rows.Add(new(line, addr, "error", e.Message)); }
        }
        var failed = rows.Count(x => x.Status == "error");
        if (!req.DryRun)
        {
            audit.Record("user.import", "user", null, failed == 0 ? Outcome.Success : Outcome.Failure, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["rows"] = rows.Count.ToString(), ["created"] = created.ToString(), ["failed"] = failed.ToString() });
            await db.SaveChangesAsync(ct);
        }
        return new ImportUsersResult(req.DryRun, rows.Count, created, failed, [.. rows]);
    }

    /// <summary>RFC 4180: quoted cells may contain commas, quotes ("") and line breaks. A leading byte-order mark is ignored.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder(); var quoted = false;
        text = text.TrimStart('\uFEFF');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                else cell.Append(c);
            }
            else if (c == '"' && cell.Length == 0) quoted = true;
            else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = [];
            }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }
}
