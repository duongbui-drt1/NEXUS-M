using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Audit;

public sealed class BusinessAuditEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorEmail { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Outcome { get; set; } = "Success";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? MetadataJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<BusinessAuditEvent> BusinessAuditEvents => Set<BusinessAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_audit");
        modelBuilder.Entity<BusinessAuditEvent>(e =>
        {
            e.ToTable("business_audit_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
            e.Property(x => x.ActorEmail).HasColumnName("actor_email").HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasColumnName("action").HasMaxLength(120).IsRequired();
            e.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(80).IsRequired();
            e.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
            e.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
            e.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
            e.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.OccurredAtUtc }).HasDatabaseName("ix_m_audit_tenant_occurred");
            e.HasIndex(x => new { x.TenantId, x.Action }).HasDatabaseName("ix_m_audit_tenant_action");
        });
    }
}

public sealed record BusinessAuditDto(
    Guid Id,
    Guid TenantId,
    Guid? ActorUserId,
    string ActorEmail,
    string Action,
    string EntityType,
    string EntityId,
    string Outcome,
    string? BeforeJson,
    string? AfterJson,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc);

public interface IBusinessAuditReader
{
    Task<Result<PagedResult<BusinessAuditDto>>> QueryAsync(
        int page = 1,
        int pageSize = 20,
        string? action = null,
        string? entityType = null,
        CancellationToken ct = default);
}

public sealed class BusinessAuditService(AuditDbContext db, IRequestContext requestContext)
    : IBusinessAuditWriter, IBusinessAuditReader
{
    public async Task WriteAsync(BusinessAuditEntry entry, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            throw new InvalidOperationException("Cannot write M business audit event without explicit TenantId.");
        }

        var record = new BusinessAuditEvent
        {
            Id = Guid.CreateVersion7(),
            TenantId = requestContext.TenantId.Value,
            ActorUserId = requestContext.UserId,
            ActorEmail = requestContext.Email ?? "system",
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Outcome = entry.Outcome,
            BeforeJson = entry.BeforeState is null ? null : JsonSerializer.Serialize(entry.BeforeState),
            AfterJson = entry.AfterState is null ? null : JsonSerializer.Serialize(entry.AfterState),
            MetadataJson = entry.Metadata is null ? null : JsonSerializer.Serialize(entry.Metadata),
            CorrelationId = requestContext.CorrelationId,
            OccurredAtUtc = DateTimeOffset.UtcNow
        };

        db.BusinessAuditEvents.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<PagedResult<BusinessAuditDto>>> QueryAsync(
        int page = 1,
        int pageSize = 20,
        string? action = null,
        string? entityType = null,
        CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var tid = requestContext.TenantId.Value;

        var query = db.BusinessAuditEvents.AsNoTracking().Where(x => x.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(action))
        {
            var act = action.Trim();
            query = query.Where(x => x.Action == act);
        }
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var et = entityType.Trim();
            query = query.Where(x => x.EntityType == et);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BusinessAuditDto(
                x.Id, x.TenantId, x.ActorUserId, x.ActorEmail, x.Action,
                x.EntityType, x.EntityId, x.Outcome, x.BeforeJson, x.AfterJson,
                x.CorrelationId, x.OccurredAtUtc))
            .ToListAsync(ct);

        return new PagedResult<BusinessAuditDto>(items, page, pageSize, total);
    }
}
