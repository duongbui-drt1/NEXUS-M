using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.IntegrationTests.Infrastructure;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;
using NexusM.SharedKernel.Security;
using Npgsql;
using Xunit;

namespace NexusM.IntegrationTests;

public sealed class Phase2FoundationAndSecurityTests(NexusMTestFixture fixture) : IClassFixture<NexusMTestFixture>
{
    private void ConfigureAllPermissions(params string[] perms)
    {
        fixture.CoreStub.EntitlementHandler = (_, code) =>
            new EntitlementEvaluationDto(code, code == "NEXUS_M", code == "NEXUS_M" ? "OK" : "NOT_IN_PLAN");
        fixture.CoreStub.AuthzHandler = (_, _) =>
            new UserAuthorizationSnapshot(
                new HashSet<string>(perms, StringComparer.OrdinalIgnoreCase),
                new OrganizationScopeSnapshot(true, [], [], []));
    }

    [Fact]
    public async Task CrossTenantRlsIsolation_TenantACannotReadOrMutateTenantBWorkspacesOrAudit()
    {
        ConfigureAllPermissions(NexusMPermissions.WorkspaceRead, NexusMPermissions.WorkspaceManage, NexusMPermissions.AuditRead);

        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();

        var clientA = fixture.Factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NexusMTestFixture.IssueJwt(userA, "a@tenant-a.local", tenantA));

