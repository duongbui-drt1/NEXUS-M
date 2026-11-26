using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Foundation.Domain;

public enum OrganizationComplexityMode
{
    Simple = 1,       // Micro-business / SME: minimal setup, no forced legal-entity/BU hierarchy
    Standard = 2,     // Mid-market: departments & teams
    Enterprise = 3    // Multi-legal-entity, multi-BU group
}

public sealed class Workspace
{
    private Workspace() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string DefaultLocale { get; private set; } = "vi-VN";
    public string DefaultTimezone { get; private set; } = "Asia/Ho_Chi_Minh";
    public string DefaultCurrency { get; private set; } = "VND";
    public OrganizationComplexityMode OrgMode { get; private set; } = OrganizationComplexityMode.Simple;
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Version { get; private set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<Workspace> Create(
        Guid tenantId,
        string code,
        string name,
        string? description = null,
        string? defaultLocale = null,
        string? defaultTimezone = null,
        string? defaultCurrency = null,
        OrganizationComplexityMode orgMode = OrganizationComplexityMode.Simple,
        bool isDefault = false)
    {
        if (tenantId == Guid.Empty)
        {
            return Error.Validation("WORKSPACE_TENANT_REQUIRED", "TenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 40)
        {
            return Error.Validation("WORKSPACE_INVALID_CODE", "Workspace code is required (max 40 chars).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return Error.Validation("WORKSPACE_INVALID_NAME", "Workspace name is required (max 120 chars).");
        }

        var currency = string.IsNullOrWhiteSpace(defaultCurrency) ? "VND" : defaultCurrency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            return Error.Validation("WORKSPACE_INVALID_CURRENCY", "Default currency must be a 3-letter ISO 4217 code.");
        }

        var now = DateTimeOffset.UtcNow;
        return new Workspace
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            DefaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? "vi-VN" : defaultLocale.Trim(),
            DefaultTimezone = string.IsNullOrWhiteSpace(defaultTimezone) ? "Asia/Ho_Chi_Minh" : defaultTimezone.Trim(),
            DefaultCurrency = currency,
            OrgMode = orgMode,
            IsDefault = isDefault,
            IsActive = true,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public Result<Workspace> Update(
        string name,
        string? description,
        string defaultLocale,
        string defaultTimezone,
        string defaultCurrency,
        OrganizationComplexityMode orgMode,
        int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            return Error.Conflict("WORKSPACE_CONCURRENCY_CONFLICT", $"Workspace was modified concurrently (expected v{expectedVersion}, current v{Version}).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return Error.Validation("WORKSPACE_INVALID_NAME", "Workspace name is required (max 120 chars).");
        }

        var currency = defaultCurrency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            return Error.Validation("WORKSPACE_INVALID_CURRENCY", "Default currency must be a 3-letter ISO 4217 code.");
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        DefaultLocale = string.IsNullOrWhiteSpace(defaultLocale) ? "vi-VN" : defaultLocale.Trim();
        DefaultTimezone = string.IsNullOrWhiteSpace(defaultTimezone) ? "Asia/Ho_Chi_Minh" : defaultTimezone.Trim();
        DefaultCurrency = currency;
        OrgMode = orgMode;
        Version++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        return this;
    }
}

public sealed class UserPreference
{
    private UserPreference() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Locale { get; private set; } = "vi-VN";
    public string Timezone { get; private set; } = "Asia/Ho_Chi_Minh";
    public string Theme { get; private set; } = "light";
    public Guid? DefaultWorkspaceId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static UserPreference CreateDefault(Guid tenantId, Guid userId, Guid? defaultWorkspaceId = null)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            Locale = "vi-VN",
            Timezone = "Asia/Ho_Chi_Minh",
            Theme = "light",
            DefaultWorkspaceId = defaultWorkspaceId,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

    public void Update(string locale, string timezone, string theme, Guid? defaultWorkspaceId)
    {
        Locale = string.IsNullOrWhiteSpace(locale) ? "vi-VN" : locale.Trim();
        Timezone = string.IsNullOrWhiteSpace(timezone) ? "Asia/Ho_Chi_Minh" : timezone.Trim();
        Theme = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light";
        DefaultWorkspaceId = defaultWorkspaceId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
