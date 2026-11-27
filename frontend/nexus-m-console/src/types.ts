export interface ApiMeta {
  correlationId: string;
  asOfUtc?: string | null;
}

export interface ApiResponse<T> {
  success: boolean;
  data: T;
  meta: ApiMeta;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

export interface WorkspaceDto {
  id: string;
  tenantId: string;
  code: string;
  name: string;
  description: string;
  defaultLocale: string;
  defaultTimezone: string;
  defaultCurrency: string;
  orgMode: 'Simple' | 'Standard' | 'Enterprise';
  isDefault: boolean;
  isActive: boolean;
  version: number;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface UserPreferenceDto {
  userId: string;
  locale: string;
  timezone: string;
  theme: 'light' | 'dark';
  defaultWorkspaceId?: string | null;
  updatedAtUtc: string;
}

export interface OrganizationScopeSnapshot {
  hasUnrestrictedTenantScope: boolean;
  legalEntityIds: string[];
  branchIds: string[];
  organizationUnitIds: string[];
}

export interface ModuleAvailabilityDto {
  code: string;
  nameKey: string;
  route: string;
  maturity: 'Implemented' | 'Planned';
  isEntitled: boolean;
  hasPermission: boolean;
  isAccessible: boolean;
  entitlementReason: string;
  sortOrder: number;
}

export interface ManagementContextDto {
  userId: string;
  email: string;
  tenantId: string;
  activeWorkspace: WorkspaceDto;
  workspaces: WorkspaceDto[];
  preferences: UserPreferenceDto;
  orgScope: OrganizationScopeSnapshot;
  permissions: string[];
  modules: ModuleAvailabilityDto[];
}

export interface BusinessAuditDto {
  id: string;
  tenantId: string;
  actorUserId?: string | null;
  actorEmail: string;
  action: string;
  entityType: string;
  entityId: string;
  outcome: string;
  correlationId: string;
  occurredAtUtc: string;
}

export interface MetricVersionDto {
  id: string;
  version: number;
  formulaExpression: string;
  unit: string;
  currencyCode?: string | null;
  timeBasis: string;
  aggregationMethod: string;
  accountingBasis: string;
  sourceApplication: string;
  freshnessSlaMinutes: number;
  accessClassification: string;
  publishedAtUtc: string;
}

export interface MetricDefinitionDto {
  id: string;
  tenantId: string;
  metricCode: string;
  displayNameKey: string;
  descriptionKey: string;
  currentVersion: number;
  isActive: boolean;
  latestVersion: MetricVersionDto;
  versions: MetricVersionDto[];
}

export interface DataConnectorDto {
  id: string;
  code: string;
  name: string;
  sourceApplication: string;
  schemaVersion: number;
  freshnessSlaMinutes: number;
  hasSecretReference: boolean;
  isEnabled: boolean;
  lastSucceededAtUtc?: string | null;
  createdAtUtc: string;
}

export interface IngestionRunDto {
  id: string;
  sourceApplication: string;
  status: string;
  recordsReceived: number;
  recordsAccepted: number;
  recordsDeduplicated: number;
  errorSummary?: string | null;
  correlationId: string;
  completedAtUtc: string;
}

export interface DeadLetterItemDto {
  id: string;
  eventType: string;
  producer: string;
  failureReason: string;
  manualRetryCount: number;
  status: string;
  failedAtUtc: string;
}

export interface ReconciliationRecordDto {
  id: string;
  metricCode: string;
  sourceApplication: string;
  expectedCount: number;
  actualCount: number;
  expectedSum: number;
  actualSum: number;
  status: 'MATCHED' | 'DISCREPANCY';
  checkedAtUtc: string;
}

export interface ExecutiveDashboardDto {
  id: string;
  code: string;
  title: string;
  audienceRole: string;
  isDefault: boolean;
  version: number;
}

export interface ManagementAlertDto {
  id: string;
  metricCode: string;
  severity: string;
  summary: string;
  observedValue?: number | null;
  freshnessStatus: string;
  status: string;
  triggeredAtUtc: string;
}

export interface SavedReportDto {
  id: string;
  code: string;
  title: string;
  metricCodesCsv: string;
  normalizeToCurrency?: string | null;
  scheduleCron?: string | null;
  createdAtUtc: string;
}

export interface ApprovalRequestDto {
  id: string;
  title: string;
  category: string;
  summary: string;
  requestedByUserId: string;
  status: string;
  slaDueAtUtc: string;
  version: number;
  createdAtUtc: string;
}

export interface WorkforceOverviewDto {
  asOfDate: string;
  totalUnits: number;
  totalPositions: number;
  activeAssignmentsCount: number;
  totalFteAllocated: number;
  totalHeadcountTarget: number;
  units: { id: string; code: string; name: string; unitType: string; headcountTarget: number }[];
  positions: { id: string; code: string; title: string; isExecutiveSeat: boolean }[];
  activeAssignments: { id: string; userDisplayEmail: string; validFromDate: string; validToDate?: string | null; allocationPercent: number }[];
}

export interface RiskItemDto {
  id: string;
  code: string;
  title: string;
  category: string;
  likelihood: number;
  impact: number;
  score: number;
  riskLevel: string;
  status: string;
  assessmentVersion: number;
}

export interface ComplianceObligationDto {
  id: string;
  code: string;
  title: string;
  jurisdictionCode: string;
  regulatorySource: string;
  contentValidationStatus: string;
  dueAtUtc: string;
  status: string;
}

export interface BoardReportPackageDto {
  id: string;
  code: string;
  title: string;
  consolidationCurrency: string;
  metricCodesCsv: string;
  sha256Checksum: string;
  status: string;
  createdAtUtc: string;
}

export interface EvaluatedObjectiveDto {
  id: string;
  code: string;
  title: string;
  periodCode: string;
  keyResults: {
    id: string;
    title: string;
    linkedMetricCode?: string | null;
    targetValue: number;
    actualValue?: number | null;
    attainmentPercent?: number | null;
    freshnessStatus?: string | null;
  }[];
}

export interface AiAskResponseDto {
  answer: string;
  provider: string;
  modelCode: string;
  promptTokens: number;
  completionTokens: number;
  estimatedCostUsd: number;
  requiresHumanConfirmation: boolean;
  citations: {
    metricCode: string;
    definitionVersion?: number | null;
    aggregatedValue?: number | null;
    currencyCode?: string | null;
    freshnessStatus: string;
    status: string;
  }[];
}

export interface EnterpriseSearchHitDto {
  category: string;
  id: string;
  code: string;
  title: string;
  route: string;
}
