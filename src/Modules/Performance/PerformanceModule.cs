using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusM.Modules.Metrics.Application;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Performance;

public sealed class BoardReportPackage
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public string ConsolidationCurrency { get; set; } = "VND";
    public string MetricCodesCsv { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "[]";
    public string Sha256Checksum { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft"; // Draft, Published
    public Guid PreparedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

public sealed class StrategicObjective
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = "2026-Q4";
    public Guid OwnerUserId { get; set; }
    public Guid? COrganizationUnitId { get; set; }
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public ICollection<KeyResultTarget> KeyResults { get; set; } = [];
}

/// <summary>
/// Strictly separates TargetValue (owned here) from ActualValue (resolved live from authoritative m_metrics).
/// </summary>
public sealed class KeyResultTarget
{
    public Guid Id { get; set; }
    public Guid ObjectiveId { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? LinkedMetricCode { get; set; }
    public decimal TargetValue { get; set; }
    public string Unit { get; set; } = "COUNT";
    public string? CurrencyCode { get; set; }
}

public sealed class StrategicInitiative
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ObjectiveId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public decimal BudgetResponsibilityAmount { get; set; }
    public string CurrencyCode { get; set; } = "VND";
    public string Status { get; set; } = "InProgress";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class PerformanceDbContext(DbContextOptions<PerformanceDbContext> options) : DbContext(options)
{
    public DbSet<BoardReportPackage> BoardReports => Set<BoardReportPackage>();
    public DbSet<StrategicObjective> Objectives => Set<StrategicObjective>();
    public DbSet<KeyResultTarget> KeyResults => Set<KeyResultTarget>();
    public DbSet<StrategicInitiative> Initiatives => Set<StrategicInitiative>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_performance");

        modelBuilder.Entity<BoardReportPackage>(e =>
        {
            e.ToTable("board_reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.PeriodStartUtc).HasColumnName("period_start_utc").IsRequired();
            e.Property(x => x.PeriodEndUtc).HasColumnName("period_end_utc").IsRequired();
            e.Property(x => x.ConsolidationCurrency).HasColumnName("consolidation_currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.MetricCodesCsv).HasColumnName("metric_codes_csv").HasMaxLength(1000).IsRequired();
            e.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Sha256Checksum).HasColumnName("sha256_checksum").HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
            e.Property(x => x.PreparedByUserId).HasColumnName("prepared_by_user_id").IsRequired();
            e.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_perf_board_reports_tenant_code");
        });

        modelBuilder.Entity<StrategicObjective>(e =>
        {
            e.ToTable("strategic_objectives");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.PeriodCode).HasColumnName("period_code").HasMaxLength(32).IsRequired();
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            e.Property(x => x.COrganizationUnitId).HasColumnName("c_organization_unit_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasMany(x => x.KeyResults).WithOne().HasForeignKey(k => k.ObjectiveId);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_perf_objectives_tenant_code");
        });

        modelBuilder.Entity<KeyResultTarget>(e =>
        {
            e.ToTable("key_result_targets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ObjectiveId).HasColumnName("objective_id").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.LinkedMetricCode).HasColumnName("linked_metric_code").HasMaxLength(80);
            e.Property(x => x.TargetValue).HasColumnName("target_value").HasPrecision(24, 6).IsRequired();
            e.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(24).IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3);
        });

        modelBuilder.Entity<StrategicInitiative>(e =>
        {
            e.ToTable("strategic_initiatives");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.ObjectiveId).HasColumnName("objective_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            e.Property(x => x.BudgetResponsibilityAmount).HasColumnName("budget_responsibility_amount").HasPrecision(24, 6).IsRequired();
            e.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        });
    }
}

public sealed record CreateBoardReportRequest(
    string Code,
    string Title,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    string ConsolidationCurrency,
    IReadOnlyList<string> MetricCodes);

public sealed record CreateKeyResultInput(string Title, decimal TargetValue, string Unit = "COUNT", string? LinkedMetricCode = null, string? CurrencyCode = null);

public sealed record CreateObjectiveRequest(
    string Code,
    string Title,
    string PeriodCode,
    Guid OwnerUserId,
    Guid? COrganizationUnitId,
    IReadOnlyList<CreateKeyResultInput> KeyResults);

public sealed record CreateInitiativeRequest(
    Guid ObjectiveId,
    string Code,
    string Title,
    Guid OwnerUserId,
    decimal BudgetResponsibilityAmount,
    string CurrencyCode = "VND");

public sealed record EvaluatedKeyResultDto(
    Guid Id,
    string Title,
    string? LinkedMetricCode,
    decimal TargetValue,
    decimal? ActualValue,
    decimal? AttainmentPercent,
    string? FreshnessStatus);

public sealed record EvaluatedObjectiveDto(
    Guid Id,
    string Code,
    string Title,
    string PeriodCode,
    Guid OwnerUserId,
    IReadOnlyList<EvaluatedKeyResultDto> KeyResults);

public interface IPerformanceService
{
    Task<Result<IReadOnlyList<BoardReportPackage>>> ListBoardReportsAsync(CancellationToken ct = default);
    Task<Result<BoardReportPackage>> CreateConsolidatedBoardReportAsync(CreateBoardReportRequest request, CancellationToken ct = default);
    Task<Result<BoardReportPackage>> ApproveAndPublishBoardReportAsync(Guid reportId, CancellationToken ct = default);
    Task<Result<StrategicObjective>> CreateObjectiveAsync(CreateObjectiveRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EvaluatedObjectiveDto>>> EvaluateObjectivesAsync(DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, CancellationToken ct = default);
    Task<Result<StrategicInitiative>> CreateInitiativeAsync(CreateInitiativeRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<StrategicInitiative>>> ListInitiativesAsync(CancellationToken ct = default);
}

public sealed class PerformanceService(
    PerformanceDbContext db,
    IMetricsService metricsService,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter) : IPerformanceService
{
    public async Task<Result<IReadOnlyList<BoardReportPackage>>> ListBoardReportsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.BoardReports.AsNoTracking().Where(b => b.TenantId == tid).OrderByDescending(b => b.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<Result<BoardReportPackage>> CreateConsolidatedBoardReportAsync(CreateBoardReportRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        if (request.MetricCodes is null || request.MetricCodes.Count == 0)
            return Error.Validation("BOARD_REPORT_METRICS_REQUIRED", "At least one metric code is required.");

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        var normCurrency = request.ConsolidationCurrency.Trim().ToUpperInvariant();

        var consolidated = new List<MetricQueryResponseDto>();
        foreach (var mc in request.MetricCodes.Select(m => m.Trim().ToUpperInvariant()).Distinct())
        {
            var res = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                mc, FromUtc: request.PeriodStartUtc, ToUtc: request.PeriodEndUtc, NormalizeToCurrency: normCurrency), ct);
            if (res.IsFailure) return res.Error!;
            consolidated.Add(res.Value);
        }

        var json = JsonSerializer.Serialize(consolidated);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

        var pkg = new BoardReportPackage
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            PeriodStartUtc = request.PeriodStartUtc,
            PeriodEndUtc = request.PeriodEndUtc,
            ConsolidationCurrency = normCurrency,
            MetricCodesCsv = string.Join(",", consolidated.Select(c => c.MetricCode)),
            SnapshotJson = json,
            Sha256Checksum = checksum,
            Status = "Draft",
            PreparedByUserId = requestContext.UserId.Value,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.BoardReports.Add(pkg);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.board.report_drafted", "BoardReportPackage", pkg.Id.ToString("D"),
            AfterState: new { pkg.Code, pkg.ConsolidationCurrency, pkg.Sha256Checksum }), ct);

        return pkg;
    }

    public async Task<Result<BoardReportPackage>> ApproveAndPublishBoardReportAsync(Guid reportId, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        var tid = requestContext.TenantId.Value;
        var pkg = await db.BoardReports.FirstOrDefaultAsync(b => b.Id == reportId && b.TenantId == tid, ct);
        if (pkg is null) return Error.NotFound("BOARD_REPORT_NOT_FOUND", "Board report package was not found.");

        if (pkg.Status == "Published")
            return Error.Conflict("BOARD_REPORT_ALREADY_PUBLISHED", "Published board report snapshots are immutable.");

        if (pkg.PreparedByUserId == requestContext.UserId.Value)
            return Error.Forbidden("SEPARATION_OF_DUTIES_VIOLATION", "Board report preparer cannot self-approve publication.");

        pkg.Status = "Published";
        pkg.ApprovedByUserId = requestContext.UserId.Value;
        pkg.PublishedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.board.report_published", "BoardReportPackage", pkg.Id.ToString("D"),
            AfterState: new { pkg.Code, pkg.ApprovedByUserId, pkg.Sha256Checksum }), ct);

        return pkg;
    }

    public async Task<Result<StrategicObjective>> CreateObjectiveAsync(CreateObjectiveRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();

        var obj = new StrategicObjective
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            PeriodCode = request.PeriodCode.Trim(),
            OwnerUserId = request.OwnerUserId,
            COrganizationUnitId = request.COrganizationUnitId,
            Status = "Active",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        foreach (var kr in request.KeyResults ?? [])
        {
            var item = new KeyResultTarget
            {
                Id = Guid.CreateVersion7(),
                ObjectiveId = obj.Id,
                TenantId = tid,
                Title = kr.Title.Trim(),
                LinkedMetricCode = string.IsNullOrWhiteSpace(kr.LinkedMetricCode) ? null : kr.LinkedMetricCode.Trim().ToUpperInvariant(),
                TargetValue = kr.TargetValue,
                Unit = kr.Unit.Trim().ToUpperInvariant(),
                CurrencyCode = string.IsNullOrWhiteSpace(kr.CurrencyCode) ? null : kr.CurrencyCode.Trim().ToUpperInvariant()
            };
            obj.KeyResults.Add(item);
            db.KeyResults.Add(item);
        }

        db.Objectives.Add(obj);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.performance.objective_created", "StrategicObjective", obj.Id.ToString("D"),
            AfterState: new { obj.Code, obj.PeriodCode, KeyResultCount = obj.KeyResults.Count }), ct);

        return obj;
    }

    public async Task<Result<IReadOnlyList<EvaluatedObjectiveDto>>> EvaluateObjectivesAsync(
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var scope = requestContext.OrgScope;

        var q = db.Objectives.AsNoTracking().Include(o => o.KeyResults).Where(o => o.TenantId == tid);
        if (!scope.HasUnrestrictedTenantScope)
        {
            q = q.Where(o => !o.COrganizationUnitId.HasValue || scope.OrganizationUnitIds.Contains(o.COrganizationUnitId.Value));
        }

        var list = await q.OrderBy(o => o.Code).ToListAsync(ct);
        var evaluated = new List<EvaluatedObjectiveDto>();

        foreach (var obj in list)
        {
            var krs = new List<EvaluatedKeyResultDto>();
            foreach (var kr in obj.KeyResults)
            {
                decimal? actual = null;
                string? freshness = null;

                if (!string.IsNullOrWhiteSpace(kr.LinkedMetricCode))
                {
                    var mRes = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                        kr.LinkedMetricCode, FromUtc: periodStartUtc, ToUtc: periodEndUtc, NormalizeToCurrency: kr.CurrencyCode), ct);
                    if (mRes.IsSuccess)
                    {
                        actual = mRes.Value.AggregatedValue;
                        freshness = mRes.Value.OverallFreshnessStatus;
                    }
                }

                decimal? attainment = (actual.HasValue && kr.TargetValue != 0m)
                    ? decimal.Round((actual.Value / kr.TargetValue) * 100m, 2)
                    : null;

                krs.Add(new EvaluatedKeyResultDto(kr.Id, kr.Title, kr.LinkedMetricCode, kr.TargetValue, actual, attainment, freshness));
            }

            evaluated.Add(new EvaluatedObjectiveDto(obj.Id, obj.Code, obj.Title, obj.PeriodCode, obj.OwnerUserId, krs));
        }

        return evaluated;
    }

    public async Task<Result<StrategicInitiative>> CreateInitiativeAsync(CreateInitiativeRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var init = new StrategicInitiative
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            ObjectiveId = request.ObjectiveId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Title = request.Title.Trim(),
            OwnerUserId = request.OwnerUserId,
            BudgetResponsibilityAmount = request.BudgetResponsibilityAmount,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Status = "InProgress",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Initiatives.Add(init);
        await db.SaveChangesAsync(ct);
        return init;
    }

    public async Task<Result<IReadOnlyList<StrategicInitiative>>> ListInitiativesAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.Initiatives.AsNoTracking().Where(i => i.TenantId == tid).OrderBy(i => i.Code).ToListAsync(ct);
    }
}
