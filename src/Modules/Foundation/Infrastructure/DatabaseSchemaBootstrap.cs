using Npgsql;

namespace NexusM.Modules.Foundation.Infrastructure;

/// <summary>
/// Idempotent DDL bootstrap for Phase 2 + Phase 3 schemas (m_foundation, m_audit, m_metrics, m_integration)
/// with mandatory PostgreSQL FORCE ROW LEVEL SECURITY, immutable audit & metric-version triggers,
/// and NOBYPASSRLS app role grants (ADR-004, ADR-006, ADR-007).
/// </summary>
public static class DatabaseSchemaBootstrap
{
    public const string MigrationSql = """
        CREATE SCHEMA IF NOT EXISTS m_foundation;
        CREATE SCHEMA IF NOT EXISTS m_audit;
        CREATE SCHEMA IF NOT EXISTS m_metrics;
        CREATE SCHEMA IF NOT EXISTS m_integration;

        -- ============================================================================
        -- PHASE 2: m_foundation & m_audit
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_foundation.workspaces (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(40) NOT NULL,
            name varchar(120) NOT NULL,
            description varchar(500) NOT NULL DEFAULT '',
            default_locale varchar(20) NOT NULL DEFAULT 'vi-VN',
            default_timezone varchar(64) NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
            default_currency varchar(3) NOT NULL DEFAULT 'VND',
            org_mode integer NOT NULL DEFAULT 1,
            is_default boolean NOT NULL DEFAULT false,
            is_active boolean NOT NULL DEFAULT true,
            version integer NOT NULL DEFAULT 1,
            created_at_utc timestamptz NOT NULL,
            updated_at_utc timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_workspaces_tenant_code
            ON m_foundation.workspaces (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_foundation.user_preferences (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            user_id uuid NOT NULL,
            locale varchar(20) NOT NULL DEFAULT 'vi-VN',
            timezone varchar(64) NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',
            theme varchar(16) NOT NULL DEFAULT 'light',
            default_workspace_id uuid NULL,
            updated_at_utc timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_user_prefs_tenant_user
            ON m_foundation.user_preferences (tenant_id, user_id);

        CREATE TABLE IF NOT EXISTS m_audit.business_audit_events (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            actor_user_id uuid NULL,
            actor_email varchar(256) NOT NULL,
            action varchar(120) NOT NULL,
            entity_type varchar(80) NOT NULL,
            entity_id varchar(128) NOT NULL,
            outcome varchar(32) NOT NULL DEFAULT 'Success',
            before_json jsonb NULL,
            after_json jsonb NULL,
            metadata_json jsonb NULL,
            correlation_id varchar(128) NOT NULL,
            occurred_at_utc timestamptz NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_m_audit_tenant_occurred
            ON m_audit.business_audit_events (tenant_id, occurred_at_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_m_audit_tenant_action
            ON m_audit.business_audit_events (tenant_id, action);

        CREATE OR REPLACE FUNCTION m_audit.prevent_audit_mutation()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            RAISE EXCEPTION 'AUDIT_IMMUTABLE: m_audit.business_audit_events is append-only and cannot be updated or deleted.';
        END;
        $$;

        DROP TRIGGER IF EXISTS trg_m_audit_immutable ON m_audit.business_audit_events;
        CREATE TRIGGER trg_m_audit_immutable
            BEFORE UPDATE OR DELETE ON m_audit.business_audit_events
            FOR EACH ROW
            EXECUTE FUNCTION m_audit.prevent_audit_mutation();

        -- ============================================================================
        -- PHASE 3: m_metrics (KPI Catalog & Management Read Models — ADR-006)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_metrics.metric_definitions (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            metric_code varchar(80) NOT NULL,
            display_name_key varchar(160) NOT NULL,
            description_key varchar(500) NOT NULL DEFAULT '',
            current_version integer NOT NULL DEFAULT 1,
            is_active boolean NOT NULL DEFAULT true,
            created_at_utc timestamptz NOT NULL,
            updated_at_utc timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_metric_defs_tenant_code
            ON m_metrics.metric_definitions (tenant_id, metric_code);

        CREATE TABLE IF NOT EXISTS m_metrics.metric_definition_versions (
            id uuid PRIMARY KEY,
            metric_definition_id uuid NOT NULL REFERENCES m_metrics.metric_definitions(id) ON DELETE RESTRICT,
            tenant_id uuid NOT NULL,
            version integer NOT NULL,
            formula_expression varchar(1000) NOT NULL,
            unit integer NOT NULL,
            currency_code varchar(3) NULL,
            time_basis integer NOT NULL,
            aggregation_method integer NOT NULL,
            accounting_basis integer NOT NULL,
            source_application varchar(64) NOT NULL,
            freshness_sla_minutes integer NOT NULL,
            access_classification integer NOT NULL,
            published_at_utc timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_metric_def_ver
            ON m_metrics.metric_definition_versions (tenant_id, metric_definition_id, version);

        -- Published metric definition versions are immutable (ADR-006)
        CREATE OR REPLACE FUNCTION m_metrics.prevent_version_mutation()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            RAISE EXCEPTION 'METRIC_VERSION_IMMUTABLE: Published metric_definition_versions cannot be updated or deleted.';
        END;
        $$;

        DROP TRIGGER IF EXISTS trg_m_metric_ver_immutable ON m_metrics.metric_definition_versions;
        CREATE TRIGGER trg_m_metric_ver_immutable
            BEFORE UPDATE OR DELETE ON m_metrics.metric_definition_versions
            FOR EACH ROW
            EXECUTE FUNCTION m_metrics.prevent_version_mutation();

        CREATE TABLE IF NOT EXISTS m_metrics.metric_observations (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            metric_definition_id uuid NOT NULL REFERENCES m_metrics.metric_definitions(id) ON DELETE RESTRICT,
            metric_definition_version_id uuid NOT NULL REFERENCES m_metrics.metric_definition_versions(id) ON DELETE RESTRICT,
            legal_entity_id uuid NULL,
            branch_id uuid NULL,
            org_unit_id uuid NULL,
            period_start_utc timestamptz NOT NULL,
            period_end_utc timestamptz NOT NULL,
            reporting_timezone varchar(64) NOT NULL DEFAULT 'UTC',
            currency_code varchar(3) NULL,
            accounting_basis integer NOT NULL DEFAULT 0,
            value_numeric numeric(24, 6) NOT NULL,
            source_application varchar(64) NOT NULL,
            source_reference_id varchar(128) NOT NULL,
            source_data_version bigint NOT NULL DEFAULT 1,
            observed_at_utc timestamptz NOT NULL,
            ingested_at_utc timestamptz NOT NULL,
            quality_status integer NOT NULL DEFAULT 1
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_metric_obs_idempotency
            ON m_metrics.metric_observations (tenant_id, source_application, source_reference_id, metric_definition_version_id, source_data_version);
        CREATE INDEX IF NOT EXISTS ix_m_metric_obs_lookup
            ON m_metrics.metric_observations (tenant_id, metric_definition_id, period_start_utc);

        -- ============================================================================
        -- PHASE 3: m_integration (M10 Data & Integration Hub — ADR-005)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_integration.connectors (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            name varchar(160) NOT NULL,
            source_application varchar(64) NOT NULL,
            schema_version integer NOT NULL DEFAULT 1,
            freshness_sla_minutes integer NOT NULL DEFAULT 60,
            secret_reference varchar(256) NULL,
            is_enabled boolean NOT NULL DEFAULT true,
            last_succeeded_at_utc timestamptz NULL,
            created_at_utc timestamptz NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_connectors_tenant_code
            ON m_integration.connectors (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_integration.ingestion_runs (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            connector_id uuid NULL,
            source_application varchar(64) NOT NULL,
            status varchar(32) NOT NULL,
            records_received integer NOT NULL DEFAULT 0,
            records_accepted integer NOT NULL DEFAULT 0,
            records_deduplicated integer NOT NULL DEFAULT 0,
            error_summary varchar(500) NULL,
            correlation_id varchar(128) NOT NULL,
            started_at_utc timestamptz NOT NULL,
            completed_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS m_integration.inbox_events (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            event_id uuid NOT NULL,
            consumer_name varchar(80) NOT NULL,
            event_type varchar(120) NOT NULL,
            schema_version integer NOT NULL DEFAULT 1,
            producer varchar(64) NOT NULL,
            correlation_id varchar(128) NOT NULL,
            occurred_at_utc timestamptz NOT NULL,
            processed_at_utc timestamptz NOT NULL,
            payload_json jsonb NOT NULL
        );

        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_inbox_event_consumer
            ON m_integration.inbox_events (tenant_id, event_id, consumer_name);

        CREATE TABLE IF NOT EXISTS m_integration.outbox_events (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            event_type varchar(120) NOT NULL,
            schema_version integer NOT NULL DEFAULT 1,
            payload_json jsonb NOT NULL,
            correlation_id varchar(128) NOT NULL,
            status varchar(32) NOT NULL DEFAULT 'Pending',
            attempt_count integer NOT NULL DEFAULT 0,
            created_at_utc timestamptz NOT NULL,
            published_at_utc timestamptz NULL
        );

        CREATE TABLE IF NOT EXISTS m_integration.dead_letters (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            original_event_id uuid NULL,
            event_type varchar(120) NOT NULL,
            producer varchar(64) NOT NULL,
            payload_json jsonb NOT NULL,
            failure_reason varchar(500) NOT NULL,
            manual_retry_count integer NOT NULL DEFAULT 0,
            status varchar(32) NOT NULL DEFAULT 'DeadLettered',
            failed_at_utc timestamptz NOT NULL,
            last_retried_at_utc timestamptz NULL
        );

        CREATE TABLE IF NOT EXISTS m_integration.reconciliations (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            metric_code varchar(80) NOT NULL,
            source_application varchar(64) NOT NULL,
            period_start_utc timestamptz NOT NULL,
            period_end_utc timestamptz NOT NULL,
            expected_count integer NOT NULL,
            actual_count integer NOT NULL,
            expected_sum numeric(24, 6) NOT NULL,
            actual_sum numeric(24, 6) NOT NULL,
            status varchar(32) NOT NULL,
            checked_at_utc timestamptz NOT NULL
        );

        -- ============================================================================
        -- Mandatory FORCE ROW LEVEL SECURITY across ALL 12 tenant tables (ADR-004)
        -- ============================================================================
        DO $$
        DECLARE
            rec record;
        BEGIN
            FOR rec IN
                SELECT schemaname, tablename
                FROM pg_tables
                WHERE schemaname IN ('m_foundation', 'm_audit', 'm_metrics', 'm_integration')
            LOOP
                EXECUTE format('ALTER TABLE %I.%I ENABLE ROW LEVEL SECURITY;', rec.schemaname, rec.tablename);
                EXECUTE format('ALTER TABLE %I.%I FORCE ROW LEVEL SECURITY;', rec.schemaname, rec.tablename);
                EXECUTE format('DROP POLICY IF EXISTS p_tenant_isolation ON %I.%I;', rec.schemaname, rec.tablename);
                EXECUTE format(
                    'CREATE POLICY p_tenant_isolation ON %I.%I USING (tenant_id = NULLIF(current_setting(''app.current_tenant_id'', true), '''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''app.current_tenant_id'', true), '''')::uuid);',
                    rec.schemaname, rec.tablename);
            END LOOP;
        END
        $$;
        """;

    public static async Task ApplyAsync(string adminConnectionString, string? appRoleName = "nexus_m_app", CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = new NpgsqlCommand(MigrationSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(appRoleName))
        {
            var grantSql = $"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{appRoleName}') THEN
                        EXECUTE 'GRANT USAGE ON SCHEMA m_foundation, m_audit, m_metrics, m_integration TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA m_foundation TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA m_integration TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE m_metrics.metric_definitions, m_metrics.metric_observations TO "{appRoleName}"';
                        EXECUTE 'REVOKE ALL ON TABLE m_metrics.metric_definition_versions FROM "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT ON TABLE m_metrics.metric_definition_versions TO "{appRoleName}"';
                        EXECUTE 'REVOKE ALL ON TABLE m_audit.business_audit_events FROM "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT ON TABLE m_audit.business_audit_events TO "{appRoleName}"';
                    END IF;
                END
                $$;
                """;

            await using var grantCmd = new NpgsqlCommand(grantSql, conn);
            await grantCmd.ExecuteNonQueryAsync(ct);
        }
    }
}
