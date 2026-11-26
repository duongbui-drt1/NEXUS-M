# ADR-006: Metric Semantics, Currency Normalization, Lineage & Freshness Contract

**Status:** Proposed (Phase 1 Freeze)  
**Date:** 2026-11-27  
**Deciders:** Principal Architect / Data Architect  
**References:** Master Engineering Prompt Section 9, NEXUS C `/api/v1/platform/fx`

## Context
Management intelligence fails when dashboards silently mix currencies, accounting bases, timezones, legal entities, or unversioned metric formulas, or when stale data is presented as realtime.

## Decision
1. **Mandatory Metric Definition Metadata (`m_metrics.metric_definitions` + `metric_definition_versions`):**
   Every metric MUST declare:
   - `metricCode` (stable business identifier, never a localized label)
   - `version` (immutable integer version; formula edits create a new version)
   - `displayNameKey` & `descriptionKey` (i18n keys)
   - `formulaExpression` (structured/documented calculation specification)
   - `unit` (e.g., `CURRENCY`, `PERCENT`, `COUNT`, `HOURS`, `DAYS`)
   - `currencyCode` (ISO 4217 when `unit = CURRENCY`, else `NULL`)
   - `timeBasis` (`POINT_IN_TIME`, `PERIOD_SUM`, `PERIOD_AVG`, `ROLLING_WINDOW`)
   - `aggregationMethod` (`SUM`, `AVG`, `MIN`, `MAX`, `WEIGHTED_AVG`, `NON_ADDITIVE`)
   - `accountingBasis` (`ACCRUAL`, `CASH`, `OPERATIONAL`, `NOT_APPLICABLE`)
   - `sourceApplication` (`NEXUS_F`, `NEXUS_E`, `NEXUS_M`, `EXTERNAL:<connector>`)
   - `freshnessSlaMinutes` (threshold after which data is flagged `STALE`)
   - `accessClassification` (`PUBLIC_INTERNAL`, `MANAGEMENT`, `EXECUTIVE_RESTRICTED`, `BOARD_CONFIDENTIAL`)
2. **Observation & Lineage Contract (`m_metrics.metric_observations`):**
   Every metric observation MUST record:
   - `metricDefinitionVersionId`, `tenantId`, `legalEntityId?`, `orgUnitId?`, `periodStartUtc`, `periodEndUtc`, `reportingTimezone`, `currencyCode?`, `valueNumeric`, `sourceApplication`, `sourceReferenceId`, `sourceDataVersion`, `observedAtUtc`, `ingestedAtUtc`, `qualityStatus` (`VERIFIED`, `PARTIAL`, `ESTIMATED`, `STALE`, `FAILED_RECONCILIATION`).
3. **Strict Anti-Mixing Guardrails (Enforced Server-Side):**
   - Queries/comparisons MUST reject (`422 METRIC_COMPARISON_INVALID`) or require explicit normalization when combining:
     - Different `currencyCode` values (must convert using versioned NEXUS C FX rates `/api/v1/platform/fx` with rate timestamp cited).
     - Different `accountingBasis` values (`CASH` vs `ACCRUAL`).
     - Different `metricDefinitionVersionId` without an explicit compatibility flag.
     - Non-additive metrics (`PERCENT`, ratios) aggregated via raw `SUM`.
     - Mismatched reporting periods or timezones.
4. **Freshness & Data Quality Transparency:**
   - Every dashboard widget and report API response MUST include `asOfUtc`, `freshnessStatus` (`FRESH`, `DELAYED`, `STALE`), `qualityStatus`, `sourceApplication`, and `definitionVersion`.

## Consequences
- **Positive:** Auditable, reproducible executive numbers; eliminates silent financial/operational misreporting.
- **Trade-off:** Higher schema discipline on ingestion connectors and query builders.
