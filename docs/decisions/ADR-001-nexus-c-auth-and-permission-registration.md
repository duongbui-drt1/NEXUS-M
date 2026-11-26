# ADR-001: NEXUS C Authentication, Session & `NEXUS_M` Permission Catalog Registration

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Technical Lead  
**References:** NEXUS C `PLATFORM-CONTRACT.md` v1.0.0, `ECOSYSTEM-INTEGRATION.md`, `src/Nexus.SharedKernel/Security/Permissions.cs`

## Context
NEXUS C is the authoritative identity, authentication, tenancy, and authorization platform for the NEXUS ecosystem.
- Human users authenticate against NEXUS C (`/api/v1/auth/login`, `/refresh`, `/switch-tenant`, or Clerk JWKS dual-scheme) and receive a tenant-bound JWT (`tid` claim is authoritative).
- Service-to-service calls exchange `client_id` + `client_secret` at `POST /api/v1/auth/token` (`grant_type=client_credentials`) for a short-lived (600s) service JWT with explicit scopes (wildcards `*` are forbidden).
- **Verified Phase 0 Gap:** NEXUS C's `access_control.roles` already supports `ApplicationCode = "NEXUS_M"`, and `POST /api/v1/auth/authorize` evaluates RBAC + Explicit Deny + Org Scope. However, `RolesController` currently exposes only `GET /api/v1/platform/roles/permissions` backed by 45 hardcoded `platform.*` permissions — there is no endpoint to register `m.*` domain permissions.

## Decision
1. **Zero Duplicate Auth or Role Authority in NEXUS M:**
   - NEXUS M MUST NOT issue user JWTs, store user passwords, maintain shadow user/membership tables, or maintain an independent role-assignment database.
   - Every HTTP request to NEXUS M validates the NEXUS C JWT (`Issuer`, `Audience`, signing key / Clerk JWKS). `tenantId` is derived strictly from the verified `tid` claim; any mismatched `X-Tenant-Id` header MUST fail closed with `403 TENANT_SPOOFING_DETECTED`.
2. **Service Principal (`nexus-m-service`):**
   - NEXUS M uses a dedicated least-privilege `ServiceClient` (`clientId: "nexus-m-service"`, `applicationCode: "NEXUS_M"`) for platform metadata/entitlement/notification/file calls, never a superuser token.
3. **`NEXUS_M` Permission Namespace (`m.<module>.<action>`):**
   - NEXUS M defines a versioned permission manifest (e.g., `m.workspace.read`, `m.workspace.manage`, `m.dashboard.read`, `m.dashboard.manage`, `m.kpi.read`, `m.kpi.manage`, `m.report.read`, `m.report.manage`, `m.report.export`, `m.workflow.read`, `m.workflow.create`, `m.workflow.approve`, `m.workflow.manage`, `m.org.read`, `m.org.manage`, `m.risk.read`, `m.risk.manage`, `m.compliance.read`, `m.compliance.manage`, `m.performance.read`, `m.performance.manage`, `m.integration.read`, `m.integration.manage`, `m.ai.invoke`).
   - **Target Contract (Additive NEXUS C Extension):** Register this manifest into NEXUS C via an additive endpoint (`POST /api/v1/platform/applications/{code}/permissions/sync`) so `TenantOwner`/`TenantAdmin` can assign `NEXUS_M` roles in NEXUS C Console and `POST /api/v1/auth/authorize` evaluates `m.*` permissions natively.
   - **Interim Guard (Until C Endpoint Ships):** Until the additive C sync endpoint is deployed, NEXUS M's authorization adapter MUST still delegate authentication, tenant membership, entitlement (`GET /api/v1/platform/entitlements/NEXUS_M`), and organization scope (`GET /api/v1/auth/organization-scope`) to NEXUS C, and map verified C roles/permissions fail-closed without creating any mutable local role store.
4. **Fail-Closed Policy:**
   - If NEXUS C authorization/entitlement verification is unreachable or returns an error, NEXUS M MUST deny access (`503` or `403`), never fail open.

## Consequences
- **Positive:** Single source of truth for identity, session revocation, tenant suspension, and RBAC across C, F, E, and M.
- **Trade-off:** Requires one small additive PR in NEXUS C (`permissions/sync`) to enable full custom `m.*` role editing in NEXUS C Console.
