using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusM.Modules.Integration.Domain;
using NexusM.Modules.Integration.Infrastructure;
using NexusM.Modules.Metrics.Application;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Integration.Application;

public sealed record DataConnectorDto(
    Guid Id,
    string Code,
    string Name,
    string SourceApplication,
    int SchemaVersion,
    int FreshnessSlaMinutes,
    bool HasSecretReference,
    bool IsEnabled,
    DateTimeOffset? LastSucceededAtUtc,
    DateTimeOffset CreatedAtUtc);

public sealed record CreateConnectorRequest(
    string Code,
    string Name,
    string SourceApplication,
    int SchemaVersion = 1,
    int FreshnessSlaMinutes = 60,
    string? SecretReference = null);

public sealed record InboundEventEnvelopeRequest(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    string Producer,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    IReadOnlyList<IngestObservationItem>? Observations);

public sealed record InboundEventProcessResultDto(
    Guid EventId,
    bool WasDuplicate,
    string Status,
    int InsertedObservations,
    Guid? DeadLetterId = null);

public sealed record RunReconciliationRequest(
    string MetricCode,
    string SourceApplication,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    int ExpectedCount,
    decimal ExpectedSum);

public interface IIntegrationService
{
    Task<Result<IReadOnlyList<DataConnectorDto>>> ListConnectorsAsync(CancellationToken ct = default);
    Task<Result<DataConnectorDto>> CreateConnectorAsync(CreateConnectorRequest request, CancellationToken ct = default);
    Task<Result<InboundEventProcessResultDto>> ProcessInboundEventAsync(InboundEventEnvelopeRequest envelope, CancellationToken ct = default);
    Task<Result<IReadOnlyList<IngestionRun>>> ListIngestionRunsAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<DeadLetterItem>>> ListDeadLettersAsync(CancellationToken ct = default);
    Task<Result<DeadLetterItem>> RetryDeadLetterAsync(Guid deadLetterId, CancellationToken ct = default);
    Task<Result<ReconciliationRecord>> ReconcileMetricWindowAsync(RunReconciliationRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ReconciliationRecord>>> ListReconciliationsAsync(CancellationToken ct = default);
}

public sealed class IntegrationService(
    IntegrationDbContext db,
    IMetricsService metricsService,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter) : IIntegrationService
{
    private const string DefaultConsumerName = "nexus-m.m10.kpi-ingestor";

    private static DataConnectorDto Map(DataConnector c) => new(
        c.Id, c.Code, c.Name, c.SourceApplication, c.SchemaVersion,
        c.FreshnessSlaMinutes, !string.IsNullOrWhiteSpace(c.SecretReference),
        c.IsEnabled, c.LastSucceededAtUtc, c.CreatedAtUtc);

    public async Task<Result<IReadOnlyList<DataConnectorDto>>> ListConnectorsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var list = await db.Connectors.AsNoTracking()
            .Where(c => c.TenantId == tid)
            .OrderBy(c => c.Code)
            .ToListAsync(ct);

        return list.Select(Map).ToList();
    }

    public async Task<Result<DataConnectorDto>> CreateConnectorAsync(CreateConnectorRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await db.Connectors.AnyAsync(c => c.TenantId == tid && c.Code == normCode, ct))
        {
            return Error.Conflict("CONNECTOR_CODE_EXISTS", $"Connector '{normCode}' already exists.");
        }

        var created = DataConnector.Create(
            tid, normCode, request.Name, request.SourceApplication,
            request.SchemaVersion, request.FreshnessSlaMinutes, request.SecretReference);

        if (created.IsFailure)
        {
            return created.Error!;
        }

        db.Connectors.Add(created.Value);
        await db.SaveChangesAsync(ct);

