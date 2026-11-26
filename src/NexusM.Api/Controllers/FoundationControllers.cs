using Microsoft.AspNetCore.Mvc;
using NexusM.BuildingBlocks.Security;
using NexusM.BuildingBlocks.Web;
using NexusM.Modules.Audit;
using NexusM.Modules.Foundation.Application;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Security;

namespace NexusM.Api.Controllers;

[ApiController]
[Route("api/v1/m")]
public sealed class ContextController(IFoundationService foundationService) : ControllerBase
{
    [HttpGet("context")]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task<IActionResult> GetContext(CancellationToken ct)
        => (await foundationService.GetManagementContextAsync(ct)).ToActionResult(HttpContext, asOfUtc: DateTimeOffset.UtcNow);

    [HttpGet("modules")]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task<IActionResult> ListModules(CancellationToken ct)
        => (await foundationService.ListModuleAvailabilityAsync(ct)).ToActionResult(HttpContext);

    [HttpGet("permissions/manifest")]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public IActionResult GetPermissionManifest()
        => Ok(new { success = true, data = NexusMPermissions.Manifest });
}

[ApiController]
[Route("api/v1/m/workspaces")]
public sealed class WorkspacesController(IFoundationService foundationService) : ControllerBase
{
    [HttpGet]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task<IActionResult> List(CancellationToken ct)
        => (await foundationService.ListWorkspacesAsync(ct)).ToActionResult(HttpContext);

    [HttpPost]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceManage)]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceRequest request, CancellationToken ct)
        => (await foundationService.CreateWorkspaceAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPut("{id:guid}")]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceManage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkspaceRequest request, CancellationToken ct)
        => (await foundationService.UpdateWorkspaceAsync(id, request, ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m/preferences")]
public sealed class PreferencesController(IFoundationService foundationService) : ControllerBase
{
    [HttpGet]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => (await foundationService.GetPreferencesAsync(ct)).ToActionResult(HttpContext);

    [HttpPut]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task<IActionResult> Update([FromBody] UpdateUserPreferenceRequest request, CancellationToken ct)
        => (await foundationService.UpdatePreferencesAsync(request, ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m/audit")]
public sealed class AuditController(IBusinessAuditReader auditReader) : ControllerBase
{
    /// <summary>
    /// Strictly read-only endpoint over immutable m_audit.business_audit_events (ADR-007).
    /// </summary>
    [HttpGet]
    [RequireNexusMPermission(NexusMPermissions.AuditRead)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        CancellationToken ct = default)
        => (await auditReader.QueryAsync(page, pageSize, action, entityType, ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m/realtime")]
public sealed class RealtimeController(IRequestContext requestContext) : ControllerBase
{
    /// <summary>
    /// Authenticated, tenant-scoped Server-Sent Events stream (ADR-005).
    /// </summary>
    [HttpGet("stream")]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var connectedPayload = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType = "management.realtime.connected.v1",
            tenantId = requestContext.TenantId,
            correlationId = requestContext.CorrelationId,
            occurredAt = DateTimeOffset.UtcNow
        });

        await Response.WriteAsync($"event: connected\ndata: {connectedPayload}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(25));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await Response.WriteAsync($": heartbeat {DateTimeOffset.UtcNow:O}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal client disconnect
        }
    }
}
