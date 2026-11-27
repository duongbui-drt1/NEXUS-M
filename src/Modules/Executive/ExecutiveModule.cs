using Microsoft.EntityFrameworkCore;
using NexusM.BuildingBlocks.Realtime;
using NexusM.Modules.Metrics.Application;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Executive;

public sealed class ExecutiveDashboard
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AudienceRole { get; set; } = "CEO"; // CEO, Owner, Board, CFO, COO, DepartmentManager
    public bool IsDefault { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public ICollection<DashboardWidget> Widgets { get; set; } = [];
}

public sealed class DashboardWidget
{
    public Guid Id { get; set; }
    public Guid DashboardId { get; set; }
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MetricCode { get; set; } = string.Empty;
    public string? NormalizeToCurrency { get; set; }
    public bool ComparePreviousPeriod { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class ManagementAlertRule
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MetricCode { get; set; } = string.Empty;
    public string ConditionOperator { get; set; } = "LessThan"; // LessThan, GreaterThan, StaleOnly
    public decimal? ThresholdValue { get; set; }
    public string? NormalizeToCurrency { get; set; }
    public string Severity { get; set; } = "High";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ManagementAlert
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public string MetricCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "High";
    public string Summary { get; set; } = string.Empty;
    public decimal? ObservedValue { get; set; }
    public string FreshnessStatus { get; set; } = "Fresh";
    public string Status { get; set; } = "Open"; // Open, Acknowledged
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTimeOffset TriggeredAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
}

public sealed class ExecutiveDbContext(DbContextOptions<ExecutiveDbContext> options) : DbContext(options)
{
    public DbSet<ExecutiveDashboard> Dashboards => Set<ExecutiveDashboard>();
    public DbSet<DashboardWidget> Widgets => Set<DashboardWidget>();
    public DbSet<ManagementAlertRule> AlertRules => Set<ManagementAlertRule>();
    public DbSet<ManagementAlert> Alerts => Set<ManagementAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_executive");

        modelBuilder.Entity<ExecutiveDashboard>(e =>
        {
            e.ToTable("dashboards");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            e.Property(x => x.AudienceRole).HasColumnName("audience_role").HasMaxLength(40).IsRequired();
            e.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            e.HasMany(x => x.Widgets).WithOne().HasForeignKey(w => w.DashboardId);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_exec_dashboards_tenant_code");
        });

        modelBuilder.Entity<DashboardWidget>(e =>
        {
            e.ToTable("dashboard_widgets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.DashboardId).HasColumnName("dashboard_id").IsRequired();
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            e.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.NormalizeToCurrency).HasColumnName("normalize_to_currency").HasMaxLength(3);
            e.Property(x => x.ComparePreviousPeriod).HasColumnName("compare_previous_period").IsRequired();
            e.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        });

