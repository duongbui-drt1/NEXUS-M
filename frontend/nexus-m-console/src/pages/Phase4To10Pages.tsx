import React, { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { nexusMApi } from '../api/client';
import { ForbiddenBanner } from '../components/AppShell';
import type { ManagementContextDto } from '../types';

export const ExecutiveDashboardPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.dashboard.read');
  const dashboards = useQuery({ queryKey: ['m-dashboards'], queryFn: () => nexusMApi.listDashboards(), enabled: canRead });
  const alerts = useQuery({ queryKey: ['m-alerts'], queryFn: () => nexusMApi.listAlerts(), enabled: canRead });

  if (!canRead) return <section className="page"><h1>Executive Intelligence (M01)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Executive Intelligence (M01)</h1>
      <p className="muted">Role-aware executive dashboards, period-over-period comparisons, freshness indicators, and threshold alerts.</p>
      <div className="card-grid">
        {dashboards.data?.data.map((d) => (
          <div key={d.id} className="card">
            <div className="card-header">
              <strong>{d.title}</strong>
              <span className="pill">{d.audienceRole}</span>
            </div>
            <div className="meta-row">
              <span>Code: {d.code}</span>
              <span>v{d.version}</span>
            </div>
          </div>
        ))}
      </div>
      <h2 style={{ marginTop: 24 }}>Active Management Alerts ({alerts.data?.data.length ?? 0})</h2>
      <table className="data-table">
        <thead>
          <tr><th>Metric</th><th>Severity</th><th>Summary</th><th>Freshness</th><th>Status</th></tr>
        </thead>
        <tbody>
          {alerts.data?.data.map((a) => (
            <tr key={a.id}>
              <td><strong>{a.metricCode}</strong></td>
              <td>{a.severity}</td>
              <td>{a.summary}</td>
              <td><span className="pill subtle">{a.freshnessStatus}</span></td>
              <td>{a.status}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
};

export const BiWorkspacePage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.report.read');
  const reports = useQuery({ queryKey: ['m-bi-reports'], queryFn: () => nexusMApi.listBiReports(), enabled: canRead });

  if (!canRead) return <section className="page"><h1>Business Intelligence (M02)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Business Intelligence & Saved Reports (M02)</h1>
      <p className="muted">Reproducible report runs with SHA-256 snapshots, FX currency normalization, and permission-audited CSV/JSON exports.</p>
      <table className="data-table">
        <thead>
          <tr><th>Code</th><th>Title</th><th>Metrics</th><th>Currency</th><th>Schedule</th></tr>
        </thead>
        <tbody>
          {reports.data?.data.map((r) => (
            <tr key={r.id}>
              <td><strong>{r.code}</strong></td>
              <td>{r.title}</td>
              <td><code>{r.metricCodesCsv}</code></td>
              <td>{r.normalizeToCurrency ?? 'Native'}</td>
              <td>{r.scheduleCron ?? 'On-demand'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
};

export const WorkflowInboxPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.workflow.read');
  const inbox = useQuery({ queryKey: ['m-wf-inbox'], queryFn: () => nexusMApi.listWorkflowInbox(), enabled: canRead });

  if (!canRead) return <section className="page"><h1>Workflow & Approval Hub (M03)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Workflow & Approval Hub (M03)</h1>
      <p className="muted">Unified approval inbox backed by NEXUS C Shared Workflow Engine with Maker-Checker separation of duties and SLA tracking.</p>
      <table className="data-table">
        <thead>
          <tr><th>Category</th><th>Title</th><th>Summary</th><th>SLA Due (UTC)</th><th>Status</th></tr>
        </thead>
        <tbody>
          {inbox.data?.data.map((item) => (
            <tr key={item.id}>
              <td><span className="pill subtle">{item.category}</span></td>
              <td><strong>{item.title}</strong></td>
              <td>{item.summary}</td>
              <td>{item.slaDueAtUtc}</td>
              <td><span className="pill">{item.status}</span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
};

export const OrganizationPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.org.read');
  const [asOfDate, setAsOfDate] = useState('');
  const overview = useQuery({
    queryKey: ['m-org-overview', asOfDate],
    queryFn: () => nexusMApi.getOrgOverview(asOfDate || undefined),
    enabled: canRead,
  });

  if (!canRead) return <section className="page"><h1>Organization & People (M04)</h1><ForbiddenBanner /></section>;

  const d = overview.data?.data;
  return (
    <section className="page">
      <h1>Organization & People Overview (M04)</h1>
      <p className="muted">Effective-dated positions and assignments overlaid onto authoritative NEXUS C organization units.</p>
      <div className="form-row" style={{ marginBottom: 16 }}>
        <input type="date" value={asOfDate} onChange={(e) => setAsOfDate(e.target.value)} aria-label="Point-in-time date" />
      </div>
      {d && (
        <div className="card-grid">
          <div className="card"><strong>Units</strong><p>{d.totalUnits}</p></div>
          <div className="card"><strong>Positions</strong><p>{d.totalPositions}</p></div>
          <div className="card"><strong>Active FTE / Target</strong><p>{d.totalFteAllocated} / {d.totalHeadcountTarget}</p></div>
        </div>
      )}
    </section>
  );
};

export const RiskGovernancePage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.risk.read');
  const risks = useQuery({ queryKey: ['m-risks'], queryFn: () => nexusMApi.listRisks(), enabled: canRead });

  if (!canRead) return <section className="page"><h1>Risk & Governance (M05)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Risk & Governance Register (M05)</h1>
      <p className="muted">Versioned risk matrix assessments, controls, incidents, and escalation status.</p>
      <table className="data-table">
        <thead>
          <tr><th>Code</th><th>Title</th><th>Category</th><th>L × I = Score</th><th>Level</th><th>Status</th></tr>
        </thead>
        <tbody>
          {risks.data?.data.map((r) => (
            <tr key={r.id}>
              <td><strong>{r.code}</strong></td>
              <td>{r.title}</td>
              <td>{r.category}</td>
              <td>{r.likelihood} × {r.impact} = <strong>{r.score}</strong></td>
              <td><span className="pill">{r.riskLevel}</span></td>
              <td>{r.status} (v{r.assessmentVersion})</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
};

export const CompliancePage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.compliance.read');
  const obligations = useQuery({ queryKey: ['m-obligations'], queryFn: () => nexusMApi.listObligations(), enabled: canRead });

  if (!canRead) return <section className="page"><h1>Compliance Intelligence (M06)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Compliance Intelligence (M06)</h1>
      <p className="muted">Multi-jurisdiction regulatory obligations, authoritative validation status, deadlines, and NEXUS C file evidence.</p>
      <table className="data-table">
        <thead>
          <tr><th>Code</th><th>Jurisdiction</th><th>Title</th><th>Validation</th><th>Due (UTC)</th><th>Status</th></tr>
        </thead>
        <tbody>
          {obligations.data?.data.map((o) => (
            <tr key={o.id}>
              <td><strong>{o.code}</strong></td>
              <td><span className="pill subtle">{o.jurisdictionCode}</span></td>
              <td>{o.title}</td>
              <td>{o.contentValidationStatus}</td>
              <td>{o.dueAtUtc}</td>
              <td><span className="pill">{o.status}</span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
};

export const BoardReportingPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.report.read');
  const packs = useQuery({ queryKey: ['m-board-reports'], queryFn: () => nexusMApi.listBoardReports(), enabled: canRead });

  if (!canRead) return <section className="page"><h1>Board & Corporate Reporting (M07)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Board & Corporate Reporting (M07)</h1>
      <p className="muted">Consolidated multi-entity board packs with Maker-Checker publication approval and SHA-256 snapshot integrity.</p>
      <table className="data-table">
        <thead>
          <tr><th>Code</th><th>Title</th><th>Currency</th><th>Metrics</th><th>Status</th><th>Checksum</th></tr>
        </thead>
        <tbody>
          {packs.data?.data.map((p) => (
            <tr key={p.id}>
              <td><strong>{p.code}</strong></td>
              <td>{p.title}</td>
              <td>{p.consolidationCurrency}</td>
              <td><code>{p.metricCodesCsv}</code></td>
              <td><span className="pill">{p.status}</span></td>
              <td><code>{p.sha256Checksum.slice(0, 12)}…</code></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
};

export const PerformanceOkrPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const canRead = context.permissions.includes('m.performance.read');
  const now = new Date();
  const start = new Date(now.getFullYear(), 0, 1).toISOString();
  const end = new Date(now.getFullYear(), 11, 31).toISOString();
  const objectives = useQuery({
    queryKey: ['m-objectives'],
    queryFn: () => nexusMApi.listObjectives(start, end),
    enabled: canRead,
  });

  if (!canRead) return <section className="page"><h1>Resource & Performance Management (M08)</h1><ForbiddenBanner /></section>;

  return (
    <section className="page">
      <h1>Resource & Performance Management — OKRs (M08)</h1>
      <p className="muted">Strategic objectives and Key Results clearly separating planned targets from live authoritative metric actuals.</p>
      {objectives.data?.data.map((obj) => (
        <div key={obj.id} className="card" style={{ marginBottom: 14 }}>
          <div className="card-header">
            <strong>{obj.code} — {obj.title}</strong>
            <span className="pill subtle">{obj.periodCode}</span>
          </div>
          {obj.keyResults.map((kr) => (
            <div key={kr.id} className="meta-row">
              <span>{kr.title}</span>
              <span>Target: <strong>{kr.targetValue}</strong></span>
              <span>Actual: <strong>{kr.actualValue ?? '—'}</strong> ({kr.attainmentPercent ?? 0}%)</span>
            </div>
          ))}
        </div>
      ))}
    </section>
  );
};

export const AiAssistantPage: React.FC<{ context: ManagementContextDto }> = ({ context }) => {
  const aiModule = context.modules.find((m) => m.code === 'M09');
  const canUse = Boolean(aiModule?.isAccessible) && context.permissions.includes('m.ai.invoke');
  const [question, setQuestion] = useState('');
  const [metricsCsv, setMetricsCsv] = useState('');

  const askMutation = useMutation({
    mutationFn: () =>
      nexusMApi.askAi({
        question,
        metricCodes: metricsCsv.split(',').map((s) => s.trim()).filter(Boolean),
      }),
  });

  if (!canUse) {
    return (
      <section className="page">
        <h1>AI Executive Intelligence (M09)</h1>
        <ForbiddenBanner />
      </section>
    );
  }

  return (
    <section className="page">
      <h1>AI Executive Intelligence (M09 — Optional Governed Capability)</h1>
      <p className="muted">
        Permission-aware grounded retrieval via NEXUS C AI Gateway. Autonomous approvals are strictly prohibited; human confirmation is required.
      </p>
      <form
        className="card form-stack"
        onSubmit={(e) => {
          e.preventDefault();
          askMutation.mutate();
        }}
      >
        <label>
          Metric Codes to Ground On (comma-separated)
          <input value={metricsCsv} onChange={(e) => setMetricsCsv(e.target.value)} placeholder="NET_REVENUE, SHIPMENTS_DELIVERED" />
        </label>
        <label>
          Executive Question
          <textarea rows={3} value={question} onChange={(e) => setQuestion(e.target.value)} required />
        </label>
        <button type="submit" className="btn-primary" disabled={askMutation.isPending}>Ask Grounded Assistant</button>
      </form>

      {askMutation.data && (
        <div className="card" style={{ marginTop: 16 }}>
          <div className="card-header">
            <strong>Grounded Response ({askMutation.data.data.modelCode})</strong>
            <span className="pill">Human Confirmation Required</span>
          </div>
          <p>{askMutation.data.data.answer}</p>
          <div className="meta-row">
            {askMutation.data.data.citations.map((c) => (
              <span key={c.metricCode} className="pill subtle">
                {c.metricCode}: {c.status} ({c.freshnessStatus})
              </span>
            ))}
          </div>
        </div>
      )}
    </section>
  );
};
