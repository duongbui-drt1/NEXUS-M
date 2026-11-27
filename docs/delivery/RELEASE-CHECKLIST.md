# NEXUS M 1.0 — Release Readiness Checklist

**Status:** Engineering Implementation & Verification Complete — Ready for Release Review  
**Date:** 2026-11-27

- [x] **Phase 0:** Repository & NEXUS C 1.0 contract audit completed and approved.
- [x] **Phase 1:** 7 ADRs (`ADR-001`..`ADR-007`), Data Ownership, Security, Integration, API/Event Contracts, and MVP Scope frozen.
- [x] **Phase 2 (`M00` + `m_audit`):** Modular monolith, NEXUS C auth/entitlements, `FORCE RLS`, immutable audit, React 19 Console, CI pipeline verified.
- [x] **Phase 3 (`M10` + KPI Catalog):** Versioned immutable metrics, anti-mixing guardrails, NEXUS C FX normalization, idempotent inbox, DLQ bounded replay, reconciliations verified.
- [x] **Phase 4 (`M01`):** Role-aware executive dashboards, Period-over-Period comparison, alerts, SSE realtime updates verified.
- [x] **Phase 5 (`M02`):** Saved BI reports, SHA-256 immutable snapshots, permission-gated & audited CSV/JSON exports verified.
- [x] **Phase 6 (`M03`):** Workflow & Approval Hub backed by NEXUS C Shared Workflow Engine, Maker-Checker separation of duties, `Idempotency-Key` replay/conflict handling verified.
- [x] **Phase 7 (`M04`):** Organization unit overlays, positions, effective-dated management assignments preserving historical point-in-time queries verified.
- [x] **Phase 8 (`M05` + `M06`):** Configurable risk matrix & immutable assessment history, controls, incidents, multi-jurisdiction compliance obligations & evidence verified.
- [x] **Phase 9 (`M07` + `M08` + Search):** Consolidated board report packs (Maker-Checker publication), OKR target-vs-actual evaluation, permission-filtered Enterprise Search verified.
- [x] **Phase 10 (`M09`):** Optional `NEXUS_M_AI`-entitled grounded assistant via NEXUS C AI Gateway, citations, usage/cost tracking, autonomous execution blocked (`403`) verified.
- [x] **Phase 11 (Hardening):** `37/37` PostgreSQL tables verified with `FORCE ROW LEVEL SECURITY`, idempotent upgrade verified, `pg_dump`/`psql` backup-restore drill verified, API latency benchmark (`p95 < 250ms` + 20x concurrent burst) verified, documentation complete.
- [ ] **Manual Release Gate (Owner Action):** Enable branch protection on `main`, review & merge PRs when authorized.
