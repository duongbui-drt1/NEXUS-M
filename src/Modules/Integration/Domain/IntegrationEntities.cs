using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Integration.Domain;

public sealed class DataConnector
{
    private DataConnector() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string SourceApplication { get; private set; } = string.Empty;
    public int SchemaVersion { get; private set; } = 1;
    public int FreshnessSlaMinutes { get; private set; } = 60;
    public string? SecretReference { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public DateTimeOffset? LastSucceededAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private static readonly string[] AllowedSecretSchemes = ["vault://", "kms://", "env://"];

    public static Result<DataConnector> Create(
        Guid tenantId,
        string code,
        string name,
        string sourceApplication,
        int schemaVersion,
        int freshnessSlaMinutes,
        string? secretReference)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 60)
        {
            return Error.Validation("CONNECTOR_INVALID_CODE", "Connector code is required (max 60 chars).");
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(sourceApplication))
        {
            return Error.Validation("CONNECTOR_METADATA_REQUIRED", "Connector name and sourceApplication are required.");
        }

        if (schemaVersion <= 0 || freshnessSlaMinutes <= 0)
        {
            return Error.Validation("CONNECTOR_INVALID_LIMITS", "SchemaVersion and FreshnessSlaMinutes must be > 0.");
        }

        string? normalizedRef = null;
        if (!string.IsNullOrWhiteSpace(secretReference))
        {
            normalizedRef = secretReference.Trim();
            if (!AllowedSecretSchemes.Any(prefix => normalizedRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                return Error.Unprocessable(
                    "CONNECTOR_SECRET_PLAINTEXT_FORBIDDEN",
                    "Plaintext credentials are forbidden. Use vault://, kms://, or env:// secret references.");
            }
        }

        return new DataConnector
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            SourceApplication = sourceApplication.Trim().ToUpperInvariant(),
            SchemaVersion = schemaVersion,
            FreshnessSlaMinutes = freshnessSlaMinutes,
            SecretReference = normalizedRef,
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void MarkSucceeded(DateTimeOffset completedAtUtc) => LastSucceededAtUtc = completedAtUtc;
}

public sealed class IngestionRun
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ConnectorId { get; set; }
    public string SourceApplication { get; set; } = string.Empty;
    public string Status { get; set; } = "Succeeded";
    public int RecordsReceived { get; set; }
    public int RecordsAccepted { get; set; }
    public int RecordsDeduplicated { get; set; }
    public string? ErrorSummary { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
}

public sealed class InboxEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; set; }
    public string ConsumerName { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string Producer { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset ProcessedAtUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

public sealed class OutboxEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string PayloadJson { get; set; } = "{}";
    public string CorrelationId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public sealed class DeadLetterItem
{
    public const int MaxManualRetries = 3;

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? OriginalEventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Producer { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string FailureReason { get; set; } = string.Empty;
    public int ManualRetryCount { get; set; }
    public string Status { get; set; } = "DeadLettered";
    public DateTimeOffset FailedAtUtc { get; set; }
    public DateTimeOffset? LastRetriedAtUtc { get; set; }
}

public sealed class ReconciliationRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string MetricCode { get; set; } = string.Empty;
    public string SourceApplication { get; set; } = string.Empty;
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public int ExpectedCount { get; set; }
    public int ActualCount { get; set; }
    public decimal ExpectedSum { get; set; }
    public decimal ActualSum { get; set; }
    public string Status { get; set; } = "MATCHED";
    public DateTimeOffset CheckedAtUtc { get; set; }
}
