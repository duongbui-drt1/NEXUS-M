# ADR-004: Multi-Tenant PostgreSQL Isolation, `FORCE RLS` & Local Sandbox Discipline

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Database Architect / Security Engineer

## Context
NEXUS M holds sensitive executive KPIs, financial/operational read models, board reports, risk registers, and compliance evidence across multiple tenants. Cross-tenant leakage is an unacceptable critical defect. NEXUS C 1.0 proved PostgreSQL 17 + `FORCE ROW LEVEL SECURITY` + non-superuser runtime DB role as the ecosystem standard.

## Decision
1. **Dedicated PostgreSQL Database & Schema-per-Bounded-Context:**
   - NEXUS M uses its own PostgreSQL 17 database (`nexus_m`), strictly separate from NEXUS C (`nexus_c`), NEXUS F, and NEXUS E databases. No cross-database SQL access is permitted.
   - Schemas cleanly separate bounded contexts and data classes:
     - `m_foundation` (workspaces, module configs, user preferences, saved views)
     - `m_org` (org unit profiles, positions, effective-dated management assignments)
     - `m_metrics` (metric definitions, versions, targets, observations, lineage, freshness/quality)
     - `m_executive` (dashboards, widgets, alerts)
     - `m_bi` (report definitions, runs, snapshots, schedules)
     - `m_workflow` (M03 templates, comments, evidence links, SLA policies, inbox projections)
     - `m_governance` (risks, assessments, controls, incidents, compliance obligations)
     - `m_performance` (strategic plans, OKRs, initiatives, portfolios)
     - `m_integration` (connectors, ingestion runs, inbox/outbox, dead letters, reconciliation)
     - `m_audit` (append-only M-local business audit supplements where needed alongside C audit)
2. **Mandatory `FORCE ROW LEVEL SECURITY` on Every Tenant Table:**
   - Every tenant-scoped table includes `tenant_id uuid NOT NULL` and enables `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY`.
   - Policy checks `tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid`.
   - Runtime application connects as a **non-superuser, `NOBYPASSRLS` PostgreSQL role** (`nexus_m_app`). Migrations run under a separate owner role (`nexus_m_migrator`).
   - EF Core connection/transaction interceptor sets `SET LOCAL app.current_tenant_id = '<tid>'` for every unit of work and background job. Missing tenant context fails closed (returns 0 rows / rejects writes).
3. **Tenant-Aware Caching, Search, Exports & Jobs:**
   - All cache keys MUST be prefixed with `nexus-m:{tenantId}:...`.
   - Background jobs carry explicit `TenantId` and `CorrelationId` in their payload and open a tenant-scoped DB session per tenant.
4. **Local Development & Test Database Hygiene:**
   - Maintain a dedicated untracked local sandbox DB (`nexus_m_local_sandbox`) for manual testing; never drop or wipe it, and never touch NEXUS C's `nexus_local_sandbox` or `nexus_dev`.
   - Automated integration/security tests MUST create and drop only uniquely named ephemeral databases (`nexus_m_test_<guid>_tmp`) and execute security assertions using the real `NOBYPASSRLS` role.

## Consequences
- **Positive:** Defense-in-depth multi-tenant isolation enforced by PostgreSQL itself, even if an application query misses a `WHERE tenant_id = ...` clause.
- **Trade-off:** Requires disciplined transaction scoping (`SET LOCAL`) for every HTTP request, event consumer, and background worker.
