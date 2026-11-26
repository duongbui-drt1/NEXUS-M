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
