namespace NexusM.SharedKernel.Context;

public sealed record OrganizationScopeSnapshot(
    bool HasUnrestrictedTenantScope,
    IReadOnlyList<Guid> LegalEntityIds,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<Guid> OrganizationUnitIds)
{
    public static readonly OrganizationScopeSnapshot Empty = new(false, [], [], []);
}

public interface IRequestContext
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    Guid? TenantId { get; }
    string CorrelationId { get; }
    string? RawBearerToken { get; }
    bool IsPlatformUser { get; }
    IReadOnlySet<string> Permissions { get; }
    OrganizationScopeSnapshot OrgScope { get; }

    void SetCorrelationId(string correlationId);
    void SetPrincipal(Guid userId, string email, Guid? tenantId, bool isPlatformUser, string? rawBearerToken);
    void SetAuthorizationState(IEnumerable<string> permissions, OrganizationScopeSnapshot orgScope);
    IDisposable BeginTenantScope(Guid tenantId);
}

public sealed class RequestContext : IRequestContext
{
    private Guid? _scopedTenantOverride;
    private Guid? _principalTenantId;
    private HashSet<string> _permissions = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAuthenticated { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Email { get; private set; }
    public Guid? TenantId => _scopedTenantOverride ?? _principalTenantId;
    public string CorrelationId { get; private set; } = Guid.CreateVersion7().ToString("D");
    public string? RawBearerToken { get; private set; }
    public bool IsPlatformUser { get; private set; }
    public IReadOnlySet<string> Permissions => _permissions;
    public OrganizationScopeSnapshot OrgScope { get; private set; } = OrganizationScopeSnapshot.Empty;

    public void SetCorrelationId(string correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            CorrelationId = correlationId.Trim();
        }
    }

    public void SetPrincipal(Guid userId, string email, Guid? tenantId, bool isPlatformUser, string? rawBearerToken)
    {
        IsAuthenticated = true;
        UserId = userId;
        Email = email;
        _principalTenantId = tenantId;
        IsPlatformUser = isPlatformUser;
        RawBearerToken = rawBearerToken;
    }

    public void SetAuthorizationState(IEnumerable<string> permissions, OrganizationScopeSnapshot orgScope)
    {
        _permissions = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        OrgScope = orgScope;
    }

    public IDisposable BeginTenantScope(Guid tenantId)
    {
        var previous = _scopedTenantOverride;
        _scopedTenantOverride = tenantId;
        return new ScopeReset(() => _scopedTenantOverride = previous);
    }

    private sealed class ScopeReset(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
