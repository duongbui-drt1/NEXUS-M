# ADR-002: Workflow Engine Ownership Boundary (`M03` vs NEXUS C Shared Workflow)

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Technical Lead  
**References:** NEXUS C `ECOSYSTEM-INTEGRATION.md` Step 6, `PLATFORM-CONTRACT.md` Section 6, NEXUS M `02-PRD-1.0.md`

## Context
NEXUS C 1.0 provides a production-hardened Shared Workflow Engine (`/api/v1/platform/workflows/*`) with immutable versioned definitions, `FOR UPDATE` row-locked task decisions, Maker–Checker enforcement, sequential step validation, time-bounded delegations, escalations, and `platform.workflow.*.v1` events. `ECOSYSTEM-INTEGRATION.md` mandates: *"Never re-implement approval state machines inside F/E/M."*

Meanwhile, NEXUS M's M03 (Workflow & Approval) requires rich management capabilities: business templates (expense, procurement, contract, internal proposal), comments, evidence attachments, SLA monitoring dashboards, conditional routing metadata, and a unified executive approval inbox across applications.

## Decision
1. **NEXUS C Owns Core Approval State Machine Execution:**
   - M03 delegates core workflow definition versioning, instance lifecycle (`Submitted → Approved / Rejected / Cancelled`), task locking (`FOR UPDATE`), Maker–Checker, delegation eligibility, and immutable decision transitions to NEXUS C (`/api/v1/platform/workflows/*`).
2. **NEXUS M (`M03`) Owns the Management Orchestration & Inbox Layer:**
   - M03 stores management-specific domain records linked by `cWorkflowDefinitionId` / `cWorkflowInstanceId`:
     - Management workflow templates & category metadata (e.g., `CAPEX_PROPOSAL`, `BOARD_REPORT_APPROVAL`, `RISK_EXCEPTION`).
     - Discussion comments and evidence links (referencing NEXUS C File Storage `fileId`).
     - SLA policy definitions, breach alerts, and cycle-time analytics.
     - Unified Approval Inbox read model projected from `platform.workflow.*.v1` events (covering M, F, and E approval requests).
3. **Orchestration Only — Never Source Transaction Owner:**
   - M03 never mutates NEXUS F ledgers or NEXUS E shipments directly; approving a workflow emits/propagates the decision via NEXUS C workflow events so the owning source application executes the business transaction.

## Consequences
- **Positive:** Eliminates split-brain state machines and guarantees consistent Maker–Checker and audit guarantees across the entire NEXUS suite.
- **Trade-off:** M03 depends on NEXUS C workflow API availability for submitting/deciding tasks; inbox reads remain resilient via M's event-projected read model.
