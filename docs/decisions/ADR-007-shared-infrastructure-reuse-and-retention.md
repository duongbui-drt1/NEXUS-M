# ADR-007: Reuse of NEXUS C Shared Services & Retention Policy

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Security Engineer  
**References:** NEXUS C `PLATFORM-CONTRACT.md` Section 8, Master Prompt Sections 5 & 8

## Context
NEXUS C 1.0 already implements production-grade shared platform capabilities:
- File Storage (`/api/v1/platform/files` — allowlisted MIME types, SHA-256 hash, `retentionDays`, HMAC-signed expiring download URLs, audited downloads).
- Notifications (`/api/v1/platform/notifications` — In-App, Email via Resend, SSRF-guarded Webhooks, templates, user preferences, retry).
- Immutable Audit (`/api/v1/platform/audit` — append-only trigger + `FORCE RLS`).
- Configuration, Feature Flags, Reference Data & FX (`/api/v1/platform/configuration`, `/reference-data`, `/fx`).
- AI Gateway (`/api/v1/platform/integrations/ai/invoke` — entitlement-gated).

## Decision
1. **File & Evidence Storage (M03, M05, M06, M07):**
   - NEXUS M NEVER stores raw S3 credentials or binary blobs directly in M's database.
   - M uploads/retrieves attachments through NEXUS C `/api/v1/platform/files` and persists only `cFileId`, business link (`entityType`, `entityId`), classification, and version metadata in M.
   - Every attachment download verifies BOTH M's resource/org authorization AND C's tenant-scoped file access.
2. **Notifications (M00, M03, M05, M06):**
   - M dispatches In-App, Email, and Webhook notifications via NEXUS C `/api/v1/platform/notifications/send` using deterministic `Idempotency-Key` values to prevent duplicate alerts.
3. **Reference Data & FX Rates (M01, M02, M07):**
   - ISO 4217 currencies and FX rates are consumed from NEXUS C `/api/v1/platform/fx` and `/reference-data` (cached with TTL + rate-date citation).
4. **Business Audit (`m_audit` + NEXUS C Audit):**
   - Platform-level actions (login, role change, file download, workflow decision) are audited automatically by NEXUS C.
   - M-specific management actions (metric definition versioning, report publication/export, risk assessment, compliance evidence linking, workspace/config changes) are written to M's append-only `m_audit.business_audit_events` table (PostgreSQL `BEFORE UPDATE OR DELETE` immutability trigger + `SELECT, INSERT`-only runtime grant + `FORCE RLS`) and optionally mirrored as audit events.
5. **AI Gateway (M09 — Phase 10, Optional):**
   - M09 is strictly optional and disabled unless tenant entitlement is active. When enabled, M09 performs server-side permission-aware retrieval first, attaches source citations/freshness, and invokes models through NEXUS C's AI Gateway (`/api/v1/platform/integrations/ai/invoke`) for centralized quota, cost, and provider governance.
6. **Retention Defaults:**
   - Operational logs: 30–90 days (no secrets/PII).
   - Business audit & workflow decision history: minimum 7 years (configurable per tenant/jurisdiction, immutable).
   - Board/corporate report snapshots: immutable for configured corporate retention window.

## Consequences
- **Positive:** Massive reduction in duplicated security surface (SSRF, MIME validation, email deliverability, AI key custody).
- **Trade-off:** Requires resilient HTTP client policies (timeouts, circuit breakers, correlation propagation) for NEXUS C calls.
