using Microsoft.EntityFrameworkCore;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.Modules.Metrics.Domain;
using NexusM.Modules.Metrics.Infrastructure;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Metrics.Application;

public sealed record MetricVersionDto(
    Guid Id,
    int Version,
    string FormulaExpression,
    string Unit,
    string? CurrencyCode,
    string TimeBasis,
    string AggregationMethod,
    string AccountingBasis,
    string SourceApplication,
    int FreshnessSlaMinutes,
    string AccessClassification,
    DateTimeOffset PublishedAtUtc);

public sealed record MetricDefinitionDto(
    Guid Id,
    Guid TenantId,
    string MetricCode,
    string DisplayNameKey,
    string DescriptionKey,
    int CurrentVersion,
    bool IsActive,
    MetricVersionDto LatestVersion,
    IReadOnlyList<MetricVersionDto> Versions);

public sealed record CreateMetricDefinitionRequest(
    string MetricCode,
    string DisplayNameKey,
    string DescriptionKey,
    string FormulaExpression,
    MetricUnit Unit,
    string? CurrencyCode,
    MetricTimeBasis TimeBasis,
    MetricAggregationMethod AggregationMethod,
    AccountingBasis AccountingBasis,
    string SourceApplication,
    int FreshnessSlaMinutes = 60,
    AccessClassification AccessClassification = AccessClassification.Management);

public sealed record PublishMetricVersionRequest(
    string FormulaExpression,
    MetricUnit Unit,
    string? CurrencyCode,
    MetricTimeBasis TimeBasis,
    MetricAggregationMethod AggregationMethod,
    AccountingBasis AccountingBasis,
    string SourceApplication,
    int FreshnessSlaMinutes = 60,
    AccessClassification AccessClassification = AccessClassification.Management);

public sealed record IngestObservationItem(
    string MetricCode,
    int? DefinitionVersion,
    Guid? LegalEntityId,
    Guid? BranchId,
    Guid? OrgUnitId,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    string ReportingTimezone,
    string? CurrencyCode,
    AccountingBasis AccountingBasis,
    decimal ValueNumeric,
    string SourceApplication,
    string SourceReferenceId,
    long SourceDataVersion = 1,
    DateTimeOffset? ObservedAtUtc = null,
    DataQualityStatus QualityStatus = DataQualityStatus.Verified);

public sealed record IngestObservationsBatchRequest(IReadOnlyList<IngestObservationItem> Items);

public sealed record IngestObservationsResultDto(int ReceivedCount, int InsertedCount, int DeduplicatedCount);

public sealed record MetricObservationPointDto(
    Guid Id,
    int DefinitionVersion,
    Guid? LegalEntityId,
    Guid? BranchId,
    Guid? OrgUnitId,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    string ReportingTimezone,
    string? OriginalCurrencyCode,
    decimal OriginalValue,
    string? NormalizedCurrencyCode,
    decimal NormalizedValue,
    decimal? AppliedFxRate,
    DateTimeOffset? FxRateAsOfUtc,
    string AccountingBasis,
    string SourceApplication,
    string SourceReferenceId,
    long SourceDataVersion,
    DateTimeOffset ObservedAtUtc,
    string QualityStatus,
    string FreshnessStatus);

public sealed record MetricQueryRequest(
    string MetricCode,
    int? DefinitionVersion = null,
    Guid? LegalEntityId = null,
    Guid? BranchId = null,
    Guid? OrgUnitId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? NormalizeToCurrency = null,
    MetricAggregationMethod? RequestedAggregation = null);

public sealed record MetricQueryResponseDto(
    string MetricCode,
    int DefinitionVersion,
    string FormulaExpression,
    string Unit,
    string? EffectiveCurrencyCode,
    string AccountingBasis,
    string AggregationMethod,
    decimal? AggregatedValue,
    string OverallFreshnessStatus,
    string OverallQualityStatus,
    DateTimeOffset? LatestObservedAtUtc,
    int FreshnessSlaMinutes,
    IReadOnlyList<MetricObservationPointDto> Points);

public sealed record MetricWindowSummaryDto(int Count, decimal Sum);

