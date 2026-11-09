using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Application.Common;

public sealed class TenantContext : ITenantContext
{
    public bool Scoped { get; private set; }
    public Guid TenantId { get; private set; }
    public bool IsPlatform { get; private set; }
    public bool Support { get; private set; }
    public void Use(Guid tenantId, bool isPlatform, bool support = false) { Scoped = true; TenantId = tenantId; IsPlatform = isPlatform; Support = support; }
    public IDisposable Switch(Guid tenantId, bool isPlatform = false)
    {
        var (s, t, p, sup) = (Scoped, TenantId, IsPlatform, Support);
        Use(tenantId, isPlatform, support: true); // acting inside another organisation, so its audit trail labels the person as platform support
        return new Restore(() => { Scoped = s; TenantId = t; IsPlatform = p; Support = sup; });
    }
    private sealed class Restore(Action undo) : IDisposable { public void Dispose() => undo(); }
}
