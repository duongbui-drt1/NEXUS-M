# NEXUS M — Conceptual Domain Model

## Organizational context
Proposed flexible hierarchy:

Tenant → Workspace → Organization → Legal Entity / Business Unit / Department → Team, Position and Management Assignment.

This is not a mandatory hierarchy. Tenant is not automatically a legal entity. Small organizations may use only a subset. Users may have multiple effective-dated assignments where policy permits.

## Core domains
- **Workspace/org:** workspace, organization profile, organizational unit, legal-entity reference, reporting relationship, effective-dated assignment.
- **Metrics:** metric definition/version/observation, target, dimension, source reference, lineage, freshness and quality assessment.
- **Executive intelligence:** dashboard, widget, alert, insight, comparison scope and saved view.
- **Workflow:** workflow definition/version, instance, stage, task, approval decision, delegation, escalation, evidence reference and immutable history event.
- **Governance:** risk, assessment, control, incident, mitigation action and evidence.
- **Reporting:** report definition/run/snapshot, review, approval and distribution record.
- **Performance/resources:** objective, key result, KPI target, initiative, portfolio and responsibility assignment.
- **Integration:** connector, subscription, ingestion run, event receipt, reconciliation record, retry and dead-letter item.

## Ownership
| Data | Authority |
|---|---|
| User, tenant, membership, authentication, authorization, entitlement | NEXUS C |
| M workflow, approval history, KPI definition, management report/read model | NEXUS M |
| Financial transaction and accounting ledger | NEXUS F |
| Logistics/transport execution | NEXUS E |
| External operational facts | Respective source system |

## Invariants
1. Never duplicate C identity, tenant, membership or role authority.
2. Never make M the authoritative ledger for F or execution system for E.
3. Cross-app references identify source system, stable ID and schema/version.
4. Metric observations trace to definition version and source.
5. Effective-dated assignments must not rewrite historical reporting context.
6. Workflow decisions preserve actor, timestamp, outcome, policy/context and correlation reference.
7. Tenant/org scope is explicit in queries and background processing.
8. Never silently mix currencies, periods, timezones or accounting bases.

## Modeling gate
Do not translate this conceptual model directly into EF entities or migrations. Validate contracts, lifecycle, cardinality, temporal behavior, authorization and retention first; record decisions as ADRs.
