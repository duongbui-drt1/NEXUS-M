import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AppShell } from '../src/components/AppShell';
import { AuditLogPage, WorkspacePage } from '../src/pages/FoundationPages';
import { IntegrationsPage, KpiCatalogPage } from '../src/pages/Phase3DataPages';
import { AiAssistantPage, CompliancePage, ExecutiveDashboardPage, RiskGovernancePage, WorkflowInboxPage } from '../src/pages/Phase4To10Pages';
import type { ManagementContextDto } from '../src/types';

function makeContext(permissions: string[]): ManagementContextDto {
  return {
    userId: '019a0000-0000-7000-8000-000000000001',
    email: 'manager@demo.local',
    tenantId: '019a0000-0000-7000-8000-000000000099',
    activeWorkspace: {
      id: '019a0000-0000-7000-8000-000000000010',
      tenantId: '019a0000-0000-7000-8000-000000000099',
      code: 'DEFAULT',
      name: 'Main Management Workspace',
      description: 'Default',
      defaultLocale: 'vi-VN',
      defaultTimezone: 'Asia/Ho_Chi_Minh',
      defaultCurrency: 'VND',
      orgMode: 'Simple',
      isDefault: true,
      isActive: true,
      version: 1,
      createdAtUtc: '2026-11-27T00:00:00Z',
      updatedAtUtc: '2026-11-27T00:00:00Z',
    },
    workspaces: [],
    preferences: {
      userId: '019a0000-0000-7000-8000-000000000001',
      locale: 'vi-VN',
      timezone: 'Asia/Ho_Chi_Minh',
      theme: 'light',
      updatedAtUtc: '2026-11-27T00:00:00Z',
    },
    orgScope: {
      hasUnrestrictedTenantScope: true,
      legalEntityIds: [],
      branchIds: [],
      organizationUnitIds: [],
    },
    permissions,
    modules: [
      {
        code: 'M00',
        nameKey: 'modules.m00.foundation',
        route: '/workspace',
        maturity: 'Implemented',
        isEntitled: true,
        hasPermission: true,
        isAccessible: true,
        entitlementReason: 'OK',
        sortOrder: 0,
      },
      {
        code: 'M09',
        nameKey: 'modules.m09.ai',
        route: '/ai-assistant',
        maturity: 'Planned',
        isEntitled: false,
        hasPermission: false,
        isAccessible: false,
        entitlementReason: 'NOT_IN_PLAN',
        sortOrder: 90,
      },
    ],
  };
}

describe('NEXUS M AppShell & Permission-Aware Rendering', () => {
  it('hides audit navigation and displays soft-red friendly forbidden notice for lower-privilege users', () => {
    const ctx = makeContext(['m.workspace.read']);
    const qc = new QueryClient();

    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/workspace']}>
          <Routes>
            <Route element={<AppShell context={ctx} onLogout={vi.fn()} />}>
              <Route path="/workspace" element={<WorkspacePage context={ctx} />} />
            </Route>
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.queryByText('Nhật ký quản trị')).not.toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng liên hệ Admin để xem chi tiết');
    expect(screen.queryByText(/m\.workspace\.manage/)).not.toBeInTheDocument();
  });

  it('shows audit navigation when m.audit.read is granted and blocks unauthorized direct audit route', () => {
    const qc = new QueryClient();
    const ctxWithoutAudit = makeContext(['m.workspace.read']);
    render(
      <QueryClientProvider client={qc}>
        <AuditLogPage context={ctxWithoutAudit} />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng liên hệ Admin để xem chi tiết');
  });

  it('guards KPI Catalog and Data & Integration Hub when user lacks m.kpi.read / m.integration.read', () => {
    const qc = new QueryClient();
    const ctxRestricted = makeContext(['m.workspace.read']);

    const { unmount } = render(
      <QueryClientProvider client={qc}>
        <KpiCatalogPage context={ctxRestricted} />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng liên hệ Admin để xem chi tiết');
    unmount();

    render(
      <QueryClientProvider client={qc}>
        <IntegrationsPage context={ctxRestricted} />
      </QueryClientProvider>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng liên hệ Admin để xem chi tiết');
  });

  it('guards M01 Executive, M03 Workflow, M05 Risk, M06 Compliance and M09 AI Assistant when unentitled or unauthorized', () => {
    const qc = new QueryClient();
    // Even if m.ai.invoke is in permissions, M09 isentitled=false in makeContext so AiAssistantPage MUST block!
    const ctxAiBlocked = makeContext(['m.workspace.read', 'm.ai.invoke']);

    for (const Page of [ExecutiveDashboardPage, WorkflowInboxPage, RiskGovernancePage, CompliancePage, AiAssistantPage]) {
      const { unmount } = render(
        <QueryClientProvider client={qc}>
          <Page context={ctxAiBlocked} />
        </QueryClientProvider>,
      );
      expect(screen.getByRole('alert')).toHaveTextContent('Vui lòng liên hệ Admin để xem chi tiết');
      unmount();
    }
  });
});
