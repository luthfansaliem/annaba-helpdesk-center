using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Persistence;

/// <summary>Fixed-window limiter in one atomic upsert (safe under concurrency, shared by all app instances).</summary>
public sealed class PostgresRateLimiter(IDbContextFactory<AnnabaDigitalDbContext> factory, IClock clock) : IRateLimiter
{
    private sealed record Row(int Count, DateTimeOffset WindowStart);

    public async Task<RateLimitResult> HitAsync(string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        var seconds = (int)window.TotalSeconds;
        var rows = await db.Database.SqlQuery<Row>($"""
            INSERT INTO rate_limit_buckets ("key", count, window_start) VALUES ({key}, 1, {now})
            ON CONFLICT ("key") DO UPDATE SET
              count = CASE WHEN rate_limit_buckets.window_start + ({seconds} * interval '1 second') <= {now} THEN 1 ELSE rate_limit_buckets.count + 1 END,
              window_start = CASE WHEN rate_limit_buckets.window_start + ({seconds} * interval '1 second') <= {now} THEN {now} ELSE rate_limit_buckets.window_start END
            RETURNING count, window_start
            """).ToListAsync(ct); // ToListAsync: INSERT…RETURNING is not composable
        var row = rows.Single();
        var retry = row.WindowStart + window - now;
        return new RateLimitResult(row.Count <= limit, retry < TimeSpan.Zero ? TimeSpan.Zero : retry);
    }

    public async Task ResetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.ExecuteSqlAsync($"""DELETE FROM rate_limit_buckets WHERE "key" = {key}""", ct);
    }
}
