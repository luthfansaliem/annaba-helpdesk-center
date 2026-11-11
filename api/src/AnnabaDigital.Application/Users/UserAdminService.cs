using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

/// <summary>
/// USR-001…005. Every mutation: (1) coarse permission at the endpoint, (2) hierarchy rule — you cannot act on someone with permissions you lack,
/// (3) anti-escalation on roles, (4) last-Super-Admin / self-lockout invariants under row locks, (5) audit in the same transaction.
/// </summary>
public sealed class UserAdminService(
    IAppDb db, IClock clock, SettingsProvider settings, AuthorizationService authz, SessionService sessions, AuditWriter audit,
    IEmailQueue email, IAppLinks links, ITokenService tokens, InvitationAdminService invitations)
{
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    { ["name"] = "name", ["email"] = "email", ["status"] = "status", ["lastLogin"] = "lastLogin", ["createdAt"] = "createdAt" };

    // ============================================================ reads
    public async Task<PagedResult<UserListItem>> ListAsync(UserQuery q, SessionPrincipal actor, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100); var now = clock.UtcNow;
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim().ToLowerInvariant(); // Contains() is parameterised and wildcard-safe in EF (no manual LIKE escaping)
            query = query.Where(u => u.Name.ToLower().Contains(term) || u.Email.Contains(term) || (u.Profile != null && u.Profile.Department != null && u.Profile.Department.ToLower().Contains(term)));
        }
        var st = q.Status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(st) && st != "all")
        {
            query = st switch
            {
                "locked" => query.Where(u => u.LockedUntil != null && u.LockedUntil > now),
                "pending" => query.Where(u => u.Status == UserStatus.Pending), "active" => query.Where(u => u.Status == UserStatus.Active),
                "suspended" => query.Where(u => u.Status == UserStatus.Suspended), "deactivated" => query.Where(u => u.Status == UserStatus.Deactivated),
                _ => throw DomainException.Validation("Unknown status filter.", new Dictionary<string, string> { ["status"] = "Use active, pending, suspended, deactivated or locked." }),
            };
        }
        if (!string.IsNullOrWhiteSpace(q.Role)) query = query.Where(u => u.UserRoles.Any(r => r.Role!.Key == q.Role));
        if (q.Verified is { } v) query = query.Where(u => (u.EmailVerifiedAt != null) == v);

        var desc = string.Equals(q.Dir, "desc", StringComparison.OrdinalIgnoreCase);
        var sort = q.Sort is not null && SortColumns.TryGetValue(q.Sort, out var col) ? col : "createdAt";
        query = (sort, desc) switch
        {
            ("name", false) => query.OrderBy(u => u.Name).ThenBy(u => u.Id), ("name", true) => query.OrderByDescending(u => u.Name).ThenBy(u => u.Id),
            ("email", false) => query.OrderBy(u => u.Email), ("email", true) => query.OrderByDescending(u => u.Email),
            ("status", false) => query.OrderBy(u => u.Status).ThenBy(u => u.Name), ("status", true) => query.OrderByDescending(u => u.Status).ThenBy(u => u.Name),
            ("lastLogin", false) => query.OrderBy(u => u.LastLoginAt).ThenBy(u => u.Id), ("lastLogin", true) => query.OrderByDescending(u => u.LastLoginAt).ThenBy(u => u.Id),
            (_, false) => query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id), _ => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id),
        };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * size).Take(size).Select(u => new
        {
            u.Id, u.Name, u.Email, u.Status, u.LockedUntil, Verified = u.EmailVerifiedAt != null, u.LastLoginAt, u.CreatedAt,
            Mfa = u.MfaFactors.Any(f => f.ConfirmedAt != null),
            AvatarAt = db.Avatars.Where(a => a.UserId == u.Id).Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefault(),
            Roles = u.UserRoles.Select(r => new { r.Role!.Id, r.Role.Key, r.Role.Name }).ToList(),
        }).ToListAsync(ct);
        // Hierarchy rule, so the screen only offers what the server will accept: a row is "manageable" unless that person holds permissions the actor does not.
        var mine = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var roleIds = rows.SelectMany(r => r.Roles.Select(x => x.Id)).Distinct().ToList();
        var grants = (await db.RolePermissions.AsNoTracking().Where(rp => roleIds.Contains(rp.RoleId) && db.Roles.Any(r => r.Id == rp.RoleId && r.ArchivedAt == null)).Select(rp => new { rp.RoleId, rp.PermissionKey }).ToListAsync(ct))
            .GroupBy(g => g.RoleId).ToDictionary(g => g.Key, g => g.Select(x => x.PermissionKey).ToList());
        bool Manageable(Guid id, IEnumerable<Guid> roles) => Guards.CanManage(actor.UserId, mine, id, roles.SelectMany(r => grants.GetValueOrDefault(r) ?? []));
        return new PagedResult<UserListItem>(rows.Select(r => new UserListItem(r.Id, r.Name, r.Email, r.Status.ToString().ToLowerInvariant(), r.LockedUntil > now, r.Verified,
            r.Roles.OrderBy(x => x.Name).Select(x => new RoleRef(x.Id, x.Key, x.Name)).ToArray(), r.LastLoginAt, r.CreatedAt, r.Mfa, r.AvatarAt?.ToUnixTimeMilliseconds(), Manageable(r.Id, r.Roles.Select(x => x.Id)))).ToList(), page, size, total);
    }

    public async Task<UserDetail> GetAsync(Guid id, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).Include(x => x.UserRoles).ThenInclude(r => r.Role).Include(x => x.MfaFactors).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw DomainException.NotFound("User");
        var perms = await authz.GetEffectivePermissionsAsync(id, ct);
        var live = await db.Sessions.CountAsync(s => s.UserId == id && s.RevokedAt == null && s.IdleExpiresAt > now && s.AbsoluteExpiresAt > now, ct);
        var avatarAt = await db.Avatars.AsNoTracking().Where(a => a.UserId == id).Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefaultAsync(ct);
        return new UserDetail(u.Id, u.Name, u.Email, u.Status.ToString().ToLowerInvariant(), u.LockedUntil > now, u.EmailVerifiedAt != null,
            u.UserRoles.Select(r => new RoleRef(r.Role!.Id, r.Role.Key, r.Role.Name)).OrderBy(r => r.Name).ToArray(), u.LastLoginAt, u.CreatedAt,
            u.MfaFactors.Any(f => f.ConfirmedAt != null), u.Profile?.Phone, u.Profile?.Department, u.Profile?.JobTitle, [.. perms.Order()], live, u.Version, avatarAt?.ToUnixTimeMilliseconds(),
            u.Profile?.ManagerId, u.Profile?.ManagerId is { } mid ? await db.Users.AsNoTracking().Where(x => x.Id == mid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null,
            u.Profile?.EmployeeId, u.Profile?.SiteId is { } sid ? await db.Locations.AsNoTracking().Where(l => l.Id == sid).Select(l => l.Name).FirstOrDefaultAsync(ct) : null, u.Profile?.ExternalId);
    }

    public async Task<UserStats> StatsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // One round trip for every head-count (status, locked, unverified and the 12 monthly growth points) instead of one query each.
        var e = Enumerable.Range(0, 12).Select(i => now.AddDays(-30 * (11 - i))).ToArray();
        DateTimeOffset e0 = e[0], e1 = e[1], e2 = e[2], e3 = e[3], e4 = e[4], e5 = e[5], e6 = e[6], e7 = e[7], e8 = e[8], e9 = e[9], e10 = e[10], e11 = e[11];
        var c = await db.Users.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Total = g.Count(),
            Active = g.Count(u => u.Status == UserStatus.Active), Pending = g.Count(u => u.Status == UserStatus.Pending),
            Suspended = g.Count(u => u.Status == UserStatus.Suspended), Deactivated = g.Count(u => u.Status == UserStatus.Deactivated),
            Locked = g.Count(u => u.LockedUntil != null && u.LockedUntil > now), Unverified = g.Count(u => u.EmailVerifiedAt == null),
            G0 = g.Count(u => u.CreatedAt <= e0), G1 = g.Count(u => u.CreatedAt <= e1), G2 = g.Count(u => u.CreatedAt <= e2), G3 = g.Count(u => u.CreatedAt <= e3),
            G4 = g.Count(u => u.CreatedAt <= e4), G5 = g.Count(u => u.CreatedAt <= e5), G6 = g.Count(u => u.CreatedAt <= e6), G7 = g.Count(u => u.CreatedAt <= e7),
            G8 = g.Count(u => u.CreatedAt <= e8), G9 = g.Count(u => u.CreatedAt <= e9), G10 = g.Count(u => u.CreatedAt <= e10), G11 = g.Count(u => u.CreatedAt <= e11),
        }).FirstOrDefaultAsync(ct);
        int[] growth = c is null ? new int[12] : [c.G0, c.G1, c.G2, c.G3, c.G4, c.G5, c.G6, c.G7, c.G8, c.G9, c.G10, c.G11];
        var byRole = await db.UserRoles.AsNoTracking().Where(ur => ur.Role!.ArchivedAt == null).GroupBy(ur => ur.Role!.Name).Select(g => new StatusCount(g.Key, g.Count())).ToListAsync(ct);
        var recent = await ListAsync(new UserQuery(null, null, null, null, "createdAt", "desc", 1, 5), actor, ct);
        int active = c?.Active ?? 0, pending = c?.Pending ?? 0, susp = c?.Suspended ?? 0, deact = c?.Deactivated ?? 0;
        return new UserStats(c?.Total ?? 0, active, pending, susp, deact, c?.Locked ?? 0, c?.Unverified ?? 0, growth,
            [new("active", active), new("pending", pending), new("suspended", susp), new("deactivated", deact)], [.. byRole.OrderByDescending(r => r.Count)], [.. recent.Data]);
    }

    public async Task<IReadOnlyList<SessionInfo>> SessionsAsync(Guid userId, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("User");
        var now = clock.UtcNow;
        return await db.Sessions.AsNoTracking().Where(s => s.UserId == userId && s.RevokedAt == null && s.IdleExpiresAt > now && s.AbsoluteExpiresAt > now).OrderByDescending(s => s.LastActiveAt)
            .Select(s => new SessionInfo(s.Id, s.DeviceLabel, s.BrowserLabel, s.Ip, s.CreatedAt, s.LastActiveAt, s.IdleExpiresAt < s.AbsoluteExpiresAt ? s.IdleExpiresAt : s.AbsoluteExpiresAt)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityItem>> ActivityAsync(Guid userId, CancellationToken ct = default)
    {
        var idText = userId.ToString();
        var rows = await db.AuditEvents.AsNoTracking().Where(a => a.ActorId == userId || (a.TargetType == "user" && a.TargetId == idText)).OrderByDescending(a => a.Ts).Take(100).ToListAsync(ct);
        return rows.Select(a => new ActivityItem(a.Id, a.Ts, a.Action, a.ActorLabel, $"{a.TargetType}:{a.TargetId}", a.Outcome.ToString().ToLowerInvariant())).ToList();
    }

    // ============================================================ helpers
    private async Task<HashSet<string>> PermsOfRolesAsync(IEnumerable<Guid> roleIds, CancellationToken ct)
    {
        var ids = roleIds.ToList();
        var keys = await db.RolePermissions.AsNoTracking().Where(rp => ids.Contains(rp.RoleId) && db.Roles.Any(r => r.Id == rp.RoleId && r.ArchivedAt == null)).Select(rp => rp.PermissionKey).Distinct().ToListAsync(ct);
        return [.. keys];
    }

    private async Task DenyAsync(string action, Guid? targetId, string reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        audit.Record(action, "user", targetId?.ToString(), Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = reason });
        await db.SaveChangesAsync(ct);
        throw DomainException.DeniedBecause(reason);
    }

    /// <summary>Hierarchy rule (see class summary). Throws Denied (and audits it) when the target outranks the actor.</summary>
    private async Task<User> LoadManageableAsync(Guid id, SessionPrincipal actor, string action, ClientInfo client, CancellationToken ct)
    {
        var target = await db.Users.Include(u => u.Profile).Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound("User");
        var mine = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var theirs = await PermsOfRolesAsync(target.UserRoles.Select(r => r.RoleId), ct);
        if (!Guards.CanManage(actor.UserId, mine, id, theirs))
            await DenyAsync(action, id, "target has permissions you do not hold", actor, client, ct);
        return target;
    }

    private async Task<T> RunAsync<T>(string action, string targetType, string? targetId, SessionPrincipal actor, ClientInfo client, Func<Task<T>> work, CancellationToken ct)
    {
        try { return await db.InTransactionAsync(work, ct); }
        catch (DomainException e) when (e.Code is ErrorCodes.Invariant or ErrorCodes.Conflict)
        {
            db.ResetTracking(); // the rolled-back transaction must not leak tracked changes into the audit save below
            audit.Record(action, targetType, targetId, Outcome.Failure, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = e.Code, ["detail"] = e.Message });
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    /// <summary>Serialises concurrent changes to Super Admins: locks every active Super Admin row until the transaction ends.</summary>
    private async Task<int> OtherActiveSuperAdminsAsync(Guid excludeId, CancellationToken ct)
    {
        await db.ExecuteSqlAsync($"""
            SELECT u.id FROM users u JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id
            WHERE r.key = {SystemRoles.SuperAdmin} AND u.status = 'Active' FOR UPDATE OF u
            """, ct);
        return await db.UserRoles.CountAsync(ur => ur.Role!.Key == SystemRoles.SuperAdmin && ur.UserId != excludeId && ur.User!.Status == UserStatus.Active, ct);
    }

    /// <summary>True when this person is an active Super Admin and nobody else is (closing or removing them would leave the organisation without one).</summary>
    public async Task<bool> IsLastActiveSuperAdminAsync(Guid id, CancellationToken ct = default)
        => await db.UserRoles.AnyAsync(ur => ur.UserId == id && ur.Role!.Key == SystemRoles.SuperAdmin && ur.User!.Status == UserStatus.Active, ct) && await OtherActiveSuperAdminsAsync(id, ct) == 0;

    /// <summary>Hierarchy rule for callers outside this service (throws Denied, audited, when the target outranks the actor).</summary>
    public async Task EnsureManageableAsync(Guid id, SessionPrincipal actor, string action, ClientInfo client, CancellationToken ct = default) => await LoadManageableAsync(id, actor, action, client, ct);

    private static string Show(IEnumerable<Role> roles) => string.Join(", ", roles.Select(r => r.Name).Order());

    // ============================================================ create
    /// <summary>Field rules for a new user, shared by the single create and the CSV import.</summary>
    public static Dictionary<string, string> ValidateNewUser(CreateUserRequest req, out string name, out string addr)
    {
        var fields = new Dictionary<string, string>();
        name = (req.Name ?? "").Trim(); addr = AuthService.NormalizeEmail(req.Email);
        if (name.Length < 2 || name.Length > 200) fields["name"] = "Enter the full name.";
        if (addr.Length == 0 || addr.Length > 254 || !System.Text.RegularExpressions.Regex.IsMatch(addr, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) fields["email"] = "Enter a valid email address.";
        if (req.Mode is not null && !req.Mode.Equals("invite", StringComparison.OrdinalIgnoreCase)) fields["mode"] = "Users are created by invitation; administrators never set passwords.";
        foreach (var (k, v, max) in new[] { ("phone", req.Phone, 40), ("department", req.Department, 100), ("jobTitle", req.JobTitle, 100) }) if (v is { } t && t.Length > max) fields[k] = $"Use at most {max} characters.";
        return fields;
    }

    /// <summary>
    /// USR-003. An admin never sets or sees a password: the user is created as a *pending placeholder* with the intended roles and an invitation is
    /// emailed; the account becomes Active only when the person accepts (sets their own password). The placeholder grants nothing while pending.
    /// </summary>
    public async Task<UserDetail> CreateAsync(CreateUserRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        Guards.RequireRecentReauthForCritical(actor, s, clock); // assigns roles ⇒ critical
        var fields = ValidateNewUser(req, out var name, out var addr);
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);

        var roles = await invitations.ResolveGrantableRolesAsync(req.RoleIds ?? [], actor, "user.create", client, ct);
        string raw; User user;
        (user, raw) = await RunAsync("user.create", "user", null, actor, client, async () =>
        {
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == addr, ct)) throw DomainException.Conflict("A user with this email already exists."); // e-mail is unique across all organisations
            if (await db.Invitations.AnyAsync(i => i.Email == addr && i.Status == InvitationStatus.Pending && i.ExpiresAt > clock.UtcNow, ct)) throw DomainException.Conflict("A pending invitation already exists for this email.");
            foreach (var o in await db.Invitations.Where(i => i.Email == addr && i.Status == InvitationStatus.Pending).ToListAsync(ct)) { o.Status = InvitationStatus.Revoked; o.RevokedAt = clock.UtcNow; }
            var now = clock.UtcNow;
            var u = new User { Email = addr, Name = name, Status = UserStatus.Pending, CreatedAt = now, UpdatedAt = now };
            u.Profile = new UserProfile { Phone = req.Phone?.Trim(), Department = req.Department?.Trim(), JobTitle = req.JobTitle?.Trim() };
            foreach (var r in roles) u.UserRoles.Add(new UserRole { RoleId = r.Id, AssignedBy = actor.UserId, AssignedAt = now });
            db.Users.Add(u);
            var token = tokens.NewToken();
            var inv = new Invitation { Email = addr, TokenHash = tokens.Hash(token), InvitedBy = actor.UserId, Status = InvitationStatus.Pending, CreatedAt = now, ExpiresAt = now.AddDays(s.InvitationExpiryDays) };
            foreach (var r in roles) inv.Roles.Add(new InvitationRole { RoleId = r.Id });
            db.Invitations.Add(inv);
            audit.Record("user.create", "user", u.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["email"] = addr, ["mode"] = "invite", ["roles"] = string.Join(",", roles.Select(r => r.Key)) });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { throw DomainException.Conflict("A user with this email already exists."); } // lost a race with another create
            return (u, token);
        }, ct);
        email.Enqueue(EmailTemplates.Invitation(addr, actor.Name, $"{links.Origin.TrimEnd('/')}/invite?token={Uri.EscapeDataString(raw)}", s.InvitationExpiryDays));
        return await GetAsync(user.Id, ct);
    }

    // ============================================================ update
    public async Task<UserDetail> UpdateAsync(Guid id, UpdateUserRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var target = await LoadManageableAsync(id, actor, "user.update", client, ct);
        var fields = new Dictionary<string, string>();
        if (req.Name is not null && (req.Name.Trim().Length < 2 || req.Name.Trim().Length > 200)) fields["name"] = "Enter the full name.";
        foreach (var (k, v, max) in new[] { ("phone", req.Phone, 40), ("department", req.Department, 100), ("jobTitle", req.JobTitle, 100) }) if (v is { } t && t.Length > max) fields[k] = $"Use at most {max} characters.";
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);
        if (req.Version is { } ver && ver != target.Version) throw DomainException.Conflict("This user was changed by someone else. Reload and try again.");

        var diff = new List<AuditDiff>();
        void Set(string field, string? before, string? after, Action<string?> apply) { var a = after?.Trim(); if (after is not null && (a ?? "") != (before ?? "")) { diff.Add(new AuditDiff(field, before ?? "", a ?? "")); apply(a); } }
        target.Profile ??= new UserProfile { UserId = target.Id };
        Set("name", target.Name, req.Name, v => target.Name = v!);
        Set("phone", target.Profile.Phone, req.Phone, v => target.Profile.Phone = v);
        Set("department", target.Profile.Department, req.Department, v => target.Profile.Department = v);
        Set("jobTitle", target.Profile.JobTitle, req.JobTitle, v => target.Profile.JobTitle = v);
        if (diff.Count == 0) return await GetAsync(id, ct);

        target.Version++; target.UpdatedAt = clock.UtcNow;
        audit.Record("user.update", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: diff);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("This user was changed by someone else. Reload and try again."); }
        return await GetAsync(id, ct);
    }

    // ============================================================ manager (APR-002)
    /// <summary>Sets or clears who a person reports to. The manager must be an active person of the organisation, never the person themselves, and the chain must not loop back.</summary>
    public async Task<UserDetail> SetManagerAsync(Guid id, Guid? managerId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var target = await LoadManageableAsync(id, actor, "user.manager", client, ct);
        target.Profile ??= new UserProfile { UserId = target.Id };
        if (managerId is { } m)
        {
            if (m == id) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["managerId"] = "Nobody can be their own manager." });
            if (!await db.Users.AnyAsync(u => u.Id == m && u.Status == UserStatus.Active, ct)) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["managerId"] = "Choose an active person of your organisation." });
            // walking up from the new manager must never reach this person
            var up = m;
            for (var hops = 0; hops < 50 && up != default; hops++)
            {
                var next = await db.Profiles.AsNoTracking().Where(p => p.UserId == up).Select(p => p.ManagerId).FirstOrDefaultAsync(ct);
                if (next is null) break;
                if (next == id) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["managerId"] = "That would make the reporting line loop back to this person." });
                up = next.Value;
            }
        }
        if (target.Profile.ManagerId == managerId) return await GetAsync(id, ct);
        var before = target.Profile.ManagerId;
        target.Profile.ManagerId = managerId; target.Version++; target.UpdatedAt = clock.UtcNow;
        audit.Record("user.manager", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: [new AuditDiff("manager", before?.ToString() ?? "", managerId?.ToString() ?? "")]);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("This user was changed by someone else. Reload and try again."); }
        return await GetAsync(id, ct);
    }

    // ============================================================ status
    public async Task<UserDetail> SetStatusAsync(Guid id, string action, string? reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (to, auditAction) = action switch
        {
            "suspend" => (UserStatus.Suspended, "user.suspend"), "deactivate" => (UserStatus.Deactivated, "user.deactivate"), "activate" => (UserStatus.Active, "user.activate"),
            _ => throw DomainException.Validation("Unknown action."),
        };
        await LoadManageableAsync(id, actor, auditAction, client, ct); // hierarchy check (audited denial)
        await RunAsync(auditAction, "user", id.ToString(), actor, client, async () =>
        {
            var u = await db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).FirstAsync(x => x.Id == id, ct);
            var from = u.Status;
            if (from == to) throw DomainException.Invariant($"The user is already {to.ToString().ToLowerInvariant()}.");
            if (to == UserStatus.Active && from == UserStatus.Pending) throw DomainException.Invariant("A pending user becomes active by accepting the invitation or verifying their email.");
            if (to != UserStatus.Active && id == actor.UserId) throw DomainException.Invariant("You cannot suspend or deactivate your own account.");
            if (to != UserStatus.Active && from == UserStatus.Active && u.UserRoles.Any(r => r.Role!.Key == SystemRoles.SuperAdmin) && await OtherActiveSuperAdminsAsync(id, ct) == 0)
                throw DomainException.Invariant("This is the last active Super Admin. Assign another Super Admin first.");

            u.Status = to; u.UpdatedAt = clock.UtcNow; u.Version++; u.DeactivatedAt = to == UserStatus.Deactivated ? clock.UtcNow : null;
            var revoked = to == UserStatus.Active ? 0 : await sessions.RevokeAllForUserAsync(id, to == UserStatus.Suspended ? RevokeReason.Suspended : RevokeReason.Admin, null, ct);
            // A pending placeholder that is blocked must not be able to activate itself through a still-valid invitation.
            var invRevoked = to == UserStatus.Active ? 0 : await db.Invitations.Where(i => i.Email == u.Email && i.Status == InvitationStatus.Pending)
                .ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InvitationStatus.Revoked).SetProperty(i => i.RevokedAt, clock.UtcNow), ct);
            audit.Record(auditAction, "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string>(reason is null ? [] : [new("reason", reason)]) { ["sessionsRevoked"] = revoked.ToString(), ["invitationsRevoked"] = invRevoked.ToString() },
                [new AuditDiff("status", from.ToString().ToLowerInvariant(), to.ToString().ToLowerInvariant())]);
            if (revoked > 0) audit.Security("session.revoked", Severity.Low, $"{revoked} session(s) revoked because the account was {to.ToString().ToLowerInvariant()}", id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<UserDetail> UnlockAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var u = await LoadManageableAsync(id, actor, "user.unlock", client, ct);
        u.LockedUntil = null; u.FailedLoginCount = 0; u.UpdatedAt = clock.UtcNow; u.Version++;
        audit.Record("user.unlock", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ============================================================ credentials (users.reset_credentials)
    private async Task<User> LoadForCredentialResetAsync(Guid id, string action, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock); // takes over someone's sign-in, so a fresh password check
        if (id == actor.UserId) throw DomainException.Conflict("Use your own profile to change your password or two-step verification.");
        var u = await LoadManageableAsync(id, actor, action, client, ct); // hierarchy: not someone who holds permissions you don't
        if (u.Status != UserStatus.Active) throw DomainException.Conflict("Only active accounts can be changed this way.");
        return u;
    }

    /// <summary>For someone who lost their phone and every recovery code: removes their second factor, signs them out everywhere and tells them. If policy requires two-step verification they enrol again at next sign-in.</summary>
    public async Task ResetMfaAsync(Guid id, string? reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var u = await LoadForCredentialResetAsync(id, "user.mfa.reset", actor, client, ct);
        if (!await db.MfaFactors.AnyAsync(f => f.UserId == id && f.ConfirmedAt != null, ct)) throw DomainException.Conflict("This person has no two-step verification set up.");
        await RunAsync("user.mfa.reset", "user", id.ToString(), actor, client, async () =>
        {
            await db.MfaRecoveryCodes.Where(c => c.UserId == id).ExecuteDeleteAsync(ct);
            await db.MfaFactors.Where(f => f.UserId == id).ExecuteDeleteAsync(ct);
            var revoked = await sessions.RevokeAllForUserAsync(id, "mfa_reset_by_admin", null, ct);
            audit.Record("user.mfa.reset", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["sessionsRevoked"] = revoked.ToString(), ["reason"] = (reason ?? "").Trim() });
            audit.Security("mfa.reset", Severity.High, $"Two-step verification reset for {u.Name} by an administrator", id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        email.Enqueue(EmailTemplates.MfaResetByAdmin(u.Email, u.Name, actor.Name));
    }

    /// <summary>The administrator never sees or sets a password: the person is signed out everywhere, the old password stops working for sign-in, and they get a one-time link to choose a new one.</summary>
    public async Task RequirePasswordResetAsync(Guid id, string? reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var u = await LoadForCredentialResetAsync(id, "user.password.require_reset", actor, client, ct);
        var cred = await db.Credentials.FirstOrDefaultAsync(c => c.UserId == id, ct) ?? throw DomainException.Conflict("This account has no password (it signs in through single sign-on).");
        string raw = "";
        await RunAsync("user.password.require_reset", "user", id.ToString(), actor, client, async () =>
        {
            var now = clock.UtcNow;
            cred.MustChange = true; u.UpdatedAt = now; u.Version++;
            foreach (var old in await db.PasswordResetTokens.Where(t => t.UserId == id && t.ConsumedAt == null).ToListAsync(ct)) old.ConsumedAt = now;
            raw = tokens.NewToken();
            db.PasswordResetTokens.Add(new PasswordResetToken { UserId = id, TokenHash = tokens.Hash(raw), ExpiresAt = now + AccountService.ResetTtl, RequestIp = client.Ip, CreatedAt = now });
            var revoked = await sessions.RevokeAllForUserAsync(id, "password_reset_required", null, ct);
            audit.Record("user.password.require_reset", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["sessionsRevoked"] = revoked.ToString(), ["reason"] = (reason ?? "").Trim() });
            audit.Security("password.reset_required", Severity.Medium, $"{u.Name} must choose a new password (required by an administrator)", id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        email.Enqueue(EmailTemplates.PasswordResetRequiredByAdmin(u.Email, u.Name, actor.Name, $"{links.Origin.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(raw)}"));
    }

    public async Task ResendVerificationAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var u = await LoadManageableAsync(id, actor, "user.resend_verification", client, ct);
        if (u.Status != UserStatus.Pending) throw DomainException.Invariant("Only pending users can be re-sent an invitation or verification link.");
        var s = await settings.GetAsync(ct);
        EmailMessage message;
        if (await db.Credentials.AnyAsync(c => c.UserId == id, ct))
        {
            // self-registered account waiting for email verification: new link, previous one invalidated
            var now = clock.UtcNow;
            foreach (var old in await db.EmailVerificationTokens.Where(t => t.UserId == id && t.ConsumedAt == null).ToListAsync(ct)) old.ConsumedAt = now;
            var rawV = tokens.NewToken();
            db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = id, TokenHash = tokens.Hash(rawV), ExpiresAt = now + AccountService.VerifyTtl, CreatedAt = now });
            message = EmailTemplates.VerifyEmail(u.Email, u.Name, $"{links.Origin.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(rawV)}");
        }
        else
        {
            // admin-created placeholder: the invitation is the only way in — rotate it (or issue a new one if none exists)
            var inv = await db.Invitations.Include(i => i.Roles).Where(i => i.Email == u.Email && i.Status != InvitationStatus.Accepted).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
            string raw;
            if (inv is null)
            {
                raw = tokens.NewToken(); var now = clock.UtcNow;
                inv = new Invitation { Email = u.Email, TokenHash = tokens.Hash(raw), InvitedBy = actor.UserId, Status = InvitationStatus.Pending, CreatedAt = now, ExpiresAt = now.AddDays(s.InvitationExpiryDays) };
                foreach (var ur in u.UserRoles) inv.Roles.Add(new InvitationRole { RoleId = ur.RoleId });
                db.Invitations.Add(inv);
            }
            else raw = await invitations.RotateAsync(inv, s, ct); // the previous link stops working
            message = EmailTemplates.Invitation(u.Email, actor.Name, $"{links.Origin.TrimEnd('/')}/invite?token={Uri.EscapeDataString(raw)}", s.InvitationExpiryDays);
        }
        audit.Record("user.resend_verification", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        email.Enqueue(message);
    }

    // ============================================================ roles
    public async Task<UserDetail> SetRolesAsync(Guid id, Guid[] roleIds, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        Guards.RequireRecentReauthForCritical(actor, s, clock);
        await LoadManageableAsync(id, actor, "user.assign_role", client, ct);
        var wanted = await invitations.ResolveGrantableRolesAsync(roleIds, actor, "user.assign_role", client, ct); // every selected role must be grantable by the actor

        await RunAsync("user.assign_role", "user", id.ToString(), actor, client, async () =>
        {
            var u = await db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).FirstAsync(x => x.Id == id, ct);
            var before = u.UserRoles.Select(r => r.Role!).ToList();
            var removesSuper = before.Any(r => r.Key == SystemRoles.SuperAdmin) && wanted.All(r => r.Key != SystemRoles.SuperAdmin);
            if (removesSuper)
            {
                if (id == actor.UserId) throw DomainException.Invariant("You cannot remove your own Super Admin role.");
                if (u.Status == UserStatus.Active && await OtherActiveSuperAdminsAsync(id, ct) == 0) throw DomainException.Invariant("This is the last active Super Admin. Assign another Super Admin first.");
            }
            var keep = wanted.Select(r => r.Id).ToHashSet();
            foreach (var ur in u.UserRoles.Where(r => !keep.Contains(r.RoleId)).ToList()) db.UserRoles.Remove(ur);
            foreach (var r in wanted.Where(r => u.UserRoles.All(x => x.RoleId != r.Id))) db.UserRoles.Add(new UserRole { UserId = id, RoleId = r.Id, AssignedBy = actor.UserId, AssignedAt = clock.UtcNow });
            u.Version++; u.UpdatedAt = clock.UtcNow;
            audit.Record("user.assign_role", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: [new AuditDiff("roles", Show(before), Show(wanted))]);
            audit.Security("privilege.change", Severity.Medium, $"Roles for {u.Name} changed to {Show(wanted)}", id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await GetAsync(id, ct);
    }

    // ============================================================ sessions
    public async Task<int> RevokeSessionsAsync(Guid id, Guid? sessionId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await LoadManageableAsync(id, actor, "session.revoke", client, ct);
        var targets = await db.Sessions.Where(s => s.UserId == id && s.RevokedAt == null && (sessionId == null || s.Id == sessionId)).ToListAsync(ct);
        if (sessionId is not null && targets.Count == 0) throw DomainException.NotFound("Session");
        foreach (var s in targets) { s.RevokedAt = clock.UtcNow; s.RevokedReason = RevokeReason.Admin; }
        audit.Record("session.revoke", "user", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["count"] = targets.Count.ToString() });
        if (targets.Count > 0) audit.Security("session.revoked", Severity.Low, $"Administrator revoked {targets.Count} session(s)", id);
        await db.SaveChangesAsync(ct);
        return targets.Count;
    }

    // ============================================================ bulk
    public async Task<IReadOnlyList<BulkItemResult>> BulkAsync(Guid[] ids, string? action, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var list = (ids ?? []).Distinct().ToArray();
        if (list.Length == 0 || list.Length > 100) throw DomainException.Validation("Select between 1 and 100 users.");
        var need = action switch { "suspend" => Permissions.UsersSuspend, "resend_verification" => Permissions.UsersUpdate, _ => throw DomainException.Validation("Unknown bulk action.") };
        if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(need))
        {
            audit.Record("user.bulk." + action, "user", null, Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["missing"] = need });
            await db.SaveChangesAsync(ct);
            throw DomainException.Denied(need);
        }
        var results = new List<BulkItemResult>();
        foreach (var id in list)
        {
            try
            {
                if (action == "suspend") await SetStatusAsync(id, "suspend", null, actor, client, ct); else await ResendVerificationAsync(id, actor, client, ct);
                results.Add(new BulkItemResult(id, true, null));
            }
            catch (DomainException e) { db.ResetTracking(); results.Add(new BulkItemResult(id, false, e.Message)); } // partial success is expected; each item is audited on its own
        }
        return results;
    }
}
