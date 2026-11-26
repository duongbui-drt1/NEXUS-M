# NEXUS M — Architecture Decision Records (ADR) Index

**Status:** Phase 1 Architecture & Domain Freeze  
**Last Updated:** 2026-11-27

| ADR | Title | Status | Scope |
|---|---|---|---|
| [ADR-001](ADR-001-nexus-c-auth-and-permission-registration.md) | NEXUS C Authentication, Session & `NEXUS_M` Permission Catalog Registration | Proposed (Frozen for Phase 1 Review) | Auth, RBAC, NEXUS C Contract |
| [ADR-002](ADR-002-workflow-ownership-boundary.md) | Workflow Engine Ownership Boundary (`M03` vs NEXUS C Shared Workflow) | Proposed (Frozen for Phase 1 Review) | M03, NEXUS C Workflow |
| [ADR-003](ADR-003-universal-organization-model-and-effective-dating.md) | Universal Organization Hierarchy & Effective-Dated Management Assignments | Proposed (Frozen for Phase 1 Review) | M04, M00, NEXUS C Organization |
| [ADR-004](ADR-004-multi-tenant-database-isolation-and-rls.md) | Multi-Tenant PostgreSQL Isolation, `FORCE RLS` & Local Sandbox Discipline | Proposed (Frozen for Phase 1 Review) | Data Architecture, Security |
| [ADR-005](ADR-005-event-envelope-ingestion-and-realtime-transport.md) | Event Envelope, Idempotent Ingestion (`M10`) & Realtime Transport (`SSE` vs `SignalR`) | Proposed (Frozen for Phase 1 Review) | M10, Messaging, Realtime |
| [ADR-006](ADR-006-metric-semantics-and-analytics-contract.md) | Metric Semantics, Currency Normalization, Lineage & Freshness Contract | Proposed (Frozen for Phase 1 Review) | M01, M02, M10 |
| [ADR-007](ADR-007-shared-infrastructure-reuse-and-retention.md) | Reuse of NEXUS C Shared Services (Files, Notifications, Audit, FX, AI Gateway) & Retention | Proposed (Frozen for Phase 1 Review) | Cross-cutting, M05–M09 |
