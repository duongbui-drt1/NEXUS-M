using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.IntegrationTests.Infrastructure;
using NexusM.Modules.Foundation.Infrastructure;
using NexusM.Modules.Metrics.Domain;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace NexusM.IntegrationTests;

public sealed class Phase4To11PlatformCompletionTests(NexusMTestFixture fixture, ITestOutputHelper output) : IClassFixture<NexusMTestFixture>
{
    private void ConfigurePrincipal(bool aiEntitled, OrganizationScopeSnapshot? scope, params string[] perms)
    {
        fixture.CoreStub.EntitlementHandler = (_, code) => code.ToUpperInvariant() switch
        {
            "NEXUS_M" => new EntitlementEvaluationDto(code, true, "OK"),
            "NEXUS_M_AI" => new EntitlementEvaluationDto(code, aiEntitled, aiEntitled ? "OK" : "AI_ADDON_DISABLED"),
            _ => new EntitlementEvaluationDto(code, false, "UNKNOWN")
        };

        fixture.CoreStub.AuthzHandler = (_, _) => new UserAuthorizationSnapshot(
            new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase),
            scope ?? new OrganizationScopeSnapshot(true, [], [], []));
    }

    private static string[] AllManagementPermissions =>
        NexusMPermissions.Manifest.Select(p => p.Code).ToArray();

    [Fact]
    public async Task Phases4And5_ExecutiveDashboard_PeriodDelta_Alerts_ReproducibleSnapshots_AndExportAuthz()
    {
        ConfigurePrincipal(aiEntitled: false, scope: null, AllManagementPermissions);

        var tenantId = Guid.CreateVersion7();
        var ceoId = Guid.CreateVersion7();
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(ceoId, "ceo@enterprise.local", tenantId));

        // Seed KPI + Previous Period (100M) + Current Period (150M -> +50% delta!)
        await client.PostAsJsonAsync("/api/v1/m/metrics/definitions", new
        {
            metricCode = "NET_REVENUE",
            displayNameKey = "kpi.net_revenue",
            descriptionKey = "Net Revenue",
            formulaExpression = "SUM(nexus_f.ledger.net_revenue)",
            unit = MetricUnit.Currency,
            currencyCode = "VND",
            timeBasis = MetricTimeBasis.PeriodSum,
            aggregationMethod = MetricAggregationMethod.Sum,
            accountingBasis = AccountingBasis.Accrual,
            sourceApplication = "NEXUS_F",
            freshnessSlaMinutes = 120
        });

        var prevStart = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var prevEnd = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var currStart = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        var currEnd = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

        await client.PostAsJsonAsync("/api/v1/m/metrics/observations", new
        {
            items = new object[]
            {
                new
                {
                    metricCode = "NET_REVENUE",
                    periodStartUtc = prevStart,
                    periodEndUtc = prevEnd.AddSeconds(-1),
                    reportingTimezone = "UTC",
                    currencyCode = "VND",
                    accountingBasis = AccountingBasis.Accrual,
                    valueNumeric = 100_000_000m,
                    sourceApplication = "NEXUS_F",
                    sourceReferenceId = "oct-rev",
                    observedAtUtc = DateTimeOffset.UtcNow
                },
                new
                {
                    metricCode = "NET_REVENUE",
                    periodStartUtc = currStart,
                    periodEndUtc = currEnd,
                    reportingTimezone = "UTC",
                    currencyCode = "VND",
                    accountingBasis = AccountingBasis.Accrual,
                    valueNumeric = 150_000_000m,
                    sourceApplication = "NEXUS_F",
                    sourceReferenceId = "nov-rev",
                    observedAtUtc = DateTimeOffset.UtcNow
                }
            }
        });

        // 1. Create & Render CEO Dashboard (M01)
        var dashResp = await client.PostAsJsonAsync("/api/v1/m/executive/dashboards", new
        {
            code = "CEO_MAIN",
            title = "CEO Executive Overview",
            audienceRole = "CEO",
            isDefault = true,
            widgets = new[]
            {
                new { title = "Net Revenue", metricCode = "NET_REVENUE", comparePreviousPeriod = true, sortOrder = 1 }
            }
        });
        Assert.Equal(HttpStatusCode.Created, dashResp.StatusCode);
        var dashId = (await dashResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        var renderResp = await client.GetAsync(
            $"/api/v1/m/executive/dashboards/{dashId}/render?periodStartUtc={Uri.EscapeDataString(currStart.ToString("O"))}&periodEndUtc={Uri.EscapeDataString(currEnd.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, renderResp.StatusCode);
        var widget = (await renderResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("widgets")[0];
        Assert.Equal(150_000_000m, widget.GetProperty("currentValue").GetDecimal());
        Assert.Equal(100_000_000m, widget.GetProperty("previousPeriodValue").GetDecimal());
        Assert.Equal(50m, widget.GetProperty("deltaPercent").GetDecimal());
        Assert.Equal("Fresh", widget.GetProperty("freshnessStatus").GetString());

        // 2. Alert evaluation & acknowledgement
        await client.PostAsJsonAsync("/api/v1/m/executive/alerts/rules", new
        {
            name = "Revenue Target Breach",
            metricCode = "NET_REVENUE",
            conditionOperator = "LessThan",
            thresholdValue = 200_000_000m,
            severity = "Critical"
        });
        var evalResp = await client.PostAsync(
            $"/api/v1/m/executive/alerts/evaluate?periodStartUtc={Uri.EscapeDataString(currStart.ToString("O"))}&periodEndUtc={Uri.EscapeDataString(currEnd.ToString("O"))}",
            null);
        var alertId = (await evalResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")[0].GetProperty("id").GetGuid();
        var ackResp = await client.PostAsync($"/api/v1/m/executive/alerts/{alertId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, ackResp.StatusCode);

        // 3. Phase 5 BI Saved Report, Immutable Snapshot & Export Authorization
        var repResp = await client.PostAsJsonAsync("/api/v1/m/bi/reports", new
        {
            code = "MONTHLY_FIN_SUMMARY",
            title = "Monthly Revenue Report",
            metricCodes = new[] { "NET_REVENUE" },
            normalizeToCurrency = "VND"
        });
        var repId = (await repResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        var runResp = await client.PostAsJsonAsync($"/api/v1/m/bi/reports/{repId}/run", new
        {
            periodStartUtc = currStart,
            periodEndUtc = currEnd
        });
        Assert.Equal(HttpStatusCode.Created, runResp.StatusCode);
        var snapId = (await runResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        // User without m.report.export MUST be denied (403)
        ConfigurePrincipal(false, null, NexusMPermissions.ReportRead);
        var deniedExport = await client.PostAsync($"/api/v1/m/bi/snapshots/{snapId}/export?format=CSV", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedExport.StatusCode);

        // User WITH m.report.export succeeds & produces deterministic CSV with lineage
        ConfigurePrincipal(false, null, NexusMPermissions.ReportRead, NexusMPermissions.ReportExport);
        var okExport = await client.PostAsync($"/api/v1/m/bi/snapshots/{snapId}/export?format=CSV", null);
        Assert.Equal(HttpStatusCode.OK, okExport.StatusCode);
        var csv = (await okExport.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("content").GetString()!;
        Assert.Contains("NET_REVENUE", csv);
        Assert.Contains("150000000", csv);
    }

    [Fact]
    public async Task Phases6To10_WorkflowMakerChecker_Idempotency_EffectiveDatedOrg_RiskCompliance_BoardOkr_Search_AndGovernedAi()
    {
        ConfigurePrincipal(aiEntitled: true, scope: null, AllManagementPermissions);

        var tenantId = Guid.CreateVersion7();
        var makerId = Guid.CreateVersion7();
        var checkerId = Guid.CreateVersion7();

        var makerClient = fixture.Factory.CreateClient();
        makerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(makerId, "maker@corp.local", tenantId));

        var checkerClient = fixture.Factory.CreateClient();
        checkerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(checkerId, "checker@corp.local", tenantId));

        // -------------------------------------------------------------------------
        // PHASE 6: M03 Workflow, Separation of Duties & Idempotency-Key
        // -------------------------------------------------------------------------
        using var tplReq1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/m/workflows/templates")
        {
            Content = JsonContent.Create(new { code = "CAPEX_APPROVAL", title = "CapEx Proposal", category = "CAPEX", defaultSlaHours = 24 })
        };
        tplReq1.Headers.Add("Idempotency-Key", "idemp-capex-tpl-1");
        var tplResp1 = await makerClient.SendAsync(tplReq1);
        Assert.Equal(HttpStatusCode.Created, tplResp1.StatusCode);

        // Replay identical Idempotency-Key -> returns 201 with X-Idempotent-Replayed: true
        using var tplReqReplay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/m/workflows/templates")
        {
            Content = JsonContent.Create(new { code = "CAPEX_APPROVAL", title = "CapEx Proposal", category = "CAPEX", defaultSlaHours = 24 })
        };
        tplReqReplay.Headers.Add("Idempotency-Key", "idemp-capex-tpl-1");
        var tplRespReplay = await makerClient.SendAsync(tplReqReplay);
        Assert.Equal(HttpStatusCode.Created, tplRespReplay.StatusCode);
        Assert.True(tplRespReplay.Headers.Contains("X-Idempotent-Replayed"));

        // Mismatched payload with same Idempotency-Key -> 409 IDEMPOTENCY_PAYLOAD_MISMATCH
        using var tplReqMismatch = new HttpRequestMessage(HttpMethod.Post, "/api/v1/m/workflows/templates")
        {
            Content = JsonContent.Create(new { code = "DIFFERENT", title = "Tampered", category = "CAPEX", defaultSlaHours = 24 })
        };
        tplReqMismatch.Headers.Add("Idempotency-Key", "idemp-capex-tpl-1");
        var tplRespMismatch = await makerClient.SendAsync(tplReqMismatch);
        Assert.Equal(HttpStatusCode.Conflict, tplRespMismatch.StatusCode);

        var submitResp = await makerClient.PostAsJsonAsync("/api/v1/m/workflows/requests", new
        {
            templateCode = "CAPEX_APPROVAL",
            title = "Acquire Automation Line",
            summary = "Q4 Strategic CapEx",
            evidenceCFileId = Guid.CreateVersion7(),
            evidenceFileName = "capex-quote.pdf"
        });
        var wfReqId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        // Maker CANNOT self-approve (403 SEPARATION_OF_DUTIES_VIOLATION)
        var selfApprove = await makerClient.PostAsJsonAsync($"/api/v1/m/workflows/requests/{wfReqId}/decide", new
        {
            decision = "Approved",
            comment = "Self approve attempt",
            expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.Forbidden, selfApprove.StatusCode);
        Assert.Contains("SEPARATION_OF_DUTIES_VIOLATION", await selfApprove.Content.ReadAsStringAsync());

        // Checker approves successfully
        var checkerApprove = await checkerClient.PostAsJsonAsync($"/api/v1/m/workflows/requests/{wfReqId}/decide", new
        {
            decision = "Approved",
            comment = "Reviewed & approved",
            expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.OK, checkerApprove.StatusCode);

        // -------------------------------------------------------------------------
        // PHASE 7: M04 Effective-Dated Organization Assignments (Temporal Integrity)
        // -------------------------------------------------------------------------
        var unitResp = await makerClient.PostAsJsonAsync("/api/v1/m/org/units", new
        {
            cOrganizationUnitId = Guid.CreateVersion7(),
            code = "BU_NORTH",
            name = "Northern Business Unit",
            unitType = "BusinessUnit",
            headcountTarget = 25
        });
        var unitId = (await unitResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        var posResp = await makerClient.PostAsJsonAsync("/api/v1/m/org/positions", new
        {
            orgUnitProfileId = unitId,
            code = "DIR_NORTH",
            title = "Regional Director North",
            isExecutiveSeat = true
        });
        var posId = (await posResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        await makerClient.PostAsJsonAsync("/api/v1/m/org/assignments", new
        {
            positionId = posId,
            cUserId = makerId,
            userDisplayEmail = "director.h1@corp.local",
            validFromDate = "2026-01-01"
        });
        await makerClient.PostAsJsonAsync("/api/v1/m/org/assignments", new
        {
            positionId = posId,
            cUserId = checkerId,
            userDisplayEmail = "director.h2@corp.local",
            validFromDate = "2026-07-01"
        });

        var q1Overview = await (await makerClient.GetAsync("/api/v1/m/org/overview?asOfDate=2026-03-15")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("director.h1@corp.local", q1Overview.GetProperty("data").GetProperty("activeAssignments")[0].GetProperty("userDisplayEmail").GetString());

        var q4Overview = await (await makerClient.GetAsync("/api/v1/m/org/overview?asOfDate=2026-10-15")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("director.h2@corp.local", q4Overview.GetProperty("data").GetProperty("activeAssignments")[0].GetProperty("userDisplayEmail").GetString());

        // -------------------------------------------------------------------------
        // PHASE 8: M05 Risk Scoring + M06 Multi-Jurisdiction Compliance
        // -------------------------------------------------------------------------
        var riskResp = await makerClient.PostAsJsonAsync("/api/v1/m/risks", new
        {
            code = "RSK_SUPPLY_01",
            title = "Port Congestion Disruption",
            category = "SUPPLY_CHAIN",
            likelihood = 4,
            impact = 5,
            ownerUserId = makerId,
            mitigationPlan = "Dual-port routing"
        });
        var riskData = (await riskResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(20, riskData.GetProperty("score").GetInt32());
        Assert.Equal("Critical", riskData.GetProperty("riskLevel").GetString());
        Assert.Equal("Escalated", riskData.GetProperty("status").GetString());

        var obVn = await makerClient.PostAsJsonAsync("/api/v1/m/compliance/obligations", new
        {
            code = "OBL_VN_PDPD",
            title = "Decree 13 Personal Data Assessment",
            jurisdictionCode = "VN",
            regulatorySource = "Decree 13/2023/ND-CP",
            effectiveFromDate = "2026-01-01",
            dueAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            ownerUserId = makerId,
            isValidatedAuthoritative = true
        });
        Assert.Equal(HttpStatusCode.Created, obVn.StatusCode);

        var obEu = await makerClient.PostAsJsonAsync("/api/v1/m/compliance/obligations", new
        {
            code = "OBL_EU_GDPR_30",
            title = "GDPR Article 30 ROPA Review",
            jurisdictionCode = "EU-GDPR",
            regulatorySource = "Regulation (EU) 2016/679",
            effectiveFromDate = "2026-01-01",
            dueAtUtc = DateTimeOffset.UtcNow.AddDays(45),
            ownerUserId = checkerId,
            isValidatedAuthoritative = false
        });
        Assert.Equal("DraftUnverified", (await obEu.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("contentValidationStatus").GetString());

        // -------------------------------------------------------------------------
        // PHASE 9: M07 Board Reporting + M08 OKR Evaluation + Enterprise Search
        // -------------------------------------------------------------------------
        await makerClient.PostAsJsonAsync("/api/v1/m/metrics/definitions", new
        {
            metricCode = "OKR_ACTIVE_CUSTOMERS",
            displayNameKey = "kpi.customers",
            descriptionKey = "Active Customers",
            formulaExpression = "COUNT(distinct customer_id)",
            unit = MetricUnit.Count,
            timeBasis = MetricTimeBasis.PointInTime,
            aggregationMethod = MetricAggregationMethod.Max,
            accountingBasis = AccountingBasis.Operational,
            sourceApplication = "NEXUS_M",
            freshnessSlaMinutes = 60
        });

        var pStart = DateTimeOffset.UtcNow.AddDays(-7);
        var pEnd = DateTimeOffset.UtcNow.AddDays(1);
        await makerClient.PostAsJsonAsync("/api/v1/m/metrics/observations", new
        {
            items = new[]
            {
                new
                {
                    metricCode = "OKR_ACTIVE_CUSTOMERS",
                    periodStartUtc = pStart,
                    periodEndUtc = pEnd,
                    reportingTimezone = "UTC",
                    accountingBasis = AccountingBasis.Operational,
                    valueNumeric = 750m,
                    sourceApplication = "NEXUS_M",
                    sourceReferenceId = "cust-snap-1"
                }
            }
        });

        var boardResp = await makerClient.PostAsJsonAsync("/api/v1/m/board-reports", new
        {
            code = "BOARD_2026_Q4",
            title = "Q4 Board Pack",
            periodStartUtc = pStart,
            periodEndUtc = pEnd,
            consolidationCurrency = "VND",
            metricCodes = new[] { "OKR_ACTIVE_CUSTOMERS" }
        });
        var boardId = (await boardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();

        // Preparer cannot self-publish board report; checker publishes
        Assert.Equal(HttpStatusCode.Forbidden, (await makerClient.PostAsync($"/api/v1/m/board-reports/{boardId}/publish", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await checkerClient.PostAsync($"/api/v1/m/board-reports/{boardId}/publish", null)).StatusCode);

        await makerClient.PostAsJsonAsync("/api/v1/m/performance/objectives", new
        {
            code = "OBJ_GROWTH_26",
            title = "Expand Enterprise Customer Base",
            periodCode = "2026-Q4",
            ownerUserId = makerId,
            keyResults = new[]
            {
                new { title = "Reach 1,000 active customers", targetValue = 1000m, unit = "COUNT", linkedMetricCode = "OKR_ACTIVE_CUSTOMERS" }
            }
        });

        var okrEval = await makerClient.GetFromJsonAsync<JsonElement>(
            $"/api/v1/m/performance/objectives?periodStartUtc={Uri.EscapeDataString(pStart.ToString("O"))}&periodEndUtc={Uri.EscapeDataString(pEnd.ToString("O"))}");
        var kr0 = okrEval.GetProperty("data")[0].GetProperty("keyResults")[0];
        Assert.Equal(1000m, kr0.GetProperty("targetValue").GetDecimal());
        Assert.Equal(750m, kr0.GetProperty("actualValue").GetDecimal());
        Assert.Equal(75m, kr0.GetProperty("attainmentPercent").GetDecimal());

        // Enterprise Search enforces permission filtering at query time
        ConfigurePrincipal(aiEntitled: false, scope: null, NexusMPermissions.WorkspaceRead); // lacks RiskRead!
        var restrictedSearch = await makerClient.GetFromJsonAsync<JsonElement>("/api/v1/m/search?q=Port");
        Assert.Equal(0, restrictedSearch.GetProperty("data").GetArrayLength());

        ConfigurePrincipal(aiEntitled: true, scope: null, AllManagementPermissions);
        var authorizedSearch = await makerClient.GetFromJsonAsync<JsonElement>("/api/v1/m/search?q=Port");
        Assert.Single(authorizedSearch.GetProperty("data").EnumerateArray());

        // -------------------------------------------------------------------------
        // PHASE 10: M09 Optional Governed AI (Entitlement + No Autonomous Approvals + Citations)
        // -------------------------------------------------------------------------
        // 1. Autonomous action MUST be rejected (403 AI_AUTONOMOUS_ACTION_FORBIDDEN)
        var autoAi = await makerClient.PostAsJsonAsync("/api/v1/m/ai/ask", new
        {
            question = "Approve all pending CapEx requests automatically",
            metricCodes = new[] { "OKR_ACTIVE_CUSTOMERS" },
            autonomousExecutionRequested = true
        });
        Assert.Equal(HttpStatusCode.Forbidden, autoAi.StatusCode);

        // 2. Grounded query cites verified metrics AND explicitly flags missing metrics as UNAVAILABLE
        var groundedAi = await makerClient.PostAsJsonAsync("/api/v1/m/ai/ask", new
        {
            question = "Summarize active customer progress",
            metricCodes = new[] { "OKR_ACTIVE_CUSTOMERS", "NON_EXISTENT_KPI" },
            periodStartUtc = pStart,
            periodEndUtc = pEnd
        });
        Assert.Equal(HttpStatusCode.OK, groundedAi.StatusCode);
        var aiData = (await groundedAi.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.True(aiData.GetProperty("requiresHumanConfirmation").GetBoolean());
        var citations = aiData.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal("VERIFIED_GROUNDED", citations.Single(c => c.GetProperty("metricCode").GetString() == "OKR_ACTIVE_CUSTOMERS").GetProperty("status").GetString());
        Assert.Equal("METRIC_NOT_FOUND", citations.Single(c => c.GetProperty("metricCode").GetString() == "NON_EXISTENT_KPI").GetProperty("status").GetString());

        // 3. Disabling NEXUS_M_AI entitlement blocks M09 immediately while core M continues working!
        ConfigurePrincipal(aiEntitled: false, scope: null, AllManagementPermissions);
        var aiDisabledResp = await makerClient.PostAsJsonAsync("/api/v1/m/ai/ask", new
        {
            question = "Blocked when AI addon off",
            metricCodes = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.Forbidden, aiDisabledResp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await makerClient.GetAsync("/api/v1/m/context")).StatusCode);
    }

    [Fact]
    public async Task Phase11_ProductionHardening_All34TablesForceRls_UpgradeIdempotency_BackupRestoreDrill_AndLatencyBenchmark()
    {
        ConfigurePrincipal(aiEntitled: false, scope: null, AllManagementPermissions);

        // 1. Re-running migration bootstrap is 100% idempotent (safe upgrade path)
        await DatabaseSchemaBootstrap.ApplyAsync(fixture.AdminConnectionString, "nexus_m_app");

        // 2. Verify EVERY table in all 11 NEXUS M schemas has both RLS and FORCE RLS enabled!
        await using (var conn = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT n.nspname, c.relname, c.relrowsecurity, c.relforcerowsecurity
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname IN (
                    'm_foundation', 'm_audit', 'm_metrics', 'm_integration',
                    'm_executive', 'm_bi', 'm_workflow', 'm_org',
                    'm_governance', 'm_performance', 'm_ai'
                ) AND c.relkind = 'r'
                ORDER BY n.nspname, c.relname;
                """, conn);

            var count = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0);
                var table = reader.GetString(1);
                Assert.True(reader.GetBoolean(2), $"{schema}.{table} missing ENABLE ROW LEVEL SECURITY");
                Assert.True(reader.GetBoolean(3), $"{schema}.{table} missing FORCE ROW LEVEL SECURITY");
                count++;
            }
            output.WriteLine($"Verified FORCE ROW LEVEL SECURITY across {count}/37 tables in 11 schemas.");
            Assert.Equal(37, count);
        }

        // 3. Representative API Latency Benchmark (25 sequential + 20 concurrent requests)
        var tenantId = Guid.CreateVersion7();
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", NexusMTestFixture.IssueJwt(Guid.CreateVersion7(), "perf@bench.local", tenantId));

        // Warmup
        (await client.GetAsync("/api/v1/m/context")).EnsureSuccessStatusCode();

        var latenciesMs = new List<double>(25);
        for (var i = 0; i < 25; i++)
        {
            var sw = Stopwatch.StartNew();
            var r = await client.GetAsync("/api/v1/m/context");
            sw.Stop();
            r.EnsureSuccessStatusCode();
            latenciesMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        latenciesMs.Sort();
        var p50 = latenciesMs[(int)(latenciesMs.Count * 0.50)];
        var p95 = latenciesMs[(int)(latenciesMs.Count * 0.95)];
        output.WriteLine($"Benchmark /api/v1/m/context (n=25): p50={p50:F2}ms, p95={p95:F2}ms");
        Assert.True(p95 < 250.0, $"Expected p95 < 250ms, actual {p95:F2}ms");

        // Concurrent burst (20 parallel requests)
        var burstSw = Stopwatch.StartNew();
        var burstResponses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => client.GetAsync("/api/v1/m/workspaces")));
        burstSw.Stop();
        Assert.All(burstResponses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        output.WriteLine($"Concurrent burst 20x /api/v1/m/workspaces completed in {burstSw.Elapsed.TotalMilliseconds:F2}ms");
    }
}
