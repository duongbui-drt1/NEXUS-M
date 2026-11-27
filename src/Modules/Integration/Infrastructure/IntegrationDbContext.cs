using Microsoft.EntityFrameworkCore;
using NexusM.Modules.Integration.Domain;

namespace NexusM.Modules.Integration.Infrastructure;

public sealed class IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : DbContext(options)
{
    public DbSet<DataConnector> Connectors => Set<DataConnector>();
    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();
    public DbSet<InboxEvent> InboxEvents => Set<InboxEvent>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<DeadLetterItem> DeadLetters => Set<DeadLetterItem>();
    public DbSet<ReconciliationRecord> Reconciliations => Set<ReconciliationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_integration");

        modelBuilder.Entity<DataConnector>(e =>
        {
            e.ToTable("connectors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            e.Property(x => x.SourceApplication).HasColumnName("source_application").HasMaxLength(64).IsRequired();
            e.Property(x => x.SchemaVersion).HasColumnName("schema_version").IsRequired();
            e.Property(x => x.FreshnessSlaMinutes).HasColumnName("freshness_sla_minutes").IsRequired();
            e.Property(x => x.SecretReference).HasColumnName("secret_reference").HasMaxLength(256);
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
            e.Property(x => x.LastSucceededAtUtc).HasColumnName("last_succeeded_at_utc");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_connectors_tenant_code");
        });

        modelBuilder.Entity<IngestionRun>(e =>
        {
            e.ToTable("ingestion_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.ConnectorId).HasColumnName("connector_id");
            e.Property(x => x.SourceApplication).HasColumnName("source_application").HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.RecordsReceived).HasColumnName("records_received").IsRequired();
            e.Property(x => x.RecordsAccepted).HasColumnName("records_accepted").IsRequired();
            e.Property(x => x.RecordsDeduplicated).HasColumnName("records_deduplicated").IsRequired();
            e.Property(x => x.ErrorSummary).HasColumnName("error_summary").HasMaxLength(500);
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
            e.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc").IsRequired();
        });

        modelBuilder.Entity<InboxEvent>(e =>
        {
            e.ToTable("inbox_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.EventId).HasColumnName("event_id").IsRequired();
            e.Property(x => x.ConsumerName).HasColumnName("consumer_name").HasMaxLength(80).IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
            e.Property(x => x.SchemaVersion).HasColumnName("schema_version").IsRequired();
            e.Property(x => x.Producer).HasColumnName("producer").HasMaxLength(64).IsRequired();
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
            e.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc").IsRequired();
            e.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.EventId, x.ConsumerName }).IsUnique().HasDatabaseName("ux_m_inbox_event_consumer");
        });

        modelBuilder.Entity<OutboxEvent>(e =>
        {
            e.ToTable("outbox_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
            e.Property(x => x.SchemaVersion).HasColumnName("schema_version").IsRequired();
            e.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        });

        modelBuilder.Entity<DeadLetterItem>(e =>
        {
            e.ToTable("dead_letters");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.OriginalEventId).HasColumnName("original_event_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(120).IsRequired();
            e.Property(x => x.Producer).HasColumnName("producer").HasMaxLength(64).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500).IsRequired();
            e.Property(x => x.ManualRetryCount).HasColumnName("manual_retry_count").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.FailedAtUtc).HasColumnName("failed_at_utc").IsRequired();
            e.Property(x => x.LastRetriedAtUtc).HasColumnName("last_retried_at_utc");
        });

        modelBuilder.Entity<ReconciliationRecord>(e =>
        {
            e.ToTable("reconciliations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.SourceApplication).HasColumnName("source_application").HasMaxLength(64).IsRequired();
            e.Property(x => x.PeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
            e.Property(x => x.PeriodEndUtc).HasColumnName("period_end_utc").IsRequired();
            e.Property(x => x.ExpectedCount).HasColumnName("expected_count").IsRequired();
            e.Property(x => x.ActualCount).HasColumnName("actual_count").IsRequired();
            e.Property(x => x.ExpectedSum).HasColumnName("expected_sum").HasPrecision(24, 6).IsRequired();
            e.Property(x => x.ActualSum).HasColumnName("actual_sum").HasPrecision(24, 6).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.CheckedAtUtc).HasColumnName("checked_at_utc").IsRequired();
        });
    }
}
