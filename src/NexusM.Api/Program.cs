using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.BuildingBlocks.Persistence;
using NexusM.BuildingBlocks.Security;
using NexusM.BuildingBlocks.Web;
using NexusM.Modules.Audit;
using NexusM.Modules.Foundation.Application;
using NexusM.Modules.Foundation.Infrastructure;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
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

// Core context & caching
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IRequestContext, RequestContext>();
builder.Services.AddScoped<TenantRlsConnectionInterceptor>();

// Database (EF Core + PostgreSQL + FORCE RLS interceptor)
var runtimeConnStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

builder.Services.AddDbContext<FoundationDbContext>((sp, opt) =>
{
    opt.UseNpgsql(runtimeConnStr)
       .AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
});

builder.Services.AddDbContext<AuditDbContext>((sp, opt) =>
{
    opt.UseNpgsql(runtimeConnStr)
       .AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
});

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

// Module Services
builder.Services.AddScoped<BusinessAuditService>();
builder.Services.AddScoped<IBusinessAuditWriter>(sp => sp.GetRequiredService<BusinessAuditService>());
builder.Services.AddScoped<IBusinessAuditReader>(sp => sp.GetRequiredService<BusinessAuditService>());
builder.Services.AddScoped<IFoundationService, FoundationService>();

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

// Observability & Health Checks
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

builder.Services.AddHealthChecks()
    .AddNpgSql(runtimeConnStr, name: "postgresql", tags: ["ready"]);

var app = builder.Build();

// Apply idempotent Phase 2 schema/RLS DDL when migrator connection is configured and not disabled
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
