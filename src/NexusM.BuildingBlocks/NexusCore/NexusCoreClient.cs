using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;
using NexusM.SharedKernel.Security;

namespace NexusM.BuildingBlocks.NexusCore;

public sealed class NexusCoreOptions
{
    public const string SectionName = "NexusCore";
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string ClientId { get; set; } = "nexus-m-service";
    public string ClientSecret { get; set; } = string.Empty;
    public int CacheSeconds { get; set; } = 30;
}

public sealed record EntitlementEvaluationDto(
    string FeatureCode,
    bool IsEntitled,
    string Reason,
    long? QuotaLimit = null,
    long? CurrentUsage = null);

public sealed record UserAuthorizationSnapshot(
    IReadOnlySet<string> Permissions,
    OrganizationScopeSnapshot OrgScope);

public sealed record FxRateQuoteDto(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    DateTimeOffset AsOfUtc,
    string Source = "NEXUS_C_FX");

public sealed record CoreWorkflowStartResultDto(Guid InstanceId, Guid? CurrentTaskId, string Status);
public sealed record CoreWorkflowDecisionResultDto(Guid InstanceId, string InstanceStatus, string Outcome);
public sealed record CoreAiGatewayResultDto(string Provider, string Model, string AnswerText, int PromptTokens, int CompletionTokens, decimal EstimatedCostUsd);

