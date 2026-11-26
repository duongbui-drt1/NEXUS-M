using Microsoft.EntityFrameworkCore;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.Modules.Foundation.Domain;
using NexusM.Modules.Foundation.Infrastructure;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Modules;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Foundation.Application;

public sealed record WorkspaceDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string Description,
    string DefaultLocale,
    string DefaultTimezone,
    string DefaultCurrency,
    string OrgMode,
    bool IsDefault,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateWorkspaceRequest(
    string Code,
    string Name,
    string? Description = null,
    string? DefaultLocale = "vi-VN",
    string? DefaultTimezone = "Asia/Ho_Chi_Minh",
    string? DefaultCurrency = "VND",
    OrganizationComplexityMode OrgMode = OrganizationComplexityMode.Simple);

public sealed record UpdateWorkspaceRequest(
    string Name,
    string? Description,
    string DefaultLocale,
    string DefaultTimezone,
    string DefaultCurrency,
    OrganizationComplexityMode OrgMode,
    int ExpectedVersion);

public sealed record UserPreferenceDto(
    Guid UserId,
    string Locale,
    string Timezone,
    string Theme,
    Guid? DefaultWorkspaceId,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpdateUserPreferenceRequest(
    string Locale,
    string Timezone,
    string Theme,
    Guid? DefaultWorkspaceId);

public sealed record ModuleAvailabilityDto(
    string Code,
    string NameKey,
    string Route,
    string Maturity,
    bool IsEntitled,
    bool HasPermission,
    bool IsAccessible,
    string EntitlementReason,
    int SortOrder);

public sealed record ManagementContextDto(
    Guid UserId,
    string Email,
    Guid TenantId,
    WorkspaceDto ActiveWorkspace,
    IReadOnlyList<WorkspaceDto> Workspaces,
    UserPreferenceDto Preferences,
    OrganizationScopeSnapshot OrgScope,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ModuleAvailabilityDto> Modules);

public interface IFoundationService
{
    Task<Result<ManagementContextDto>> GetManagementContextAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<WorkspaceDto>>> ListWorkspacesAsync(CancellationToken ct = default);
    Task<Result<WorkspaceDto>> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken ct = default);
    Task<Result<WorkspaceDto>> UpdateWorkspaceAsync(Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken ct = default);
    Task<Result<UserPreferenceDto>> GetPreferencesAsync(CancellationToken ct = default);
    Task<Result<UserPreferenceDto>> UpdatePreferencesAsync(UpdateUserPreferenceRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ModuleAvailabilityDto>>> ListModuleAvailabilityAsync(CancellationToken ct = default);
}

public sealed class FoundationService(
    FoundationDbContext db,
    IRequestContext requestContext,
    INexusCoreClient nexusCoreClient,
    IBusinessAuditWriter auditWriter) : IFoundationService
{
    private static WorkspaceDto Map(Workspace w) => new(
        w.Id, w.TenantId, w.Code, w.Name, w.Description,
        w.DefaultLocale, w.DefaultTimezone, w.DefaultCurrency,
        w.OrgMode.ToString(), w.IsDefault, w.IsActive, w.Version,
        w.CreatedAtUtc, w.UpdatedAtUtc);

    private async Task<Workspace> EnsureDefaultWorkspaceAsync(Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Workspaces
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .OrderByDescending(w => w.IsDefault)
            .ThenBy(w => w.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing;
        }

        var created = Workspace.Create(
            tenantId,
            "DEFAULT",
            "Main Management Workspace",
            "Auto-provisioned default workspace for progressive management onboarding.",
            "vi-VN",
            "Asia/Ho_Chi_Minh",
            "VND",
            OrganizationComplexityMode.Simple,
            isDefault: true).Value;

        db.Workspaces.Add(created);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.workspace.auto_provisioned",
            "Workspace",
            created.Id.ToString("D"),
            AfterState: new { created.Code, created.Name, OrgMode = created.OrgMode.ToString() }), ct);

        return created;
    }

    public async Task<Result<ManagementContextDto>> GetManagementContextAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        var userId = requestContext.UserId.Value;

        var defaultWs = await EnsureDefaultWorkspaceAsync(tenantId, ct);
        var workspaces = await db.Workspaces.AsNoTracking()
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .ToListAsync(ct);

        var prefResult = await GetPreferencesAsync(ct);
        if (prefResult.IsFailure)
        {
            return prefResult.Error!;
        }

        var activeWs = workspaces.FirstOrDefault(w => w.Id == prefResult.Value.DefaultWorkspaceId) ?? defaultWs;

        var modulesResult = await ListModuleAvailabilityAsync(ct);
        if (modulesResult.IsFailure)
        {
            return modulesResult.Error!;
        }

        return new ManagementContextDto(
            userId,
            requestContext.Email ?? string.Empty,
            tenantId,
            Map(activeWs),
            workspaces.Select(Map).ToList(),
            prefResult.Value,
            requestContext.OrgScope,
            requestContext.Permissions.OrderBy(p => p).ToList(),
            modulesResult.Value);
    }

    public async Task<Result<IReadOnlyList<WorkspaceDto>>> ListWorkspacesAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        await EnsureDefaultWorkspaceAsync(tenantId, ct);

        var list = await db.Workspaces.AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .ToListAsync(ct);

        return list.Select(Map).ToList();
    }

    public async Task<Result<WorkspaceDto>> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        var normalizedCode = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        var exists = await db.Workspaces.AnyAsync(w => w.TenantId == tenantId && w.Code == normalizedCode, ct);
        if (exists)
        {
            return Error.Conflict("WORKSPACE_CODE_EXISTS", $"Workspace code '{normalizedCode}' already exists in this tenant.");
        }

        var created = Workspace.Create(
            tenantId,
            normalizedCode,
            request.Name,
            request.Description,
            request.DefaultLocale,
            request.DefaultTimezone,
            request.DefaultCurrency,
            request.OrgMode,
            isDefault: false);

        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.Workspaces.Add(created.Value);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.workspace.created",
            "Workspace",
            created.Value.Id.ToString("D"),
            AfterState: Map(created.Value)), ct);

        return Map(created.Value);
    }

    public async Task<Result<WorkspaceDto>> UpdateWorkspaceAsync(Guid workspaceId, UpdateWorkspaceRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        var ws = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId && w.TenantId == tenantId, ct);
        if (ws is null)
        {
            return Error.NotFound("WORKSPACE_NOT_FOUND", "Workspace was not found in the active tenant.");
        }

        var before = Map(ws);
        var updated = ws.Update(
            request.Name,
            request.Description,
            request.DefaultLocale,
            request.DefaultTimezone,
            request.DefaultCurrency,
            request.OrgMode,
            request.ExpectedVersion);

        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(ct);

        var after = Map(ws);
        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.workspace.updated",
            "Workspace",
            ws.Id.ToString("D"),
            BeforeState: before,
            AfterState: after), ct);

        return after;
    }

    public async Task<Result<UserPreferenceDto>> GetPreferencesAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        var userId = requestContext.UserId.Value;

        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);
        if (pref is null)
        {
            var defaultWs = await EnsureDefaultWorkspaceAsync(tenantId, ct);
            pref = UserPreference.CreateDefault(tenantId, userId, defaultWs.Id);
            db.UserPreferences.Add(pref);
            await db.SaveChangesAsync(ct);
        }

        return new UserPreferenceDto(pref.UserId, pref.Locale, pref.Timezone, pref.Theme, pref.DefaultWorkspaceId, pref.UpdatedAtUtc);
    }

    public async Task<Result<UserPreferenceDto>> UpdatePreferencesAsync(UpdateUserPreferenceRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        var userId = requestContext.UserId.Value;

        if (request.DefaultWorkspaceId.HasValue)
        {
            var wsExists = await db.Workspaces.AnyAsync(w => w.Id == request.DefaultWorkspaceId.Value && w.TenantId == tenantId, ct);
            if (!wsExists)
            {
                return Error.Validation("WORKSPACE_NOT_FOUND", "Selected default workspace does not exist in this tenant.");
            }
        }

        var pref = await db.UserPreferences.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);
        if (pref is null)
        {
            pref = UserPreference.CreateDefault(tenantId, userId, request.DefaultWorkspaceId);
            db.UserPreferences.Add(pref);
        }

        pref.Update(request.Locale, request.Timezone, request.Theme, request.DefaultWorkspaceId);
        await db.SaveChangesAsync(ct);

        return new UserPreferenceDto(pref.UserId, pref.Locale, pref.Timezone, pref.Theme, pref.DefaultWorkspaceId, pref.UpdatedAtUtc);
    }

    public async Task<Result<IReadOnlyList<ModuleAvailabilityDto>>> ListModuleAvailabilityAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || string.IsNullOrWhiteSpace(requestContext.RawBearerToken))
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = requestContext.TenantId.Value;
        var entitlementCache = new Dictionary<string, EntitlementEvaluationDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var entCode in NexusMModuleCatalog.All.Select(m => m.RequiredEntitlementCode).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var eval = await nexusCoreClient.EvaluateEntitlementAsync(
                tenantId, entCode, requestContext.RawBearerToken, requestContext.CorrelationId, ct);

            entitlementCache[entCode] = eval.IsSuccess
                ? eval.Value
                : new EntitlementEvaluationDto(entCode, false, eval.Error!.Code);
        }

        var result = NexusMModuleCatalog.All
            .OrderBy(m => m.SortOrder)
            .Select(m =>
            {
                var ent = entitlementCache[m.RequiredEntitlementCode];
                var hasPerm = requestContext.Permissions.Contains(m.RequiredPermission);
                var accessible = ent.IsEntitled && hasPerm && m.Maturity == ModuleMaturity.Implemented;
                return new ModuleAvailabilityDto(
                    m.Code,
                    m.NameKey,
                    m.Route,
                    m.Maturity.ToString(),
                    ent.IsEntitled,
                    hasPerm,
                    accessible,
                    ent.Reason,
                    m.SortOrder);
            })
            .ToList();

        return result;
    }
}
