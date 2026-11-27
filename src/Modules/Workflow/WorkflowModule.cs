using Microsoft.EntityFrameworkCore;
using NexusM.BuildingBlocks.NexusCore;
using NexusM.BuildingBlocks.Realtime;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Workflow;

public sealed class WorkflowTemplate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "MANAGEMENT_DECISION";
    public string CoreDefinitionCode { get; set; } = "NEXUS_M_STANDARD_APPROVAL";
    public int DefaultSlaHours { get; set; } = 48;
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ApprovalRequestOverlay
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public Guid CWorkflowInstanceId { get; set; }
    public Guid? CCurrentTaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    public Guid? DelegatedToUserId { get; set; }
    public string Status { get; set; } = "Submitted"; // Submitted, Approved, Rejected, Escalated, Cancelled
    public DateTimeOffset SlaDueAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WorkflowDecisionLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RequestId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Outcome { get; set; } = string.Empty; // Approved, Rejected, Delegated, Escalated, Cancelled
    public string? Comment { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public sealed class WorkflowComment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RequestId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class WorkflowEvidence
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RequestId { get; set; }
    public Guid CFileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid AttachedByUserId { get; set; }
    public DateTimeOffset AttachedAtUtc { get; set; }
}

public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowTemplate> Templates => Set<WorkflowTemplate>();
    public DbSet<ApprovalRequestOverlay> Requests => Set<ApprovalRequestOverlay>();
    public DbSet<WorkflowDecisionLog> DecisionLogs => Set<WorkflowDecisionLog>();
    public DbSet<WorkflowComment> Comments => Set<WorkflowComment>();
    public DbSet<WorkflowEvidence> Evidences => Set<WorkflowEvidence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_workflow");

        modelBuilder.Entity<WorkflowTemplate>(e =>
        {
            e.ToTable("templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
            e.Property(x => x.CoreDefinitionCode).HasColumnName("core_definition_code").HasMaxLength(80).IsRequired();
            e.Property(x => x.DefaultSlaHours).HasColumnName("default_sla_hours").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_wf_templates_tenant_code");
        });

        modelBuilder.Entity<ApprovalRequestOverlay>(e =>
        {
            e.ToTable("approval_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.TemplateId).HasColumnName("template_id").IsRequired();
            e.Property(x => x.CWorkflowInstanceId).HasColumnName("c_workflow_instance_id").IsRequired();
            e.Property(x => x.CCurrentTaskId).HasColumnName("c_current_task_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
            e.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
            e.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
            e.Property(x => x.DelegatedToUserId).HasColumnName("delegated_to_user_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.SlaDueAtUtc).HasColumnName("sla_due_at_utc").IsRequired();
            e.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
            e.Property(x => x.DecidedAtUtc).HasColumnName("decided_at_utc");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        });

        modelBuilder.Entity<WorkflowDecisionLog>(e =>
        {
            e.ToTable("decision_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
            e.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(1000);
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
            e.HasOne<ApprovalRequestOverlay>().WithMany().HasForeignKey(x => x.RequestId);
        });

        modelBuilder.Entity<WorkflowComment>(e =>
        {
            e.ToTable("comments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.AuthorUserId).HasColumnName("author_user_id").IsRequired();
            e.Property(x => x.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasOne<ApprovalRequestOverlay>().WithMany().HasForeignKey(x => x.RequestId);
        });

        modelBuilder.Entity<WorkflowEvidence>(e =>
        {
            e.ToTable("evidences");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.RequestId).HasColumnName("request_id").IsRequired();
            e.Property(x => x.CFileId).HasColumnName("c_file_id").IsRequired();
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(256).IsRequired();
            e.Property(x => x.AttachedByUserId).HasColumnName("attached_by_user_id").IsRequired();
            e.Property(x => x.AttachedAtUtc).HasColumnName("attached_at_utc").IsRequired();
            e.HasOne<ApprovalRequestOverlay>().WithMany().HasForeignKey(x => x.RequestId);
        });
    }
}

public sealed record CreateTemplateRequest(
    string Code,
    string Title,
    string Category = "MANAGEMENT_DECISION",
    string CoreDefinitionCode = "NEXUS_M_STANDARD_APPROVAL",
    int DefaultSlaHours = 48);

public sealed record SubmitApprovalRequest(
    string TemplateCode,
    string Title,
    string Summary,
    Guid? EvidenceCFileId = null,
    string? EvidenceFileName = null);

public sealed record WorkflowDecisionRequest(
    string Decision, // Approved, Rejected
    string? Comment = null,
    int ExpectedVersion = 1);

public sealed record DelegateWorkflowRequest(Guid DelegateToUserId, string? Reason = null);

public interface IWorkflowHubService
{
    Task<Result<IReadOnlyList<WorkflowTemplate>>> ListTemplatesAsync(CancellationToken ct = default);
    Task<Result<WorkflowTemplate>> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken ct = default);
    Task<Result<ApprovalRequestOverlay>> SubmitRequestAsync(SubmitApprovalRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ApprovalRequestOverlay>>> ListInboxAsync(string? status = null, CancellationToken ct = default);
    Task<Result<ApprovalRequestOverlay>> DecideAsync(Guid requestId, WorkflowDecisionRequest request, CancellationToken ct = default);
    Task<Result<ApprovalRequestOverlay>> DelegateAsync(Guid requestId, DelegateWorkflowRequest request, CancellationToken ct = default);
    Task<Result<ApprovalRequestOverlay>> EscalateOverdueAsync(Guid requestId, CancellationToken ct = default);
    Task<Result<WorkflowComment>> AddCommentAsync(Guid requestId, string body, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WorkflowDecisionLog>>> GetDecisionHistoryAsync(Guid requestId, CancellationToken ct = default);
}

public sealed class WorkflowHubService(
    WorkflowDbContext db,
    INexusCoreClient nexusCoreClient,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter,
    IRealtimeBroadcaster broadcaster) : IWorkflowHubService
{
    public async Task<Result<IReadOnlyList<WorkflowTemplate>>> ListTemplatesAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.Templates.AsNoTracking().Where(t => t.TenantId == tid).OrderBy(t => t.Title).ToListAsync(ct);
    }

    public async Task<Result<WorkflowTemplate>> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        if (await db.Templates.AnyAsync(t => t.TenantId == tid && t.Code == normCode, ct))
            return Error.Conflict("WORKFLOW_TEMPLATE_EXISTS", $"Template '{normCode}' already exists.");

        var tpl = new WorkflowTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            Category = request.Category.Trim().ToUpperInvariant(),
            CoreDefinitionCode = request.CoreDefinitionCode.Trim().ToUpperInvariant(),
            DefaultSlaHours = Math.Max(1, request.DefaultSlaHours),
            Version = 1,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Templates.Add(tpl);
        await db.SaveChangesAsync(ct);
        return tpl;
    }

    public async Task<Result<ApprovalRequestOverlay>> SubmitRequestAsync(SubmitApprovalRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        var tid = requestContext.TenantId.Value;
        var normCode = request.TemplateCode.Trim().ToUpperInvariant();
        var tpl = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tid && t.Code == normCode && t.IsActive, ct);
        if (tpl is null) return Error.NotFound("WORKFLOW_TEMPLATE_NOT_FOUND", $"Active template '{normCode}' was not found.");

        var reqId = Guid.CreateVersion7();

        // Delegate core state machine creation to NEXUS C Shared Workflow Engine (ADR-002)
        var coreRes = await nexusCoreClient.StartWorkflowInstanceAsync(
            tpl.CoreDefinitionCode,
            reqId.ToString("D"),
            request.Title.Trim(),
            requestContext.RawBearerToken ?? string.Empty,
            requestContext.CorrelationId,
            ct);

        if (coreRes.IsFailure) return coreRes.Error!;

        var now = DateTimeOffset.UtcNow;
        var overlay = new ApprovalRequestOverlay
        {
            Id = reqId,
            TenantId = tid,
            TemplateId = tpl.Id,
            CWorkflowInstanceId = coreRes.Value.InstanceId,
            CCurrentTaskId = coreRes.Value.CurrentTaskId ?? Guid.CreateVersion7(),
            Title = request.Title.Trim(),
            Category = tpl.Category,
            Summary = request.Summary?.Trim() ?? string.Empty,
            RequestedByUserId = requestContext.UserId.Value,
            Status = "Submitted",
            SlaDueAtUtc = now.AddHours(tpl.DefaultSlaHours),
            Version = 1,
            CreatedAtUtc = now
        };

        db.Requests.Add(overlay);
        db.DecisionLogs.Add(new WorkflowDecisionLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RequestId = overlay.Id,
            ActorUserId = requestContext.UserId.Value,
            Outcome = "Submitted",
            Comment = overlay.Summary,
            CorrelationId = requestContext.CorrelationId,
            OccurredAtUtc = now
        });

        if (request.EvidenceCFileId.HasValue)
        {
            db.Evidences.Add(new WorkflowEvidence
            {
                Id = Guid.CreateVersion7(),
                TenantId = tid,
                RequestId = overlay.Id,
                CFileId = request.EvidenceCFileId.Value,
                FileName = string.IsNullOrWhiteSpace(request.EvidenceFileName) ? "evidence-attachment" : request.EvidenceFileName.Trim(),
                AttachedByUserId = requestContext.UserId.Value,
                AttachedAtUtc = now
            });
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.workflow.submitted", "ApprovalRequestOverlay", overlay.Id.ToString("D"),
            AfterState: new { overlay.CWorkflowInstanceId, overlay.Category, overlay.SlaDueAtUtc }), ct);

        return overlay;
    }

    public async Task<Result<IReadOnlyList<ApprovalRequestOverlay>>> ListInboxAsync(string? status = null, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var q = db.Requests.AsNoTracking().Where(r => r.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim();
            q = q.Where(r => r.Status == st);
        }
        return await q.OrderBy(r => r.SlaDueAtUtc).ToListAsync(ct);
    }

    public async Task<Result<ApprovalRequestOverlay>> DecideAsync(Guid requestId, WorkflowDecisionRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        if (request.Decision is not ("Approved" or "Rejected"))
            return Error.Validation("WORKFLOW_INVALID_DECISION", "Decision must be 'Approved' or 'Rejected'.");

        var tid = requestContext.TenantId.Value;
        var actorId = requestContext.UserId.Value;

        var overlay = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId && r.TenantId == tid, ct);
        if (overlay is null) return Error.NotFound("WORKFLOW_REQUEST_NOT_FOUND", "Approval request was not found.");

        if (overlay.Version != request.ExpectedVersion)
            return Error.Conflict("WORKFLOW_CONCURRENCY_CONFLICT", $"Expected v{request.ExpectedVersion}, current v{overlay.Version}.");

        if (overlay.Status is "Approved" or "Rejected" or "Cancelled")
            return Error.Conflict("WORKFLOW_ALREADY_FINALIZED", $"Workflow is already in terminal status '{overlay.Status}'.");

        // Mandatory Separation of Duties / Maker-Checker enforcement (Master Prompt Section 4 M03)
        if (overlay.RequestedByUserId == actorId)
        {
            return Error.Forbidden(
                "SEPARATION_OF_DUTIES_VIOLATION",
                "Requester cannot approve or reject their own management workflow request.");
        }

        // Delegate authoritative transition to NEXUS C Shared Workflow Engine
        if (overlay.CCurrentTaskId.HasValue)
        {
            var coreDecide = await nexusCoreClient.DecideWorkflowTaskAsync(
                overlay.CCurrentTaskId.Value,
                request.Decision,
                request.Comment,
                requestContext.RawBearerToken ?? string.Empty,
                requestContext.CorrelationId,
                ct);

            if (coreDecide.IsFailure) return coreDecide.Error!;
        }

        var now = DateTimeOffset.UtcNow;
        overlay.Status = request.Decision;
        overlay.DecidedByUserId = actorId;
        overlay.DecidedAtUtc = now;
        overlay.Version++;

        db.DecisionLogs.Add(new WorkflowDecisionLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RequestId = overlay.Id,
            ActorUserId = actorId,
            Outcome = request.Decision,
            Comment = request.Comment,
            CorrelationId = requestContext.CorrelationId,
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            $"m.workflow.{request.Decision.ToLowerInvariant()}",
            "ApprovalRequestOverlay",
            overlay.Id.ToString("D"),
            AfterState: new { overlay.Status, overlay.DecidedByUserId, overlay.CWorkflowInstanceId }), ct);

        _ = await nexusCoreClient.SendNotificationAsync(
            overlay.RequestedByUserId,
            $"Workflow {request.Decision}: {overlay.Title}",
            request.Comment ?? $"Your request '{overlay.Title}' was {request.Decision}.",
            $"m-wf-notify:{overlay.Id:N}:{overlay.Version}",
            requestContext.RawBearerToken ?? string.Empty,
            requestContext.CorrelationId,
            ct);

        broadcaster.Publish(new RealtimeEventMessage(
            $"management.workflow.{request.Decision.ToLowerInvariant()}.v1",
            tid,
            requestContext.CorrelationId,
            now,
            new { overlay.Id, overlay.Status, overlay.Title }));

        return overlay;
    }

    public async Task<Result<ApprovalRequestOverlay>> DelegateAsync(Guid requestId, DelegateWorkflowRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        var tid = requestContext.TenantId.Value;
        var overlay = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId && r.TenantId == tid, ct);
        if (overlay is null) return Error.NotFound("WORKFLOW_REQUEST_NOT_FOUND", "Approval request was not found.");

        if (request.DelegateToUserId == overlay.RequestedByUserId)
            return Error.Forbidden("SEPARATION_OF_DUTIES_VIOLATION", "Cannot delegate approval back to the original requester.");

        overlay.DelegatedToUserId = request.DelegateToUserId;
        overlay.Version++;

        db.DecisionLogs.Add(new WorkflowDecisionLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RequestId = overlay.Id,
            ActorUserId = requestContext.UserId.Value,
            Outcome = "Delegated",
            Comment = request.Reason ?? $"Delegated to {request.DelegateToUserId:D}",
            CorrelationId = requestContext.CorrelationId,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return overlay;
    }

    public async Task<Result<ApprovalRequestOverlay>> EscalateOverdueAsync(Guid requestId, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        var tid = requestContext.TenantId.Value;
        var overlay = await db.Requests.FirstOrDefaultAsync(r => r.Id == requestId && r.TenantId == tid, ct);
        if (overlay is null) return Error.NotFound("WORKFLOW_REQUEST_NOT_FOUND", "Approval request was not found.");

        overlay.Status = "Escalated";
        overlay.Version++;

        db.DecisionLogs.Add(new WorkflowDecisionLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RequestId = overlay.Id,
            ActorUserId = requestContext.UserId.Value,
            Outcome = "Escalated",
            Comment = "SLA escalation triggered",
            CorrelationId = requestContext.CorrelationId,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return overlay;
    }

    public async Task<Result<WorkflowComment>> AddCommentAsync(Guid requestId, string body, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        if (string.IsNullOrWhiteSpace(body)) return Error.Validation("COMMENT_EMPTY", "Comment body is required.");

        var tid = requestContext.TenantId.Value;
        var exists = await db.Requests.AnyAsync(r => r.Id == requestId && r.TenantId == tid, ct);
        if (!exists) return Error.NotFound("WORKFLOW_REQUEST_NOT_FOUND", "Approval request was not found.");

        var comment = new WorkflowComment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RequestId = requestId,
            AuthorUserId = requestContext.UserId.Value,
            Body = body.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment;
    }

    public async Task<Result<IReadOnlyList<WorkflowDecisionLog>>> GetDecisionHistoryAsync(Guid requestId, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.DecisionLogs.AsNoTracking()
            .Where(l => l.TenantId == tid && l.RequestId == requestId)
            .OrderBy(l => l.OccurredAtUtc)
            .ToListAsync(ct);
    }
}
