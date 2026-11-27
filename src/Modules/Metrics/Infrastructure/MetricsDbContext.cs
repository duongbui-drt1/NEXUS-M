using Microsoft.EntityFrameworkCore;
using NexusM.Modules.Metrics.Domain;

namespace NexusM.Modules.Metrics.Infrastructure;

public sealed class MetricsDbContext(DbContextOptions<MetricsDbContext> options) : DbContext(options)
{
    public DbSet<MetricDefinition> MetricDefinitions => Set<MetricDefinition>();
    public DbSet<MetricDefinitionVersion> MetricDefinitionVersions => Set<MetricDefinitionVersion>();
    public DbSet<MetricObservation> MetricObservations => Set<MetricObservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_metrics");

        modelBuilder.Entity<MetricDefinition>(e =>
        {
            e.ToTable("metric_definitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.DisplayNameKey).HasColumnName("display_name_key").HasMaxLength(160).IsRequired();
            e.Property(x => x.DescriptionKey).HasColumnName("description_key").HasMaxLength(500).IsRequired();
            e.Property(x => x.CurrentVersion).HasColumnName("current_version").IsConcurrencyToken().IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            e.HasMany(x => x.Versions)
             .WithOne()
             .HasForeignKey(v => v.MetricDefinitionId);

            e.HasIndex(x => new { x.TenantId, x.MetricCode }).IsUnique().HasDatabaseName("ux_m_metric_defs_tenant_code");
        });

        modelBuilder.Entity<MetricDefinitionVersion>(e =>
        {
            e.ToTable("metric_definition_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MetricDefinitionId).HasColumnName("metric_definition_id").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsRequired();
            e.Property(x => x.FormulaExpression).HasColumnName("formula_expression").HasMaxLength(1000).IsRequired();
            e.Property(x => x.Unit).HasColumnName("unit").IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
            e.Property(x => x.TimeBasis).HasColumnName("time_basis").IsRequired();
            e.Property(x => x.AggregationMethod).HasColumnName("aggregation_method").IsRequired();
            e.Property(x => x.AccountingBasis).HasColumnName("accounting_basis").IsRequired();
            e.Property(x => x.SourceApplication).HasColumnName("source_application").HasMaxLength(64).IsRequired();
            e.Property(x => x.FreshnessSlaMinutes).HasColumnName("freshness_sla_minutes").IsRequired();
            e.Property(x => x.AccessClassification).HasColumnName("access_classification").IsRequired();
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc").IsRequired();

            e.HasIndex(x => new { x.TenantId, x.MetricDefinitionId, x.Version }).IsUnique().HasDatabaseName("ux_m_metric_def_ver");
        });

        modelBuilder.Entity<MetricObservation>(e =>
        {
            e.ToTable("metric_observations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.MetricDefinitionId).HasColumnName("metric_definition_id").IsRequired();
            e.Property(x => x.MetricDefinitionVersionId).HasColumnName("metric_definition_version_id").IsRequired();
            e.Property(x => x.LegalEntityId).HasColumnName("legal_entity_id");
            e.Property(x => x.BranchId).HasColumnName("branch_id");
            e.Property(x => x.OrgUnitId).HasColumnName("org_unit_id");
            e.Property(x => x.PeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
            e.Property(x => x.PeriodEndUtc).HasColumnName("period_end_utc").IsRequired();
            e.Property(x => x.ReportingTimezone).HasColumnName("reporting_timezone").HasMaxLength(64).IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
            e.Property(x => x.AccountingBasis).HasColumnName("accounting_basis").IsRequired();
            e.Property(x => x.ValueNumeric).HasColumnName("value_numeric").HasPrecision(24, 6).IsRequired();
            e.Property(x => x.SourceApplication).HasColumnName("source_application").HasMaxLength(64).IsRequired();
            e.Property(x => x.SourceReferenceId).HasColumnName("source_reference_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.SourceDataVersion).HasColumnName("source_data_version").IsRequired();
            e.Property(x => x.ObservedAtUtc).HasColumnName("observed_at_utc").IsRequired();
            e.Property(x => x.IngestedAtUtc).HasColumnName("ingested_at_utc").IsRequired();
            e.Property(x => x.QualityStatus).HasColumnName("quality_status").IsRequired();

            e.HasIndex(x => new { x.TenantId, x.SourceApplication, x.SourceReferenceId, x.MetricDefinitionVersionId, x.SourceDataVersion })
             .IsUnique()
             .HasDatabaseName("ux_m_metric_obs_idempotency");
            e.HasIndex(x => new { x.TenantId, x.MetricDefinitionId, x.PeriodStartUtc })
             .HasDatabaseName("ix_m_metric_obs_lookup");
        });
    }
}
