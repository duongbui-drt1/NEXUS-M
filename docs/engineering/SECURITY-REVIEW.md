# NEXUS M — Security Review & Hardening Evidence (Phase 11)

**Status:** Verified  
**Review Date:** 2026-11-27

## 1. Security Controls Verification Summary

| Control Area | Implementation | Verification Evidence |
|---|---|---|
| **Single Identity & AuthZ Authority** | Consumes NEXUS C JWT + `/api/v1/auth/permissions` + `/organization-scope` + `/entitlements/*`; zero local password/role tables | `Phase2FoundationAndSecurityTests` PASS |
| **Anti-Tenant-Spoofing** | `CorrelationAndRequestContextMiddleware` rejects `X-Tenant-Id != tid` with `403 TENANT_SPOOFING_DETECTED` | `Phase2FoundationAndSecurityTests` PASS |
| **PostgreSQL `FORCE RLS`** | `37/37` tables across all 11 `m_*` schemas enforce `FORCE ROW LEVEL SECURITY` under `NOBYPASSRLS` role `nexus_m_app` | `Phase11_ProductionHardening` + `pg_class` verification PASS |
| **Fail-Closed Core Outage** | Unreachable NEXUS C entitlement/authz returns `503 ServiceUnavailable`, never failing open | `Phase2FoundationAndSecurityTests` PASS |
| **Organization-Scope Enforcement** | `MetricsService`, `OrganizationOverlayService`, `PerformanceService` intersect queries with verified NEXUS C `OrgScope` | `Phase3DataAndKpiFoundationTests` (`403 ORGANIZATION_SCOPE_DENIED`) PASS |
| **Separation of Duties** | Maker-Checker enforced on `M03` workflow approvals and `M07` board report publications | `Phase4To11PlatformCompletionTests` (`403 SEPARATION_OF_DUTIES_VIOLATION`) PASS |
| **Append-Only Immutability** | PostgreSQL triggers + `REVOKE UPDATE, DELETE` on audit, metric versions, BI snapshots, workflow decision logs, and risk histories | `AUDIT_IMMUTABLE` & `METRIC_VERSION_IMMUTABLE` trigger tests PASS |
| **Plaintext Secret Prevention** | `DataConnector` rejects non-vault secret strings (`422 CONNECTOR_SECRET_PLAINTEXT_FORBIDDEN`); `.env` gitignored | `Phase3DataAndKpiFoundationTests` PASS |
| **Export & Search Authorization** | BI exports require `m.report.export` + audit log; Enterprise Search filters per `m.*` permission at SQL query time | `Phase4To11PlatformCompletionTests` PASS |
| **Governed Optional AI (`M09`)** | Gated by `NEXUS_M_AI` entitlement + `m.ai.invoke`; blocks `autonomousExecutionRequested` (`403`); cites sources | `Phase4To11PlatformCompletionTests` PASS |
| **Frontend Error Hygiene** | Lower-privilege users see soft-red `"Vui lòng liên hệ Admin để xem chi tiết"` without leaking internal permission codes | Vitest `AppShell.test.tsx` (4/4 PASS) |

## 2. Remaining Operational Governance Actions Before Public Production Launch
- Enable GitHub branch protection rules on `main` in `duongbui-drt1/NEXUS-M` (require PR approval + passing `NEXUS M CI` status checks, block force-push).
- Provision dedicated production `ServiceClient` (`nexus-m-service`) secret in production Vault/Secret Manager.
