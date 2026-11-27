using Microsoft.EntityFrameworkCore;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Governance;

public sealed class RiskItem
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = "OPERATIONAL";
    public int Likelihood { get; set; } // 1..5
    public int Impact { get; set; }     // 1..5
    public int Score { get; set; }      // Likelihood * Impact (1..25)
    public string RiskLevel { get; set; } = "Medium"; // Low, Medium, High, Critical
    public Guid OwnerUserId { get; set; }
    public string MitigationPlan { get; set; } = string.Empty;
    public Guid? EvidenceCFileId { get; set; }
    public string Status { get; set; } = "Open"; // Open, Mitigating, Escalated, Closed
    public int AssessmentVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class RiskAssessmentHistory
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RiskId { get; set; }
    public int Version { get; set; }
    public int Likelihood { get; set; }
    public int Impact { get; set; }
    public int Score { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public Guid AssessedByUserId { get; set; }
    public DateTimeOffset AssessedAtUtc { get; set; }
}

public sealed class GovernanceControl
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public Guid? LinkedRiskId { get; set; }
    public string EffectivenessStatus { get; set; } = "Effective";
    public DateTimeOffset AssessedAtUtc { get; set; }
}

public sealed class RiskIncident
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? LinkedRiskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "High";
    public string Status { get; set; } = "Investigating";
    public DateTimeOffset ReportedAtUtc { get; set; }
}

