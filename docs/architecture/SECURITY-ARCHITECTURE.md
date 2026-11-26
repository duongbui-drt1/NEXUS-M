# NEXUS M — Security Architecture

**Status:** Phase 1 Architecture Freeze  
**Last Updated:** 2026-11-27

## 1. Security Principles
1. **Fail Closed Everywhere:** Missing tenant context, expired/invalid JWT, unreachable NEXUS C authorization check, or unmapped permission ALWAYS results in denial (`401`/`403`/`503`).
2. **Server-Side Enforcement Only:** The React frontend hides unauthorized navigation/actions for UX only; every API endpoint, query, export, search, and realtime stream enforces authorization server-side.
3. **Defense-in-Depth Multi-Tenancy:** Application-layer tenant resolution + PostgreSQL `FORCE ROW LEVEL SECURITY` + non-superuser `NOBYPASSRLS` role + tenant-prefixed cache keys.

## 2. Authentication & Request Context Pipeline
Every authenticated HTTP request to NEXUS M executes the following pipeline in strict order:
1. **Correlation & Tracing:** Extract or generate `X-Correlation-Id` and W3C `traceparent`.
2. **JWT Validation:** Validate signature, issuer, audience, and lifetime against NEXUS C signing key / Clerk JWKS.
3. **Tenant Context Resolution:**
   - Extract authoritative `tenantId` from JWT `tid` claim.
   - If `X-Tenant-Id` header is present and differs from `tid`, abort immediately with `403 TENANT_SPOOFING_DETECTED`.
   - Reject missing `tid` on tenant-scoped endpoints (`403 TENANT_CONTEXT_REQUIRED`).
4. **Entitlement Gate:** Verify tenant has active `NEXUS_M` entitlement (cached short TTL with tenant-prefixed key, invalidated by `platform.subscription.*.v1` / `platform.application.*.v1`).
5. **Authorization & Org-Scope Gate:**
   - Verify required `m.*` permission via NEXUS C authorization adapter (ADR-001).
   - Resolve effective `{ legalEntityIds, branchIds, organizationUnitIds }` from NEXUS C `/api/v1/auth/organization-scope`.
   - Intersect any user-requested org/entity filter with the verified scope; deny (`403 ORGANIZATION_SCOPE_DENIED`) if outside scope.
6. **Database RLS Binding:** Open PostgreSQL transaction as `nexus_m_app` (`NOBYPASSRLS`) and execute `SET LOCAL app.current_tenant_id = @tenantId`.

## 3. Sensitive Data, Exports & Search Controls
- **Field Masking:** Sensitive compensation/financial/board fields are masked by default unless the caller holds the explicit classification permission (`EXECUTIVE_RESTRICTED`, `BOARD_CONFIDENTIAL`).
- **Export Authorization:** Exporting PDF/XLSX/CSV requires explicit `m.report.export` permission + resource/org scope check + immutable audit record (`actor`, `reportId`, `filters`, `rowCount`, `correlationId`).
- **Enterprise Search:** Search queries MUST inject `tenantId` and verified `organization-scope` predicates at query time — never post-filter after unbounded retrieval.
- **Secret Hygiene:** Zero plaintext secrets in Git, appsettings, DB columns, or logs. Integration credentials reference external secret vaults (`vault://`, `kms://`, `env://`) or encrypted secret storage managed via NEXUS C.

## 4. Immutable Business Audit (`m_audit`)
- `m_audit.business_audit_events` enforces:
  - PostgreSQL `BEFORE UPDATE OR DELETE` trigger raising an exception (`AUDIT_IMMUTABLE`).
  - Runtime role `nexus_m_app` granted `SELECT, INSERT` ONLY (no `UPDATE`, `DELETE`, `TRUNCATE`).
  - `FORCE ROW LEVEL SECURITY` scoped by `tenant_id`.
- Mandatory audited events: metric definition publication, report publication/export, workflow submission/decision, org/assignment changes, risk assessment updates, compliance evidence changes, connector/integration configuration changes, and sensitive data access.

## 5. Mandatory Security Verification Suite (Phase 2+)
Every module PR MUST include automated security tests executed against a real PostgreSQL instance using the `NOBYPASSRLS` role:
- Cross-tenant read/write isolation (`Tenant A` token cannot read/mutate `Tenant B` rows even via direct ID).
- Tenant spoofing header rejection (`X-Tenant-Id != tid` → `403`).
- Unauthorized / missing-permission rejection (`401`/`403`).
- Organization-scope boundary violation (`Branch A` manager requesting `Branch B` KPIs → `403`).
- Export authorization & audit trail verification.
- Audit table immutability verification (`UPDATE`/`DELETE` rejected by PostgreSQL).
