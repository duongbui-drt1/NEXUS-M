using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.BuildingBlocks.Persistence;
using NexusM.BuildingBlocks.Realtime;
using NexusM.BuildingBlocks.Security;
using NexusM.BuildingBlocks.Web;
using NexusM.Modules.Ai;
using NexusM.Modules.Audit;
using NexusM.Modules.Bi;
using NexusM.Modules.Executive;
using NexusM.Modules.Foundation.Application;
using NexusM.Modules.Foundation.Infrastructure;
using NexusM.Modules.Governance;
using NexusM.Modules.Integration.Application;
using NexusM.Modules.Integration.Infrastructure;
using NexusM.Modules.Metrics.Application;
using NexusM.Modules.Metrics.Infrastructure;
using NexusM.Modules.Organization;
using NexusM.Modules.Performance;
using NexusM.Modules.Workflow;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter());
});

// Core context, realtime broadcaster & caching
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRealtimeBroadcaster, InMemoryRealtimeBroadcaster>();
builder.Services.AddScoped<IRequestContext, RequestContext>();
builder.Services.AddScoped<TenantRlsConnectionInterceptor>();

// Database (EF Core + PostgreSQL 17 + FORCE RLS interceptor across all bounded contexts)
var runtimeConnStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

void ConfigureModuleDb<TContext>(IServiceProvider sp, DbContextOptionsBuilder opt) where TContext : DbContext
{
    opt.UseNpgsql(runtimeConnStr)
       .AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
}

builder.Services.AddDbContext<FoundationDbContext>(ConfigureModuleDb<FoundationDbContext>);
builder.Services.AddDbContext<AuditDbContext>(ConfigureModuleDb<AuditDbContext>);
builder.Services.AddDbContext<MetricsDbContext>(ConfigureModuleDb<MetricsDbContext>);
builder.Services.AddDbContext<IntegrationDbContext>(ConfigureModuleDb<IntegrationDbContext>);
builder.Services.AddDbContext<ExecutiveDbContext>(ConfigureModuleDb<ExecutiveDbContext>);
builder.Services.AddDbContext<BiDbContext>(ConfigureModuleDb<BiDbContext>);
builder.Services.AddDbContext<WorkflowDbContext>(ConfigureModuleDb<WorkflowDbContext>);
builder.Services.AddDbContext<OrganizationDbContext>(ConfigureModuleDb<OrganizationDbContext>);
builder.Services.AddDbContext<GovernanceDbContext>(ConfigureModuleDb<GovernanceDbContext>);
builder.Services.AddDbContext<PerformanceDbContext>(ConfigureModuleDb<PerformanceDbContext>);
builder.Services.AddDbContext<AiDbContext>(ConfigureModuleDb<AiDbContext>);

// NEXUS C Integration Client (ADR-001)
builder.Services.Configure<NexusCoreOptions>(builder.Configuration.GetSection(NexusCoreOptions.SectionName));
builder.Services.AddHttpClient<INexusCoreClient, NexusCoreHttpClient>((sp, client) =>
{
    var opts = builder.Configuration.GetSection(NexusCoreOptions.SectionName).Get<NexusCoreOptions>() ?? new NexusCoreOptions();
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// JWT Validation (consumes NEXUS C issued tokens — ADR-001)
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "nexus-c";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "nexus-ecosystem";
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// Rate Limiting
builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opt.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.User.FindFirst("tid")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "global";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// Module Application Services (M00 - M10)
builder.Services.AddScoped<BusinessAuditService>();
builder.Services.AddScoped<IBusinessAuditWriter>(sp => sp.GetRequiredService<BusinessAuditService>());
builder.Services.AddScoped<IBusinessAuditReader>(sp => sp.GetRequiredService<BusinessAuditService>());
builder.Services.AddScoped<IFoundationService, FoundationService>();
builder.Services.AddScoped<IMetricsService, MetricsService>();
builder.Services.AddScoped<IIntegrationService, IntegrationService>();
builder.Services.AddScoped<IExecutiveService, ExecutiveService>();
builder.Services.AddScoped<IBiService, BiService>();
builder.Services.AddScoped<IWorkflowHubService, WorkflowHubService>();
builder.Services.AddScoped<IOrganizationOverlayService, OrganizationOverlayService>();
builder.Services.AddScoped<IGovernanceService, GovernanceService>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddScoped<IExecutiveAiService, ExecutiveAiService>();

// Web, OpenAPI, Exception Handling, CORS
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5174"];
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("ConsoleCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Observability (Traces + Metrics) & Health Checks
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

builder.Services.AddHealthChecks()
    .AddNpgSql(runtimeConnStr, name: "postgresql", tags: ["ready"]);

var app = builder.Build();

var autoBootstrap = builder.Configuration.GetValue("Database:AutoBootstrap", true);
var migratorConnStr = builder.Configuration.GetConnectionString("Migrator");
if (autoBootstrap && !string.IsNullOrWhiteSpace(migratorConnStr))
{
    await DatabaseSchemaBootstrap.ApplyAsync(migratorConnStr);
}

app.UseExceptionHandler();
app.UseCors("ConsoleCors");
app.UseAuthentication();
app.UseMiddleware<CorrelationAndRequestContextMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<IdempotencyMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();

app.Run();

public partial class Program;
