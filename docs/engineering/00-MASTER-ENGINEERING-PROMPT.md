# NEXUS M 1.0 — Master Engineering Prompt

## Role and working method
Act as principal architect and senior engineering team. Inspect before modifying. Verify claims against repository evidence. Distinguish verified facts, assumptions, proposals and unresolved questions. Work incrementally and document decisions.

## Mission
Build **NEXUS M**, a Universal Enterprise Management & Decision Intelligence Platform for micro-businesses, SMEs, mid-market organizations, enterprises and enterprise groups. Universal scope does not mean every feature is enabled for every customer; use progressive module activation, entitlements and configurable organization structures.

## Ecosystem ownership — non-negotiable
- **NEXUS C:** identity, users, tenants, memberships, authentication, authorization, platform/service identities and platform subscription/entitlements.
- **NEXUS M:** management domain logic, workflows/approvals, KPI definitions, management read models, strategic objectives, risk/governance, management insights and reports.
- **NEXUS F:** authoritative financial transactions and accounting.
- **NEXUS E:** authoritative logistics and transport execution.
- Integrate through documented, versioned APIs/events. Never access another app's private database or duplicate its authoritative records.

## Product modules
- **M00 Management Foundation:** app shell, workspace/org context, navigation, preferences, localization, module registry, dashboards, search, notifications and activity timeline.
- **M01 Executive Intelligence:** CEO/C-level/board dashboards, business health, KPIs, trends, alerts, comparisons and permission-aware drill-down.
- **M02 Business Intelligence:** metric catalog, definitions, analytics, report builder, saved/scheduled reports, export, lineage and data quality.
- **M03 Workflow & Approval:** versioned workflows, routing, approvals, delegation, escalation, SLA, tasks, evidence, immutable decision history, idempotency, concurrency and separation of duties.
- **M04 Organization & People:** configurable units, departments, teams, positions, reporting lines and effective-dated assignments. Not silently full HR/payroll.
- **M05 Risk & Governance:** risk register, assessment/scoring, owners, controls, incidents, mitigation and evidence.
- **M06 Compliance Intelligence:** jurisdiction-aware obligations, regulatory catalogs, deadlines, evidence and alerts. Regulatory content requires validation.
- **M07 Board & Corporate Reporting:** board/periodic reports, consolidation, templates, approvals, versioning and secure distribution.
- **M08 Resource & Performance Management:** objectives, OKRs, KPI targets, resources, budget responsibility, initiatives and portfolio.
- **M09 AI Executive Intelligence:** optional assistant, natural-language analytics, summaries, anomaly explanations and recommendations. Require provider abstraction, entitlement, usage/cost controls, permission-aware retrieval, citations, freshness, tool allowlists and human approval. AI is not a core runtime dependency.
- **M10 Data & Integration Hub:** connectors, ingestion, event consumers, normalization, read models, metric catalog, lineage, freshness, quality, reconciliation, retries, DLQ, replay and pipeline monitoring.

## Architecture direction (proposal; verify first)
Start as a modular monolith, not microservices. Align with actual NEXUS C contracts and supported technology versions. Candidate stack: ASP.NET Core/.NET, EF Core, PostgreSQL, React + TypeScript + Vite, Redis, RabbitMQ, S3-compatible storage, OpenTelemetry and Docker. Verify versions and packages against current support policies, official documentation and repository lockfiles before adoption.

Use CQRS/read models for analytics only when justified. Use event-driven integration where useful; do not introduce continuous polling by default. Keep module boundaries explicit.

## Data and metric integrity
Every metric defines formula, unit, currency, time basis, aggregation, source, definition version, last update, freshness expectation, quality status and access classification. Never silently mix currencies, periods, timezones, legal entities, accounting bases or metric definitions. Preserve lineage.

## Security
Enforce authorization server-side at tenant, organization, resource and operation scope. Apply least privilege. Tenant-scope caches, jobs, events, search, exports and files. Mask sensitive data. Never log secrets. Separate immutable business audit from operational logs. Fail closed when authorization is uncertain.

## Engineering rules
1. Inspect repository, branches, history, files, dependencies, tests and CI before changes.
2. Inspect accessible NEXUS C contracts; record gaps rather than inventing them.
3. Do not rewrite working code or add dependencies without a justified plan.
4. Never weaken authentication/authorization or present mocks as production integrations.
5. Do not delete data, volumes, branches or files destructively.
6. Do not force-push, merge, release, deploy or run destructive DB operations without explicit approval.
7. Add tests and documentation with meaningful implementation.
8. Never claim production readiness without security, migration, backup/restore and operational evidence.

## Roadmap and gates
- **Phase 0 — Audit:** inspect repo and accessible C contracts; establish baseline; produce audit, risk register, dependency inventory, integration map and prioritized backlog. No feature coding.
- **Phase 1 — Freeze:** resolve ownership, contracts, tenancy/org model, security, metric semantics, NFRs and ADRs.
- **Phase 2 — Foundation:** app shell, context resolution, module registry, entitlement-aware navigation and audit.
- **Phase 3 — Data/integration/KPI foundation.**
- **Phase 4 — Executive intelligence.**
- **Phase 5 — Business intelligence.**
- **Phase 6 — Workflow/approval.**
- **Phase 7 — Organization/people.**
- **Phase 8 — Risk/compliance.**
- **Phase 9 — Corporate reporting/resources.**
- **Phase 10 — Optional AI.**
- **Phase 11 — Production hardening.**

## First task: Phase 0 only
Do not scaffold the application or implement product features. Inspect and report:
- repository state, default/current branch, history and files;
- language/framework/package versions, dependency health, build/test/CI status;
- architecture, conventions, security controls and deployment assumptions;
- accessible NEXUS C contracts and unresolved dependencies;
- tenant/org boundary, data ownership and integration risks;
- prioritized findings with severity, evidence, impact and remediation;
- Phase 1 decision backlog and proposed ADRs.

Stop after the Phase 0 report and wait for review.
