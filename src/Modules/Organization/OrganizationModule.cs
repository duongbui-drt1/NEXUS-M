using Microsoft.EntityFrameworkCore;
using NexusM.SharedKernel.Audit;
using NexusM.SharedKernel.Context;
using NexusM.SharedKernel.Results;

namespace NexusM.Modules.Organization;

public sealed class OrgUnitProfile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid COrganizationUnitId { get; set; }
    public Guid? CLegalEntityId { get; set; }
    public Guid? CBranchId { get; set; }
    public Guid? ParentProfileId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UnitType { get; set; } = "Department"; // BusinessUnit, Department, Team, CostCenter
    public int HeadcountTarget { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class ManagementPosition
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrgUnitProfileId { get; set; }
    public Guid? ReportsToPositionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsExecutiveSeat { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>
/// Effective-dated management assignment referencing NEXUS C UserId.
/// Never overwrites historical records in-place; closes prior record with ValidToDate (ADR-003).
/// </summary>
public sealed class ManagementAssignment
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PositionId { get; set; }
    public Guid OrgUnitProfileId { get; set; }
    public Guid CUserId { get; set; }
    public string UserDisplayEmail { get; set; } = string.Empty;
    public DateOnly ValidFromDate { get; set; }
    public DateOnly? ValidToDate { get; set; }
    public decimal AllocationPercent { get; set; } = 100m;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : DbContext(options)
{
    public DbSet<OrgUnitProfile> UnitProfiles => Set<OrgUnitProfile>();
    public DbSet<ManagementPosition> Positions => Set<ManagementPosition>();
    public DbSet<ManagementAssignment> Assignments => Set<ManagementAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_org");

        modelBuilder.Entity<OrgUnitProfile>(e =>
        {
            e.ToTable("unit_profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.COrganizationUnitId).HasColumnName("c_organization_unit_id").IsRequired();
            e.Property(x => x.CLegalEntityId).HasColumnName("c_legal_entity_id");
            e.Property(x => x.CBranchId).HasColumnName("c_branch_id");
            e.Property(x => x.ParentProfileId).HasColumnName("parent_profile_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            e.Property(x => x.UnitType).HasColumnName("unit_type").HasMaxLength(32).IsRequired();
            e.Property(x => x.HeadcountTarget).HasColumnName("headcount_target").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_org_profiles_tenant_code");
        });

        modelBuilder.Entity<ManagementPosition>(e =>
        {
            e.ToTable("positions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.OrgUnitProfileId).HasColumnName("org_unit_profile_id").IsRequired();
            e.Property(x => x.ReportsToPositionId).HasColumnName("reports_to_position_id");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            e.Property(x => x.IsExecutiveSeat).HasColumnName("is_executive_seat").IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_org_positions_tenant_code");
        });

        modelBuilder.Entity<ManagementAssignment>(e =>
        {
            e.ToTable("assignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.PositionId).HasColumnName("position_id").IsRequired();
            e.Property(x => x.OrgUnitProfileId).HasColumnName("org_unit_profile_id").IsRequired();
            e.Property(x => x.CUserId).HasColumnName("c_user_id").IsRequired();
            e.Property(x => x.UserDisplayEmail).HasColumnName("user_display_email").HasMaxLength(256).IsRequired();
            e.Property(x => x.ValidFromDate).HasColumnName("valid_from_date").IsRequired();
            e.Property(x => x.ValidToDate).HasColumnName("valid_to_date");
            e.Property(x => x.AllocationPercent).HasColumnName("allocation_percent").HasPrecision(6, 2).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        });
    }
}

public sealed record CreateOrgUnitProfileRequest(
    Guid COrganizationUnitId,
    string Code,
    string Name,
    string UnitType = "Department",
    Guid? CLegalEntityId = null,
    Guid? CBranchId = null,
    Guid? ParentProfileId = null,
    int HeadcountTarget = 5);

public sealed record CreatePositionRequest(
    Guid OrgUnitProfileId,
    string Code,
    string Title,
    Guid? ReportsToPositionId = null,
    bool IsExecutiveSeat = false);

public sealed record AssignPositionRequest(
    Guid PositionId,
    Guid CUserId,
    string UserDisplayEmail,
    DateOnly ValidFromDate,
    DateOnly? ValidToDate = null,
    decimal AllocationPercent = 100m);

public sealed record WorkforceOverviewDto(
    DateOnly AsOfDate,
    int TotalUnits,
    int TotalPositions,
    int ActiveAssignmentsCount,
    decimal TotalFteAllocated,
    int TotalHeadcountTarget,
    IReadOnlyList<OrgUnitProfile> Units,
    IReadOnlyList<ManagementPosition> Positions,
    IReadOnlyList<ManagementAssignment> ActiveAssignments);

public interface IOrganizationOverlayService
{
    Task<Result<OrgUnitProfile>> CreateUnitProfileAsync(CreateOrgUnitProfileRequest request, CancellationToken ct = default);
    Task<Result<ManagementPosition>> CreatePositionAsync(CreatePositionRequest request, CancellationToken ct = default);
    Task<Result<ManagementAssignment>> AssignPositionAsync(AssignPositionRequest request, CancellationToken ct = default);
    Task<Result<WorkforceOverviewDto>> GetOverviewAsync(DateOnly? asOfDate = null, CancellationToken ct = default);
}

public sealed class OrganizationOverlayService(
    OrganizationDbContext db,
    IRequestContext requestContext,
    IBusinessAuditWriter auditWriter) : IOrganizationOverlayService
{
    public async Task<Result<OrgUnitProfile>> CreateUnitProfileAsync(CreateOrgUnitProfileRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        if (request.COrganizationUnitId == Guid.Empty || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return Error.Validation("ORG_UNIT_INVALID", "COrganizationUnitId, Code, and Name are required.");

        var tid = requestContext.TenantId.Value;
        var normCode = request.Code.Trim().ToUpperInvariant();
        if (await db.UnitProfiles.AnyAsync(u => u.TenantId == tid && u.Code == normCode, ct))
            return Error.Conflict("ORG_UNIT_CODE_EXISTS", $"Unit profile '{normCode}' already exists.");

        var unit = new OrgUnitProfile
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            COrganizationUnitId = request.COrganizationUnitId,
            CLegalEntityId = request.CLegalEntityId,
            CBranchId = request.CBranchId,
            ParentProfileId = request.ParentProfileId,
            Code = normCode,
            Name = request.Name.Trim(),
            UnitType = request.UnitType.Trim(),
            HeadcountTarget = Math.Max(0, request.HeadcountTarget),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.UnitProfiles.Add(unit);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.org.unit_profile_created", "OrgUnitProfile", unit.Id.ToString("D"),
            AfterState: new { unit.Code, unit.COrganizationUnitId, unit.UnitType }), ct);

        return unit;
    }

    public async Task<Result<ManagementPosition>> CreatePositionAsync(CreatePositionRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var unit = await db.UnitProfiles.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.OrgUnitProfileId && u.TenantId == tid, ct);
        if (unit is null) return Error.NotFound("ORG_UNIT_NOT_FOUND", "Organization unit profile was not found.");

        var pos = new ManagementPosition
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            OrgUnitProfileId = unit.Id,
            ReportsToPositionId = request.ReportsToPositionId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Title = request.Title.Trim(),
            IsExecutiveSeat = request.IsExecutiveSeat,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Positions.Add(pos);
        await db.SaveChangesAsync(ct);
        return pos;
    }

    public async Task<Result<ManagementAssignment>> AssignPositionAsync(AssignPositionRequest request, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        if (request.ValidToDate.HasValue && request.ValidToDate.Value < request.ValidFromDate)
            return Error.Validation("ASSIGNMENT_INVALID_DATES", "ValidToDate must be >= ValidFromDate.");

        var tid = requestContext.TenantId.Value;
        var pos = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PositionId && p.TenantId == tid, ct);
        if (pos is null) return Error.NotFound("POSITION_NOT_FOUND", "Management position was not found.");

        // Effective-dated transition: close overlapping open-ended assignment on this seat without erasing history (ADR-003)
        var overlapping = await db.Assignments
            .Where(a => a.TenantId == tid &&
                        a.PositionId == pos.Id &&
                        (a.ValidToDate == null || a.ValidToDate >= request.ValidFromDate) &&
                        a.ValidFromDate < request.ValidFromDate)
            .ToListAsync(ct);

        foreach (var prior in overlapping)
        {
            prior.ValidToDate = request.ValidFromDate.AddDays(-1);
        }

        var assignment = new ManagementAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tid,
            PositionId = pos.Id,
            OrgUnitProfileId = pos.OrgUnitProfileId,
            CUserId = request.CUserId,
            UserDisplayEmail = request.UserDisplayEmail.Trim(),
            ValidFromDate = request.ValidFromDate,
            ValidToDate = request.ValidToDate,
            AllocationPercent = Math.Clamp(request.AllocationPercent, 1m, 100m),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new BusinessAuditEntry(
            "m.org.assignment_recorded", "ManagementAssignment", assignment.Id.ToString("D"),
            AfterState: new { assignment.PositionId, assignment.CUserId, assignment.ValidFromDate, assignment.ValidToDate }), ct);

        return assignment;
    }

    public async Task<Result<WorkforceOverviewDto>> GetOverviewAsync(DateOnly? asOfDate = null, CancellationToken ct = default)
    {
        if (!requestContext.TenantId.HasValue) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tid = requestContext.TenantId.Value;
        var targetDate = asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var scope = requestContext.OrgScope;

        var unitsQuery = db.UnitProfiles.AsNoTracking().Where(u => u.TenantId == tid);
        if (!scope.HasUnrestrictedTenantScope)
        {
            unitsQuery = unitsQuery.Where(u =>
                scope.OrganizationUnitIds.Contains(u.COrganizationUnitId) ||
                (u.CBranchId.HasValue && scope.BranchIds.Contains(u.CBranchId.Value)) ||
                (u.CLegalEntityId.HasValue && scope.LegalEntityIds.Contains(u.CLegalEntityId.Value)));
        }

        var units = await unitsQuery.OrderBy(u => u.Code).ToListAsync(ct);
        var unitIds = units.Select(u => u.Id).ToHashSet();

        var positions = await db.Positions.AsNoTracking()
            .Where(p => p.TenantId == tid && unitIds.Contains(p.OrgUnitProfileId))
            .OrderBy(p => p.Code)
            .ToListAsync(ct);

        var activeAssignments = await db.Assignments.AsNoTracking()
            .Where(a => a.TenantId == tid &&
                        unitIds.Contains(a.OrgUnitProfileId) &&
                        a.ValidFromDate <= targetDate &&
                        (a.ValidToDate == null || a.ValidToDate >= targetDate))
            .ToListAsync(ct);

        var fte = decimal.Round(activeAssignments.Sum(a => a.AllocationPercent / 100m), 2);
        return new WorkforceOverviewDto(
            targetDate,
            units.Count,
            positions.Count,
            activeAssignments.Count,
            fte,
            units.Sum(u => u.HeadcountTarget),
            units,
            positions,
            activeAssignments);
    }
}
