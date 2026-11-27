using Microsoft.AspNetCore.Mvc;
using NexusM.BuildingBlocks.Security;
using NexusM.BuildingBlocks.Web;
using NexusM.Modules.Integration.Application;
using NexusM.Modules.Metrics.Application;
using NexusM.Modules.Metrics.Domain;
using NexusM.SharedKernel.Security;

namespace NexusM.Api.Controllers;

[ApiController]
[Route("api/v1/m/metrics")]
public sealed class MetricsController(IMetricsService metricsService) : ControllerBase
{
    [HttpGet("definitions")]
    [RequireNexusMPermission(NexusMPermissions.KpiRead)]
    public async Task<IActionResult> ListDefinitions(CancellationToken ct)
        => (await metricsService.ListDefinitionsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("definitions")]
    [RequireNexusMPermission(NexusMPermissions.KpiManage)]
    public async Task<IActionResult> CreateDefinition([FromBody] CreateMetricDefinitionRequest request, CancellationToken ct)
        => (await metricsService.CreateDefinitionAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("definitions/{id:guid}/versions")]
    [RequireNexusMPermission(NexusMPermissions.KpiManage)]
    public async Task<IActionResult> PublishVersion(Guid id, [FromBody] PublishMetricVersionRequest request, CancellationToken ct)
        => (await metricsService.PublishVersionAsync(id, request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("observations")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationManage)]
    public async Task<IActionResult> IngestObservations([FromBody] IngestObservationsBatchRequest request, CancellationToken ct)
        => (await metricsService.IngestObservationsAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("query")]
    [RequireNexusMPermission(NexusMPermissions.KpiRead)]
    public async Task<IActionResult> Query(
        [FromQuery] string metricCode,
        [FromQuery] int? version = null,
        [FromQuery] Guid? legalEntityId = null,
        [FromQuery] Guid? branchId = null,
        [FromQuery] Guid? orgUnitId = null,
        [FromQuery] DateTimeOffset? fromUtc = null,
        [FromQuery] DateTimeOffset? toUtc = null,
        [FromQuery] string? normalizeToCurrency = null,
        [FromQuery] MetricAggregationMethod? aggregation = null,
        CancellationToken ct = default)
        => (await metricsService.QueryMetricAsync(new MetricQueryRequest(
            metricCode, version, legalEntityId, branchId, orgUnitId,
            fromUtc, toUtc, normalizeToCurrency, aggregation), ct))
           .ToActionResult(HttpContext, asOfUtc: DateTimeOffset.UtcNow);
}

[ApiController]
[Route("api/v1/m/integrations")]
public sealed class IntegrationsController(IIntegrationService integrationService) : ControllerBase
{
    [HttpGet("connectors")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationRead)]
    public async Task<IActionResult> ListConnectors(CancellationToken ct)
        => (await integrationService.ListConnectorsAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("connectors")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationManage)]
    public async Task<IActionResult> CreateConnector([FromBody] CreateConnectorRequest request, CancellationToken ct)
        => (await integrationService.CreateConnectorAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpPost("events/ingest")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationManage)]
    public async Task<IActionResult> IngestEvent([FromBody] InboundEventEnvelopeRequest request, CancellationToken ct)
        => (await integrationService.ProcessInboundEventAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("runs")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationRead)]
    public async Task<IActionResult> ListRuns(CancellationToken ct)
        => (await integrationService.ListIngestionRunsAsync(ct)).ToActionResult(HttpContext);

    [HttpGet("dead-letters")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationRead)]
    public async Task<IActionResult> ListDeadLetters(CancellationToken ct)
        => (await integrationService.ListDeadLettersAsync(ct)).ToActionResult(HttpContext);

    [HttpPost("dead-letters/{id:guid}/retry")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationManage)]
    public async Task<IActionResult> RetryDeadLetter(Guid id, CancellationToken ct)
        => (await integrationService.RetryDeadLetterAsync(id, ct)).ToActionResult(HttpContext);

    [HttpPost("reconciliations")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationManage)]
    public async Task<IActionResult> RunReconciliation([FromBody] RunReconciliationRequest request, CancellationToken ct)
        => (await integrationService.ReconcileMetricWindowAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("reconciliations")]
    [RequireNexusMPermission(NexusMPermissions.IntegrationRead)]
    public async Task<IActionResult> ListReconciliations(CancellationToken ct)
        => (await integrationService.ListReconciliationsAsync(ct)).ToActionResult(HttpContext);
}
