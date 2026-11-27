import React, { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { nexusMApi } from '../api/client';
import { ForbiddenBanner } from '../components/AppShell';
import type { ManagementContextDto } from '../types';

export const KpiCatalogPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const qc = useQueryClient();
  const canRead = context.permissions.includes('m.kpi.read');
  const canManage = context.permissions.includes('m.kpi.manage');

  const [metricCode, setMetricCode] = useState('');
  const [displayNameKey, setDisplayNameKey] = useState('');
  const [formula, setFormula] = useState('');
  const [unit, setUnit] = useState(1); // 1 = Currency, 3 = Count
  const [currencyCode, setCurrencyCode] = useState('VND');
  const [sourceApp, setSourceApp] = useState('NEXUS_F');
  const [slaMinutes, setSlaMinutes] = useState(60);
  const [errMsg, setErrMsg] = useState<string | null>(null);

  const defsQuery = useQuery({
    queryKey: ['m-kpi-defs'],
    queryFn: () => nexusMApi.listMetricDefinitions(),
    enabled: canRead,
  });

  const createMutation = useMutation({
    mutationFn: () =>
      nexusMApi.createMetricDefinition({
        metricCode,
        displayNameKey,
        descriptionKey: displayNameKey,
        formulaExpression: formula,
        unit,
        currencyCode: unit === 1 ? currencyCode : null,
        timeBasis: 2,
        aggregationMethod: 1,
        accountingBasis: unit === 1 ? 1 : 3,
        sourceApplication: sourceApp,
        freshnessSlaMinutes: slaMinutes,
      }),
    onSuccess: () => {
      setMetricCode('');
      setDisplayNameKey('');
      setFormula('');
      setErrMsg(null);
      void qc.invalidateQueries({ queryKey: ['m-kpi-defs'] });
    },
    onError: (err: Error) => setErrMsg(err.message),
  });

  if (!canRead) {
    return (
      <section className="page">
        <h1>KPI Catalog & Metric Definitions</h1>
        <ForbiddenBanner />
      </section>
    );
  }

  return (
    <section className="page">
      <h1>KPI Catalog & Metric Definitions</h1>
      <p className="muted">
        Versioned metric definitions with mandatory formula, unit, currency, accounting basis, source attribution, and freshness SLA.
      </p>

      {defsQuery.isLoading && <p className="muted">Loading KPI definitions...</p>}
      {defsQuery.data && (
        <table className="data-table">
          <thead>
            <tr>
              <th>Code</th>
              <th>Version</th>
              <th>Formula</th>
              <th>Unit / Currency</th>
              <th>Basis</th>
              <th>Source</th>
              <th>Freshness SLA</th>
            </tr>
          </thead>
          <tbody>
            {defsQuery.data.data.map((d) => (
              <tr key={d.id}>
                <td><strong>{d.metricCode}</strong></td>
                <td>v{d.currentVersion}</td>
                <td><code>{d.latestVersion.formulaExpression}</code></td>
                <td>{d.latestVersion.unit}{d.latestVersion.currencyCode ? ` (${d.latestVersion.currencyCode})` : ''}</td>
                <td>{d.latestVersion.accountingBasis}</td>
                <td><span className="pill subtle">{d.latestVersion.sourceApplication}</span></td>
                <td>{d.latestVersion.freshnessSlaMinutes} min</td>
              </tr>
            ))}
            {defsQuery.data.data.length === 0 && (
              <tr>
                <td colSpan={7} className="muted">No KPI definitions registered yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      )}

      <div className="card" style={{ marginTop: 20 }}>
        <h2>Register Metric Definition</h2>
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
            <input placeholder="Metric Code (e.g. REV_GROSS)" value={metricCode} onChange={(e) => setMetricCode(e.target.value)} required />
            <input placeholder="Label Key (e.g. kpi.rev_gross)" value={displayNameKey} onChange={(e) => setDisplayNameKey(e.target.value)} required />
            <input placeholder="Formula Expression" value={formula} onChange={(e) => setFormula(e.target.value)} required />
            <select value={unit} onChange={(e) => setUnit(Number(e.target.value))}>
              <option value={1}>Currency</option>
              <option value={3}>Count</option>
            </select>
            {unit === 1 && (
              <input placeholder="ISO Currency" value={currencyCode} onChange={(e) => setCurrencyCode(e.target.value)} maxLength={3} required />
            )}
            <input placeholder="Source App (NEXUS_F)" value={sourceApp} onChange={(e) => setSourceApp(e.target.value)} required />
            <input type="number" min={1} value={slaMinutes} onChange={(e) => setSlaMinutes(Number(e.target.value))} required />
            <button type="submit" className="btn-primary" disabled={createMutation.isPending}>
              {createMutation.isPending ? 'Saving...' : 'Publish v1'}
            </button>
          </form>
        )}
        {errMsg && <div className="alert-danger" style={{ marginTop: 12 }}>{errMsg}</div>}
      </div>
    </section>
  );
};