        var clientB = fixture.Factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NexusMTestFixture.IssueJwt(userB, "b@tenant-b.local", tenantB));

        // Tenant A creates a workspace
        var createA = await clientA.PostAsJsonAsync("/api/v1/m/workspaces", new
        {
            code = "EXEC_A",
            name = "Tenant A Executive",
            defaultCurrency = "VND",
            orgMode = 2
        });
        Assert.Equal(HttpStatusCode.Created, createA.StatusCode);
        var wsAId = (await createA.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // Tenant B lists workspaces — must NEVER see Tenant A's workspace
        var listB = await clientB.GetAsync("/api/v1/m/workspaces");
        Assert.Equal(HttpStatusCode.OK, listB.StatusCode);
        var listBBody = await listB.Content.ReadAsStringAsync();
        Assert.DoesNotContain("EXEC_A", listBBody);
        Assert.DoesNotContain(wsAId.ToString("D"), listBBody);

        // Tenant B tries to update Tenant A's workspace by direct ID — blocked by RLS (404)
        var hijackResp = await clientB.PutAsJsonAsync($"/api/v1/m/workspaces/{wsAId}", new
        {
            name = "Hijacked",
            description = "Cross-tenant attack",
            defaultLocale = "en-US",
            defaultTimezone = "UTC",
            defaultCurrency = "USD",
            orgMode = 1,
            expectedVersion = 1
        });
        Assert.Equal(HttpStatusCode.NotFound, hijackResp.StatusCode);

        // Tenant B audit query must NOT see Tenant A's audit events
        var auditB = await clientB.GetAsync("/api/v1/m/audit");
        Assert.Equal(HttpStatusCode.OK, auditB.StatusCode);
        var auditBBody = await auditB.Content.ReadAsStringAsync();
        Assert.DoesNotContain("a@tenant-a.local", auditBBody);
    }

    [Fact]
    public async Task SecurityGuards_RejectTenantSpoofing_DeniedEntitlement_MissingPermission_AndFailClosedOnCoreOutage()
    {
        var tenantId = Guid.CreateVersion7();
        var otherTenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var token = NexusMTestFixture.IssueJwt(userId, "user@demo.local", tenantId);

        // 1. Tenant Spoofing Header rejected (403 TENANT_SPOOFING_DETECTED)
        ConfigureAllPermissions(NexusMPermissions.WorkspaceRead);
        var spoofClient = fixture.Factory.CreateClient();
        spoofClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        spoofClient.DefaultRequestHeaders.Add("X-Tenant-Id", otherTenantId.ToString("D"));
        var spoofResp = await spoofClient.GetAsync("/api/v1/m/context");
        Assert.Equal(HttpStatusCode.Forbidden, spoofResp.StatusCode);
        Assert.Contains("TENANT_SPOOFING_DETECTED", await spoofResp.Content.ReadAsStringAsync());

        // 2. Entitlement Denied (403 ENTITLEMENT_DENIED)
        fixture.CoreStub.EntitlementHandler = (_, code) => new EntitlementEvaluationDto(code, false, "SUBSCRIPTION_SUSPENDED");
        var entClient = fixture.Factory.CreateClient();
        entClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var entResp = await entClient.GetAsync("/api/v1/m/context");
        Assert.Equal(HttpStatusCode.Forbidden, entResp.StatusCode);
        Assert.Contains("ENTITLEMENT_DENIED", await entResp.Content.ReadAsStringAsync());

        // 3. Missing Permission Denied (403 PERMISSION_DENIED)
        ConfigureAllPermissions(NexusMPermissions.WorkspaceRead); // lacks WorkspaceManage
        var permResp = await entClient.PostAsJsonAsync("/api/v1/m/workspaces", new { code = "NO_PERM", name = "Blocked" });
        Assert.Equal(HttpStatusCode.Forbidden, permResp.StatusCode);

        // 4. Fail-Closed when NEXUS C is unreachable (503)
        fixture.CoreStub.EntitlementHandler = (_, _) => Error.ServiceUnavailable("NEXUS_C_UNREACHABLE", "Core down");
        var outageResp = await entClient.GetAsync("/api/v1/m/context");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, outageResp.StatusCode);
    }

    [Fact]
    public async Task ManagementContext_AutoProvisionsDefaultWorkspace_SupportsOptimisticConcurrency_AndEnforcesImmutableAudit()
    {
        ConfigureAllPermissions(NexusMPermissions.WorkspaceRead, NexusMPermissions.WorkspaceManage, NexusMPermissions.AuditRead);

        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", NexusMTestFixture.IssueJwt(userId, "ceo@demo.local", tenantId));

        // 1. GET /api/v1/m/context auto-provisions default workspace & returns dynamic modules
        var ctxResp = await client.GetAsync("/api/v1/m/context");
        Assert.Equal(HttpStatusCode.OK, ctxResp.StatusCode);
        var ctxJson = await ctxResp.Content.ReadFromJsonAsync<JsonElement>();
        var activeWs = ctxJson.GetProperty("data").GetProperty("activeWorkspace");
        Assert.Equal("DEFAULT", activeWs.GetProperty("code").GetString());
        var wsId = activeWs.GetProperty("id").GetGuid();

        var modules = ctxJson.GetProperty("data").GetProperty("modules").EnumerateArray().ToList();
        var m00 = modules.Single(m => m.GetProperty("code").GetString() == "M00");
        var m09 = modules.Single(m => m.GetProperty("code").GetString() == "M09");
        Assert.True(m00.GetProperty("isAccessible").GetBoolean());
        Assert.False(m09.GetProperty("isEntitled").GetBoolean());

        // 2. Optimistic concurrency check on workspace update
        var staleUpdate = await client.PutAsJsonAsync($"/api/v1/m/workspaces/{wsId}", new
        {
            name = "Stale Update",
            description = "",
            defaultLocale = "vi-VN",
            defaultTimezone = "Asia/Ho_Chi_Minh",
            defaultCurrency = "VND",
            orgMode = 1,
            expectedVersion = 99
        });
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);

        // 3. Verify m_audit.business_audit_events is immutable at PostgreSQL level (trigger + privileges)
        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        await using var setTid = new NpgsqlCommand($"SELECT set_config('app.current_tenant_id', '{tenantId:D}', false);", conn);
        await setTid.ExecuteNonQueryAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var delCmd = new NpgsqlCommand("DELETE FROM m_audit.business_audit_events;", conn);
            await delCmd.ExecuteNonQueryAsync();
        });
        Assert.Contains("AUDIT_IMMUTABLE", ex.Message);
    }
}
