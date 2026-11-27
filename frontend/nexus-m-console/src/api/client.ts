import type {
  AiAskResponseDto,
  ApiResponse,
  ApprovalRequestDto,
  BoardReportPackageDto,
  BusinessAuditDto,
  ComplianceObligationDto,
  DataConnectorDto,
  DeadLetterItemDto,
  EnterpriseSearchHitDto,
  EvaluatedObjectiveDto,
  ExecutiveDashboardDto,
  IngestionRunDto,
  ManagementAlertDto,
  ManagementContextDto,
  MetricDefinitionDto,
  PagedResult,
  ReconciliationRecordDto,
  RiskItemDto,
  SavedReportDto,
  UserPreferenceDto,
  WorkforceOverviewDto,
  WorkspaceDto,
} from '../types';

export class ApiError extends Error {
  status: number;
  errorCode: string;
  correlationId?: string;

  constructor(status: number, errorCode: string, message: string, correlationId?: string) {
    super(message);
    this.status = status;
    this.errorCode = errorCode;
    this.correlationId = correlationId;
  }
}

const TOKEN_STORAGE_KEY = 'nexus_m_access_token';

export function getStoredToken(): string | null {
  return sessionStorage.getItem(TOKEN_STORAGE_KEY);
}

export function setStoredToken(token: string | null): void {
  if (token) {
    sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
  } else {
    sessionStorage.removeItem(TOKEN_STORAGE_KEY);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<ApiResponse<T>> {
  const token = getStoredToken();
  const headers = new Headers(init?.headers);
  headers.set('Accept', 'application/json');
  if (init?.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const res = await fetch(path, { ...init, headers });
  const json = await res.json().catch(() => null);

  if (!res.ok) {
    throw new ApiError(
      res.status,
      json?.errorCode ?? `HTTP_${res.status}`,
      json?.detail ?? res.statusText,
      json?.correlationId,
    );
  }

  return json as ApiResponse<T>;
}

export const nexusMApi = {
  getContext: () => request<ManagementContextDto>('/api/v1/m/context'),
  listWorkspaces: () => request<WorkspaceDto[]>('/api/v1/m/workspaces'),
  createWorkspace: (payload: {
    code: string;
    name: string;
    description?: string;
    defaultLocale?: string;
    defaultTimezone?: string;
    defaultCurrency?: string;
    orgMode?: number;
  }) => request<WorkspaceDto>('/api/v1/m/workspaces', { method: 'POST', body: JSON.stringify(payload) }),
  updatePreferences: (payload: {
    locale: string;
    timezone: string;
    theme: 'light' | 'dark';
    defaultWorkspaceId?: string | null;
  }) => request<UserPreferenceDto>('/api/v1/m/preferences', { method: 'PUT', body: JSON.stringify(payload) }),
  listAudit: (page = 1, pageSize = 20) =>
    request<PagedResult<BusinessAuditDto>>(`/api/v1/m/audit?page=${page}&pageSize=${pageSize}`),
  listMetricDefinitions: () => request<MetricDefinitionDto[]>('/api/v1/m/metrics/definitions'),
  createMetricDefinition: (payload: {
    metricCode: string;
    displayNameKey: string;
    descriptionKey: string;
    formulaExpression: string;
    unit: number;
    currencyCode?: string | null;
    timeBasis: number;
    aggregationMethod: number;
    accountingBasis: number;
    sourceApplication: string;
    freshnessSlaMinutes: number;
  }) => request<MetricDefinitionDto>('/api/v1/m/metrics/definitions', { method: 'POST', body: JSON.stringify(payload) }),
  listConnectors: () => request<DataConnectorDto[]>('/api/v1/m/integrations/connectors'),
  createConnector: (payload: {
    code: string;
    name: string;
    sourceApplication: string;
    freshnessSlaMinutes: number;
    secretReference?: string | null;
  }) => request<DataConnectorDto>('/api/v1/m/integrations/connectors', { method: 'POST', body: JSON.stringify(payload) }),
  listRuns: () => request<IngestionRunDto[]>('/api/v1/m/integrations/runs'),
  listDeadLetters: () => request<DeadLetterItemDto[]>('/api/v1/m/integrations/dead-letters'),
  retryDeadLetter: (id: string) => request<DeadLetterItemDto>(`/api/v1/m/integrations/dead-letters/${id}/retry`, { method: 'POST' }),
  listReconciliations: () => request<ReconciliationRecordDto[]>('/api/v1/m/integrations/reconciliations'),
  listDashboards: () => request<ExecutiveDashboardDto[]>('/api/v1/m/executive/dashboards'),
  listAlerts: () => request<ManagementAlertDto[]>('/api/v1/m/executive/alerts'),
  listBiReports: () => request<SavedReportDto[]>('/api/v1/m/bi/reports'),
  listWorkflowInbox: () => request<ApprovalRequestDto[]>('/api/v1/m/workflows/inbox'),
  getOrgOverview: (asOfDate?: string) =>
    request<WorkforceOverviewDto>(`/api/v1/m/org/overview${asOfDate ? `?asOfDate=${encodeURIComponent(asOfDate)}` : ''}`),
  listRisks: () => request<RiskItemDto[]>('/api/v1/m/risks'),
  listObligations: () => request<ComplianceObligationDto[]>('/api/v1/m/compliance/obligations'),
  listBoardReports: () => request<BoardReportPackageDto[]>('/api/v1/m/board-reports'),
  listObjectives: (periodStartUtc: string, periodEndUtc: string) =>
    request<EvaluatedObjectiveDto[]>(
      `/api/v1/m/performance/objectives?periodStartUtc=${encodeURIComponent(periodStartUtc)}&periodEndUtc=${encodeURIComponent(periodEndUtc)}`,
    ),
  askAi: (payload: { question: string; metricCodes: string[] }) =>
    request<AiAskResponseDto>('/api/v1/m/ai/ask', { method: 'POST', body: JSON.stringify(payload) }),
  search: (q: string) => request<EnterpriseSearchHitDto[]>(`/api/v1/m/search?q=${encodeURIComponent(q)}`),
};
