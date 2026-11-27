using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusM.BuildingBlocks.Security;
using NexusM.BuildingBlocks.Web;
using NexusM.Modules.Ai;
using NexusM.Modules.Bi;
using NexusM.Modules.Executive;
using NexusM.Modules.Foundation.Infrastructure;
using NexusM.Modules.Governance;
using NexusM.Modules.Metrics.Infrastructure;
using NexusM.Modules.Organization;
using NexusM.Modules.Performance;
using NexusM.Modules.Workflow;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Security;

namespace NexusM.Api.Controllers;

[ApiController]
[Route("api/v1/m/executive")]
public sealed class ExecutiveController(IExecutiveService executiveService) : ControllerBase
{
    [HttpGet("dashboards")]
    [RequireNexusMPermission(NexusMPermissions.DashboardRead)]
    public async Task<IActionResult> ListDashboards([FromQuery] string? audienceRole = null, CancellationToken ct = default)
        => (await executiveService.ListDashboardsAsync(audienceRole, ct)).ToActionResult(HttpContext);

    [HttpPost("dashboards")]
    [RequireNexusMPermission(NexusMPermissions.DashboardManage)]
    public async Task<IActionResult> CreateDashboard([FromBody] CreateDashboardRequest request, CancellationToken ct)
        => (await executiveService.CreateDashboardAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("dashboards/{id:guid}/render")]
    [RequireNexusMPermission(NexusMPermissions.DashboardRead)]
    public async Task<IActionResult> RenderDashboard(
        Guid id,
        [FromQuery] DateTimeOffset periodStartUtc,
        [FromQuery] DateTimeOffset periodEndUtc,
        [FromQuery] Guid? branchId = null,
        [FromQuery] Guid? legalEntityId = null,
        CancellationToken ct = default)
        => (await executiveService.RenderDashboardAsync(id, periodStartUtc, periodEndUtc, branchId, legalEntityId, ct))
           .ToActionResult(HttpContext, asOfUtc: DateTimeOffset.UtcNow);

    [HttpPost("alerts/rules")]
    [RequireNexusMPermission(NexusMPermissions.DashboardManage)]
    public async Task<IActionResult> CreateAlertRule([FromBody] CreateAlertRuleRequest request, CancellationToken ct)
        => (await executiveService.CreateAlertRuleAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("alerts/evaluate")]
    [RequireNexusMPermission(NexusMPermissions.DashboardManage)]
    public async Task<IActionResult> EvaluateAlerts(
        [FromQuery] DateTimeOffset periodStartUtc,
        [FromQuery] DateTimeOffset periodEndUtc,
        CancellationToken ct)
        => (await executiveService.EvaluateAlertsAsync(periodStartUtc, periodEndUtc, ct)).ToActionResult(HttpContext);

    [HttpGet("alerts")]
    [RequireNexusMPermission(NexusMPermissions.DashboardRead)]
    public async Task<IActionResult> ListAlerts(CancellationToken ct)
        => (await executiveService.ListAlertsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("alerts/{id:guid}/acknowledge")]
    [RequireNexusMPermission(NexusMPermissions.DashboardManage)]
    public async Task<IActionResult> AcknowledgeAlert(Guid id, CancellationToken ct)
        => (await executiveService.AcknowledgeAlertAsync(id, ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m/bi")]
public sealed class BiController(IBiService biService) : ControllerBase
{
    [HttpGet("reports")]
    [RequireNexusMPermission(NexusMPermissions.ReportRead)]
    public async Task<IActionResult> ListReports(CancellationToken ct)
        => (await biService.ListReportsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("reports")]
    [RequireNexusMPermission(NexusMPermissions.ReportManage)]
    public async Task<IActionResult> CreateReport([FromBody] CreateSavedReportRequest request, CancellationToken ct)
        => (await biService.CreateReportAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("reports/{id:guid}/run")]
    [RequireNexusMPermission(NexusMPermissions.ReportRead)]
    public async Task<IActionResult> RunReport(Guid id, [FromBody] RunReportRequest request, CancellationToken ct)
        => (await biService.RunReportAsync(id, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("snapshots/{snapshotId:guid}/export")]
    [RequireNexusMPermission(NexusMPermissions.ReportExport)]
    public async Task<IActionResult> ExportSnapshot(Guid snapshotId, [FromQuery] string format = "CSV", CancellationToken ct = default)
        => (await biService.ExportSnapshotAsync(snapshotId, format, ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m/workflows")]
public sealed class WorkflowsController(IWorkflowHubService workflowService) : ControllerBase
{
    [HttpGet("templates")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowRead)]
    public async Task<IActionResult> ListTemplates(CancellationToken ct)
        => (await workflowService.ListTemplatesAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("templates")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowManage)]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateTemplateRequest request, CancellationToken ct)
        => (await workflowService.CreateTemplateAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("requests")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowCreate)]
    public async Task<IActionResult> SubmitRequest([FromBody] SubmitApprovalRequest request, CancellationToken ct)
        => (await workflowService.SubmitRequestAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("inbox")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowRead)]
    public async Task<IActionResult> ListInbox([FromQuery] string? status = null, CancellationToken ct = default)
        => (await workflowService.ListInboxAsync(status, ct)).ToActionResult(HttpContext);

    [HttpPost("requests/{id:guid}/decide")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowApprove)]
    public async Task<IActionResult> Decide(Guid id, [FromBody] WorkflowDecisionRequest request, CancellationToken ct)
        => (await workflowService.DecideAsync(id, request, ct)).ToActionResult(HttpContext);

    [HttpPost("requests/{id:guid}/delegate")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowApprove)]
    public async Task<IActionResult> Delegate(Guid id, [FromBody] DelegateWorkflowRequest request, CancellationToken ct)
        => (await workflowService.DelegateAsync(id, request, ct)).ToActionResult(HttpContext);

