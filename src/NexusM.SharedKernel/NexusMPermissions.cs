namespace NexusM.SharedKernel.Security;

public sealed record PermissionManifestItem(string Code, string Name, string Category, string Scope = "Tenant");

public static class NexusMPermissions
{
    public const string ApplicationCode = "NEXUS_M";

    // M00 — Management Foundation
    public const string WorkspaceRead = "m.workspace.read";
    public const string WorkspaceManage = "m.workspace.manage";
    public const string AuditRead = "m.audit.read";

    // M01 — Executive Intelligence
    public const string DashboardRead = "m.dashboard.read";
    public const string DashboardManage = "m.dashboard.manage";

    // M02 — Business Intelligence & KPI Catalog
    public const string KpiRead = "m.kpi.read";
    public const string KpiManage = "m.kpi.manage";
    public const string ReportRead = "m.report.read";
    public const string ReportManage = "m.report.manage";
    public const string ReportExport = "m.report.export";

    // M03 — Workflow & Approval Hub
    public const string WorkflowRead = "m.workflow.read";
    public const string WorkflowCreate = "m.workflow.create";
    public const string WorkflowApprove = "m.workflow.approve";
    public const string WorkflowManage = "m.workflow.manage";

    // M04 — Organization & People
    public const string OrgRead = "m.org.read";
    public const string OrgManage = "m.org.manage";

    // M05 & M06 — Risk, Governance & Compliance
    public const string RiskRead = "m.risk.read";
    public const string RiskManage = "m.risk.manage";
    public const string ComplianceRead = "m.compliance.read";
    public const string ComplianceManage = "m.compliance.manage";

    // M07 & M08 — Board Reporting & Performance
    public const string PerformanceRead = "m.performance.read";
    public const string PerformanceManage = "m.performance.manage";

    // M09 — Optional AI Intelligence
    public const string AiInvoke = "m.ai.invoke";

    // M10 — Data & Integration Hub
    public const string IntegrationRead = "m.integration.read";
    public const string IntegrationManage = "m.integration.manage";

    public static readonly IReadOnlyList<PermissionManifestItem> Manifest =
    [
        new(WorkspaceRead, "Read Management Workspace & Navigation", "M00_Foundation"),
        new(WorkspaceManage, "Manage Workspaces & Configuration", "M00_Foundation"),
        new(AuditRead, "Read Management Business Audit Trail", "M00_Foundation"),

        new(DashboardRead, "Read Executive Dashboards & Alerts", "M01_Executive"),
        new(DashboardManage, "Configure Dashboards & Widgets", "M01_Executive"),

        new(KpiRead, "Read KPI Catalog & Observations", "M02_BI"),
        new(KpiManage, "Manage Versioned Metric Definitions", "M02_BI"),
        new(ReportRead, "Read Saved & Scheduled Reports", "M02_BI"),
        new(ReportManage, "Create & Manage BI Reports", "M02_BI"),
        new(ReportExport, "Export Management Reports (CSV/XLSX/PDF)", "M02_BI"),

        new(WorkflowRead, "Read Approval Inbox & Workflow History", "M03_Workflow"),
        new(WorkflowCreate, "Submit Management Approval Requests", "M03_Workflow"),
        new(WorkflowApprove, "Approve/Reject Management Workflow Tasks", "M03_Workflow"),
        new(WorkflowManage, "Manage Workflow Templates & SLA Policies", "M03_Workflow"),

        new(OrgRead, "Read Organization Chart & Workforce Overview", "M04_Organization"),
        new(OrgManage, "Manage Org Profiles, Positions & Assignments", "M04_Organization"),

        new(RiskRead, "Read Risk Register & Controls", "M05_Risk"),
        new(RiskManage, "Manage Risks, Assessments & Incidents", "M05_Risk"),
        new(ComplianceRead, "Read Compliance Obligations & Deadlines", "M06_Compliance"),
        new(ComplianceManage, "Manage Compliance Obligations & Evidence", "M06_Compliance"),

        new(PerformanceRead, "Read Strategic Objectives & OKRs", "M08_Performance"),
        new(PerformanceManage, "Manage Objectives, Targets & Initiatives", "M08_Performance"),

        new(AiInvoke, "Invoke AI Executive Assistant", "M09_AI"),

        new(IntegrationRead, "Read Data Connectors & Freshness Health", "M10_Integration"),
        new(IntegrationManage, "Manage Data Ingestion & Replay Dead Letters", "M10_Integration")
    ];
}
