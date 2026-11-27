using NexusM.SharedKernel.Security;

namespace NexusM.SharedKernel.Modules;

public enum ModuleMaturity
{
    Implemented = 1,
    Planned = 2
}

public sealed record ModuleDescriptor(
    string Code,
    string NameKey,
    string Route,
    string RequiredEntitlementCode,
    string RequiredPermission,
    ModuleMaturity Maturity,
    int SortOrder);

public static class NexusMModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDescriptor> All =
    [
        new("M00", "modules.m00.foundation", "/workspace", "NEXUS_M", NexusMPermissions.WorkspaceRead, ModuleMaturity.Implemented, 0),
        new("M01", "modules.m01.executive", "/executive", "NEXUS_M", NexusMPermissions.DashboardRead, ModuleMaturity.Planned, 10),
        new("M02", "modules.m02.bi", "/bi", "NEXUS_M", NexusMPermissions.ReportRead, ModuleMaturity.Planned, 20),
        new("M03", "modules.m03.workflow", "/workflows", "NEXUS_M", NexusMPermissions.WorkflowRead, ModuleMaturity.Planned, 30),
        new("M04", "modules.m04.organization", "/organization", "NEXUS_M", NexusMPermissions.OrgRead, ModuleMaturity.Planned, 40),
        new("M05", "modules.m05.risk", "/risk", "NEXUS_M", NexusMPermissions.RiskRead, ModuleMaturity.Planned, 50),
        new("M06", "modules.m06.compliance", "/compliance", "NEXUS_M", NexusMPermissions.ComplianceRead, ModuleMaturity.Planned, 60),
        new("M07", "modules.m07.board_reporting", "/board-reports", "NEXUS_M", NexusMPermissions.ReportRead, ModuleMaturity.Planned, 70),
        new("M08", "modules.m08.performance", "/performance", "NEXUS_M", NexusMPermissions.PerformanceRead, ModuleMaturity.Planned, 80),
        new("M09", "modules.m09.ai", "/ai-assistant", "NEXUS_M_AI", NexusMPermissions.AiInvoke, ModuleMaturity.Planned, 90),
        new("M10", "modules.m10.integrations", "/integrations", "NEXUS_M", NexusMPermissions.IntegrationRead, ModuleMaturity.Implemented, 100)
    ];
}
