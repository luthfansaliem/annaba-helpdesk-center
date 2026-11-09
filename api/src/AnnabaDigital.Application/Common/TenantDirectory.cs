using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Common;

public sealed record TenantInfo(Guid Id, string Name, string Slug, TenantStatus Status, bool IsPlatform);

/// <summary>An organisation's name, status and platform flag, cached briefly because every request needs it (session check, sign-in, /me). Changing an organisation clears the cache.</summary>
public sealed class TenantDirectory(IAppDb db, AppCache cache)
{
    private static string Key(Guid id) => "tenant:" + id;

    public async Task<TenantInfo?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var info = await cache.GetOrAddAsync<TenantInfo?>(Key(id), async () =>
            await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => new TenantInfo(t.Id, t.Name, t.Slug, t.Status, t.IsPlatform)).FirstOrDefaultAsync(ct), TimeSpan.FromSeconds(15));
        if (info is null) cache.Remove(Key(id)); // never remember "not found"
        return info;
    }

    public void Invalidate() => cache.Clear();
}
