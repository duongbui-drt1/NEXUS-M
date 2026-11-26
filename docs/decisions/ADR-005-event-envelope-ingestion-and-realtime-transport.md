# ADR-005: Event Envelope, Idempotent Ingestion (`M10`) & Realtime Transport

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Technical Lead  
**References:** NEXUS C `PLATFORM-CONTRACT.md` Section 7, `ECOSYSTEM-INTEGRATION.md` Step 7

## Context
NEXUS C publishes platform domain events onto RabbitMQ topic exchange `nexus.events` (DLX: `nexus.events.dlx`) using the frozen `EventEnvelope` schema (`eventId`, `eventType`, `tenantId`, `occurredAt`, `correlationId`, `schemaVersion`, `producer`, `causationId`, `payload`). NEXUS F and NEXUS E will publish operational summary events on the same envelope standard.

On the frontend, NEXUS M Console needs live updates for KPI alerts, workflow inbox changes, and report-run completions without continuous polling.

## Decision
1. **Adopt Frozen `EventEnvelope` v1:**
   - All events consumed or emitted by NEXUS M use the exact NEXUS C `EventEnvelope` contract.
   - Routing keys follow `<producer>.<domain>.<action>.v<schemaVersion>` (e.g., `management.alert.triggered.v1`, `management.report.published.v1`).
2. **Mandatory Transactional Outbox & Inbox (`m_integration`):**
   - **Outbox:** M domain state mutations and outgoing `EventEnvelope` records are committed in the same PostgreSQL transaction and relayed via `FOR UPDATE SKIP LOCKED` with exponential backoff and DLQ routing.
   - **Inbox:** Every consumed event is deduplicated inside a local transaction on `UNIQUE (event_id, consumer_name)`. Consumers MUST handle duplicate, delayed, and out-of-order deliveries gracefully (checking `occurredAt` / source version where monotonic state applies).
   - **Replay & Reconciliation:** Dead-lettered messages are inspectable and manually retryable (capped, audited); read models support controlled replay from source APIs/events.
3. **Realtime Transport — Server-Sent Events (SSE) for MVP, SignalR Optional Later:**
   - **MVP (Phase 2–6):** Use **SSE** (`/api/v1/m/realtime/stream`) matching NEXUS C's `/api/v1/platform/realtime/stream` pattern: unidirectional server-to-client push for KPI alert updates, approval inbox updates, and report completion events.
   - **Security:** Every realtime subscription authenticates the NEXUS C JWT, binds to `tenantId` + verified `organization-scope`, and re-checks authorization before pushing any tenant/org-scoped payload.
   - SignalR may be introduced only if genuine bidirectional collaboration (e.g., live multi-user co-editing) is required later.
4. **Local Development Fallback (No Docker Dependency Required for Core Dev):**
   - Messaging and cache adapters are abstracted behind interfaces (`IEventBus`, `IDistributedCache`). Production uses RabbitMQ + Redis; local dev without Docker supports PostgreSQL-backed outbox/inbox + in-memory cache while preserving identical contract semantics.

## Consequences
- **Positive:** 100% wire compatibility with NEXUS C/F/E; simpler proxy/firewall compatibility with SSE; zero polling by default.
- **Trade-off:** SSE is unidirectional (commands remain standard REST `POST`/`PUT`/`PATCH` with `Idempotency-Key`).
