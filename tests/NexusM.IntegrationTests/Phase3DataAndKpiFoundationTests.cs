using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.IntegrationTests.Infrastructure;
using NexusM.Modules.Metrics.Domain;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Security;
using Npgsql;
using Xunit;

namespace NexusM.IntegrationTests;

public sealed class Phase3DataAndKpiFoundationTests(NexusMTestFixture fixture) : IClassFixture<NexusMTestFixture>
{
    private void ConfigureContext(OrganizationScopeSnapshot? orgScope = null, params string[] perms)
    {
        fixture.CoreStub.EntitlementHandler = (_, code) =>
            new EntitlementEvaluationDto(code, code == "NEXUS_M", code == "NEXUS_M" ? "OK" : "NOT_IN_PLAN");

        fixture.CoreStub.AuthzHandler = (_, _) =>
            new UserAuthorizationSnapshot(
                new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase),
                orgScope ?? new OrganizationScopeSnapshot(true, [], [], []));

        fixture.CoreStub.FxHandler = (from, to) =>
            from.Equals(to, StringComparison.OrdinalIgnoreCase)
                ? new FxRateQuoteDto(from, to, 1m, DateTimeOffset.UtcNow, "IDENTITY")
                : new FxRateQuoteDto(from, to, 25000m, new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), "NEXUS_C_FX");
    }

    [Fact]
    public async Task MetricCatalog_EnforcesImmutableVersions_AntiMixingRules_FxNormalization_AndFreshnessSla()
    {
        ConfigureContext(null,
            NexusMPermissions.KpiRead,
            NexusMPermissions.KpiManage,
            NexusMPermissions.IntegrationManage);

        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(userId, "cfo@demo.local", tenantId));

        // 1. Create Currency Metric (REV_GROSS v1) & NonAdditive Percent Metric (GROSS_MARGIN)
        var revResp = await client.PostAsJsonAsync("/api/v1/m/metrics/definitions", new
        {
            metricCode = "REV_GROSS",
            displayNameKey = "kpi.rev_gross.name",
            descriptionKey = "kpi.rev_gross.desc",
            formulaExpression = "SUM(nexus_f.invoice.total_gross)",
            unit = MetricUnit.Currency,
            currencyCode = "VND",
            timeBasis = MetricTimeBasis.PeriodSum,
            aggregationMethod = MetricAggregationMethod.Sum,
            accountingBasis = AccountingBasis.Accrual,
            sourceApplication = "NEXUS_F",
            freshnessSlaMinutes = 30
        });
        Assert.Equal(HttpStatusCode.Created, revResp.StatusCode);
        var revDefId = (await revResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        // Publish v2 formula — v1 must remain intact
        var v2Resp = await client.PostAsJsonAsync($"/api/v1/m/metrics/definitions/{revDefId}/versions", new
        {
            formulaExpression = "SUM(nexus_f.invoice.total_gross - credit_notes)",
            unit = MetricUnit.Currency,
            currencyCode = "VND",
            timeBasis = MetricTimeBasis.PeriodSum,
            aggregationMethod = MetricAggregationMethod.Sum,
            accountingBasis = AccountingBasis.Accrual,
            sourceApplication = "NEXUS_F",
            freshnessSlaMinutes = 30
        });
        Assert.Equal(HttpStatusCode.Created, v2Resp.StatusCode);
        var v2Json = await v2Resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, v2Json.GetProperty("data").GetProperty("currentVersion").GetInt32());
        Assert.Equal(2, v2Json.GetProperty("data").GetProperty("versions").GetArrayLength());

        // Verify PostgreSQL trigger blocks mutating published metric_definition_versions
        await using (var conn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await conn.OpenAsync();
            await using var setTid = new NpgsqlCommand($"SELECT set_config('app.current_tenant_id', '{tenantId:D}', false);", conn);
            await setTid.ExecuteNonQueryAsync();
            var pgEx = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var upd = new NpgsqlCommand("UPDATE m_metrics.metric_definition_versions SET formula_expression = 'TAMPERED';", conn);
                await upd.ExecuteNonQueryAsync();
            });
            Assert.Contains("METRIC_VERSION_IMMUTABLE", pgEx.Message);
        }

        // 2. Ingest multi-currency observations (VND + USD) with stale timestamp (> 60m old vs 30m SLA)
        var periodStart = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero);
        var staleObservedAt = DateTimeOffset.UtcNow.AddHours(-3);

        var ingestResp = await client.PostAsJsonAsync("/api/v1/m/metrics/observations", new
        {
            items = new object[]
            {
                new
                {
                    metricCode = "REV_GROSS",
                    periodStartUtc = periodStart,
                    periodEndUtc = periodEnd,
                    reportingTimezone = "Asia/Ho_Chi_Minh",
                    currencyCode = "VND",
                    accountingBasis = AccountingBasis.Accrual,
                    valueNumeric = 10_000_000m,
                    sourceApplication = "NEXUS_F",
                    sourceReferenceId = "inv-batch-vn-01",
                    sourceDataVersion = 1,
                    observedAtUtc = staleObservedAt
                },
                new
                {
                    metricCode = "REV_GROSS",
                    periodStartUtc = periodStart,
                    periodEndUtc = periodEnd,
                    reportingTimezone = "Asia/Ho_Chi_Minh",
                    currencyCode = "USD",
                    accountingBasis = AccountingBasis.Accrual,
                    valueNumeric = 100m,
                    sourceApplication = "NEXUS_F",
                    sourceReferenceId = "inv-batch-us-01",
                    sourceDataVersion = 1,
                    observedAtUtc = staleObservedAt
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, ingestResp.StatusCode);

        // 3. Query WITHOUT normalizeToCurrency MUST fail with 422 METRIC_COMPARISON_INVALID
        var unnormalizedResp = await client.GetAsync("/api/v1/m/metrics/query?metricCode=REV_GROSS");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unnormalizedResp.StatusCode);
        Assert.Contains("METRIC_COMPARISON_INVALID", await unnormalizedResp.Content.ReadAsStringAsync());

        // 4. Query WITH normalizeToCurrency=VND converts 100 USD * 25,000 = 2,500,000 -> total 12,500,000 VND & flags Stale!
        var normalizedResp = await client.GetAsync("/api/v1/m/metrics/query?metricCode=REV_GROSS&normalizeToCurrency=VND");
        Assert.Equal(HttpStatusCode.OK, normalizedResp.StatusCode);
        var normData = (await normalizedResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(12_500_000m, normData.GetProperty("aggregatedValue").GetDecimal());
        Assert.Equal("VND", normData.GetProperty("effectiveCurrencyCode").GetString());
        Assert.Equal("Stale", normData.GetProperty("overallFreshnessStatus").GetString());

        // 5. Percentage metric with SUM rejected at creation & query
        var badPct = await client.PostAsJsonAsync("/api/v1/m/metrics/definitions", new
        {
            metricCode = "MARGIN_BAD",
            displayNameKey = "kpi.margin",
            descriptionKey = "desc",
            formulaExpression = "(rev - cost) / rev * 100",
            unit = MetricUnit.Percent,
            timeBasis = MetricTimeBasis.PeriodAvg,
            aggregationMethod = MetricAggregationMethod.Sum,
            accountingBasis = AccountingBasis.Accrual,
            sourceApplication = "NEXUS_F",
            freshnessSlaMinutes = 60
        });
        Assert.Equal(HttpStatusCode.BadRequest, badPct.StatusCode);
    }

    [Fact]
    public async Task IntegrationHub_EnforcesSecretVaultRefs_IdempotentInbox_BoundedDeadLetterReplay_Reconciliation_OrgScope_AndRls()
    {
        var allowedBranch = Guid.CreateVersion7();
        var forbiddenBranch = Guid.CreateVersion7();

        ConfigureContext(
            new OrganizationScopeSnapshot(true, [], [], []),
            NexusMPermissions.KpiRead,
            NexusMPermissions.KpiManage,
            NexusMPermissions.IntegrationRead,
            NexusMPermissions.IntegrationManage);

        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var clientA = fixture.Factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(Guid.CreateVersion7(), "ops@a.local", tenantA));

        var clientB = fixture.Factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(Guid.CreateVersion7(), "ops@b.local", tenantB));

        // 1. Plaintext secret rejected (422 CONNECTOR_SECRET_PLAINTEXT_FORBIDDEN); vault:// accepted
        var badConn = await clientA.PostAsJsonAsync("/api/v1/m/integrations/connectors", new
        {
            code = "FIN_SYNC",
            name = "NEXUS F Connector",
            sourceApplication = "NEXUS_F",
            secretReference = "plaintext-api-secret-123"
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badConn.StatusCode);

        var okConn = await clientA.PostAsJsonAsync("/api/v1/m/integrations/connectors", new
        {
            code = "EXP_SYNC",
            name = "NEXUS E Connector",
            sourceApplication = "NEXUS_E",
            secretReference = "vault://nexus/m/express-connector"
        });
        Assert.Equal(HttpStatusCode.Created, okConn.StatusCode);

        // 2. Define shipment count KPI & ingest via EventEnvelope (idempotent!)
        await clientA.PostAsJsonAsync("/api/v1/m/metrics/definitions", new
        {
            metricCode = "SHIPMENTS_DELIVERED",
            displayNameKey = "kpi.shipments",
            descriptionKey = "Delivered shipments",
            formulaExpression = "COUNT(nexus_e.shipment.delivered)",
            unit = MetricUnit.Count,
            timeBasis = MetricTimeBasis.PeriodSum,
            aggregationMethod = MetricAggregationMethod.Sum,
            accountingBasis = AccountingBasis.Operational,
            sourceApplication = "NEXUS_E",
            freshnessSlaMinutes = 60
        });

        var eventId = Guid.CreateVersion7();
        var pStart = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);
        var pEnd = new DateTimeOffset(2026, 11, 27, 23, 59, 59, TimeSpan.Zero);

        var envelope = new
        {
            eventId,
            eventType = "express.summary.published.v1",
            schemaVersion = 1,
            producer = "NEXUS_E",
            occurredAt = DateTimeOffset.UtcNow,
            observations = new[]
            {
                new
                {
                    metricCode = "SHIPMENTS_DELIVERED",
                    branchId = allowedBranch,
                    periodStartUtc = pStart,
                    periodEndUtc = pEnd,
                    reportingTimezone = "UTC",
                    accountingBasis = AccountingBasis.Operational,
                    valueNumeric = 120m,
                    sourceApplication = "NEXUS_E",
                    sourceReferenceId = "exp-daily-01",
                    sourceDataVersion = 1
                }
            }
        };

        var ev1 = await clientA.PostAsJsonAsync("/api/v1/m/integrations/events/ingest", envelope);
        Assert.Equal(HttpStatusCode.OK, ev1.StatusCode);
        Assert.Equal(1, (await ev1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("insertedObservations").GetInt32());

        // Duplicate replay of exact same EventId MUST be deduplicated (0 inserted)
        var evDup = await clientA.PostAsJsonAsync("/api/v1/m/integrations/events/ingest", envelope);
        Assert.Equal(HttpStatusCode.OK, evDup.StatusCode);
        var evDupData = (await evDup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.True(evDupData.GetProperty("wasDuplicate").GetBoolean());
        Assert.Equal(0, evDupData.GetProperty("insertedObservations").GetInt32());

        // 3. Reconciliation: MATCHED vs DISCREPANCY
        var reconMatch = await clientA.PostAsJsonAsync("/api/v1/m/integrations/reconciliations", new
        {
            metricCode = "SHIPMENTS_DELIVERED",
            sourceApplication = "NEXUS_E",
            periodStartUtc = pStart,
            periodEndUtc = pEnd,
            expectedCount = 1,
            expectedSum = 120m
        });
        Assert.Equal("MATCHED", (await reconMatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("status").GetString());

        var reconMismatch = await clientA.PostAsJsonAsync("/api/v1/m/integrations/reconciliations", new
        {
            metricCode = "SHIPMENTS_DELIVERED",
            sourceApplication = "NEXUS_E",
            periodStartUtc = pStart,
            periodEndUtc = pEnd,
            expectedCount = 1,
            expectedSum = 999m
        });
        Assert.Equal("DISCREPANCY", (await reconMismatch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("status").GetString());

        // 4. Dead-Letter Queue bounded manual retry cap (max 3 retries)
        var badEvent = await clientA.PostAsJsonAsync("/api/v1/m/integrations/events/ingest", new
        {
            eventId = Guid.CreateVersion7(),
            eventType = "express.summary.published.v1",
            schemaVersion = 1,
            producer = "NEXUS_E",
            occurredAt = DateTimeOffset.UtcNow,
            observations = new[]
            {
                new
                {
                    metricCode = "UNKNOWN_METRIC_XYZ",
                    periodStartUtc = pStart,
                    periodEndUtc = pEnd,
                    reportingTimezone = "UTC",
                    accountingBasis = AccountingBasis.Operational,
                    valueNumeric = 10m,
                    sourceApplication = "NEXUS_E",
                    sourceReferenceId = "bad-ref-1",
                    sourceDataVersion = 1
                }
            }
        });
        var dlqId = (await badEvent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("deadLetterId").GetGuid();

        for (var i = 0; i < 3; i++)
        {
            var r = await clientA.PostAsync($"/api/v1/m/integrations/dead-letters/{dlqId}/retry", null);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        var cappedRetry = await clientA.PostAsync($"/api/v1/m/integrations/dead-letters/{dlqId}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, cappedRetry.StatusCode);

        // 5. Organization Scope boundary: restricted branch manager querying forbiddenBranch -> 403 ORGANIZATION_SCOPE_DENIED
        ConfigureContext(
            new OrganizationScopeSnapshot(false, [], [allowedBranch], []),
            NexusMPermissions.KpiRead);
        var scopeDenied = await clientA.GetAsync($"/api/v1/m/metrics/query?metricCode=SHIPMENTS_DELIVERED&branchId={forbiddenBranch:D}");
        Assert.Equal(HttpStatusCode.Forbidden, scopeDenied.StatusCode);
        Assert.Contains("ORGANIZATION_SCOPE_DENIED", await scopeDenied.Content.ReadAsStringAsync());

        // 6. Cross-tenant FORCE RLS: Tenant B sees 0 KPI definitions and 0 connectors
        ConfigureContext(null, NexusMPermissions.KpiRead, NexusMPermissions.IntegrationRead);
        var defsB = await clientB.GetAsync("/api/v1/m/metrics/definitions");
        Assert.Equal(0, (await defsB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetArrayLength());
        var connsB = await clientB.GetAsync("/api/v1/m/integrations/connectors");
        Assert.Equal(0, (await connsB.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetArrayLength());
    }
}
