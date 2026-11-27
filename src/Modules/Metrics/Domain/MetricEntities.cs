using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Metrics.Domain;

public enum MetricUnit
{
    Currency = 1,
    Percent = 2,
    Count = 3,
    Hours = 4,
    Days = 5
}

public enum MetricTimeBasis
{
    PointInTime = 1,
    PeriodSum = 2,
    PeriodAvg = 3,
    RollingWindow = 4
}

public enum MetricAggregationMethod
{
    Sum = 1,
    Avg = 2,
    Min = 3,
    Max = 4,
    WeightedAvg = 5,
    NonAdditive = 6
}

public enum AccountingBasis
{
    NotApplicable = 0,
    Accrual = 1,
    Cash = 2,
    Operational = 3
}

public enum AccessClassification
{
    PublicInternal = 1,
    Management = 2,
    ExecutiveRestricted = 3,
    BoardConfidential = 4
}

public enum DataQualityStatus
{
    Verified = 1,
    Partial = 2,
    Estimated = 3,
    Stale = 4,
    FailedReconciliation = 5
}

public enum FreshnessStatus
{
    Fresh = 1,
    Delayed = 2,
    Stale = 3
}

public sealed class MetricDefinition
{
    private MetricDefinition() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string MetricCode { get; private set; } = string.Empty;
    public string DisplayNameKey { get; private set; } = string.Empty;
    public string DescriptionKey { get; private set; } = string.Empty;
    public int CurrentVersion { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public ICollection<MetricDefinitionVersion> Versions { get; private set; } = [];

    public static Result<(MetricDefinition Definition, MetricDefinitionVersion InitialVersion)> Create(
        Guid tenantId,
        string metricCode,
        string displayNameKey,
        string descriptionKey,
        string formulaExpression,
        MetricUnit unit,
        string? currencyCode,
        MetricTimeBasis timeBasis,
        MetricAggregationMethod aggregationMethod,
        AccountingBasis accountingBasis,
        string sourceApplication,
        int freshnessSlaMinutes,
        AccessClassification accessClassification)
    {
        if (tenantId == Guid.Empty)
        {
            return Error.Validation("METRIC_TENANT_REQUIRED", "TenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(metricCode) || metricCode.Trim().Length > 80)
        {
            return Error.Validation("METRIC_CODE_INVALID", "MetricCode is required (max 80 chars, stable business identifier).");
        }

        if (string.IsNullOrWhiteSpace(displayNameKey) || string.IsNullOrWhiteSpace(formulaExpression) || string.IsNullOrWhiteSpace(sourceApplication))
        {
            return Error.Validation("METRIC_METADATA_REQUIRED", "DisplayNameKey, FormulaExpression, and SourceApplication are required.");
        }

        if (freshnessSlaMinutes <= 0)
        {
            return Error.Validation("METRIC_SLA_INVALID", "FreshnessSlaMinutes must be positive.");
        }

        var normalizedCurrency = NormalizeAndValidateCurrency(unit, currencyCode, out var currencyErr);
        if (currencyErr is not null)
        {
            return currencyErr;
        }

        if (unit == MetricUnit.Percent && aggregationMethod == MetricAggregationMethod.Sum)
        {
            return Error.Validation("METRIC_PERCENT_SUM_FORBIDDEN", "Percentage metrics cannot declare SUM as default aggregation; use NonAdditive or WeightedAvg.");
        }

        var now = DateTimeOffset.UtcNow;
        var def = new MetricDefinition
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MetricCode = metricCode.Trim().ToUpperInvariant(),
            DisplayNameKey = displayNameKey.Trim(),
            DescriptionKey = descriptionKey?.Trim() ?? string.Empty,
            CurrentVersion = 1,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var v1 = MetricDefinitionVersion.CreateInternal(
            def.Id, tenantId, 1, formulaExpression.Trim(), unit, normalizedCurrency,
            timeBasis, aggregationMethod, accountingBasis, sourceApplication.Trim().ToUpperInvariant(),
            freshnessSlaMinutes, accessClassification, now);

        return (def, v1);
    }

    public Result<MetricDefinitionVersion> PublishNextVersion(
        string formulaExpression,
        MetricUnit unit,
        string? currencyCode,
        MetricTimeBasis timeBasis,
        MetricAggregationMethod aggregationMethod,
        AccountingBasis accountingBasis,
        string sourceApplication,
        int freshnessSlaMinutes,
        AccessClassification accessClassification)
    {
        if (string.IsNullOrWhiteSpace(formulaExpression) || string.IsNullOrWhiteSpace(sourceApplication))
        {
            return Error.Validation("METRIC_METADATA_REQUIRED", "FormulaExpression and SourceApplication are required.");
        }

        if (freshnessSlaMinutes <= 0)
        {
            return Error.Validation("METRIC_SLA_INVALID", "FreshnessSlaMinutes must be positive.");
        }

        var normalizedCurrency = NormalizeAndValidateCurrency(unit, currencyCode, out var currencyErr);
        if (currencyErr is not null)
        {
            return currencyErr;
        }

        if (unit == MetricUnit.Percent && aggregationMethod == MetricAggregationMethod.Sum)
        {
            return Error.Validation("METRIC_PERCENT_SUM_FORBIDDEN", "Percentage metrics cannot declare SUM as default aggregation.");
        }

        CurrentVersion++;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return MetricDefinitionVersion.CreateInternal(
            Id, TenantId, CurrentVersion, formulaExpression.Trim(), unit, normalizedCurrency,
            timeBasis, aggregationMethod, accountingBasis, sourceApplication.Trim().ToUpperInvariant(),
            freshnessSlaMinutes, accessClassification, UpdatedAtUtc);
    }

    private static string? NormalizeAndValidateCurrency(MetricUnit unit, string? currencyCode, out Error? error)
    {
        error = null;
        if (unit == MetricUnit.Currency)
        {
            if (string.IsNullOrWhiteSpace(currencyCode) || currencyCode.Trim().Length != 3)
            {
                error = Error.Validation("METRIC_CURRENCY_REQUIRED", "Currency metrics must declare a 3-letter ISO 4217 currency code.");
                return null;
            }
            return currencyCode.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            error = Error.Validation("METRIC_CURRENCY_UNEXPECTED", "Non-currency metrics must not specify a currency code.");
            return null;
        }

        return null;
    }
}

public sealed class MetricDefinitionVersion
{
    private MetricDefinitionVersion() { }

    public Guid Id { get; private set; }
    public Guid MetricDefinitionId { get; private set; }
    public Guid TenantId { get; private set; }
    public int Version { get; private set; }
    public string FormulaExpression { get; private set; } = string.Empty;
    public MetricUnit Unit { get; private set; }
    public string? CurrencyCode { get; private set; }
    public MetricTimeBasis TimeBasis { get; private set; }
    public MetricAggregationMethod AggregationMethod { get; private set; }
    public AccountingBasis AccountingBasis { get; private set; }
    public string SourceApplication { get; private set; } = string.Empty;
    public int FreshnessSlaMinutes { get; private set; }
    public AccessClassification AccessClassification { get; private set; }
    public DateTimeOffset PublishedAtUtc { get; private set; }

    internal static MetricDefinitionVersion CreateInternal(
        Guid metricDefinitionId,
        Guid tenantId,
        int version,
        string formulaExpression,
        MetricUnit unit,
        string? currencyCode,
        MetricTimeBasis timeBasis,
        MetricAggregationMethod aggregationMethod,
        AccountingBasis accountingBasis,
        string sourceApplication,
        int freshnessSlaMinutes,
        AccessClassification accessClassification,
        DateTimeOffset publishedAtUtc)
        => new()
        {
            Id = Guid.CreateVersion7(),
            MetricDefinitionId = metricDefinitionId,
            TenantId = tenantId,
            Version = version,
            FormulaExpression = formulaExpression,
            Unit = unit,
            CurrencyCode = currencyCode,
            TimeBasis = timeBasis,
            AggregationMethod = aggregationMethod,
            AccountingBasis = accountingBasis,
            SourceApplication = sourceApplication,
            FreshnessSlaMinutes = freshnessSlaMinutes,
            AccessClassification = accessClassification,
            PublishedAtUtc = publishedAtUtc
        };
}

public sealed class MetricObservation
{
    private MetricObservation() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid MetricDefinitionId { get; private set; }
    public Guid MetricDefinitionVersionId { get; private set; }
    public Guid? LegalEntityId { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? OrgUnitId { get; private set; }
    public DateTimeOffset PeriodStartUtc { get; private set; }
    public DateTimeOffset PeriodEndUtc { get; private set; }
    public string ReportingTimezone { get; private set; } = "UTC";
    public string? CurrencyCode { get; private set; }
    public AccountingBasis AccountingBasis { get; private set; }
    public decimal ValueNumeric { get; private set; }
    public string SourceApplication { get; private set; } = string.Empty;
    public string SourceReferenceId { get; private set; } = string.Empty;
    public long SourceDataVersion { get; private set; }
    public DateTimeOffset ObservedAtUtc { get; private set; }
    public DateTimeOffset IngestedAtUtc { get; private set; }
    public DataQualityStatus QualityStatus { get; private set; }

    public static Result<MetricObservation> Create(
        Guid tenantId,
        Guid metricDefinitionId,
        MetricDefinitionVersion defVersion,
        Guid? legalEntityId,
        Guid? branchId,
        Guid? orgUnitId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        string reportingTimezone,
        string? currencyCode,
        AccountingBasis accountingBasis,
        decimal valueNumeric,
        string sourceApplication,
        string sourceReferenceId,
        long sourceDataVersion,
        DateTimeOffset observedAtUtc,
        DataQualityStatus qualityStatus)
    {
        if (periodEndUtc < periodStartUtc)
        {
            return Error.Validation("OBSERVATION_INVALID_PERIOD", "PeriodEndUtc must be >= PeriodStartUtc.");
        }

        if (string.IsNullOrWhiteSpace(sourceApplication) || string.IsNullOrWhiteSpace(sourceReferenceId))
        {
            return Error.Validation("OBSERVATION_LINEAGE_REQUIRED", "SourceApplication and SourceReferenceId are required for lineage.");
        }

        string? normalizedCurrency = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
        if (defVersion.Unit == MetricUnit.Currency && string.IsNullOrWhiteSpace(normalizedCurrency))
        {
            return Error.Validation("OBSERVATION_CURRENCY_REQUIRED", "Currency observation requires a 3-letter ISO 4217 currency code.");
        }
        if (defVersion.Unit != MetricUnit.Currency && normalizedCurrency is not null)
        {
            return Error.Validation("OBSERVATION_CURRENCY_UNEXPECTED", "Non-currency observation cannot carry a currency code.");
        }

        return new MetricObservation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MetricDefinitionId = metricDefinitionId,
            MetricDefinitionVersionId = defVersion.Id,
            LegalEntityId = legalEntityId,
            BranchId = branchId,
            OrgUnitId = orgUnitId,
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            ReportingTimezone = string.IsNullOrWhiteSpace(reportingTimezone) ? "UTC" : reportingTimezone.Trim(),
            CurrencyCode = normalizedCurrency,
            AccountingBasis = accountingBasis,
            ValueNumeric = valueNumeric,
            SourceApplication = sourceApplication.Trim().ToUpperInvariant(),
            SourceReferenceId = sourceReferenceId.Trim(),
            SourceDataVersion = sourceDataVersion,
            ObservedAtUtc = observedAtUtc,
            IngestedAtUtc = DateTimeOffset.UtcNow,
            QualityStatus = qualityStatus
        };
    }
}