        modelBuilder.Entity<ManagementAlertRule>(e =>
        {
            e.ToTable("alert_rules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            e.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.ConditionOperator).HasColumnName("condition_operator").HasMaxLength(24).IsRequired();
            e.Property(x => x.ThresholdValue).HasColumnName("threshold_value").HasPrecision(24, 6);
            e.Property(x => x.NormalizeToCurrency).HasColumnName("normalize_to_currency").HasMaxLength(3);
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(24).IsRequired();
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        });

        modelBuilder.Entity<ManagementAlert>(e =>
        {
            e.ToTable("alerts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.RuleId).HasColumnName("rule_id").IsRequired();
            e.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(24).IsRequired();
            e.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
            e.Property(x => x.ObservedValue).HasColumnName("observed_value").HasPrecision(24, 6);
            e.Property(x => x.FreshnessStatus).HasColumnName("freshness_status").HasMaxLength(24).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
            e.Property(x => x.AcknowledgedByUserId).HasColumnName("acknowledged_by_user_id");
            e.Property(x => x.TriggeredAtUtc).HasColumnName("triggered_at_utc").IsRequired();
            e.Property(x => x.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc");
        });
    }
}

public sealed record CreateWidgetInput(
    string Title,
    string MetricCode,
    string? NormalizeToCurrency = null,
    bool ComparePreviousPeriod = true,
    int SortOrder = 0);

public sealed record CreateDashboardRequest(
    string Code,
    string Title,
    string AudienceRole,
    bool IsDefault,
    IReadOnlyList<CreateWidgetInput> Widgets);

public sealed record RenderedWidgetDto(
    Guid WidgetId,
    string Title,
    string MetricCode,
    int DefinitionVersion,
    string FormulaExpression,
    string Unit,
    string? EffectiveCurrencyCode,
    decimal? CurrentValue,
    decimal? PreviousPeriodValue,
    decimal? DeltaPercent,
    string FreshnessStatus,
    string QualityStatus,
    DateTimeOffset? LatestObservedAtUtc);

public sealed record RenderedDashboardDto(
    Guid DashboardId,
    string Code,
    string Title,
    string AudienceRole,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    IReadOnlyList<RenderedWidgetDto> Widgets,
    IReadOnlyList<ManagementAlert> OpenAlerts);

public sealed record CreateAlertRuleRequest(
    string Name,
    string MetricCode,
    string ConditionOperator,
    decimal? ThresholdValue = null,
    string? NormalizeToCurrency = null,
    string Severity = "High");

public interface IExecutiveService
{
    Task<Result<IReadOnlyList<ExecutiveDashboard>>> ListDashboardsAsync(string? audienceRole = null, CancellationToken ct = default);
    Task<Result<ExecutiveDashboard>> CreateDashboardAsync(CreateDashboardRequest request, CancellationToken ct = default);
    Task<Result<RenderedDashboardDto>> RenderDashboardAsync(
        Guid dashboardId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        Guid? branchId = null,
        Guid? legalEntityId = null,
        CancellationToken ct = default);
    Task<Result<ManagementAlertRule>> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ManagementAlert>>> EvaluateAlertsAsync(DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ManagementAlert>>> ListAlertsAsync(CancellationToken ct = default);
    Task<Result<ManagementAlert>> AcknowledgeAlertAsync(Guid alertId, CancellationToken ct = default);
}

public sealed class ExecutiveService(
    ExecutiveDbContext db,
    IMetricsService metricsService,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter,
    IRealtimeBroadcaster broadcaster) : IExecutiveService
{
    public async Task<Result<IReadOnlyList<ExecutiveDashboard>>> ListDashboardsAsync(string? audienceRole = null, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;

        var q = db.Dashboards.AsNoTracking().Include(d => d.Widgets).Where(d => d.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(audienceRole))
        {
            var role = audienceRole.Trim();
            q = q.Where(d => d.AudienceRole == role);
        }

        return await q.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Title).ToListAsync(ct);
    }

    public async Task<Result<ExecutiveDashboard>> CreateDashboardAsync(CreateDashboardRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Title))
        {
            return Error.Validation("DASHBOARD_INVALID", "Code and Title are required.");
        }

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        if (await db.Dashboards.AnyAsync(d => d.TenantId == tid && d.Code == normCode, ct))
        {
            return Error.Conflict("DASHBOARD_CODE_EXISTS", $"Dashboard '{normCode}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var dashboard = new ExecutiveDashboard
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            AudienceRole = string.IsNullOrWhiteSpace(request.AudienceRole) ? "CEO" : request.AudienceRole.Trim(),
            IsDefault = request.IsDefault,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var w in request.Widgets ?? [])
        {
            var widget = new DashboardWidget
            {
                Id = Guid.CreateVersion7(),
                DashboardId = dashboard.Id,
                TenantId = tid,
                Title = w.Title.Trim(),
                MetricCode = w.MetricCode.Trim().ToUpperInvariant(),
                NormalizeToCurrency = string.IsNullOrWhiteSpace(w.NormalizeToCurrency) ? null : w.NormalizeToCurrency.Trim().ToUpperInvariant(),
                ComparePreviousPeriod = w.ComparePreviousPeriod,
                SortOrder = w.SortOrder
            };
            dashboard.Widgets.Add(widget);
            db.Widgets.Add(widget);
        }

        db.Dashboards.Add(dashboard);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.executive.dashboard_created",
            "ExecutiveDashboard",
            dashboard.Id.ToString("D"),
            AfterState: new { dashboard.Code, dashboard.AudienceRole, WidgetCount = dashboard.Widgets.Count }), ct);

        return dashboard;
    }

    public async Task<Result<RenderedDashboardDto>> RenderDashboardAsync(
        Guid dashboardId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        Guid? branchId = null,
        Guid? legalEntityId = null,
        CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        if (periodEndUtc <= periodStartUtc) return Error.Validation("PERIOD_INVALID", "periodEndUtc must be > periodStartUtc.");

        var tid = requestContext.TenantId.Value;
        var dashboard = await db.Dashboards.AsNoTracking()
            .Include(d => d.Widgets)
            .FirstOrDefaultAsync(d => d.Id == dashboardId && d.TenantId == tid, ct);

        if (dashboard is null) return Error.NotFound("DASHBOARD_NOT_FOUND", "Executive dashboard was not found.");

        var span = periodEndUtc - periodStartUtc;
        var prevStart = periodStartUtc - span;
        var prevEnd = periodStartUtc;

        var renderedWidgets = new List<RenderedWidgetDto>();
        foreach (var w in dashboard.Widgets.OrderBy(x => x.SortOrder))
        {
            var currRes = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                w.MetricCode, null, legalEntityId, branchId, null,
                periodStartUtc, periodEndUtc, w.NormalizeToCurrency), ct);

            if (currRes.IsFailure)
            {
                return currRes.Error!;
            }

