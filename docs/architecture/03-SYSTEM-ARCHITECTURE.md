# NEXUS M — System Architecture

**Status:** Proposed direction. Verify NEXUS C contracts, repository conventions and supported versions before implementation.

## Style
Start as a modular monolith with explicit bounded contexts and dependency rules. Extract services only when scale, ownership or isolation demonstrates a concrete need.

## Candidate stack
ASP.NET Core/.NET, EF Core, PostgreSQL, React + TypeScript + Vite. Redis, RabbitMQ, S3-compatible storage, SignalR, OpenTelemetry and Docker only where justified. Verify versions and packages against current support policies and repository lockfiles.

## Logical components
API/application host; C identity/integration adapter; Management Foundation; organization context/directory; KPI catalog; analytics/reporting read models; workflow/approval engine; risk/governance/compliance; integration ingestion/event consumers; notifications/background processing.

## Integration topology
C is authoritative for users, tenants, memberships, authentication, authorization and entitlements. M owns management workflows, KPI definitions, management read models and decision records. F and E remain authoritative for their domains. Use versioned APIs/events, explicit schemas, idempotency keys, correlation IDs, retries and reconciliation. No private database sharing.

## Data architecture
Use PostgreSQL for M-owned transactional state. Use read models for analytics when justified. Preserve source references and lineage. Store attachment bytes in controlled object storage and metadata/permissions in application domain. Do not create schema/migrations before domain and ownership gates are approved.

## Metric contract
Each metric defines formula, unit, currency, time basis, aggregation, source, definition version, last update, freshness expectation, quality status and access classification. Reject or visibly flag invalid comparisons.

## Security
Authenticate through approved C integration. Authorize server-side with tenant/org/resource/action scope. Enforce isolation in DB access, caches, jobs, events, search, exports and attachments. Fail closed. Separate immutable business audit from operational logs.

## Frontend
Use feature boundaries, typed API contracts, centralized query/cache policy, validated forms and explicit loading/error/empty/forbidden states. Never fail open if permission state is unavailable.

## Reliability
Instrument APIs, jobs, queues and integrations with structured logs, metrics and traces. Include health checks, bounded retries, DLQ/replay controls, migration strategy, backup/restore, secret management and rollback.

## ADRs required
C auth/session and authorization contracts; organization hierarchy; event transport/schema; metric semantics; workflow state model; attachment security; retention; deployment topology; tenant isolation.
