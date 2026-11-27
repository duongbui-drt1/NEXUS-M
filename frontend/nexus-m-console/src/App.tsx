import React, { useState } from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { getStoredToken, nexusMApi, setStoredToken } from './api/client';
import { AppShell, ForbiddenBanner } from './components/AppShell';
import { AuditLogPage, ModuleRegistryPage, PreferencesPage, WorkspacePage } from './pages/FoundationPages';
import { IntegrationsPage, KpiCatalogPage } from './pages/Phase3DataPages';
import {
  AiAssistantPage,
  BiWorkspacePage,
  BoardReportingPage,
  CompliancePage,
  ExecutiveDashboardPage,
  OrganizationPage,
  PerformanceOkrPage,
  RiskGovernancePage,
  WorkflowInboxPage,
} from './pages/Phase4To10Pages';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: false, staleTime: 15_000 },
  },
});

const AuthenticatedApp: React.FC = () => {
  const [token, setToken] = useState<string | null>(() => getStoredToken());
  const [tokenInput, setTokenInput] = useState('');

  const contextQuery = useQuery({
    queryKey: ['m-context', token],
    queryFn: () => nexusMApi.getContext(),
    enabled: Boolean(token),
  });

  if (!token) {
    return (
      <div className="auth-gate">
        <div className="card auth-card">
          <h1>NEXUS M Console</h1>
          <p className="muted">Sign in with a verified NEXUS C tenant-scoped Bearer token.</p>
          <form
            className="form-stack"
            onSubmit={(e) => {
              e.preventDefault();
              const trimmed = tokenInput.trim();
              if (!trimmed) return;
              setStoredToken(trimmed);
              setToken(trimmed);
            }}
          >
            <textarea
              rows={4}
              placeholder="Paste NEXUS C JWT..."
              value={tokenInput}
              onChange={(e) => setTokenInput(e.target.value)}
            />
            <button type="submit" className="btn-primary">Connect Workspace</button>
          </form>
        </div>
      </div>
    );
  }

  if (contextQuery.isLoading) {
    return <div className="center-state">Loading management context from NEXUS C & NEXUS M...</div>;
  }

  if (contextQuery.isError || !contextQuery.data) {
    return (
      <div className="auth-gate">
        <div className="card auth-card">
          <h2>Access Restricted</h2>
          <ForbiddenBanner />
          <button
            type="button"
            className="btn-secondary"
            style={{ marginTop: 12 }}
            onClick={() => {
              setStoredToken(null);
              setToken(null);
            }}
          >
            Reset Session
          </button>
        </div>
      </div>
    );
  }

  const ctx = contextQuery.data.data;

  return (
    <Routes>
      <Route
        element={
          <AppShell
            context={ctx}
            onLogout={() => {
              setStoredToken(null);
              setToken(null);
            }}
          />
        }
      >
        <Route path="/workspace" element={<WorkspacePage context={ctx} />} />
        <Route path="/modules" element={<ModuleRegistryPage context={ctx} />} />
        <Route path="/kpis" element={<KpiCatalogPage context={ctx} />} />
        <Route path="/executive" element={<ExecutiveDashboardPage context={ctx} />} />
        <Route path="/bi" element={<BiWorkspacePage context={ctx} />} />
        <Route path="/workflows" element={<WorkflowInboxPage context={ctx} />} />
        <Route path="/organization" element={<OrganizationPage context={ctx} />} />
        <Route path="/risk" element={<RiskGovernancePage context={ctx} />} />
        <Route path="/compliance" element={<CompliancePage context={ctx} />} />
        <Route path="/board-reports" element={<BoardReportingPage context={ctx} />} />
        <Route path="/performance" element={<PerformanceOkrPage context={ctx} />} />
        <Route path="/ai-assistant" element={<AiAssistantPage context={ctx} />} />
        <Route path="/integrations" element={<IntegrationsPage context={ctx} />} />
        <Route path="/preferences" element={<PreferencesPage context={ctx} />} />
        <Route path="/audit" element={<AuditLogPage context={ctx} />} />
        <Route path="*" element={<Navigate to="/workspace" replace />} />
      </Route>
    </Routes>
  );
};

export const App: React.FC = () => (
  <QueryClientProvider client={queryClient}>
    <BrowserRouter>
      <AuthenticatedApp />
    </BrowserRouter>
  </QueryClientProvider>
);