            decimal? prevVal = null;
            decimal? deltaPct = null;
            if (w.ComparePreviousPeriod)
            {
                var prevRes = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                    w.MetricCode, currRes.Value.DefinitionVersion, legalEntityId, branchId, null,
                    prevStart, prevEnd, w.NormalizeToCurrency), ct);

                if (prevRes.IsSuccess && prevRes.Value.AggregatedValue.HasValue)
                {
                    prevVal = prevRes.Value.AggregatedValue.Value;
                    if (prevVal != 0m && currRes.Value.AggregatedValue.HasValue)
                    {
                        deltaPct = decimal.Round(((currRes.Value.AggregatedValue.Value - prevVal.Value) / Math.Abs(prevVal.Value)) * 100m, 2);
                    }
                }
            }

            renderedWidgets.Add(new RenderedWidgetDto(
                w.Id,
                w.Title,
                w.MetricCode,
                currRes.Value.DefinitionVersion,
                currRes.Value.FormulaExpression,
                currRes.Value.Unit,
                currRes.Value.EffectiveCurrencyCode,
                currRes.Value.AggregatedValue,
                prevVal,
                deltaPct,
                currRes.Value.OverallFreshnessStatus,
                currRes.Value.OverallQualityStatus,
                currRes.Value.LatestObservedAtUtc));
        }

        var openAlerts = await db.Alerts.AsNoTracking()
            .Where(a => a.TenantId == tid && a.Status == "Open")
            .OrderByDescending(a => a.TriggeredAtUtc)
            .Take(20)
            .ToListAsync(ct);

        return new RenderedDashboardDto(
            dashboard.Id, dashboard.Code, dashboard.Title, dashboard.AudienceRole,
            periodStartUtc, periodEndUtc, renderedWidgets, openAlerts);
    }

    public async Task<Result<ManagementAlertRule>> CreateAlertRuleAsync(CreateAlertRuleRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var rule = new ManagementAlertRule
        {
            Id = Guid.CreateVersion7(),
            TenantId = requestContext.TenantId.Value,
            Name = request.Name.Trim(),
            MetricCode = request.MetricCode.Trim().ToUpperInvariant(),
            ConditionOperator = request.ConditionOperator.Trim(),
            ThresholdValue = request.ThresholdValue,
            NormalizeToCurrency = string.IsNullOrWhiteSpace(request.NormalizeToCurrency) ? null : request.NormalizeToCurrency.Trim().ToUpperInvariant(),
            Severity = request.Severity.Trim(),
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return rule;
    }

    public async Task<Result<IReadOnlyList<ManagementAlert>>> EvaluateAlertsAsync(
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;

        var rules = await db.AlertRules.AsNoTracking().Where(r => r.TenantId == tid && r.IsEnabled).ToListAsync(ct);
        var triggered = new List<ManagementAlert>();

        foreach (var rule in rules)
        {
            var q = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                rule.MetricCode, FromUtc: periodStartUtc, ToUtc: periodEndUtc, NormalizeToCurrency: rule.NormalizeToCurrency), ct);

            if (q.IsFailure) continue;

            var val = q.Value.AggregatedValue;
            var isBreach = rule.ConditionOperator switch
            {
                "LessThan" => val.HasValue && rule.ThresholdValue.HasValue && val.Value < rule.ThresholdValue.Value,
                "GreaterThan" => val.HasValue && rule.ThresholdValue.HasValue && val.Value > rule.ThresholdValue.Value,
                "StaleOnly" => !string.Equals(q.Value.OverallFreshnessStatus, "Fresh", StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            if (isBreach)
            {
                var alert = new ManagementAlert
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tid,
                    RuleId = rule.Id,
                    MetricCode = rule.MetricCode,
                    Severity = rule.Severity,
                    Summary = $"{rule.Name}: observed {val?.ToString() ?? "N/A"} (freshness={q.Value.OverallFreshnessStatus})",
                    ObservedValue = val,
                    FreshnessStatus = q.Value.OverallFreshnessStatus,
                    Status = "Open",
                    TriggeredAtUtc = DateTimeOffset.UtcNow
                };
                db.Alerts.Add(alert);
                triggered.Add(alert);
            }
        }

        if (triggered.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var alert in triggered)
            {
                broadcaster.Publish(new RealtimeEventMessage(
                    "management.alert.triggered.v1",
                    tid,
                    requestContext.CorrelationId,
                    alert.TriggeredAtUtc,
                    new { alert.Id, alert.MetricCode, alert.Severity, alert.Summary }));
            }
        }

        return triggered;
    }

    public async Task<Result<IReadOnlyList<ManagementAlert>>> ListAlertsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.Alerts.AsNoTracking().Where(a => a.TenantId == tid).OrderByDescending(a => a.TriggeredAtUtc).Take(50).ToListAsync(ct);
    }

    public async Task<Result<ManagementAlert>> AcknowledgeAlertAsync(Guid alertId, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == alertId && a.TenantId == tid, ct);
        if (alert is null) return Error.NotFound("ALERT_NOT_FOUND", "Alert was not found.");

        alert.Status = "Acknowledged";
        alert.AcknowledgedByUserId = requestContext.UserId;
        alert.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.executive.alert_acknowledged", "ManagementAlert", alert.Id.ToString("D"),
            AfterState: new { alert.Status, alert.AcknowledgedByUserId }), ct);

        return alert;
    }
}
