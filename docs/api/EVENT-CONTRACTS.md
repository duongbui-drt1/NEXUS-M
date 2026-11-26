# NEXUS M — Event Contracts

**Status:** Phase 1 Architecture Freeze  
**Transport:** RabbitMQ Topic Exchange `nexus.events` (DLX: `nexus.events.dlx`)  
**Envelope Standard:** NEXUS C `EventEnvelope` v1

## 1. Standard Envelope
```json
{
  "eventId": "019a2f7b-...",
  "eventType": "management.alert.triggered.v1",
  "tenantId": "019a2f7b-...",
  "occurredAt": "2026-11-27T01:00:00Z",
  "correlationId": "corr-123",
  "schemaVersion": 1,
  "producer": "nexus-m",
  "causationId": "cmd-456",
  "payload": { }
}
```

## 2. Events Consumed by NEXUS M (Idempotent Inbox)

| Event Type | Producer | NEXUS M Consumer Action |
|---|---|---|
| `platform.tenant.suspended.v1` / `activated.v1` | `nexus-c` | Invalidate tenant entitlement cache; pause/resume scheduled report jobs |
| `platform.subscription.activated.v1` / `cancelled.v1` / `expired.v1` | `nexus-c` | Invalidate tenant entitlement & module-registry cache |
| `platform.application.activated.v1` / `deactivated.v1` | `nexus-c` | Activate/deactivate `NEXUS_M` workspace access cache |
| `platform.workflow.submitted.v1` / `approved.v1` / `rejected.v1` / `cancelled.v1` / `delegated.v1` / `escalated.v1` | `nexus-c` | Project into M03 Unified Approval Inbox, update SLA status, push SSE notification |
| `finance.summary.published.v1` *(future F contract)* | `nexus-f` | Ingest financial KPI observations into `m_metrics` with lineage & reconciliation |
| `express.summary.published.v1` *(future E contract)* | `nexus-e` | Ingest logistics/delivery KPI observations into `m_metrics` with lineage & reconciliation |

## 3. Events Published by NEXUS M (Transactional Outbox)

| Event Type | Trigger | Consumers |
|---|---|---|
| `management.workspace.provisioned.v1` | Default or custom workspace created | Audit, Analytics |
| `management.metric.version_published.v1` | New immutable metric definition version published | BI, Dashboards |
| `management.alert.triggered.v1` / `resolved.v1` | KPI threshold or data-freshness SLA breach | Notifications (NEXUS C), SSE |
| `management.report.published.v1` | Periodic or board report snapshot finalized | Distribution, Audit, SSE |
| `management.risk.escalated.v1` | High/critical risk threshold or mitigation overdue | Notifications (NEXUS C), Workflow |
| `management.compliance.deadline_approaching.v1` | Compliance obligation enters warning window | Notifications (NEXUS C), SSE |

## 4. Delivery Guarantees
- At-least-once delivery via Transactional Outbox (`FOR UPDATE SKIP LOCKED`).
- Consumers deduplicate on `UNIQUE (event_id, consumer_name)` inside the same DB transaction as state updates.
- Out-of-order protection via `occurredAt` / source sequence comparison on projection tables.
