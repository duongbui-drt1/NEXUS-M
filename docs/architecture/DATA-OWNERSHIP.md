# NEXUS M — Data Ownership Matrix

**Status:** Phase 1 Architecture Freeze  
**Last Updated:** 2026-11-27

## 1. Ecosystem Authority Matrix

| Domain / Data Entity | System of Record (Authoritative) | NEXUS M Role | Integration Pattern |
|---|---|---|---|
| Users, Identities, Sessions, Auth Tokens, Clerk Link | **NEXUS C** | Consumer (JWT validation only) | HTTP Auth / JWKS |
| Tenants, Tenant Status, Memberships | **NEXUS C** | Consumer (`tid` claim + events) | JWT + `platform.tenant.*.v1` |
| Roles, Permissions, Explicit Deny, Org-Scope AuthZ | **NEXUS C** | Consumer (`authorize`, `permissions`, `organization-scope`) + registers `m.*` manifest | REST + ADR-001 |
| Subscriptions, Plans, Entitlements, Usage Quotas | **NEXUS C** | Consumer (`entitlements`, `usage`) | REST + `platform.subscription.*.v1` |
| Legal Entities, Branches, Organization Units, Security Assignments | **NEXUS C** | Consumer (references stable GUIDs) | REST `/organizations/*` |
| Core Workflow Definitions, Instances, Task Decisions, Delegations | **NEXUS C** | Orchestrator & Inbox Consumer | REST `/workflows/*` + `platform.workflow.*.v1` |
| File Blobs, MIME Validation, Signed Download URLs | **NEXUS C** | Consumer (stores `cFileId` + business link) | REST `/files/*` |
| Notifications Delivery (Email/Resend, In-App, Webhooks) | **NEXUS C** | Producer (sends via templates) | REST `/notifications/send` |
| Reference Data & FX Exchange Rates | **NEXUS C** | Consumer (cached with rate date) | REST `/fx`, `/reference-data` |
| **Workspaces, Management Config, Saved Views, Preferences** | **NEXUS M (`m_foundation`)** | **Authoritative Owner** | M PostgreSQL (`FORCE RLS`) |
| **Org Unit Profiles, Positions, Reporting Lines, Effective-Dated Mgmt Assignments** | **NEXUS M (`m_org`)** | **Authoritative Owner (Overlay)** | References C `OrganizationUnitId` / `UserId` |
| **Metric Definitions, Versions, Targets, Management Read Models, Lineage, Freshness** | **NEXUS M (`m_metrics`)** | **Authoritative Owner** | Built from F/E/External contracts |
| **Executive Dashboards, Widgets, Management Alerts** | **NEXUS M (`m_executive`)** | **Authoritative Owner** | Reads `m_metrics` |
| **BI Report Definitions, Runs, Immutable Snapshots, Schedules** | **NEXUS M (`m_bi`)** | **Authoritative Owner** | Reads `m_metrics`, exports via C Files |
| **Management Workflow Templates, Comments, Evidence Links, SLA, Unified Inbox** | **NEXUS M (`m_workflow`)** | **Authoritative Owner (Overlay)** | Linked to C `workflowInstanceId` |
| **Risk Register, Assessments, Controls, Incidents, Compliance Obligations** | **NEXUS M (`m_governance`)** | **Authoritative Owner** | Links evidence via C `fileId` |
| **Strategic Plans, OKRs, Initiatives, Portfolio Tracking** | **NEXUS M (`m_performance`)** | **Authoritative Owner** | Linked to `m_metrics` & `m_org` |
| **Connectors, Ingestion Runs, Inbox/Outbox, Reconciliation Logs** | **NEXUS M (`m_integration`)** | **Authoritative Owner** | RabbitMQ `nexus.events` + API connectors |
| Financial Ledger, Invoices, AR/AP, Journal Entries | **NEXUS F** | Read-Model Consumer Only | Versioned APIs / `finance.*.v1` events |
| Orders, Shipments, Fleet, Delivery Execution | **NEXUS E** | Read-Model Consumer Only | Versioned APIs / `express.*.v1` events |

## 2. Hard Invariants
1. **No Private DB Coupling:** NEXUS M MUST NEVER connect to NEXUS C, F, or E databases directly.
2. **No Shadow Sources of Truth:** M never mutates F financial transactions or E shipments; M never duplicates C user credentials or role assignments.
3. **Rebuildable Analytical Read Models:** Every management read model in `m_metrics` MUST retain `sourceApplication`, `sourceReferenceId`, and `sourceDataVersion` so it can be reconciled or rebuilt from authoritative source contracts.
4. **Module Persistence Isolation:** Within NEXUS M's modular monolith, each module owns its own PostgreSQL schema (`m_*`) and MUST NOT execute direct SQL writes against another module's tables.
