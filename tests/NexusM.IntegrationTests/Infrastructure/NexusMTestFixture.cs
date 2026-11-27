using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.Modules.Foundation.Infrastructure;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;
using Npgsql;
using Xunit;

namespace NexusM.IntegrationTests.Infrastructure;

public sealed class StubNexusCoreClient : INexusCoreClient
{
    public Func<Guid, string, Result<EntitlementEvaluationDto>> EntitlementHandler { get; set; }
        = (_, code) => new EntitlementEvaluationDto(code, code == "NEXUS_M", code == "NEXUS_M" ? "OK" : "NOT_IN_PLAN");

    public Func<Guid, Guid, Result<UserAuthorizationSnapshot>> AuthzHandler { get; set; }
        = (_, _) => new UserAuthorizationSnapshot(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            OrganizationScopeSnapshot.Empty);

    public Func<string, string, Result<FxRateQuoteDto>> FxHandler { get; set; }
        = (from, to) => from.Equals(to, StringComparison.OrdinalIgnoreCase)
            ? new FxRateQuoteDto(from, to, 1m, DateTimeOffset.UtcNow, "IDENTITY")
            : new FxRateQuoteDto(from, to, 25000m, new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero), "NEXUS_C_FX");

    public Task<Result<EntitlementEvaluationDto>> EvaluateEntitlementAsync(Guid tenantId, string featureCode, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult(EntitlementHandler(tenantId, featureCode));

    public Task<Result<UserAuthorizationSnapshot>> GetUserAuthorizationSnapshotAsync(Guid tenantId, Guid userId, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult(AuthzHandler(tenantId, userId));

    public Task<Result<FxRateQuoteDto>> GetFxRateAsync(string baseCurrency, string quoteCurrency, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult(FxHandler(baseCurrency, quoteCurrency));

    public Task<Result<CoreWorkflowStartResultDto>> StartWorkflowInstanceAsync(string workflowDefinitionCode, string businessReferenceId, string title, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult<Result<CoreWorkflowStartResultDto>>(new CoreWorkflowStartResultDto(Guid.CreateVersion7(), Guid.CreateVersion7(), "Submitted"));

    public Task<Result<CoreWorkflowDecisionResultDto>> DecideWorkflowTaskAsync(Guid cTaskId, string decision, string? comment, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult<Result<CoreWorkflowDecisionResultDto>>(new CoreWorkflowDecisionResultDto(Guid.CreateVersion7(), decision, decision));

    public Task<Result<bool>> SendNotificationAsync(Guid recipientUserId, string subject, string body, string idempotencyKey, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult<Result<bool>>(true);

    public Task<Result<CoreAiGatewayResultDto>> InvokeAiGatewayAsync(string modelCode, string groundedPrompt, string userBearerToken, string correlationId, CancellationToken ct = default)
        => Task.FromResult<Result<CoreAiGatewayResultDto>>(new CoreAiGatewayResultDto(
            "NEXUS_C_AI",
            modelCode,
            $"Grounded summary generated strictly from cited metrics ({groundedPrompt.Length} chars).",
            PromptTokens: 180,
            CompletionTokens: 64,
            EstimatedCostUsd: 0.00042m));

    public Task<Result<bool>> SyncPermissionManifestAsync(string correlationId, CancellationToken ct = default)
        => Task.FromResult<Result<bool>>(true);
}

/// <summary>
/// Creates a uniquely named ephemeral PostgreSQL database (nexus_m_test_*_tmp) for automated tests
/// and drops ONLY that ephemeral database on disposal — never touching nexus_m_local_sandbox or NEXUS C DBs.
/// </summary>
public sealed class NexusMTestFixture : IAsyncLifetime
{
    public const string TestJwtSigningKey = "NEXUS_M_INTEGRATION_TEST_SIGNING_KEY_32_BYTES_OK";
    public string EphemeralDbName { get; } = $"nexus_m_test_{Guid.NewGuid():N}_tmp";
    private static string PgAdminUser => Environment.GetEnvironmentVariable("PGUSER") ?? Environment.UserName;
    public string AdminConnectionString => $"Host=localhost;Port=5432;Database={EphemeralDbName};Username={PgAdminUser}";
    public string AppRoleConnectionString => $"Host=localhost;Port=5432;Database={EphemeralDbName};Username=nexus_m_app;Password=nexus_m_local_dev_pw";

    public StubNexusCoreClient CoreStub { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await using (var pgConn = new NpgsqlConnection($"Host=localhost;Port=5432;Database=postgres;Username={PgAdminUser}"))
        {
            await pgConn.OpenAsync();
            await using var createDb = new NpgsqlCommand($"CREATE DATABASE \"{EphemeralDbName}\";", pgConn);
            await createDb.ExecuteNonQueryAsync();
        }

        await DatabaseSchemaBootstrap.ApplyAsync(AdminConnectionString, "nexus_m_app");

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:Default", AppRoleConnectionString);
                builder.UseSetting("ConnectionStrings:Migrator", AdminConnectionString);
                builder.UseSetting("Database:AutoBootstrap", "false");
                builder.UseSetting("Jwt:Issuer", "nexus-c");
                builder.UseSetting("Jwt:Audience", "nexus-ecosystem");
                builder.UseSetting("Jwt:SigningKey", TestJwtSigningKey);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<INexusCoreClient>();
                    services.AddSingleton<INexusCoreClient>(CoreStub);
                });
            });
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();

        NpgsqlConnection.ClearAllPools();
        await using var pgConn = new NpgsqlConnection($"Host=localhost;Port=5432;Database=postgres;Username={PgAdminUser}");
        await pgConn.OpenAsync();

        await using var terminate = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{EphemeralDbName}' AND pid <> pg_backend_pid();",
            pgConn);
        await terminate.ExecuteNonQueryAsync();

        await using var dropDb = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{EphemeralDbName}\";", pgConn);
        await dropDb.ExecuteNonQueryAsync();
    }

    public static string IssueJwt(Guid userId, string email, Guid? tenantId, string scope = "tenant")
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString("D")),
            new("email", email),
            new("scope", scope)
        };
        if (tenantId.HasValue)
        {
            claims.Add(new("tid", tenantId.Value.ToString("D")));
        }

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: "nexus-c",
            audience: "nexus-ecosystem",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
