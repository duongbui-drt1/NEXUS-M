# ADR-003: Universal Organization Hierarchy & Effective-Dated Management Assignments

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Technical Lead  
**References:** NEXUS C `OrganizationsController.cs`, `GET /api/v1/auth/organization-scope`, NEXUS M `04-DOMAIN-MODEL.md`

## Context
NEXUS C 1.0 owns authoritative organizational security entities:
- `LegalEntity`
- `Branch`
- `OrganizationUnit` (hierarchical tree)
- `UserOrgAssignment` (drives `GET /api/v1/auth/organization-scope` → `{ legalEntityIds, branchIds, organizationUnitIds }`)

NEXUS M must serve micro-businesses through enterprise groups with progressive complexity:
`Tenant → Workspace → Organization → Legal Entity / Business Unit / Department → Team, Position, and Effective-Dated Management Assignment`.

## Decision
1. **Strict Authority Split:**
   - **NEXUS C remains authoritative** for `Tenant`, `LegalEntity`, `Branch`, `OrganizationUnit`, and security-boundary `UserOrgAssignment`. NEXUS M MUST NOT create duplicate `LegalEntity` or `Branch` master tables.
   - **NEXUS M owns management overlays** referencing C's stable GUIDs:
     - `Workspace` (1..N per Tenant; default 1 auto-provisioned workspace for micro/SME tenants so small organizations require zero complex setup).
     - `OrgUnitProfile` (maps C `OrganizationUnitId` to management unit type: `BusinessUnit`, `Department`, `Team`, `CostCenter`, plus display metadata and reporting parent).
     - `Position` (management roles/seats such as CEO, CFO, Regional Director, Team Lead attached to an org unit).
     - `ManagementAssignment` (effective-dated `validFrom` / `validTo` assignment of a C `userId` to a `Position` / `OrgUnitProfile` with reporting-line linkage).
2. **Temporal Integrity:**
   - Historical assignments are never overwritten in place; closing an assignment sets `validTo` and opens a new effective-dated record so historical KPIs, org charts, and board reports preserve accurate point-in-time accountability.
3. **Authorization Enforcement:**
   - Security filtering ALWAYS intersects requested management org scopes with the caller's verified NEXUS C `organization-scope` (`legalEntityIds`, `branchIds`, `organizationUnitIds`). Management overlays can narrow visibility, never widen it beyond NEXUS C's granted scope.

## Consequences
- **Positive:** Micro-businesses work immediately with a single default Workspace and zero hierarchy setup; enterprise groups get multi-entity, multi-BU, effective-dated reporting lines without duplicating C's org authority.
- **Trade-off:** M must sync/cache C org unit references (via API/events) and handle soft-deactivated C org units gracefully in historical views.
