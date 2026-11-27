# NEXUS M — Deployment & Rollback Guide

**Status:** Verified (NEXUS M 1.0)  
**Last Updated:** 2026-11-27

## 1. Runtime Prerequisites
- **.NET Runtime:** ASP.NET Core 10.0 (`net10.0`)
- **Database:** PostgreSQL 17+ (`nexus_m` database, strictly isolated from NEXUS C/F/E databases)
- **Database Roles:**
  - `nexus_m_migrator` (DDL owner for migrations)
  - `nexus_m_app` (`LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS` — mandatory runtime role)
- **Platform Core:** Reachable NEXUS C 1.0 API (`/api/v1`) with registered `nexus-m-service` OAuth2 client (`applicationCode = NEXUS_M`) and synced `m.*` permission manifest (`POST /api/v1/platform/applications/NEXUS_M/permissions/sync`).

## 2. Environment Configuration (Zero Secrets in Git)
Provide secrets strictly via environment variables or secret manager:
- `ConnectionStrings__Default` (uses `nexus_m_app` role)
- `ConnectionStrings__Migrator` (used only during schema migration step)
- `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` (matching NEXUS C token validation)
- `NexusCore__BaseUrl`, `NexusCore__ClientId`, `NexusCore__ClientSecret`

## 3. Deployment Sequence
1. Run `DatabaseSchemaBootstrap.ApplyAsync` (additive, idempotent DDL across all 11 schemas + 37 `FORCE RLS` policies + grants).
2. Sync `NEXUS_M` permission catalog to NEXUS C (`POST /api/v1/platform/applications/NEXUS_M/permissions/sync`).
3. Deploy `NexusM.Api` (`Database__AutoBootstrap=false` in production when running dedicated migration step) and verify `/health/live` + `/health/ready` (`200 OK`).
4. Deploy `nexus-m-console` static bundle (`npm run build` → `dist/`).

## 4. Rollback Procedure
1. All schema migrations in NEXUS M 1.0 are strictly **additive and backward-compatible** (no destructive column/table drops).
2. If an application regression occurs, roll `NexusM.Api` and `nexus-m-console` container/artifact tags back to the prior verified digest immediately.
3. If data corruption occurs, pause inbound `M10` connectors, restore from the latest verified PostgreSQL backup + WAL point-in-time recovery into a staging DB first, reconcile counts/checksums, and promote.