        var dto = Map(created.Value);
        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.integration.connector_created",
            "DataConnector",
            created.Value.Id.ToString("D"),
            AfterState: dto), ct);

        return dto;
    }

    public async Task<Result<InboundEventProcessResultDto>> ProcessInboundEventAsync(
        InboundEventEnvelopeRequest envelope,
        CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        if (envelope.EventId == Guid.Empty || string.IsNullOrWhiteSpace(envelope.EventType) || string.IsNullOrWhiteSpace(envelope.Producer))
        {
            return Error.Validation("EVENT_ENVELOPE_INVALID", "EventId, EventType, and Producer are required.");
        }

        var tid = requestContext.TenantId.Value;
        var startedAt = DateTimeOffset.UtcNow;

        // 1. Idempotency check on UNIQUE (tenant_id, event_id, consumer_name) (ADR-005)
        var alreadyProcessed = await db.InboxEvents.AnyAsync(
            e => e.TenantId == tid && e.EventId == envelope.EventId && e.ConsumerName == DefaultConsumerName, ct);

        if (alreadyProcessed)
        {
            return new InboundEventProcessResultDto(envelope.EventId, WasDuplicate: true, "Deduplicated", 0);
        }

        var payloadJson = JsonSerializer.Serialize(envelope.Observations ?? []);
        var insertedObs = 0;

        if (envelope.Observations is { Count: > 0 })
        {
            var ingestRes = await metricsService.IngestObservationsAsync(new IngestObservationsBatchRequest(envelope.Observations), ct);
            if (ingestRes.IsFailure)
            {
                // Capture into Dead-Letter Queue for inspection & bounded replay (ADR-005)
                var dlq = new DeadLetterItem
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tid,
                    OriginalEventId = envelope.EventId,
                    EventType = envelope.EventType.Trim(),
                    Producer = envelope.Producer.Trim(),
                    PayloadJson = payloadJson,
                    FailureReason = $"{ingestRes.Error!.Code}: {ingestRes.Error.Message}",
                    ManualRetryCount = 0,
                    Status = "DeadLettered",
                    FailedAtUtc = DateTimeOffset.UtcNow
                };
                db.DeadLetters.Add(dlq);
                db.IngestionRuns.Add(new IngestionRun
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tid,
                    SourceApplication = envelope.Producer.Trim().ToUpperInvariant(),
                    Status = "DeadLettered",
                    RecordsReceived = envelope.Observations.Count,
                    RecordsAccepted = 0,
                    RecordsDeduplicated = 0,
                    ErrorSummary = dlq.FailureReason,
                    CorrelationId = envelope.CorrelationId ?? requestContext.CorrelationId,
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(ct);

                return new InboundEventProcessResultDto(envelope.EventId, false, "DeadLettered", 0, dlq.Id);
            }

            insertedObs = ingestRes.Value.InsertedCount;
        }

        db.InboxEvents.Add(new InboxEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            EventId = envelope.EventId,
            ConsumerName = DefaultConsumerName,
            EventType = envelope.EventType.Trim(),
            SchemaVersion = Math.Max(1, envelope.SchemaVersion),
            Producer = envelope.Producer.Trim(),
            CorrelationId = envelope.CorrelationId ?? requestContext.CorrelationId,
            OccurredAtUtc = envelope.OccurredAt,
            ProcessedAtUtc = DateTimeOffset.UtcNow,
            PayloadJson = payloadJson
        });

        db.IngestionRuns.Add(new IngestionRun
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            SourceApplication = envelope.Producer.Trim().ToUpperInvariant(),
            Status = "Succeeded",
            RecordsReceived = envelope.Observations?.Count ?? 0,
            RecordsAccepted = insertedObs,
            RecordsDeduplicated = (envelope.Observations?.Count ?? 0) - insertedObs,
            CorrelationId = envelope.CorrelationId ?? requestContext.CorrelationId,
            StartedAtUtc = startedAt,
            CompletedAtUtc = DateTimeOffset.UtcNow
        });

        var matchingConnector = await db.Connectors
            .FirstOrDefaultAsync(c => c.TenantId == tid && c.SourceApplication == envelope.Producer.Trim().ToUpperInvariant(), ct);
        matchingConnector?.MarkSucceeded(DateTimeOffset.UtcNow);

        await db.SaveChangesAsync(ct);

        return new InboundEventProcessResultDto(envelope.EventId, false, "Processed", insertedObs);
    }

    public async Task<Result<IReadOnlyList<IngestionRun>>> ListIngestionRunsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        return await db.IngestionRuns.AsNoTracking()
            .Where(r => r.TenantId == tid)
            .OrderByDescending(r => r.CompletedAtUtc)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<Result<IReadOnlyList<DeadLetterItem>>> ListDeadLettersAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        return await db.DeadLetters.AsNoTracking()
            .Where(d => d.TenantId == tid)
            .OrderByDescending(d => d.FailedAtUtc)
            .Take(50)
            .ToListAsync(ct);
    }

    public async Task<Result<DeadLetterItem>> RetryDeadLetterAsync(Guid deadLetterId, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var item = await db.DeadLetters.FirstOrDefaultAsync(d => d.Id == deadLetterId && d.TenantId == tid, ct);
        if (item is null)
        {
            return Error.NotFound("DEAD_LETTER_NOT_FOUND", "Dead-letter item was not found.");
        }

        if (item.ManualRetryCount >= DeadLetterItem.MaxManualRetries)
        {
            return Error.Conflict(
                "DEAD_LETTER_RETRY_CAP_REACHED",
                $"Maximum of {DeadLetterItem.MaxManualRetries} manual retries reached; resolve root cause first.");
        }

        item.ManualRetryCount++;
        item.LastRetriedAtUtc = DateTimeOffset.UtcNow;

        var observations = JsonSerializer.Deserialize<List<IngestObservationItem>>(item.PayloadJson) ?? [];
        if (observations.Count > 0)
        {
            var res = await metricsService.IngestObservationsAsync(new IngestObservationsBatchRequest(observations), ct);
            item.Status = res.IsSuccess ? "Replayed" : "DeadLettered";
            if (res.IsFailure)
            {
                item.FailureReason = $"{res.Error!.Code}: {res.Error.Message}";
            }
        }
        else
        {
            item.Status = "Replayed";
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.integration.dead_letter_retried",
            "DeadLetterItem",
            item.Id.ToString("D"),
            AfterState: new { item.Status, item.ManualRetryCount }), ct);

        return item;
    }

    public async Task<Result<ReconciliationRecord>> ReconcileMetricWindowAsync(RunReconciliationRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var normCode = request.MetricCode.Trim().ToUpperInvariant();
        var normSource = request.SourceApplication.Trim().ToUpperInvariant();

        var summaryRes = await metricsService.GetWindowReconciliationSummaryAsync(
            normCode, normSource, request.PeriodStartUtc, request.PeriodEndUtc, ct);

        if (summaryRes.IsFailure)
        {
            return summaryRes.Error!;
        }

        var actualCount = summaryRes.Value.Count;
        var actualSum = summaryRes.Value.Sum;
        var matched = actualCount == request.ExpectedCount && Math.Abs(actualSum - request.ExpectedSum) < 0.0001m;

        var record = new ReconciliationRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            MetricCode = normCode,
            SourceApplication = normSource,
            PeriodStartUtc = request.PeriodStartUtc,
            PeriodEndUtc = request.PeriodEndUtc,
            ExpectedCount = request.ExpectedCount,
            ActualCount = actualCount,
            ExpectedSum = request.ExpectedSum,
            ActualSum = actualSum,
            Status = matched ? "MATCHED" : "DISCREPANCY",
            CheckedAtUtc = DateTimeOffset.UtcNow
        };

        db.Reconciliations.Add(record);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.integration.reconciliation_executed",
            "ReconciliationRecord",
            record.Id.ToString("D"),
            AfterState: new { record.MetricCode, record.SourceApplication, record.Status, record.ExpectedCount, record.ActualCount, record.ExpectedSum, record.ActualSum }), ct);

        return record;
    }

    public async Task<Result<IReadOnlyList<ReconciliationRecord>>> ListReconciliationsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        return await db.Reconciliations.AsNoTracking()
            .Where(r => r.TenantId == tid)
            .OrderByDescending(r => r.CheckedAtUtc)
            .Take(50)
            .ToListAsync(ct);
    }
}
