using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.Modules.Metrics.Application;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Ai;

public sealed class AiUsageRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public string CitedMetricsJson { get; set; } = "[]";
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset InvokedAtUtc { get; set; }
}

public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options)
{
    public DbSet<AiUsageRecord> UsageRecords => Set<AiUsageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_ai");

        modelBuilder.Entity<AiUsageRecord>(e =>
        {
            e.ToTable("usage_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
            e.Property(x => x.ModelCode).HasColumnName("model_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.PromptTokens).HasColumnName("prompt_tokens").IsRequired();
            e.Property(x => x.CompletionTokens).HasColumnName("completion_tokens").IsRequired();
            e.Property(x => x.EstimatedCostUsd).HasColumnName("estimated_cost_usd").HasPrecision(18, 6).IsRequired();
            e.Property(x => x.CitedMetricsJson).HasColumnName("cited_metrics_json").HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.InvokedAtUtc).HasColumnName("invoked_at_utc").IsRequired();
        });
    }
}

public sealed record AiMetricCitationDto(
    string MetricCode,
    int? DefinitionVersion,
    decimal? AggregatedValue,
    string? CurrencyCode,
    string FreshnessStatus,
    DateTimeOffset? LatestObservedAtUtc,
    string Status);

public sealed record AskExecutiveAiRequest(
    string Question,
    IReadOnlyList<string> MetricCodes,
    DateTimeOffset? PeriodStartUtc = null,
    DateTimeOffset? PeriodEndUtc = null,
    string? NormalizeToCurrency = null,
    string ModelCode = "gemini-2.5-flash",
    bool AutonomousExecutionRequested = false);

public sealed record AskExecutiveAiResponseDto(
    string Answer,
    string Provider,
    string ModelCode,
    int PromptTokens,
    int CompletionTokens,
    decimal EstimatedCostUsd,
    bool RequiresHumanConfirmation,
    IReadOnlyList<AiMetricCitationDto> Citations);

public interface IExecutiveAiService
{
    Task<Result<AskExecutiveAiResponseDto>> AskGroundedAsync(AskExecutiveAiRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AiUsageRecord>>> ListUsageAsync(CancellationToken ct = default);
}

public sealed class ExecutiveAiService(
    AiDbContext db,
    IMetricsService metricsService,
    INexusCoreClient nexusCoreClient,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter) : IExecutiveAiService
{
    public async Task<Result<AskExecutiveAiResponseDto>> AskGroundedAsync(AskExecutiveAiRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        // Hard safety invariant (Master Prompt Section 4 M09): AI must NEVER approve or execute privileged actions autonomously.
        if (request.AutonomousExecutionRequested)
        {
            return Error.Forbidden(
                "AI_AUTONOMOUS_ACTION_FORBIDDEN",
                "AI cannot approve or execute financial, operational, or governance actions autonomously. Human confirmation is mandatory.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
            return Error.Validation("AI_QUESTION_REQUIRED", "Question is required.");

        var citations = new List<AiMetricCitationDto>();
        var groundedBuilder = new StringBuilder();
        groundedBuilder.AppendLine("SYSTEM POLICY: Answer strictly from the verified NEXUS M metrics below. Never invent missing metrics. Flag stale data explicitly.");
        groundedBuilder.AppendLine($"USER QUESTION: {request.Question.Trim()}");
        groundedBuilder.AppendLine("AUTHORIZED GROUNDED METRICS:");

        foreach (var rawCode in (request.MetricCodes ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var code = rawCode.Trim().ToUpperInvariant();
            var mRes = await metricsService.QueryMetricAsync(new MetricQueryRequest(
                code, FromUtc: request.PeriodStartUtc, ToUtc: request.PeriodEndUtc, NormalizeToCurrency: request.NormalizeToCurrency), ct);

            if (mRes.IsFailure)
            {
                if (mRes.Error!.Type == ErrorType.Forbidden)
                {
                    return mRes.Error!;
                }

                citations.Add(new AiMetricCitationDto(code, null, null, null, "UNAVAILABLE", null, mRes.Error.Code));
                groundedBuilder.AppendLine($"- {code}: DATA_UNAVAILABLE ({mRes.Error.Code})");
                continue;
            }

            var d = mRes.Value;
            citations.Add(new AiMetricCitationDto(
                d.MetricCode, d.DefinitionVersion, d.AggregatedValue, d.EffectiveCurrencyCode,
                d.OverallFreshnessStatus, d.LatestObservedAtUtc, "VERIFIED_GROUNDED"));

            groundedBuilder.AppendLine(
                $"- {d.MetricCode} (v{d.DefinitionVersion}, formula={d.FormulaExpression}): value={d.AggregatedValue?.ToString() ?? "NO_OBSERVATIONS"} {d.EffectiveCurrencyCode ?? d.Unit}, freshness={d.OverallFreshnessStatus}, quality={d.OverallQualityStatus}, asOf={d.LatestObservedAtUtc:O}");
        }

        var gatewayRes = await nexusCoreClient.InvokeAiGatewayAsync(
            request.ModelCode,
            groundedBuilder.ToString(),
            requestContext.RawBearerToken ?? string.Empty,
            requestContext.CorrelationId,
            ct);

        if (gatewayRes.IsFailure) return gatewayRes.Error!;

        var gw = gatewayRes.Value;
        var usage = new AiUsageRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = requestContext.TenantId.Value,
            UserId = requestContext.UserId.Value,
            Provider = gw.Provider,
            ModelCode = gw.Model,
            PromptTokens = gw.PromptTokens,
            CompletionTokens = gw.CompletionTokens,
            EstimatedCostUsd = gw.EstimatedCostUsd,
            CitedMetricsJson = JsonSerializer.Serialize(citations),
            CorrelationId = requestContext.CorrelationId,
            InvokedAtUtc = DateTimeOffset.UtcNow
        };

        db.UsageRecords.Add(usage);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.ai.executive_query", "AiUsageRecord", usage.Id.ToString("D"),
            AfterState: new { usage.Provider, usage.ModelCode, usage.PromptTokens, usage.CompletionTokens, usage.EstimatedCostUsd, CitationCount = citations.Count }), ct);

        return new AskExecutiveAiResponseDto(
            gw.AnswerText,
            gw.Provider,
            gw.Model,
            gw.PromptTokens,
            gw.CompletionTokens,
            gw.EstimatedCostUsd,
            RequiresHumanConfirmation: true,
            citations);
    }

    public async Task<Result<IReadOnlyList<AiUsageRecord>>> ListUsageAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.UsageRecords.AsNoTracking().Where(u => u.TenantId == tid).OrderByDescending(u => u.InvokedAtUtc).Take(50).ToListAsync(ct);
    }
}