public interface INexusCoreClient
{
    Task<Result<EntitlementEvaluationDto>> EvaluateEntitlementAsync(Guid tenantId, string featureCode, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<UserAuthorizationSnapshot>> GetUserAuthorizationSnapshotAsync(Guid tenantId, Guid userId, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<FxRateQuoteDto>> GetFxRateAsync(string baseCurrency, string quoteCurrency, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<CoreWorkflowStartResultDto>> StartWorkflowInstanceAsync(string workflowDefinitionCode, string businessReferenceId, string title, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<CoreWorkflowDecisionResultDto>> DecideWorkflowTaskAsync(Guid cTaskId, string decision, string? comment, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<bool>> SendNotificationAsync(Guid recipientUserId, string subject, string body, string idempotencyKey, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<CoreAiGatewayResultDto>> InvokeAiGatewayAsync(string modelCode, string groundedPrompt, string userBearerToken, string correlationId, CancellationToken ct = default);
    Task<Result<bool>> SyncPermissionManifestAsync(string correlationId, CancellationToken ct = default);
}

public sealed class NexusCoreHttpClient(
    HttpClient httpClient,
    IOptions<NexusCoreOptions> options,
    IMemoryCache cache,
    ILogger<NexusCoreHttpClient> logger) : INexusCoreClient
{
    private readonly NexusCoreOptions _opts = options.Value;

    public async Task<Result<EntitlementEvaluationDto>> EvaluateEntitlementAsync(
        Guid tenantId,
        string featureCode,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        var cacheKey = $"nexus-m:{tenantId:N}:entitlement:{featureCode}";
        if (cache.TryGetValue<EntitlementEvaluationDto>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/platform/entitlements/{Uri.EscapeDataString(featureCode)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

            using var resp = await httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("NEXUS C entitlement check failed for tenant {TenantId}, feature {Feature}, HTTP {Status}", tenantId, featureCode, (int)resp.StatusCode);
                return Error.ServiceUnavailable("NEXUS_C_ENTITLEMENT_UNAVAILABLE", "Unable to verify entitlement with NEXUS C (fail-closed).");
            }

            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = doc.GetProperty("data");
            var dto = new EntitlementEvaluationDto(
                data.TryGetProperty("featureCode", out var fc) ? fc.GetString() ?? featureCode : featureCode,
                data.TryGetProperty("isEntitled", out var ie) && ie.GetBoolean(),
                data.TryGetProperty("reason", out var r) ? r.GetString() ?? "UNKNOWN" : "UNKNOWN",
                data.TryGetProperty("quotaLimit", out var ql) && ql.ValueKind == JsonValueKind.Number ? ql.GetInt64() : null,
                data.TryGetProperty("currentUsage", out var cu) && cu.ValueKind == JsonValueKind.Number ? cu.GetInt64() : null);

            cache.Set(cacheKey, dto, TimeSpan.FromSeconds(Math.Max(5, _opts.CacheSeconds)));
            return dto;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NEXUS C entitlement request failed (fail-closed) for tenant {TenantId}", tenantId);
            return Error.ServiceUnavailable("NEXUS_C_UNREACHABLE", "NEXUS C authority is unreachable (fail-closed).");
        }
    }

    public async Task<Result<UserAuthorizationSnapshot>> GetUserAuthorizationSnapshotAsync(
        Guid tenantId,
        Guid userId,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        var cacheKey = $"nexus-m:{tenantId:N}:authz:{userId:N}";
        if (cache.TryGetValue<UserAuthorizationSnapshot>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using var permReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/permissions");
            permReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            permReq.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

            using var orgReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/organization-scope");
            orgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            orgReq.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

            var permTask = httpClient.SendAsync(permReq, ct);
            var orgTask = httpClient.SendAsync(orgReq, ct);
            await Task.WhenAll(permTask, orgTask);

            using var permResp = permTask.Result;
            using var orgResp = orgTask.Result;

            if (!permResp.IsSuccessStatusCode || !orgResp.IsSuccessStatusCode)
            {
                return Error.ServiceUnavailable("NEXUS_C_AUTHZ_UNAVAILABLE", "Unable to resolve permissions/org scope from NEXUS C (fail-closed).");
            }

            var permDoc = await permResp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (permDoc.TryGetProperty("data", out var permData) && permData.TryGetProperty("permissions", out var permArr) && permArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in permArr.EnumerateArray())
                {
                    if (item.GetString() is { Length: > 0 } code)
                    {
                        perms.Add(code);
                    }
                }
            }

            var orgDoc = await orgResp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var orgData = orgDoc.GetProperty("data");
            var orgScope = new OrganizationScopeSnapshot(
                orgData.TryGetProperty("hasUnrestrictedTenantScope", out var u) && u.GetBoolean(),
                ReadGuidList(orgData, "legalEntityIds"),
                ReadGuidList(orgData, "branchIds"),
                ReadGuidList(orgData, "organizationUnitIds"));

            var snapshot = new UserAuthorizationSnapshot(perms, orgScope);
            cache.Set(cacheKey, snapshot, TimeSpan.FromSeconds(Math.Max(5, _opts.CacheSeconds)));
            return snapshot;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NEXUS C authorization snapshot failed (fail-closed) for tenant {TenantId}, user {UserId}", tenantId, userId);
            return Error.ServiceUnavailable("NEXUS_C_UNREACHABLE", "NEXUS C authorization authority is unreachable (fail-closed).");
        }
    }

    public async Task<Result<FxRateQuoteDto>> GetFxRateAsync(
        string baseCurrency,
        string quoteCurrency,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        var from = baseCurrency.Trim().ToUpperInvariant();
        var to = quoteCurrency.Trim().ToUpperInvariant();
        if (from == to)
        {
            return new FxRateQuoteDto(from, to, 1m, DateTimeOffset.UtcNow, "IDENTITY");
        }

        var cacheKey = $"nexus-m:fx:{from}:{to}";
        if (cache.TryGetValue<FxRateQuoteDto>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/platform/fx/convert?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&amount=1");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

            using var resp = await httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Error.ServiceUnavailable("NEXUS_C_FX_UNAVAILABLE", $"Unable to fetch FX rate {from}->{to} from NEXUS C.");
            }

            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = doc.GetProperty("data");
            var rate = data.TryGetProperty("rate", out var r) ? r.GetDecimal() : data.GetProperty("convertedAmount").GetDecimal();
            var asOf = data.TryGetProperty("effectiveDate", out var ed) && DateTimeOffset.TryParse(ed.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            var quote = new FxRateQuoteDto(from, to, rate, asOf);
            cache.Set(cacheKey, quote, TimeSpan.FromMinutes(15));
            return quote;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NEXUS C FX lookup failed for {From}->{To}", from, to);
            return Error.ServiceUnavailable("NEXUS_C_FX_UNREACHABLE", "NEXUS C FX service is unreachable.");
        }
    }

    public async Task<Result<CoreWorkflowStartResultDto>> StartWorkflowInstanceAsync(
        string workflowDefinitionCode,
        string businessReferenceId,
        string title,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/workflows/instances");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", $"m-wf-start:{businessReferenceId}");
            req.Content = JsonContent.Create(new { definitionCode = workflowDefinitionCode, referenceId = businessReferenceId, title });

            using var resp = await httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Error.ServiceUnavailable("NEXUS_C_WORKFLOW_START_FAILED", $"NEXUS C workflow start returned HTTP {(int)resp.StatusCode}.");
            }

            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = doc.GetProperty("data");
            return new CoreWorkflowStartResultDto(
                data.GetProperty("id").GetGuid(),
                data.TryGetProperty("currentTaskId", out var tId) && tId.ValueKind == JsonValueKind.String && Guid.TryParse(tId.GetString(), out var g) ? g : null,
                data.TryGetProperty("status", out var st) ? st.GetString() ?? "Submitted" : "Submitted");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start workflow in NEXUS C for {Ref}", businessReferenceId);
            return Error.ServiceUnavailable("NEXUS_C_WORKFLOW_UNREACHABLE", "NEXUS C Workflow Engine is unreachable.");
        }
    }

    public async Task<Result<CoreWorkflowDecisionResultDto>> DecideWorkflowTaskAsync(
        Guid cTaskId,
        string decision,
        string? comment,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/workflows/tasks/{cTaskId:D}/decide");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            req.Content = JsonContent.Create(new { decision, comment });

            using var resp = await httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Error.ServiceUnavailable("NEXUS_C_WORKFLOW_DECIDE_FAILED", $"NEXUS C workflow decision returned HTTP {(int)resp.StatusCode}.");
            }

            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = doc.GetProperty("data");
            return new CoreWorkflowDecisionResultDto(
                data.GetProperty("instanceId").GetGuid(),
                data.TryGetProperty("status", out var st) ? st.GetString() ?? decision : decision,
                decision);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decide workflow task {TaskId} in NEXUS C", cTaskId);
            return Error.ServiceUnavailable("NEXUS_C_WORKFLOW_UNREACHABLE", "NEXUS C Workflow Engine is unreachable.");
        }
    }