    [HttpPost("requests/{id:guid}/escalate")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowManage)]
    public async Task<IActionResult> Escalate(Guid id, CancellationToken ct)
        => (await workflowService.EscalateOverdueAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPost("requests/{id:guid}/comments")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowRead)]
    public async Task<IActionResult> AddComment(Guid id, [FromBody] AddCommentInput input, CancellationToken ct)
        => (await workflowService.AddCommentAsync(id, input.Body, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("requests/{id:guid}/history")]
    [RequireNexusMPermission(NexusMPermissions.WorkflowRead)]
    public async Task<IActionResult> GetHistory(Guid id, CancellationToken ct)
        => (await workflowService.GetDecisionHistoryAsync(id, ct)).ToActionResult(HttpContext);
}

public sealed record AddCommentInput(string Body);

[ApiController]
[Route("api/v1/m/org")]
public sealed class OrganizationController(IOrganizationOverlayService orgService) : ControllerBase
{
    [HttpPost("units")]
    [RequireNexusMPermission(NexusMPermissions.OrgManage)]
    public async Task<IActionResult> CreateUnit([FromBody] CreateOrgUnitProfileRequest request, CancellationToken ct)
        => (await orgService.CreateUnitProfileAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("positions")]
    [RequireNexusMPermission(NexusMPermissions.OrgManage)]
    public async Task<IActionResult> CreatePosition([FromBody] CreatePositionRequest request, CancellationToken ct)
        => (await orgService.CreatePositionAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("assignments")]
    [RequireNexusMPermission(NexusMPermissions.OrgManage)]
    public async Task<IActionResult> AssignPosition([FromBody] AssignPositionRequest request, CancellationToken ct)
        => (await orgService.AssignPositionAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("overview")]
    [RequireNexusMPermission(NexusMPermissions.OrgRead)]
    public async Task<IActionResult> GetOverview([FromQuery] DateOnly? asOfDate = null, CancellationToken ct = default)
        => (await orgService.GetOverviewAsync(asOfDate, ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m")]
public sealed class GovernanceController(IGovernanceService governanceService) : ControllerBase
{
    [HttpGet("risks")]
    [RequireNexusMPermission(NexusMPermissions.RiskRead)]
    public async Task<IActionResult> ListRisks(CancellationToken ct)
        => (await governanceService.ListRisksAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("risks")]
    [RequireNexusMPermission(NexusMPermissions.RiskManage)]
    public async Task<IActionResult> CreateRisk([FromBody] CreateRiskRequest request, CancellationToken ct)
        => (await governanceService.CreateRiskAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPut("risks/{id:guid}/reassess")]
    [RequireNexusMPermission(NexusMPermissions.RiskManage)]
    public async Task<IActionResult> ReassessRisk(Guid id, [FromBody] ReassessRiskRequest request, CancellationToken ct)
        => (await governanceService.ReassessRiskAsync(id, request, ct)).ToActionResult(HttpContext);

    [HttpGet("risks/controls")]
    [RequireNexusMPermission(NexusMPermissions.RiskRead)]
    public async Task<IActionResult> ListControls(CancellationToken ct)
        => (await governanceService.ListControlsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("risks/controls")]
    [RequireNexusMPermission(NexusMPermissions.RiskManage)]
    public async Task<IActionResult> CreateControl([FromBody] CreateControlRequest request, CancellationToken ct)
        => (await governanceService.CreateControlAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("risks/incidents")]
    [RequireNexusMPermission(NexusMPermissions.RiskRead)]
    public async Task<IActionResult> ListIncidents(CancellationToken ct)
        => (await governanceService.ListIncidentsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("risks/incidents")]
    [RequireNexusMPermission(NexusMPermissions.RiskManage)]
    public async Task<IActionResult> ReportIncident([FromBody] CreateIncidentRequest request, CancellationToken ct)
        => (await governanceService.ReportIncidentAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("compliance/obligations")]
    [RequireNexusMPermission(NexusMPermissions.ComplianceRead)]
    public async Task<IActionResult> ListObligations([FromQuery] string? jurisdictionCode = null, CancellationToken ct = default)
        => (await governanceService.ListObligationsAsync(jurisdictionCode, ct)).ToActionResult(HttpContext);

    [HttpPost("compliance/obligations")]
    [RequireNexusMPermission(NexusMPermissions.ComplianceManage)]
    public async Task<IActionResult> CreateObligation([FromBody] CreateObligationRequest request, CancellationToken ct)
        => (await governanceService.CreateObligationAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("compliance/obligations/{id:guid}/evidence")]
    [RequireNexusMPermission(NexusMPermissions.ComplianceManage)]
    public async Task<IActionResult> AttachEvidence(Guid id, [FromBody] AttachEvidenceInput input, CancellationToken ct)
        => (await governanceService.AttachObligationEvidenceAsync(id, input.CFileId, ct)).ToActionResult(HttpContext);
}

public sealed record AttachEvidenceInput(Guid CFileId);

[ApiController]
[Route("api/v1/m")]
public sealed class PerformanceController(IPerformanceService performanceService) : ControllerBase
{
    [HttpGet("board-reports")]
    [RequireNexusMPermission(NexusMPermissions.ReportRead)]
    public async Task<IActionResult> ListBoardReports(CancellationToken ct)
        => (await performanceService.ListBoardReportsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("board-reports")]
    [RequireNexusMPermission(NexusMPermissions.ReportManage)]
    public async Task<IActionResult> CreateBoardReport([FromBody] CreateBoardReportRequest request, CancellationToken ct)
        => (await performanceService.CreateConsolidatedBoardReportAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("board-reports/{id:guid}/publish")]
    [RequireNexusMPermission(NexusMPermissions.ReportManage)]
    public async Task<IActionResult> PublishBoardReport(Guid id, CancellationToken ct)
        => (await performanceService.ApproveAndPublishBoardReportAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPost("performance/objectives")]
    [RequireNexusMPermission(NexusMPermissions.PerformanceManage)]
    public async Task<IActionResult> CreateObjective([FromBody] CreateObjectiveRequest request, CancellationToken ct)
        => (await performanceService.CreateObjectiveAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("performance/objectives")]
    [RequireNexusMPermission(NexusMPermissions.PerformanceRead)]
    public async Task<IActionResult> EvaluateObjectives(
        [FromQuery] DateTimeOffset periodStartUtc,
        [FromQuery] DateTimeOffset periodEndUtc,
        CancellationToken ct)
        => (await performanceService.EvaluateObjectivesAsync(periodStartUtc, periodEndUtc, ct)).ToActionResult(HttpContext);

    [HttpPost("performance/initiatives")]
    [RequireNexusMPermission(NexusMPermissions.PerformanceManage)]
    public async Task<IActionResult> CreateInitiative([FromBody] CreateInitiativeRequest request, CancellationToken ct)
        => (await performanceService.CreateInitiativeAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("performance/initiatives")]
    [RequireNexusMPermission(NexusMPermissions.PerformanceRead)]
    public async Task<IActionResult> ListInitiatives(CancellationToken ct)
        => (await performanceService.ListInitiativesAsync(ct)).ToActionResult(HttpContext);
}

[ApiController]
[Route("api/v1/m/ai")]
public sealed class AiController(IExecutiveAiService aiService) : ControllerBase
{
    [HttpPost("ask")]
    [RequireNexusMPermission(NexusMPermissions.AiInvoke, entitlementCode: "NEXUS_M_AI")]
    public async Task<IActionResult> Ask([FromBody] AskExecutiveAiRequest request, CancellationToken ct)
        => (await aiService.AskGroundedAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("usage")]
    [RequireNexusMPermission(NexusMPermissions.AiInvoke, entitlementCode: "NEXUS_M_AI")]
    public async Task<IActionResult> ListUsage(CancellationToken ct)
        => (await aiService.ListUsageAsync(ct)).ToActionResult(HttpContext);
}

public sealed record EnterpriseSearchHitDto(string Category, Guid Id, string Code, string Title, string Route);

[ApiController]
[Route("api/v1/m/search")]
public sealed class SearchController(
    IRequestContext requestContext,
    FoundationDbContext foundationDb,
    MetricsDbContext metricsDb,
    BiDbContext biDb,
    WorkflowDbContext workflowDb,
    GovernanceDbContext governanceDb,
    PerformanceDbContext performanceDb) : ControllerBase
{
    /// <summary>
    /// Enterprise search enforcing authorization and tenant isolation at query time (Master Prompt Section 5.4).
    /// </summary>
    [HttpGet]
    [RequireNexusMPermission(NexusMPermissions.WorkspaceRead)]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return Ok(new { success = true, data = Array.Empty<EnterpriseSearchHitDto>(), meta = new { correlationId = requestContext.CorrelationId } });
        }

        var tid = requestContext.TenantId!.Value;
        var term = $"%{q.Trim()}%";
        var perms = requestContext.Permissions;
        var hits = new List<EnterpriseSearchHitDto>();

        var ws = await foundationDb.Workspaces.AsNoTracking()
            .Where(w => w.TenantId == tid && (EF.Functions.ILike(w.Code, term) || EF.Functions.ILike(w.Name, term)))
            .Take(10)
            .Select(w => new EnterpriseSearchHitDto("Workspace", w.Id, w.Code, w.Name, "/workspace"))
            .ToListAsync(ct);
        hits.AddRange(ws);

        if (perms.Contains(NexusMPermissions.KpiRead))
        {
            hits.AddRange(await metricsDb.MetricDefinitions.AsNoTracking()
                .Where(m => m.TenantId == tid && (EF.Functions.ILike(m.MetricCode, term) || EF.Functions.ILike(m.DisplayNameKey, term)))
                .Take(10)
                .Select(m => new EnterpriseSearchHitDto("KPI", m.Id, m.MetricCode, m.DisplayNameKey, "/kpis"))
                .ToListAsync(ct));
        }

        if (perms.Contains(NexusMPermissions.ReportRead))
        {
            hits.AddRange(await biDb.SavedReports.AsNoTracking()
                .Where(r => r.TenantId == tid && (EF.Functions.ILike(r.Code, term) || EF.Functions.ILike(r.Title, term)))
                .Take(10)
                .Select(r => new EnterpriseSearchHitDto("Report", r.Id, r.Code, r.Title, "/bi"))
                .ToListAsync(ct));
        }

        if (perms.Contains(NexusMPermissions.WorkflowRead))
        {
            hits.AddRange(await workflowDb.Requests.AsNoTracking()
                .Where(w => w.TenantId == tid && EF.Functions.ILike(w.Title, term))
                .Take(10)
                .Select(w => new EnterpriseSearchHitDto("Workflow", w.Id, w.Category, w.Title, "/workflows"))
                .ToListAsync(ct));
        }

        if (perms.Contains(NexusMPermissions.RiskRead))
        {
            hits.AddRange(await governanceDb.Risks.AsNoTracking()
                .Where(r => r.TenantId == tid && (EF.Functions.ILike(r.Code, term) || EF.Functions.ILike(r.Title, term)))
                .Take(10)
                .Select(r => new EnterpriseSearchHitDto("Risk", r.Id, r.Code, r.Title, "/risk"))
                .ToListAsync(ct));
        }

        if (perms.Contains(NexusMPermissions.ComplianceRead))
        {
            hits.AddRange(await governanceDb.Obligations.AsNoTracking()
                .Where(o => o.TenantId == tid && (EF.Functions.ILike(o.Code, term) || EF.Functions.ILike(o.Title, term)))
                .Take(10)
                .Select(o => new EnterpriseSearchHitDto("Compliance", o.Id, o.Code, o.Title, "/compliance"))
                .ToListAsync(ct));
        }

        if (perms.Contains(NexusMPermissions.PerformanceRead))
        {
            hits.AddRange(await performanceDb.Objectives.AsNoTracking()
                .Where(o => o.TenantId == tid && (EF.Functions.ILike(o.Code, term) || EF.Functions.ILike(o.Title, term)))
                .Take(10)
                .Select(o => new EnterpriseSearchHitDto("Objective", o.Id, o.Code, o.Title, "/performance"))
                .ToListAsync(ct));
        }

        return Ok(new { success = true, data = hits, meta = new { correlationId = requestContext.CorrelationId } });
    }
}