public sealed class ComplianceObligation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty; // e.g., VN, SG, EU-GDPR, US-SOX, GLOBAL (never hardcoded)
    public string RegulatorySource { get; set; } = string.Empty;
    public string ContentValidationStatus { get; set; } = "DraftUnverified"; // DraftUnverified, ValidatedAuthoritative
    public DateOnly EffectiveFromDate { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? EvidenceCFileId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, EvidenceSubmitted, Compliant, Overdue
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class GovernanceDbContext(DbContextOptions<GovernanceDbContext> options) : DbContext(options)
{
    public DbSet<RiskItem> Risks => Set<RiskItem>();
    public DbSet<RiskAssessmentHistory> RiskHistories => Set<RiskAssessmentHistory>();
    public DbSet<GovernanceControl> Controls => Set<GovernanceControl>();
    public DbSet<RiskIncident> Incidents => Set<RiskIncident>();
    public DbSet<ComplianceObligation> Obligations => Set<ComplianceObligation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_governance");

        modelBuilder.Entity<RiskItem>(e =>
        {
            e.ToTable("risks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(60).IsRequired();
            e.Property(x => x.Likelihood).HasColumnName("likelihood").IsRequired();
            e.Property(x => x.Impact).HasColumnName("impact").IsRequired();
            e.Property(x => x.Score).HasColumnName("score").IsRequired();
            e.Property(x => x.RiskLevel).HasColumnName("risk_level").HasMaxLength(24).IsRequired();
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            e.Property(x => x.MitigationPlan).HasColumnName("mitigation_plan").HasMaxLength(1000).IsRequired();
            e.Property(x => x.EvidenceCFileId).HasColumnName("evidence_c_file_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.AssessmentVersion).HasColumnName("assessment_version").IsConcurrencyToken().IsRequired();
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_gov_risks_tenant_code");
        });

        modelBuilder.Entity<RiskAssessmentHistory>(e =>
        {
            e.ToTable("risk_assessment_histories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.RiskId).HasColumnName("risk_id").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsRequired();
            e.Property(x => x.Likelihood).HasColumnName("likelihood").IsRequired();
            e.Property(x => x.Impact).HasColumnName("impact").IsRequired();
            e.Property(x => x.Score).HasColumnName("score").IsRequired();
            e.Property(x => x.RiskLevel).HasColumnName("risk_level").HasMaxLength(24).IsRequired();
            e.Property(x => x.AssessedByUserId).HasColumnName("assessed_by_user_id").IsRequired();
            e.Property(x => x.AssessedAtUtc).HasColumnName("assessed_at_utc").IsRequired();
            e.HasOne<RiskItem>().WithMany().HasForeignKey(x => x.RiskId);
        });

        modelBuilder.Entity<GovernanceControl>(e =>
        {
            e.ToTable("controls");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.LinkedRiskId).HasColumnName("linked_risk_id");
            e.Property(x => x.EffectivenessStatus).HasColumnName("effectiveness_status").HasMaxLength(32).IsRequired();
            e.Property(x => x.AssessedAtUtc).HasColumnName("assessed_at_utc").IsRequired();
        });

        modelBuilder.Entity<RiskIncident>(e =>
        {
            e.ToTable("incidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.LinkedRiskId).HasColumnName("linked_risk_id");
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(24).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.ReportedAtUtc).HasColumnName("reported_at_utc").IsRequired();
        });

        modelBuilder.Entity<ComplianceObligation>(e =>
        {
            e.ToTable("compliance_obligations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            e.Property(x => x.JurisdictionCode).HasColumnName("jurisdiction_code").HasMaxLength(32).IsRequired();
            e.Property(x => x.RegulatorySource).HasColumnName("regulatory_source").HasMaxLength(200).IsRequired();
            e.Property(x => x.ContentValidationStatus).HasColumnName("content_validation_status").HasMaxLength(40).IsRequired();
            e.Property(x => x.EffectiveFromDate).HasColumnName("effective_from_date").IsRequired();
            e.Property(x => x.DueAtUtc).HasColumnName("due_at_utc").IsRequired();
            e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            e.Property(x => x.EvidenceCFileId).HasColumnName("evidence_c_file_id");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_gov_obligations_tenant_code");
        });
    }
}

public sealed record CreateRiskRequest(
    string Code,
    string Title,
    string Category,
    int Likelihood,
    int Impact,
    Guid OwnerUserId,
    string MitigationPlan,
    Guid? EvidenceCFileId = null);

public sealed record ReassessRiskRequest(int Likelihood, int Impact, string? MitigationPlan = null, string? Status = null);

public sealed record CreateControlRequest(string Code, string Title, Guid? LinkedRiskId = null, string EffectivenessStatus = "Effective");

public sealed record CreateIncidentRequest(string Title, string Severity = "High", Guid? LinkedRiskId = null);

public sealed record CreateObligationRequest(
    string Code,
    string Title,
    string JurisdictionCode,
    string RegulatorySource,
    DateOnly EffectiveFromDate,
    DateTimeOffset DueAtUtc,
    Guid OwnerUserId,
    bool IsValidatedAuthoritative = false,
    Guid? EvidenceCFileId = null);

public interface IGovernanceService
{
    Task<Result<IReadOnlyList<RiskItem>>> ListRisksAsync(CancellationToken ct = default);
    Task<Result<RiskItem>> CreateRiskAsync(CreateRiskRequest request, CancellationToken ct = default);
    Task<Result<RiskItem>> ReassessRiskAsync(Guid riskId, ReassessRiskRequest request, CancellationToken ct = default);
    Task<Result<GovernanceControl>> CreateControlAsync(CreateControlRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<GovernanceControl>>> ListControlsAsync(CancellationToken ct = default);
    Task<Result<RiskIncident>> ReportIncidentAsync(CreateIncidentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<RiskIncident>>> ListIncidentsAsync(CancellationToken ct = default);
    Task<Result<ComplianceObligation>> CreateObligationAsync(CreateObligationRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ComplianceObligation>>> ListObligationsAsync(string? jurisdictionCode = null, CancellationToken ct = default);
    Task<Result<ComplianceObligation>> AttachObligationEvidenceAsync(Guid obligationId, Guid cFileId, CancellationToken ct = default);
}

public sealed class GovernanceService(
    GovernanceDbContext db,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter) : IGovernanceService
{
    private static string ClassifyScore(int score) => score switch
    {
        >= 16 => "Critical",
        >= 10 => "High",
        >= 5 => "Medium",
        _ => "Low"
    };

    public async Task<Result<IReadOnlyList<RiskItem>>> ListRisksAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.Risks.AsNoTracking().Where(r => r.TenantId == tid).OrderByDescending(r => r.Score).ToListAsync(ct);
    }

    public async Task<Result<RiskItem>> CreateRiskAsync(CreateRiskRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        if (request.Likelihood is < 1 or > 5 || request.Impact is < 1 or > 5)
            return Error.Validation("RISK_MATRIX_OUT_OF_RANGE", "Likelihood and Impact must be between 1 and 5.");

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        if (await db.Risks.AnyAsync(r => r.TenantId == tid && r.Code == normCode, ct))
            return Error.Conflict("RISK_CODE_EXISTS", $"Risk '{normCode}' already exists.");

        var score = request.Likelihood * request.Impact;
        var level = ClassifyScore(score);
        var now = DateTimeOffset.UtcNow;

        var risk = new RiskItem
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            Category = request.Category.Trim().ToUpperInvariant(),
            Likelihood = request.Likelihood,
            Impact = request.Impact,
            Score = score,
            RiskLevel = level,
            OwnerUserId = request.OwnerUserId,
            MitigationPlan = request.MitigationPlan?.Trim() ?? string.Empty,
            EvidenceCFileId = request.EvidenceCFileId,
            Status = score >= 16 ? "Escalated" : "Open",
            AssessmentVersion = 1,
            UpdatedAtUtc = now
        };

        db.Risks.Add(risk);
        db.RiskHistories.Add(new RiskAssessmentHistory
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RiskId = risk.Id,
            Version = 1,
            Likelihood = risk.Likelihood,
            Impact = risk.Impact,
            Score = risk.Score,
            RiskLevel = risk.RiskLevel,
            AssessedByUserId = requestContext.UserId.Value,
            AssessedAtUtc = now
        });

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.risk.created", "RiskItem", risk.Id.ToString("D"),
            AfterState: new { risk.Code, risk.Score, risk.RiskLevel, risk.Status }), ct);

        return risk;
    }

    public async Task<Result<RiskItem>> ReassessRiskAsync(Guid riskId, ReassessRiskRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue || !requestContext.UserId.HasValue)
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");

        if (request.Likelihood is < 1 or > 5 || request.Impact is < 1 or > 5)
            return Error.Validation("RISK_MATRIX_OUT_OF_RANGE", "Likelihood and Impact must be between 1 and 5.");

        var tid = requestContext.TenantId.Value;
        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId && r.TenantId == tid, ct);
        if (risk is null) return Error.NotFound("RISK_NOT_FOUND", "Risk was not found.");

        risk.Likelihood = request.Likelihood;
        risk.Impact = request.Impact;
        risk.Score = request.Likelihood * request.Impact;
        risk.RiskLevel = ClassifyScore(risk.Score);
        if (request.MitigationPlan is not null) risk.MitigationPlan = request.MitigationPlan.Trim();
        if (!string.IsNullOrWhiteSpace(request.Status)) risk.Status = request.Status.Trim();
        risk.AssessmentVersion++;
        risk.UpdatedAtUtc = DateTimeOffset.UtcNow;

        db.RiskHistories.Add(new RiskAssessmentHistory
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            RiskId = risk.Id,
            Version = risk.AssessmentVersion,
            Likelihood = risk.Likelihood,
            Impact = risk.Impact,
            Score = risk.Score,
            RiskLevel = risk.RiskLevel,
            AssessedByUserId = requestContext.UserId.Value,
            AssessedAtUtc = risk.UpdatedAtUtc
        });

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.risk.reassessed", "RiskItem", risk.Id.ToString("D"),
            AfterState: new { risk.AssessmentVersion, risk.Score, risk.RiskLevel, risk.Status }), ct);

        return risk;
    }

    public async Task<Result<GovernanceControl>> CreateControlAsync(CreateControlRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var ctrl = new GovernanceControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = requestContext.TenantId.Value,
            Code = request.Code.Trim().ToUpperInvariant(),
            Title = request.Title.Trim(),
            LinkedRiskId = request.LinkedRiskId,
            EffectivenessStatus = request.EffectivenessStatus.Trim(),
            AssessedAtUtc = DateTimeOffset.UtcNow
        };
        db.Controls.Add(ctrl);
        await db.SaveChangesAsync(ct);
        return ctrl;
    }

    public async Task<Result<IReadOnlyList<GovernanceControl>>> ListControlsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.Controls.AsNoTracking().Where(c => c.TenantId == tid).OrderBy(c => c.Code).ToListAsync(ct);
    }

    public async Task<Result<RiskIncident>> ReportIncidentAsync(CreateIncidentRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var inc = new RiskIncident
        {
            Id = Guid.CreateVersion7(),
            TenantId = requestContext.TenantId.Value,
            LinkedRiskId = request.LinkedRiskId,
            Title = request.Title.Trim(),
            Severity = request.Severity.Trim(),
            Status = "Investigating",
            ReportedAtUtc = DateTimeOffset.UtcNow
        };
        db.Incidents.Add(inc);
        await db.SaveChangesAsync(ct);
        return inc;
    }

    public async Task<Result<IReadOnlyList<RiskIncident>>> ListIncidentsAsync(CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        return await db.Incidents.AsNoTracking().Where(i => i.TenantId == tid).OrderByDescending(i => i.ReportedAtUtc).ToListAsync(ct);
    }

    public async Task<Result<ComplianceObligation>> CreateObligationAsync(CreateObligationRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        if (string.IsNullOrWhiteSpace(request.JurisdictionCode) || string.IsNullOrWhiteSpace(request.RegulatorySource))
            return Error.Validation("OBLIGATION_JURISDICTION_REQUIRED", "Explicit JurisdictionCode and RegulatorySource are required (no single-country assumption).");

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        if (await db.Obligations.AnyAsync(o => o.TenantId == tid && o.Code == normCode, ct))
            return Error.Conflict("OBLIGATION_CODE_EXISTS", $"Obligation '{normCode}' already exists.");

        var now = DateTimeOffset.UtcNow;
        var ob = new ComplianceObligation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            Code = normCode,
            Title = request.Title.Trim(),
            JurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant(),
            RegulatorySource = request.RegulatorySource.Trim(),
            ContentValidationStatus = request.IsValidatedAuthoritative ? "ValidatedAuthoritative" : "DraftUnverified",
            EffectiveFromDate = request.EffectiveFromDate,
            DueAtUtc = request.DueAtUtc,
            OwnerUserId = request.OwnerUserId,
            EvidenceCFileId = request.EvidenceCFileId,
            Status = request.EvidenceCFileId.HasValue ? "Compliant" : (request.DueAtUtc < now ? "Overdue" : "Pending"),
            Version = 1,
            CreatedAtUtc = now
        };

        db.Obligations.Add(ob);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.compliance.obligation_created", "ComplianceObligation", ob.Id.ToString("D"),
            AfterState: new { ob.Code, ob.JurisdictionCode, ob.ContentValidationStatus, ob.DueAtUtc }), ct);

        return ob;
    }

    public async Task<Result<IReadOnlyList<ComplianceObligation>>> ListObligationsAsync(string? jurisdictionCode = null, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var q = db.Obligations.AsNoTracking().Where(o => o.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(jurisdictionCode))
        {
            var jc = jurisdictionCode.Trim().ToUpperInvariant();
            q = q.Where(o => o.JurisdictionCode == jc);
        }
        return await q.OrderBy(o => o.DueAtUtc).ToListAsync(ct);
    }

    public async Task<Result<ComplianceObligation>> AttachObligationEvidenceAsync(Guid obligationId, Guid cFileId, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var ob = await db.Obligations.FirstOrDefaultAsync(o => o.Id == obligationId && o.TenantId == tid, ct);
        if (ob is null) return Error.NotFound("OBLIGATION_NOT_FOUND", "Compliance obligation was not found.");

        ob.EvidenceCFileId = cFileId;
        ob.Status = "Compliant";
        ob.Version++;
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.compliance.evidence_attached", "ComplianceObligation", ob.Id.ToString("D"),
            AfterState: new { ob.EvidenceCFileId, ob.Status }), ct);

        return ob;
    }
}
