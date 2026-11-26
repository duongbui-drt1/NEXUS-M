# NEXUS M — MVP Scope Freeze (v1.0)

**Status:** Phase 1 Freeze (Subject to Phase 1 Review)  
**Last Updated:** 2026-11-27

## 1. Included in NEXUS M 1.0 MVP (Phases 2 → 6 + M04 Core)
1. **Phase 2 — M00 Management Foundation:**
   - .NET 10 modular monolith (`NexusM.Api`, `NexusM.SharedKernel`, `NexusM.BuildingBlocks`, module projects) + React 19 / TypeScript / Vite Console (`frontend/nexus-m-console`).
   - NEXUS C JWT validation, `tid` anti-spoofing, `NEXUS_M` entitlement check, org-scope resolution, fail-closed authorization middleware.
   - Workspace auto-provisioning (simple default for micro/SME, multi-workspace ready), Module Registry, dynamic permission/entitlement-aware navigation, localization (`vi`/`en`), `m_audit` immutable business audit, OpenTelemetry/Serilog, CI pipeline, PostgreSQL `FORCE RLS` with non-superuser `nexus_m_app` role, and dedicated `nexus_m_local_sandbox` DB.
2. **Phase 3 — M10 Data & Integration + KPI Catalog Foundation:**
   - Versioned metric definitions (`m_metrics`), metric observations with source/lineage/freshness/quality, anti-mixing validation (currency/FX via C, accounting basis, period), ingestion API + RabbitMQ outbox/inbox + reconciliation logs.
3. **Phase 4 — M01 Executive Intelligence:**
   - Role-aware Executive Dashboard, configurable KPI widgets, saved layouts, freshness/quality badges, period/org comparison with server-side org-scope enforcement, drill-down, alerts, and SSE realtime updates.
4. **Phase 5 — M02 Business Intelligence (Core):**
   - Analytics workspace, report builder, saved reports, permission-checked & audited CSV/XLSX/PDF export (stored/served via NEXUS C File Storage).
5. **Phase 6 — M03 Workflow & Approval Hub:**
   - Unified Approval Inbox projected from NEXUS C `platform.workflow.*.v1`, management templates, submit/approve/reject/delegate backed by NEXUS C Shared Workflow Engine, comments, evidence attachments (`cFileId`), SLA tracking.

## 2. Post-MVP (Phases 7 → 10)
- **Phase 7:** Full M04 Organization & People (interactive org chart, positions, effective-dated management assignments, workforce overview).
- **Phase 8:** M05 Risk & Governance + M06 Compliance Intelligence.
- **Phase 9:** M07 Board & Corporate Reporting + M08 Resource & Performance Management (OKRs/portfolios).
- **Phase 10:** M09 Optional AI Executive Intelligence (strictly behind NEXUS C AI Gateway + permission-aware retrieval + citations).
- **Phase 11:** Production Hardening (penetration/security audit, performance benchmarks, backup/restore drills, release runbooks).

## 3. Permanent Exclusions (Non-Goals)
- Accounting ledger / invoicing (NEXUS F).
- Logistics / shipment execution (NEXUS E).
- Identity / authentication / shadow RBAC authority (NEXUS C).
- Full HR/payroll system or full legal CLM.
- Autonomous AI transaction approval.