public interface IMetricsService
{
    Task<Result<IReadOnlyList<MetricDefinitionDto>>> ListDefinitionsAsync(CancellationToken ct = default);
    Task<Result<MetricDefinitionDto>> CreateDefinitionAsync(CreateMetricDefinitionRequest request, CancellationToken ct = default);
    Task<Result<MetricDefinitionDto>> PublishVersionAsync(Guid definitionId, PublishMetricVersionRequest request, CancellationToken ct = default);
    Task<Result<IngestObservationsResultDto>> IngestObservationsAsync(IngestObservationsBatchRequest request, CancellationToken ct = default);
    Task<Result<MetricQueryResponseDto>> QueryMetricAsync(MetricQueryRequest request, CancellationToken ct = default);
    Task<Result<MetricWindowSummaryDto>> GetWindowReconciliationSummaryAsync(
        string metricCode,
        string sourceApplication,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken ct = default);
}

public sealed class MetricsService(
    MetricsDbContext db,
    IRequestContext requestContext,
    INexusCoreClient nexusCoreClient,
    IBusinessAuditWriter auditWriter) : IMetricsService
{
    private static MetricVersionDto MapVersion(MetricDefinitionVersion v) => new(
        v.Id, v.Version, v.FormulaExpression, v.Unit.ToString(), v.CurrencyCode,
        v.TimeBasis.ToString(), v.AggregationMethod.ToString(), v.AccountingBasis.ToString(),
        v.SourceApplication, v.FreshnessSlaMinutes, v.AccessClassification.ToString(), v.PublishedAtUtc);

    private static MetricDefinitionDto MapDefinition(MetricDefinition d)
    {
        var ordered = d.Versions.OrderByDescending(v => v.Version).Select(MapVersion).ToList();
        return new MetricDefinitionDto(
            d.Id, d.TenantId, d.MetricCode, d.DisplayNameKey, d.DescriptionKey,
            d.CurrentVersion, d.IsActive, ordered.First(), ordered);
    }

    private static FreshnessStatus EvaluateFreshness(DateTimeOffset observedAtUtc, int slaMinutes, DateTimeOffset nowUtc)
    {
        var ageMinutes = (nowUtc - observedAtUtc).TotalMinutes;
        if (ageMinutes <= slaMinutes) return FreshnessStatus.Fresh;
        if (ageMinutes <= slaMinutes * 2.0) return FreshnessStatus.Delayed;
        return FreshnessStatus.Stale;
    }

    public async Task<Result<IReadOnlyList<MetricDefinitionDto>>> ListDefinitionsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var defs = await db.MetricDefinitions.AsNoTracking()
            .Include(d => d.Versions)
            .Where(d => d.TenantId == tid)
            .OrderBy(d => d.MetricCode)
            .ToListAsync(ct);

        return defs.Select(MapDefinition).ToList();
    }

    public async Task<Result<MetricDefinitionDto>> CreateDefinitionAsync(CreateMetricDefinitionRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var normalizedCode = request.MetricCode?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await db.MetricDefinitions.AnyAsync(d => d.TenantId == tid && d.MetricCode == normalizedCode, ct))
        {
            return Error.Conflict("METRIC_CODE_EXISTS", $"Metric '{normalizedCode}' already exists in this tenant.");
        }

        var created = MetricDefinition.Create(
            tid, normalizedCode, request.DisplayNameKey, request.DescriptionKey,
            request.FormulaExpression, request.Unit, request.CurrencyCode,
            request.TimeBasis, request.AggregationMethod, request.AccountingBasis,
            request.SourceApplication, request.FreshnessSlaMinutes, request.AccessClassification);

        if (created.IsFailure)
        {
            return created.Error!;
        }

        var (def, v1) = created.Value;
        def.Versions.Add(v1);
        db.MetricDefinitions.Add(def);
        db.MetricDefinitionVersions.Add(v1);
        await db.SaveChangesAsync(ct);

        var dto = MapDefinition(def);
        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.metric.definition_created",
            "MetricDefinition",
            def.Id.ToString("D"),
            AfterState: new { def.MetricCode, Version = 1, v1.FormulaExpression, Unit = v1.Unit.ToString(), v1.CurrencyCode }), ct);

        return dto;
    }

    public async Task<Result<MetricDefinitionDto>> PublishVersionAsync(Guid definitionId, PublishMetricVersionRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var def = await db.MetricDefinitions
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.Id == definitionId && d.TenantId == tid, ct);

        if (def is null)
        {
            return Error.NotFound("METRIC_NOT_FOUND", "Metric definition was not found.");
        }

        var nextVer = def.PublishNextVersion(
            request.FormulaExpression, request.Unit, request.CurrencyCode,
            request.TimeBasis, request.AggregationMethod, request.AccountingBasis,
            request.SourceApplication, request.FreshnessSlaMinutes, request.AccessClassification);

        if (nextVer.IsFailure)
        {
            return nextVer.Error!;
        }

        def.Versions.Add(nextVer.Value);
        db.MetricDefinitionVersions.Add(nextVer.Value);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.metric.version_published",
            "MetricDefinition",
            def.Id.ToString("D"),
            AfterState: MapVersion(nextVer.Value)), ct);

        return MapDefinition(def);
    }

    public async Task<Result<IngestObservationsResultDto>> IngestObservationsAsync(IngestObservationsBatchRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        if (request?.Items is null || request.Items.Count == 0)
        {
            return Error.Validation("OBSERVATIONS_EMPTY", "At least one observation item is required.");
        }

        if (request.Items.Count > 500)
        {
            return Error.Validation("OBSERVATIONS_BATCH_LIMIT", "Maximum 500 observations per batch.");
        }

        var tid = requestContext.TenantId.Value;
        var codes = request.Items.Select(i => i.MetricCode.Trim().ToUpperInvariant()).Distinct().ToList();
        var defs = await db.MetricDefinitions
            .Include(d => d.Versions)
            .Where(d => d.TenantId == tid && codes.Contains(d.MetricCode))
            .ToDictionaryAsync(d => d.MetricCode, StringComparer.OrdinalIgnoreCase, ct);

        var inserted = 0;
        var deduplicated = 0;

        foreach (var item in request.Items)
        {
            var code = item.MetricCode.Trim().ToUpperInvariant();
            if (!defs.TryGetValue(code, out var def))
            {
                return Error.Validation("METRIC_NOT_FOUND", $"Metric '{code}' does not exist in KPI catalog.");
            }

            var targetVerNum = item.DefinitionVersion ?? def.CurrentVersion;
            var defVer = def.Versions.FirstOrDefault(v => v.Version == targetVerNum);
            if (defVer is null)
            {
                return Error.Validation("METRIC_VERSION_NOT_FOUND", $"Metric '{code}' version {targetVerNum} does not exist.");
            }

            var normSourceApp = item.SourceApplication.Trim().ToUpperInvariant();
            var normSourceRef = item.SourceReferenceId.Trim();

            var duplicate = await db.MetricObservations.AnyAsync(o =>
                o.TenantId == tid &&
                o.SourceApplication == normSourceApp &&
                o.SourceReferenceId == normSourceRef &&
                o.MetricDefinitionVersionId == defVer.Id &&
                o.SourceDataVersion == item.SourceDataVersion, ct);

            if (duplicate)
            {
                deduplicated++;
                continue;
            }

            var obsResult = MetricObservation.Create(
                tid, def.Id, defVer,
                item.LegalEntityId, item.BranchId, item.OrgUnitId,
                item.PeriodStartUtc, item.PeriodEndUtc, item.ReportingTimezone,
                item.CurrencyCode, item.AccountingBasis, item.ValueNumeric,
                normSourceApp, normSourceRef, item.SourceDataVersion,
                item.ObservedAtUtc ?? DateTimeOffset.UtcNow,
                item.QualityStatus);

            if (obsResult.IsFailure)
            {
                return obsResult.Error!;
            }

            db.MetricObservations.Add(obsResult.Value);
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new IngestObservationsResultDto(request.Items.Count, inserted, deduplicated);
    }

    public async Task<Result<MetricQueryResponseDto>> QueryMetricAsync(MetricQueryRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var scope = requestContext.OrgScope;

        // 1. Server-side Organization Scope Enforcement (ADR-003 / SECURITY-ARCHITECTURE.md)
        if (!scope.HasUnrestrictedTenantScope)
        {
            if (request.LegalEntityId.HasValue && !scope.LegalEntityIds.Contains(request.LegalEntityId.Value))
            {
                return Error.Forbidden("ORGANIZATION_SCOPE_DENIED", "Requested LegalEntity is outside caller's authorized scope.");
            }
            if (request.BranchId.HasValue && !scope.BranchIds.Contains(request.BranchId.Value))
            {
                return Error.Forbidden("ORGANIZATION_SCOPE_DENIED", "Requested Branch is outside caller's authorized scope.");
            }
            if (request.OrgUnitId.HasValue && !scope.OrganizationUnitIds.Contains(request.OrgUnitId.Value))
            {
                return Error.Forbidden("ORGANIZATION_SCOPE_DENIED", "Requested OrganizationUnit is outside caller's authorized scope.");
            }
        }

        var code = request.MetricCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var def = await db.MetricDefinitions.AsNoTracking()
            .Include(d => d.Versions)
            .FirstOrDefaultAsync(d => d.TenantId == tid && d.MetricCode == code, ct);

        if (def is null)
        {
            return Error.NotFound("METRIC_NOT_FOUND", $"Metric '{code}' was not found.");
        }

        var targetVerNum = request.DefinitionVersion ?? def.CurrentVersion;
        var defVer = def.Versions.FirstOrDefault(v => v.Version == targetVerNum);
        if (defVer is null)
        {
            return Error.NotFound("METRIC_VERSION_NOT_FOUND", $"Metric '{code}' version {targetVerNum} was not found.");
        }

        var effectiveAgg = request.RequestedAggregation ?? defVer.AggregationMethod;
        if (defVer.AggregationMethod == MetricAggregationMethod.NonAdditive && effectiveAgg == MetricAggregationMethod.Sum)
        {
            return Error.Unprocessable("METRIC_COMPARISON_INVALID", $"Metric '{code}' is NonAdditive and cannot be aggregated via SUM.");
        }

        var query = db.MetricObservations.AsNoTracking()
            .Where(o => o.TenantId == tid && o.MetricDefinitionVersionId == defVer.Id);

        if (!scope.HasUnrestrictedTenantScope)
        {
            query = query.Where(o =>
                (!o.LegalEntityId.HasValue || scope.LegalEntityIds.Contains(o.LegalEntityId.Value)) &&
                (!o.BranchId.HasValue || scope.BranchIds.Contains(o.BranchId.Value)) &&
                (!o.OrgUnitId.HasValue || scope.OrganizationUnitIds.Contains(o.OrgUnitId.Value)));
        }

        if (request.LegalEntityId.HasValue) query = query.Where(o => o.LegalEntityId == request.LegalEntityId.Value);
        if (request.BranchId.HasValue) query = query.Where(o => o.BranchId == request.BranchId.Value);
        if (request.OrgUnitId.HasValue) query = query.Where(o => o.OrgUnitId == request.OrgUnitId.Value);
        if (request.FromUtc.HasValue) query = query.Where(o => o.PeriodStartUtc >= request.FromUtc.Value);
        if (request.ToUtc.HasValue) query = query.Where(o => o.PeriodEndUtc <= request.ToUtc.Value);

        var rows = await query.OrderBy(o => o.PeriodStartUtc).ThenBy(o => o.ObservedAtUtc).ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new MetricQueryResponseDto(
                def.MetricCode, defVer.Version, defVer.FormulaExpression, defVer.Unit.ToString(),
                request.NormalizeToCurrency?.Trim().ToUpperInvariant() ?? defVer.CurrencyCode,
                defVer.AccountingBasis.ToString(), effectiveAgg.ToString(), null,
                FreshnessStatus.Stale.ToString(), DataQualityStatus.Partial.ToString(),
                null, defVer.FreshnessSlaMinutes, []);
        }

        // 2. Anti-Mixing Guardrail #1: Accounting Basis must never be silently mixed (ADR-006)
        var distinctBases = rows.Select(r => r.AccountingBasis).Distinct().ToList();
        if (distinctBases.Count > 1)
        {
            return Error.Unprocessable("METRIC_COMPARISON_INVALID",
                $"Cannot mix different accounting bases ({string.Join(", ", distinctBases)}) in one metric series.");
        }

        // 3. Anti-Mixing Guardrail #2: Reporting Timezones must match unless observations use UTC
        var distinctTz = rows.Select(r => r.ReportingTimezone).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinctTz.Count > 1)
        {
            return Error.Unprocessable("METRIC_COMPARISON_INVALID",
                $"Cannot silently mix reporting timezones ({string.Join(", ", distinctTz)}) without explicit period normalization.");
        }

        // 4. Anti-Mixing Guardrail #3: Multi-Currency requires explicit FX Normalization via NEXUS C (ADR-006)
        var targetCurrency = string.IsNullOrWhiteSpace(request.NormalizeToCurrency)
            ? null
            : request.NormalizeToCurrency.Trim().ToUpperInvariant();

        var distinctCurrencies = rows.Where(r => r.CurrencyCode != null).Select(r => r.CurrencyCode!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinctCurrencies.Count > 1 && targetCurrency is null)
        {
            return Error.Unprocessable("METRIC_COMPARISON_INVALID",
                $"Observations contain multiple currencies ({string.Join(", ", distinctCurrencies)}). Specify normalizeToCurrency to convert via NEXUS C FX.");
        }

        var fxMap = new Dictionary<string, FxRateQuoteDto>(StringComparer.OrdinalIgnoreCase);
        if (targetCurrency is not null && defVer.Unit == MetricUnit.Currency)
        {
            foreach (var srcCurr in distinctCurrencies)
            {
                var fxRes = await nexusCoreClient.GetFxRateAsync(
                    srcCurr, targetCurrency, requestContext.RawBearerToken ?? string.Empty, requestContext.CorrelationId, ct);
                if (fxRes.IsFailure)
                {
                    return fxRes.Error!;
                }
                fxMap[srcCurr] = fxRes.Value;
            }
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var points = new List<MetricObservationPointDto>(rows.Count);

        foreach (var r in rows)
        {
            var normVal = r.ValueNumeric;
            var normCurr = r.CurrencyCode;
            decimal? fxRate = null;
            DateTimeOffset? fxAsOf = null;

            if (targetCurrency is not null && r.CurrencyCode is not null && fxMap.TryGetValue(r.CurrencyCode, out var quote))
            {
                normVal = decimal.Round(r.ValueNumeric * quote.Rate, 6);
                normCurr = targetCurrency;
                fxRate = quote.Rate;
                fxAsOf = quote.AsOfUtc;
            }

            var freshness = EvaluateFreshness(r.ObservedAtUtc, defVer.FreshnessSlaMinutes, nowUtc);
            points.Add(new MetricObservationPointDto(
                r.Id, defVer.Version, r.LegalEntityId, r.BranchId, r.OrgUnitId,
                r.PeriodStartUtc, r.PeriodEndUtc, r.ReportingTimezone,
                r.CurrencyCode, r.ValueNumeric, normCurr, normVal, fxRate, fxAsOf,
                r.AccountingBasis.ToString(), r.SourceApplication, r.SourceReferenceId,
                r.SourceDataVersion, r.ObservedAtUtc, r.QualityStatus.ToString(), freshness.ToString()));
        }

        decimal? aggregate = effectiveAgg switch
        {
            MetricAggregationMethod.Sum => points.Sum(p => p.NormalizedValue),
            MetricAggregationMethod.Avg => decimal.Round(points.Average(p => p.NormalizedValue), 6),
            MetricAggregationMethod.Min => points.Min(p => p.NormalizedValue),
            MetricAggregationMethod.Max => points.Max(p => p.NormalizedValue),
            _ => null
        };

        var latestObs = rows.Max(r => r.ObservedAtUtc);
        var overallFreshness = EvaluateFreshness(latestObs, defVer.FreshnessSlaMinutes, nowUtc);
        var overallQuality = rows.Any(r => r.QualityStatus == DataQualityStatus.FailedReconciliation)
            ? DataQualityStatus.FailedReconciliation
            : rows.Any(r => r.QualityStatus != DataQualityStatus.Verified)
                ? DataQualityStatus.Partial
                : DataQualityStatus.Verified;

        return new MetricQueryResponseDto(
            def.MetricCode,
            defVer.Version,
            defVer.FormulaExpression,
            defVer.Unit.ToString(),
            targetCurrency ?? distinctCurrencies.FirstOrDefault() ?? defVer.CurrencyCode,
            distinctBases[0].ToString(),
            effectiveAgg.ToString(),
            aggregate,
            overallFreshness.ToString(),
            overallQuality.ToString(),
            latestObs,
            defVer.FreshnessSlaMinutes,
            points);
    }

    public async Task<Result<MetricWindowSummaryDto>> GetWindowReconciliationSummaryAsync(
        string metricCode,
        string sourceApplication,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        var tid = requestContext.TenantId.Value;
        var normCode = metricCode.Trim().ToUpperInvariant();
        var normSource = sourceApplication.Trim().ToUpperInvariant();

        var def = await db.MetricDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.TenantId == tid && d.MetricCode == normCode, ct);

        if (def is null)
        {
            return Error.NotFound("METRIC_NOT_FOUND", $"Metric '{normCode}' was not found.");
        }

        var values = await db.MetricObservations.AsNoTracking()
            .Where(o => o.TenantId == tid &&
                        o.MetricDefinitionId == def.Id &&
                        o.SourceApplication == normSource &&
                        o.PeriodStartUtc >= periodStartUtc &&
                        o.PeriodEndUtc <= periodEndUtc)
            .Select(o => o.ValueNumeric)
            .ToListAsync(ct);

        return new MetricWindowSummaryDto(values.Count, values.Sum());
    }
}
