using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusM.Modules.Metrics.Application;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Bi;

public sealed class SavedReport
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string MetricCodesCsv { get; set; } = string.Empty;
    public string? NormalizeToCurrency { get; set; }
    public string? ScheduleCron { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ReportRunSnapshot
{
    public Guid Id { get; set; }
    public Guid ReportId { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public string SnapshotJson { get; set; } = "[]";
    public string Sha256Checksum { get; set; } = string.Empty;
    public int MetricSeriesCount { get; set; }
    public Guid ExecutedByUserId { get; set; }
    public DateTimeOffset GeneratedAtUtc { get; set; }
}

public sealed class ReportExportRecord
{
    public Guid Id { get; set; }
    public Guid SnapshotId { get; set; }
    public Guid TenantId { get; set; }
    public string Format { get; set; } = "CSV";
    public int RowCount { get; set; }
    public Guid ExportedByUserId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset ExportedAtUtc { get; set; }
}

public sealed class BiDbContext(DbContextOptions<BiDbContext> options) : DbContext(options)
{
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<ReportRunSnapshot> Snapshots => Set<ReportRunSnapshot>();
    public DbSet<ReportExportRecord> Exports => Set<ReportExportRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_bi");

        modelBuilder.Entity<SavedReport>(e =>
        {
            e.ToTable("saved_reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            e.Property(x => x.MetricCodesCsv).HasColumnName("metric_codes_csv").HasMaxLength(1000).IsRequired();
            e.Property(x => x.NormalizeToCurrency).HasColumnName("normalize_to_currency").HasMaxLength(3);
            e.Property(x => x.ScheduleCron).HasColumnName("schedule_cron").HasMaxLength(64);
            e.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_bi_reports_tenant_code");
        });

        modelBuilder.Entity<ReportRunSnapshot>(e =>
        {
            e.ToTable("report_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ReportId).HasColumnName("report_id").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.PeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
            e.Property(x => x.PeriodEndUtc).HasColumnName("period_end_utc").IsRequired();
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Sha256Checksum).HasColumnName("sha256_checksum").HasMaxLength(64).IsRequired();
            e.Property(x => x.MetricSeriesCount).HasColumnName("metric_series_count").IsRequired();
            e.Property(x => x.ExecutedByUserId).HasColumnName("executed_by_user_id").IsRequired();
            e.Property(x => x.GeneratedAtUtc).HasColumnName("generated_at_utc").IsRequired();
        });

        modelBuilder.Entity<ReportExportRecord>(e =>
        {
            e.ToTable("report_exports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SnapshotId).HasColumnName("snapshot_id").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Format).HasColumnName("format").HasMaxLength(16).IsRequired();
            e.Property(x => x.RowCount).HasColumnName("row_count").IsRequired();
            e.Property(x => x.ExportedByUserId).HasColumnName("exported_by_user_id").IsRequired();
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.ExportedAtUtc).HasColumnName("exported_at_utc").IsRequired();
        });
    }
}

public sealed record CreateSavedReportRequest(
    string Code,
    string Title,
    IReadOnlyList<string> MetricCodes,
    string? NormalizeToCurrency = null,
    string? ScheduleCron = null);

public sealed record RunReportRequest(
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    Guid? BranchId = null,
    Guid? LegalEntityId = null);

public sealed record ExportReportResultDto(
    Guid ExportId,
    Guid SnapshotId,
    string Format,
    string FileName,
    string Content,
    string Sha256Checksum);

public interface IBiService
{
    Task<Result<IReadOnlyList<SavedReport>>> ListReportsAsync(CancellationToken ct = default);
    Task<Result<SavedReport>> CreateReportAsync(CreateSavedReportRequest request, CancellationToken ct = default);
    Task<Result<ReportRunSnapshot>> RunReportAsync(Guid reportId, RunReportRequest request, CancellationToken ct = default);
    Task<Result<ExportReportResultDto>> ExportSnapshotAsync(Guid snapshotId, string format = "CSV", CancellationToken ct = default);
}

public sealed class BiService(
    BiDbContext db,
    IMetricsService metricsService,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter) : IBiService
{
    public async Task<Result<IReadOnlyList<SavedReport>>> ListReportsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.SavedReports.AsNoTracking().Where(r => r.TenantId == tid).OrderBy(r => r.Title).ToListAsync(ct);
    }

    public async Task<Result<SavedReport>> CreateReportAsync(CreateSavedReportRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title) || request.MetricCodes is null || request.MetricCodes.Count == 0)
            return Error.Validation("REPORT_INVALID", "Code, Title, and at least one MetricCode are required.");

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        if (await db.SavedReports.AnyAsync(r => r.TenantId == tid && r.Code == normCode, ct))
            return Error.Conflict("REPORT_CODE_EXISTS", $"Report '{normCode}' already exists.");

        var report = new SavedReport
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            MetricCodesCsv = string.Join(",", request.MetricCodes.Select(m => m.Trim().ToUpperInvariant()).Distinct()),
            NormalizeToCurrency = string.IsNullOrWhiteSpace(request.NormalizeToCurrency) ? null : request.NormalizeToCurrency.Trim().ToUpperInvariant(),
            ScheduleCron = string.IsNullOrWhiteSpace(request.ScheduleCron) ? null : request.ScheduleCron.Trim(),
            CreatedByUserId = requestContext.UserId.Value,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.SavedReports.Add(report);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.bi.report_created", "SavedReport", report.Id.ToString("D"),
            AfterState: new { report.Code, report.MetricCodesCsv, report.NormalizeToCurrency }), ct);

        return report;
    }

    public async Task<Result<ReportRunSnapshot>> RunReportAsync(Guid reportId, RunReportRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        var tid = requestContext.TenantId.Value;
        var report = await db.SavedReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId && r.TenantId == tid, ct);
        if (report is null) return Error.NotFound("REPORT_NOT_FOUND", "Saved report was not found.");

        var metricCodes = report.MetricCodesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var series = new List<MetricQueryResponseDto>();

        foreach (var code in metricCodes)
        {
            var res = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                code, null, request.LegalEntityId, request.BranchId, null,
                request.PeriodStartUtc, request.PeriodEndUtc, report.NormalizeToCurrency), ct);

            if (res.IsFailure) return res.Error!;
            series.Add(res.Value);
        }

        var json = JsonSerializer.Serialize(series);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        var snapshot = new ReportRunSnapshot
        {
            Id = Guid.CreateVersion7(),
            ReportId = report.Id,
            TenantId = tid,
            PeriodStartUtc = request.PeriodStartUtc,
            PeriodEndUtc = request.PeriodEndUtc,
            SnapshotJson = json,
            Sha256Checksum = checksum,
            MetricSeriesCount = series.Count,
            ExecutedByUserId = requestContext.UserId.Value,
            GeneratedAtUtc = DateTimeOffset.UtcNow
        };

        db.Snapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.bi.report_snapshot_generated", "ReportRunSnapshot", snapshot.Id.ToString("D"),
            AfterState: new { snapshot.ReportId, snapshot.Sha256Checksum, snapshot.MetricSeriesCount }), ct);

        return snapshot;
    }

    public async Task<Result<ExportReportResultDto>> ExportSnapshotAsync(Guid snapshotId, string format = "CSV", CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        var tid = requestContext.TenantId.Value;
        var snap = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == snapshotId && s.TenantId == tid, ct);
        if (snap is null) return Error.NotFound("SNAPSHOT_NOT_FOUND", "Report snapshot was not found.");

        var series = JsonSerializer.Deserialize<List<MetricQueryResponseDto>>(snap.SnapshotJson) ?? [];
        var normFormat = string.IsNullOrWhiteSpace(format) ? "CSV" : format.Trim().ToUpperInvariant();

        string content;
        var rowCount = 0;

        if (normFormat == "JSON")
        {
            content = snap.SnapshotJson;
            rowCount = series.Sum(s => s.Points.Count);
        }
        else
        {
            normFormat = "CSV";
            var sb = new StringBuilder();
            sb.AppendLine("MetricCode,DefinitionVersion,Formula,Unit,Currency,AccountingBasis,AggregatedValue,FreshnessStatus,QualityStatus,SnapshotChecksum");
            foreach (var s in series)
            {
                sb.AppendLine($"{s.MetricCode},{s.DefinitionVersion},\"{s.FormulaExpression.Replace("\"", "\"\"")}\",{s.Unit},{s.EffectiveCurrencyCode ?? ""},{s.AccountingBasis},{s.AggregatedValue?.ToString() ?? ""},{s.OverallFreshnessStatus},{s.OverallQualityStatus},{snap.Sha256Checksum}");
                rowCount++;
            }
            content = sb.ToString();
        }

        var export = new ReportExportRecord
        {
            Id = Guid.CreateVersion7(),
            SnapshotId = snap.Id,
            TenantId = tid,
            Format = normFormat,
            RowCount = rowCount,
            ExportedByUserId = requestContext.UserId.Value,
            CorrelationId = requestContext.CorrelationId,
            ExportedAtUtc = DateTimeOffset.UtcNow
        };
        db.Exports.Add(export);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.bi.report_exported", "ReportExportRecord", export.Id.ToString("D"),
            AfterState: new { export.SnapshotId, export.Format, export.RowCount, snap.Sha256Checksum }), ct);

        return new ExportReportResultDto(
            export.Id,
            snap.Id,
            normFormat,
            $"nexus-m-report-{snap.Id:N}.{normFormat.ToLowerInvariant()}",
            content,
            snap.Sha256Checksum);
    }
}
