# NEXUS M — API Conventions & Contract Standards

**Status:** Phase 1 Architecture Freeze  
**Aligned With:** NEXUS C `PLATFORM-CONTRACT.md` v1.0.0  
**Base URL Prefix:** `/api/v1/m`

## 1. URI & Versioning Rules
- All NEXUS M HTTP endpoints are prefixed with `/api/v1/m/<bounded-context>/<resource>` (e.g., `/api/v1/m/workspaces`, `/api/v1/m/metrics/definitions`, `/api/v1/m/dashboards`, `/api/v1/m/reports`, `/api/v1/m/workflows/inbox`, `/api/v1/m/org/units`, `/api/v1/m/risks`).
- Additive optional fields are backward-compatible; breaking changes require `/api/v2/m/...` and an ADR.
- All endpoints are documented via OpenAPI 3.1 (`Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore` in non-production environments).

## 2. Standard Response Envelopes (1:1 Compatible with NEXUS C)
### Success (`ApiResponse<T>`)
```json
{
  "success": true,
  "data": { },
  "meta": {
    "correlationId": "019a2f7b-...",
    "asOfUtc": "2026-11-27T01:00:00Z"
  }
}
```
### Error (RFC 9457 `application/problem+json`)
```json
{
  "type": "https://errors.nexus.internal/METRIC_COMPARISON_INVALID",
  "title": "Unprocessable Entity",
  "status": 422,
  "detail": "Cannot compare VND and USD metrics without explicit FX normalization.",
  "errorCode": "METRIC_COMPARISON_INVALID",
  "correlationId": "019a2f7b-..."
}
```

## 3. Pagination, Filtering & Sorting (`PagedResult<T>`)
- Query parameters: `page` (1-based, default `1`), `pageSize` (default `20`, max `100`), optional `search`, `sortBy` (allowlisted columns only), `sortDescending`.
- Response payload: `{ items, page, pageSize, totalCount, totalPages, hasNext, hasPrevious }`.

## 4. Idempotency, Concurrency & Headers
- **Correlation:** `X-Correlation-Id` echoed in every response header, `meta.correlationId`, audit record, and outbox event.
- **Idempotency:** All state-changing `POST`/`PUT`/`PATCH` commands (workflow decisions, report runs, metric ingestion, alert acknowledgements) support `Idempotency-Key` (max 128 chars, 24h replay window; payload mismatch → `409 IDEMPOTENCY_PAYLOAD_MISMATCH`).
- **Optimistic Concurrency:** Mutable management aggregates expose `version` / `ETag` (`If-Match` mismatch → `412 PRECONDITION_FAILED` or `409 CONCURRENCY_CONFLICT`).
- **Rate Limiting:** Per-tenant + per-principal rate limits enforced on expensive analytics queries and report exports (`429 RATE_LIMIT_EXCEEDED`).

## 5. Planned Phase 2–6 Endpoint Surface (Summary)
| Module | Endpoint Group | Key Permissions |
|---|---|---|
| M00 Foundation | `GET /api/v1/m/context`, `GET/PUT /api/v1/m/workspaces/*`, `GET /api/v1/m/modules`, `GET /api/v1/m/realtime/stream` | `m.workspace.read`, `m.workspace.manage` |
| M10 Integration | `GET/POST /api/v1/m/integrations/connectors`, `POST /ingest`, `GET /runs`, `GET/POST /dead-letters/{id}/retry` | `m.integration.read`, `m.integration.manage` |
| M02 Metrics/BI | `GET/POST /api/v1/m/metrics/definitions`, `POST /definitions/{id}/versions`, `GET /observations`, `GET/POST /reports`, `POST /reports/{id}/export` | `m.kpi.read`, `m.kpi.manage`, `m.report.read`, `m.report.export` |
| M01 Executive | `GET/POST/PUT /api/v1/m/dashboards`, `GET /dashboards/{id}/data`, `GET/POST /alerts` | `m.dashboard.read`, `m.dashboard.manage` |
| M03 Workflow | `GET /api/v1/m/workflows/inbox`, `POST /workflows/requests`, `POST /workflows/tasks/{id}/decide`, `GET/POST /workflows/{id}/comments` | `m.workflow.read`, `m.workflow.create`, `m.workflow.approve` |
| M04 Org | `GET /api/v1/m/org/tree`, `GET/POST/PUT /api/v1/m/org/positions`, `GET/POST /api/v1/m/org/assignments` | `m.org.read`, `m.org.manage` |
