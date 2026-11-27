# NEXUS M — Operations, Backup/Restore & Incident Runbook

**Status:** Verified (NEXUS M 1.0)  
**Last Updated:** 2026-11-27

## 1. Health & Observability Endpoints
- **Liveness:** `GET /health/live`
- **Readiness (PostgreSQL `nexus_m_app` check):** `GET /health/ready`
- **Structured Logs:** Compact JSON via Serilog (`X-Correlation-Id` propagated across HTTP, audit, and outbox/inbox records; secrets never logged).
- **Distributed Tracing & Metrics:** OpenTelemetry ASP.NET Core + HttpClient instrumentation (`traceparent` propagated to NEXUS C).

## 2. Backup & Restore Procedure (Verified Drill: 2026-11-27)
### Backup
```bash
pg_dump --format=custom --no-owner --no-privileges -d nexus_m -f nexus_m_$(date -u +%Y%m%dT%H%M%SZ).dump
```
*(Or logical SQL dump `pg_dump --no-owner --no-privileges -d nexus_m -f backup.sql` paired with continuous WAL archiving).*

### Restore Verification
1. Restore into an isolated target database (never overwrite an active database unverified).
2. Re-apply role grants via `DatabaseSchemaBootstrap.ApplyAsync(migratorConn, "nexus_m_app")`.
3. Verify `37/37` tables report `relrowsecurity = true AND relforcerowsecurity = true`.
4. Verify immutable triggers (`trg_m_audit_immutable`, `trg_m_metric_ver_immutable`, `trg_m_bi_snapshot_immutable`, `trg_m_wf_decision_immutable`, `trg_m_gov_risk_hist_immutable`) are active.

## 3. Dead-Letter & Reconciliation Incident Playbook (`M10`)
1. Inspect failed ingestion items via `GET /api/v1/m/integrations/dead-letters`.
2. Fix upstream schema or missing KPI definition first.
3. Replay via `POST /api/v1/m/integrations/dead-letters/{id}/retry` (capped at 3 manual retries to prevent infinite poison-message loops; every retry is immutably audited).
4. Execute `POST /api/v1/m/integrations/reconciliations` to confirm `MATCHED` count and sum against the authoritative source system (`NEXUS_F` / `NEXUS_E`).