export const IntegrationsPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const qc = useQueryClient();
  const canRead = context.permissions.includes('m.integration.read');
  const canManage = context.permissions.includes('m.integration.manage');

  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [sourceApp, setSourceApp] = useState('NEXUS_F');
  const [secretRef, setSecretRef] = useState('');
  const [errMsg, setErrMsg] = useState<string | null>(null);

  const connectorsQuery = useQuery({
    queryKey: ['m-connectors'],
    queryFn: () => nexusMApi.listConnectors(),
    enabled: canRead,
  });

  const runsQuery = useQuery({
    queryKey: ['m-runs'],
    queryFn: () => nexusMApi.listRuns(),
    enabled: canRead,
  });

  const dlqQuery = useQuery({
    queryKey: ['m-dlq'],
    queryFn: () => nexusMApi.listDeadLetters(),
    enabled: canRead,
  });

  const reconQuery = useQuery({
    queryKey: ['m-reconciliations'],
    queryFn: () => nexusMApi.listReconciliations(),
    enabled: canRead,
  });

  const createConn = useMutation({
    mutationFn: () =>
      nexusMApi.createConnector({
        code,
        name,
        sourceApplication: sourceApp,
        freshnessSlaMinutes: 60,
        secretReference: secretRef || null,
      }),
    onSuccess: () => {
      setCode('');
      setName('');
      setSecretRef('');
      setErrMsg(null);
      void qc.invalidateQueries({ queryKey: ['m-connectors'] });
    },
    onError: (err: Error) => setErrMsg(err.message),
  });

  const retryDlq = useMutation({
    mutationFn: (id: string) => nexusMApi.retryDeadLetter(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['m-dlq'] }),
    onError: (err: Error) => setErrMsg(err.message),
  });

  if (!canRead) {
    return (
      <section className="page">
        <h1>Data & Integration Hub (M10)</h1>
        <ForbiddenBanner />
      </section>
    );
  }

  return (
    <section className="page">
      <h1>Data & Integration Hub (M10)</h1>
      <p className="muted">Connectors, idempotent event ingestion runs, reconciliations, and dead-letter replay.</p>

      <h2>Registered Connectors</h2>
      <table className="data-table">
        <thead>
          <tr>
            <th>Code</th>
            <th>Name</th>
            <th>Source App</th>
            <th>Schema</th>
            <th>Vault Secret</th>
            <th>Last Succeeded</th>
          </tr>
        </thead>
        <tbody>
          {connectorsQuery.data?.data.map((c) => (
            <tr key={c.id}>
              <td><strong>{c.code}</strong></td>
              <td>{c.name}</td>
              <td>{c.sourceApplication}</td>
              <td>v{c.schemaVersion}</td>
              <td>{c.hasSecretReference ? 'Configured (Vault/Env)' : 'None'}</td>
              <td>{c.lastSucceededAtUtc ?? 'Never'}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="card" style={{ marginTop: 16 }}>
        <h3>Register Connector</h3>
        {!canManage ? (
          <ForbiddenBanner />
        ) : (
          <form
            className="form-row"
            onSubmit={(e) => {
              e.preventDefault();
              createConn.mutate();
            }}
          >
            <input placeholder="Code (FIN_SYNC)" value={code} onChange={(e) => setCode(e.target.value)} required />
            <input placeholder="Connector Name" value={name} onChange={(e) => setName(e.target.value)} required />
            <input placeholder="Source App (NEXUS_F)" value={sourceApp} onChange={(e) => setSourceApp(e.target.value)} required />
            <input placeholder="vault://... (optional)" value={secretRef} onChange={(e) => setSecretRef(e.target.value)} />
            <button type="submit" className="btn-primary" disabled={createConn.isPending}>Add Connector</button>
          </form>
        )}
        {errMsg && <div className="alert-danger" style={{ marginTop: 12 }}>{errMsg}</div>}
      </div>

      <h2 style={{ marginTop: 24 }}>Reconciliation Checks</h2>
      <table className="data-table">
        <thead>
          <tr>
            <th>Metric</th>
            <th>Source</th>
            <th>Expected (Count / Sum)</th>
            <th>Actual (Count / Sum)</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          {reconQuery.data?.data.map((r) => (
            <tr key={r.id}>
              <td><strong>{r.metricCode}</strong></td>
              <td>{r.sourceApplication}</td>
              <td>{r.expectedCount} / {r.expectedSum}</td>
              <td>{r.actualCount} / {r.actualSum}</td>
              <td><span className="pill">{r.status}</span></td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2 style={{ marginTop: 24 }}>Recent Ingestion Runs & Dead Letters</h2>
      <div className="card-grid">
        <div className="card">
          <strong>Ingestion Runs ({runsQuery.data?.data.length ?? 0})</strong>
          {runsQuery.data?.data.slice(0, 5).map((run) => (
            <div key={run.id} className="meta-row">
              <span>{run.sourceApplication}</span>
              <span>{run.status}</span>
              <span>+{run.recordsAccepted} (dedup: {run.recordsDeduplicated})</span>
            </div>
          ))}
        </div>
        <div className="card">
          <strong>Dead-Letter Queue ({dlqQuery.data?.data.length ?? 0})</strong>
          {dlqQuery.data?.data.map((dlq) => (
            <div key={dlq.id} className="meta-row" style={{ alignItems: 'center', justifyContent: 'space-between' }}>
              <span>{dlq.eventType} ({dlq.manualRetryCount}/3): {dlq.status}</span>
              {canManage && dlq.manualRetryCount < 3 && (
                <button type="button" className="btn-secondary" onClick={() => retryDlq.mutate(dlq.id)}>
                  Retry
                </button>
              )}
            </div>
          ))}
        </div>
      </div>
    </section>
  );
};
