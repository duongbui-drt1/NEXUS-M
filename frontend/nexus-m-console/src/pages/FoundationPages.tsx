import React, { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { nexusMApi } from '../api/client';
import { ForbiddenBanner } from '../components/AppShell';
import type { ManagementContextDto } from '../types';

export const WorkspacePage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const qc = useQueryClient();
  const canManage = context.permissions.includes('m.workspace.manage');
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [currency, setCurrency] = useState('VND');
  const [error, setError] = useState<string | null>(null);

  const createMutation = useMutation({
    mutationFn: () => nexusMApi.createWorkspace({ code, name, defaultCurrency: currency, orgMode: 1 }),
    onSuccess: () => {
      setCode('');
      setName('');
      setError(null);
      void qc.invalidateQueries({ queryKey: ['m-context'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  return (
    <section className="page">
      <h1>Management Workspaces (M00)</h1>
      <p className="muted">
        Active workspace: <strong>{context.activeWorkspace.name}</strong> ({context.activeWorkspace.orgMode} mode,{' '}
        {context.activeWorkspace.defaultCurrency}, {context.activeWorkspace.defaultTimezone})
      </p>

      <div className="card-grid">
        {context.workspaces.map((ws) => (
          <div key={ws.id} className="card">
            <div className="card-header">
              <strong>{ws.name}</strong>
              <span className="pill">{ws.code}</span>
            </div>
            <p className="muted">{ws.description || '—'}</p>
            <div className="meta-row">
              <span>Mode: {ws.orgMode}</span>
              <span>Currency: {ws.defaultCurrency}</span>
              <span>v{ws.version}</span>
            </div>
          </div>
        ))}
      </div>

      <div className="card" style={{ marginTop: 20 }}>
        <h2>Create Workspace</h2>
        {!canManage ? (
          <ForbiddenBanner />
        ) : (
          <form
            className="form-row"
            onSubmit={(e) => {
              e.preventDefault();
              createMutation.mutate();
            }}
          >
            <input placeholder="Code (e.g. GROUP_HQ)" value={code} onChange={(e) => setCode(e.target.value)} required />
            <input placeholder="Workspace Name" value={name} onChange={(e) => setName(e.target.value)} required />
            <input placeholder="Currency (VND)" value={currency} onChange={(e) => setCurrency(e.target.value)} maxLength={3} required />
            <button type="submit" className="btn-primary" disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Creating...' : 'Create'}
            </button>
          </form>
        )}
        {error && <div className="alert-danger" style={{ marginTop: 12 }}>{error}</div>}
      </div>
    </section>
  );
};

export const ModuleRegistryPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => (
  <section className="page">
    <h1>Module Registry (M00–M10)</h1>
    <p className="muted">Progressive module activation driven by NEXUS C entitlements and server-side RBAC.</p>
    <table className="data-table">
      <thead>
        <tr>
          <th>Module</th>
          <th>Maturity</th>
          <th>Entitled</th>
          <th>Permitted</th>
          <th>Status</th>
        </tr>
      </thead>
      <tbody>
        {context.modules.map((m) => (
          <tr key={m.code}>
            <td><strong>{m.code}</strong></td>
            <td>{m.maturity}</td>
            <td>{m.isEntitled ? 'Yes' : `No (${m.entitlementReason})`}</td>
            <td>{m.hasPermission ? 'Yes' : 'No'}</td>
            <td>{m.isAccessible ? 'Active' : 'Locked'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  </section>
);

export const PreferencesPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const qc = useQueryClient();
  const [locale, setLocale] = useState(context.preferences.locale);
  const [timezone, setTimezone] = useState(context.preferences.timezone);
  const [theme, setTheme] = useState<'light' | 'dark'>(context.preferences.theme);
  const [saved, setSaved] = useState(false);

  const mutation = useMutation({
    mutationFn: () =>
      nexusMApi.updatePreferences({
        locale,
        timezone,
        theme,
        defaultWorkspaceId: context.activeWorkspace.id,
      }),
    onSuccess: () => {
      setSaved(true);
      void qc.invalidateQueries({ queryKey: ['m-context'] });
    },
  });

  return (
    <section className="page">
      <h1>User Preferences</h1>
      <form
        className="card form-stack"
        onSubmit={(e) => {
          e.preventDefault();
          setSaved(false);
          mutation.mutate();
        }}
      >
        <label>
          Locale
          <select value={locale} onChange={(e) => setLocale(e.target.value)}>
            <option value="vi-VN">Tiếng Việt (vi-VN)</option>
            <option value="en-US">English (en-US)</option>
          </select>
        </label>
        <label>
          Timezone
          <input value={timezone} onChange={(e) => setTimezone(e.target.value)} />
        </label>
        <label>
          Theme
          <select value={theme} onChange={(e) => setTheme(e.target.value as 'light' | 'dark')}>
            <option value="light">Light</option>
            <option value="dark">Dark</option>
          </select>
        </label>
        <button type="submit" className="btn-primary" disabled={mutation.isPending}>
          {mutation.isPending ? 'Saving...' : 'Save Preferences'}
        </button>
        {saved && <div className="alert-success">Preferences saved.</div>}
      </form>
    </section>
  );
};

export const AuditLogPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.audit.read');
  const query = useQuery({
    queryKey: ['m-audit'],
    queryFn: () => nexusMApi.listAudit(1, 25),
    enabled: canRead,
  });

  if (!canRead) {
    return (
      <section className="page">
        <h1>Business Audit Trail</h1>
        <ForbiddenBanner />
      </section>
    );
  }

  return (
    <section className="page">
      <h1>Immutable Business Audit Trail</h1>
      {query.isLoading && <p className="muted">Loading audit events...</p>}
      {query.isError && <div className="alert-danger">{(query.error as Error).message}</div>}
      {query.data && (
        <table className="data-table">
          <thead>
            <tr>
              <th>Timestamp (UTC)</th>
              <th>Actor</th>
              <th>Action</th>
              <th>Entity</th>
              <th>Correlation ID</th>
            </tr>
          </thead>
          <tbody>
            {query.data.data.items.map((item) => (
              <tr key={item.id}>
                <td>{item.occurredAtUtc}</td>
                <td>{item.actorEmail}</td>
                <td><code>{item.action}</code></td>
                <td>{item.entityType} ({item.entityId})</td>
                <td><code>{item.correlationId}</code></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
};