    public async Task<Result<bool>> SendNotificationAsync(
        Guid recipientUserId,
        string subject,
        string body,
        string idempotencyKey,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/notifications/send");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
            req.Content = JsonContent.Create(new { channel = "InApp", recipientUserId, subject, body });

            using var resp = await httpClient.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NEXUS C notification send degraded for recipient {Recipient}", recipientUserId);
            return false;
        }
    }

    public async Task<Result<CoreAiGatewayResultDto>> InvokeAiGatewayAsync(
        string modelCode,
        string groundedPrompt,
        string userBearerToken,
        string correlationId,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/integrations/ai/invoke");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userBearerToken);
            req.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            req.Content = JsonContent.Create(new { model = modelCode, prompt = groundedPrompt });

            using var resp = await httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Error.ServiceUnavailable("NEXUS_C_AI_UNAVAILABLE", $"NEXUS C AI Gateway returned HTTP {(int)resp.StatusCode}.");
            }

            var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var data = doc.GetProperty("data");
            return new CoreAiGatewayResultDto(
                data.TryGetProperty("provider", out var pv) ? pv.GetString() ?? "NEXUS_C_AI" : "NEXUS_C_AI",
                data.TryGetProperty("model", out var md) ? md.GetString() ?? modelCode : modelCode,
                data.TryGetProperty("output", out var outText) ? outText.GetString() ?? string.Empty : string.Empty,
                data.TryGetProperty("promptTokens", out var pt) ? pt.GetInt32() : 0,
                data.TryGetProperty("completionTokens", out var ctTok) ? ctTok.GetInt32() : 0,
                data.TryGetProperty("estimatedCostUsd", out var cost) ? cost.GetDecimal() : 0m);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NEXUS C AI Gateway invocation failed");
            return Error.ServiceUnavailable("NEXUS_C_AI_UNREACHABLE", "NEXUS C AI Gateway is unreachable.");
        }
    }

    public async Task<Result<bool>> SyncPermissionManifestAsync(string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ClientSecret))
        {
            return Error.Validation("NEXUS_C_CLIENT_SECRET_MISSING", "Service client secret is not configured.");
        }

        try
        {
            var tokenResp = await httpClient.PostAsJsonAsync("/api/v1/auth/token", new
            {
                grantType = "client_credentials",
                clientId = _opts.ClientId,
                clientSecret = _opts.ClientSecret,
                scope = "platform.applications.manage"
            }, ct);

            if (!tokenResp.IsSuccessStatusCode)
            {
                return Error.Unauthorized("NEXUS_C_S2S_TOKEN_FAILED", "Failed to obtain S2S token from NEXUS C.");
            }

            var tokenDoc = await tokenResp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var serviceToken = tokenDoc.GetProperty("data").GetProperty("accessToken").GetString()!;

            using var syncReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/platform/applications/{NexusMPermissions.ApplicationCode}/permissions/sync");
            syncReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceToken);
            syncReq.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
            syncReq.Content = JsonContent.Create(new
            {
                permissions = NexusMPermissions.Manifest.Select(p => new { code = p.Code, name = p.Name, category = p.Category, scope = p.Scope })
            });

            using var syncResp = await httpClient.SendAsync(syncReq, ct);
            if (!syncResp.IsSuccessStatusCode)
            {
                return Error.ServiceUnavailable("NEXUS_C_PERMISSION_SYNC_FAILED", $"Permission sync failed with HTTP {(int)syncResp.StatusCode}.");
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync NEXUS M permission manifest with NEXUS C");
            return Error.ServiceUnavailable("NEXUS_C_UNREACHABLE", "Unable to reach NEXUS C for permission sync.");
        }
    }

    private static IReadOnlyList<Guid> ReadGuidList(JsonElement obj, string propName)
    {
        if (!obj.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<Guid>();
        foreach (var el in arr.EnumerateArray())
        {
            if (Guid.TryParse(el.GetString(), out var g))
            {
                list.Add(g);
            }
        }
        return list;
    }
}
