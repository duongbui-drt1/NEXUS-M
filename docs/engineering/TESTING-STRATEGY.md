# NEXUS M — Testing Strategy & Verification Matrix

**Status:** Verified (NEXUS M 1.0)  
**Last Updated:** 2026-11-27

## 1. Test Pyramid & Real Infrastructure Policy
- **Backend Integration & Security Suite (`tests/NexusM.IntegrationTests`):**
  - Uses `WebApplicationFactory<Program>` against a **real PostgreSQL 17 database** (`nexus_m_test_<guid>_tmp`) connecting at runtime under the **non-superuser `NOBYPASSRLS` role** (`nexus_m_app`).
  - Ephemeral databases are uniquely created per test fixture and dropped on teardown; persistent local sandbox databases (`nexus_m_local_sandbox`, `nexus_local_sandbox`, `nexus_dev`) are never touched.
- **Frontend Suite (`frontend/nexus-m-console/tests`):**
  - TypeScript strict mode (`tsc --noEmit`), Vitest + Testing Library (`vitest run`), and production bundle build (`vite build`).

## 2. Verified Security & Invariant Coverage
1. **Cross-Tenant `FORCE RLS` Isolation:** Verified across `m_foundation`, `m_audit`, `m_metrics`, `m_integration`, `m_executive`, `m_bi`, `m_workflow`, `m_org`, `m_governance`, `m_performance`, and `m_ai` (37/37 tables).
2. **Tenant Spoofing Rejection:** `X-Tenant-Id` conflicting with JWT `tid` → `403 TENANT_SPOOFING_DETECTED`.
3. **Fail-Closed Entitlement & RBAC:** Missing permission, disabled `NEXUS_M` / `NEXUS_M_AI` entitlement, or unreachable NEXUS C → `403` / `503`.
4. **Organization Scope Enforcement:** Branch/unit-restricted principals requesting out-of-scope KPIs → `403 ORGANIZATION_SCOPE_DENIED`.
5. **Separation of Duties (Maker-Checker):** Enforced on `M03` workflow decisions and `M07` board report publications (`403 SEPARATION_OF_DUTIES_VIOLATION`).
6. **PostgreSQL Immutability Triggers:** Verified `BEFORE UPDATE OR DELETE` protection + `SELECT, INSERT`-only runtime grants on `business_audit_events`, `metric_definition_versions`, `report_snapshots`, `decision_logs`, and `risk_assessment_histories`.
7. **Idempotency & Anti-Mixing:** `Idempotency-Key` replay (`X-Idempotent-Replayed: true`), payload mismatch (`409`), inbox `(tenant_id, event_id, consumer_name)` deduplication, and `422 METRIC_COMPARISON_INVALID` anti-mixing guardrails.
8. **AI Safety Guardrails:** Rejects `autonomousExecutionRequested` (`403 AI_AUTONOMOUS_ACTION_FORBIDDEN`), enforces `NEXUS_M_AI` entitlement, and cites grounded metric versions/freshness without inventing missing metrics.
