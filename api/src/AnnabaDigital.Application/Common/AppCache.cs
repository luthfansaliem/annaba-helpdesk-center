using System.Collections.Concurrent;

namespace AnnabaDigital.Application.Common;

/// <summary>
/// A tiny in-process cache with a time limit, for data that is read on every request and changes rarely (system settings, an organisation's policy, an
/// organisation's status). The service that changes the data calls <see cref="Clear"/> so the instance that made the change sees it at once; other instances see
/// it within the time limit. Authorization data is NOT cached here.
/// </summary>
public sealed class AppCache(TimeSpan? ttl = null)
{
    private readonly ConcurrentDictionary<string, (object Value, long Expires)> _items = new();
    /// <summary>How long an entry lives unless the caller says otherwise; zero or less switches caching off.</summary>
    public TimeSpan Ttl { get; } = ttl ?? TimeSpan.FromSeconds(30);

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        var life = ttl is { } t ? (Ttl <= TimeSpan.Zero ? TimeSpan.Zero : t) : Ttl;
        if (life <= TimeSpan.Zero) return await factory();
        var now = Environment.TickCount64;
        if (_items.TryGetValue(key, out var hit) && hit.Expires > now) return (T)hit.Value;
        var value = await factory();
        _items[key] = (value!, now + (long)life.TotalMilliseconds);
        return value;
    }

    public void Remove(string key) => _items.TryRemove(key, out _);
    public void Clear() => _items.Clear();
    public int Count => _items.Count;
}
