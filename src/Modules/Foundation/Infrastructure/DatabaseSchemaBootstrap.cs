using Npgsql;

namespace NexusM.Modules.Foundation.Infrastructure;

/// <summary>
/// Idempotent DDL bootstrap for all NEXUS M 1.0 schemas (11 schemas, 34 tenant tables)
/// with mandatory PostgreSQL FORCE ROW LEVEL SECURITY, immutable history/snapshot/audit triggers,
/// and least-privilege NOBYPASSRLS runtime role grants (ADR-004, ADR-006, ADR-007).
/// </summary>
public static class DatabaseSchemaBootstrap
{
    public const string MigrationSql = """
        CREATE SCHEMA IF NOT EXISTS m_foundation;
        CREATE SCHEMA IF NOT EXISTS m_audit;
        CREATE SCHEMA IF NOT EXISTS m_metrics;
        CREATE SCHEMA IF NOT EXISTS m_integration;
        CREATE SCHEMA IF NOT EXISTS m_executive;
        CREATE SCHEMA IF NOT EXISTS m_bi;
        CREATE SCHEMA IF NOT EXISTS m_workflow;
        CREATE SCHEMA IF NOT EXISTS m_org;
        CREATE SCHEMA IF NOT EXISTS m_governance;
        CREATE SCHEMA IF NOT EXISTS m_performance;
        CREATE SCHEMA IF NOT EXISTS m_ai;

        -- Shared immutability trigger function
        CREATE OR REPLACE FUNCTION m_audit.prevent_immutable_record_mutation()
        RETURNS trigger
        LANGUAGE plpgsql
        AS $$
        BEGIN
            RAISE EXCEPTION 'IMMUTABLE_RECORD_VIOLATION: Table %.% is append-only and cannot be updated or deleted.', TG_TABLE_SCHEMA, TG_TABLE_NAME;
        END;
        $$;

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
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_workspaces_tenant_code ON m_foundation.workspaces (tenant_id, code);

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
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_user_prefs_tenant_user ON m_foundation.user_preferences (tenant_id, user_id);

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
        CREATE INDEX IF NOT EXISTS ix_m_audit_tenant_occurred ON m_audit.business_audit_events (tenant_id, occurred_at_utc DESC);
        CREATE INDEX IF NOT EXISTS ix_m_audit_tenant_action ON m_audit.business_audit_events (tenant_id, action);

        CREATE OR REPLACE FUNCTION m_audit.prevent_audit_mutation()
        RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'AUDIT_IMMUTABLE: m_audit.business_audit_events is append-only and cannot be updated or deleted.';
        END;
        $$;
        DROP TRIGGER IF EXISTS trg_m_audit_immutable ON m_audit.business_audit_events;
        CREATE TRIGGER trg_m_audit_immutable BEFORE UPDATE OR DELETE ON m_audit.business_audit_events
            FOR EACH ROW EXECUTE FUNCTION m_audit.prevent_audit_mutation();

        -- ============================================================================
        -- PHASE 3: m_metrics & m_integration
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
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_metric_defs_tenant_code ON m_metrics.metric_definitions (tenant_id, metric_code);

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
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_metric_def_ver ON m_metrics.metric_definition_versions (tenant_id, metric_definition_id, version);

        CREATE OR REPLACE FUNCTION m_metrics.prevent_version_mutation()
        RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'METRIC_VERSION_IMMUTABLE: Published metric_definition_versions cannot be updated or deleted.';
        END;
        $$;
        DROP TRIGGER IF EXISTS trg_m_metric_ver_immutable ON m_metrics.metric_definition_versions;
        CREATE TRIGGER trg_m_metric_ver_immutable BEFORE UPDATE OR DELETE ON m_metrics.metric_definition_versions
            FOR EACH ROW EXECUTE FUNCTION m_metrics.prevent_version_mutation();

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
        CREATE INDEX IF NOT EXISTS ix_m_metric_obs_lookup ON m_metrics.metric_observations (tenant_id, metric_definition_id, period_start_utc);

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
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_connectors_tenant_code ON m_integration.connectors (tenant_id, code);

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
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_inbox_event_consumer ON m_integration.inbox_events (tenant_id, event_id, consumer_name);

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
        -- PHASE 4: m_executive (M01 Executive Intelligence)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_executive.dashboards (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(160) NOT NULL,
            audience_role varchar(40) NOT NULL,
            is_default boolean NOT NULL DEFAULT false,
            version integer NOT NULL DEFAULT 1,
            created_at_utc timestamptz NOT NULL,
            updated_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_exec_dashboards_tenant_code ON m_executive.dashboards (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_executive.dashboard_widgets (
            id uuid PRIMARY KEY,
            dashboard_id uuid NOT NULL REFERENCES m_executive.dashboards(id) ON DELETE CASCADE,
            tenant_id uuid NOT NULL,
            title varchar(160) NOT NULL,
            metric_code varchar(80) NOT NULL,
            normalize_to_currency varchar(3) NULL,
            compare_previous_period boolean NOT NULL DEFAULT true,
            sort_order integer NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS m_executive.alert_rules (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            name varchar(160) NOT NULL,
            metric_code varchar(80) NOT NULL,
            condition_operator varchar(24) NOT NULL,
            threshold_value numeric(24, 6) NULL,
            normalize_to_currency varchar(3) NULL,
            severity varchar(24) NOT NULL,
            is_enabled boolean NOT NULL DEFAULT true,
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS m_executive.alerts (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            rule_id uuid NOT NULL,
            metric_code varchar(80) NOT NULL,
            severity varchar(24) NOT NULL,
            summary varchar(500) NOT NULL,
            observed_value numeric(24, 6) NULL,
            freshness_status varchar(24) NOT NULL,
            status varchar(24) NOT NULL,
            acknowledged_by_user_id uuid NULL,
            triggered_at_utc timestamptz NOT NULL,
            acknowledged_at_utc timestamptz NULL
        );

        -- ============================================================================
        -- PHASE 5: m_bi (M02 Business Intelligence)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_bi.saved_reports (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(160) NOT NULL,
            metric_codes_csv varchar(1000) NOT NULL,
            normalize_to_currency varchar(3) NULL,
            schedule_cron varchar(64) NULL,
            created_by_user_id uuid NOT NULL,
            created_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_bi_reports_tenant_code ON m_bi.saved_reports (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_bi.report_snapshots (
            id uuid PRIMARY KEY,
            report_id uuid NOT NULL REFERENCES m_bi.saved_reports(id) ON DELETE RESTRICT,
            tenant_id uuid NOT NULL,
            period_start_utc timestamptz NOT NULL,
            period_end_utc timestamptz NOT NULL,
            snapshot_json jsonb NOT NULL,
            sha256_checksum varchar(64) NOT NULL,
            metric_series_count integer NOT NULL,
            executed_by_user_id uuid NOT NULL,
            generated_at_utc timestamptz NOT NULL
        );
        DROP TRIGGER IF EXISTS trg_m_bi_snapshot_immutable ON m_bi.report_snapshots;
        CREATE TRIGGER trg_m_bi_snapshot_immutable BEFORE UPDATE OR DELETE ON m_bi.report_snapshots
            FOR EACH ROW EXECUTE FUNCTION m_audit.prevent_immutable_record_mutation();

        CREATE TABLE IF NOT EXISTS m_bi.report_exports (
            id uuid PRIMARY KEY,
            snapshot_id uuid NOT NULL REFERENCES m_bi.report_snapshots(id) ON DELETE RESTRICT,
            tenant_id uuid NOT NULL,
            format varchar(16) NOT NULL,
            row_count integer NOT NULL,
            exported_by_user_id uuid NOT NULL,
            correlation_id varchar(128) NOT NULL,
            exported_at_utc timestamptz NOT NULL
        );

        -- ============================================================================
        -- PHASE 6: m_workflow (M03 Workflow & Approval Hub)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_workflow.templates (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(160) NOT NULL,
            category varchar(60) NOT NULL,
            core_definition_code varchar(80) NOT NULL,
            default_sla_hours integer NOT NULL,
            version integer NOT NULL DEFAULT 1,
            is_active boolean NOT NULL DEFAULT true,
            created_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_wf_templates_tenant_code ON m_workflow.templates (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_workflow.approval_requests (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            template_id uuid NOT NULL REFERENCES m_workflow.templates(id) ON DELETE RESTRICT,
            c_workflow_instance_id uuid NOT NULL,
            c_current_task_id uuid NULL,
            title varchar(200) NOT NULL,
            category varchar(60) NOT NULL,
            summary varchar(1000) NOT NULL,
            requested_by_user_id uuid NOT NULL,
            delegated_to_user_id uuid NULL,
            status varchar(32) NOT NULL,
            sla_due_at_utc timestamptz NOT NULL,
            decided_by_user_id uuid NULL,
            decided_at_utc timestamptz NULL,
            version integer NOT NULL DEFAULT 1,
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS m_workflow.decision_logs (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL REFERENCES m_workflow.approval_requests(id) ON DELETE RESTRICT,
            actor_user_id uuid NOT NULL,
            outcome varchar(32) NOT NULL,
            comment varchar(1000) NULL,
            correlation_id varchar(128) NOT NULL,
            occurred_at_utc timestamptz NOT NULL
        );
        DROP TRIGGER IF EXISTS trg_m_wf_decision_immutable ON m_workflow.decision_logs;
        CREATE TRIGGER trg_m_wf_decision_immutable BEFORE UPDATE OR DELETE ON m_workflow.decision_logs
            FOR EACH ROW EXECUTE FUNCTION m_audit.prevent_immutable_record_mutation();

        CREATE TABLE IF NOT EXISTS m_workflow.comments (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL REFERENCES m_workflow.approval_requests(id) ON DELETE CASCADE,
            author_user_id uuid NOT NULL,
            body varchar(2000) NOT NULL,
            created_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS m_workflow.evidences (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            request_id uuid NOT NULL REFERENCES m_workflow.approval_requests(id) ON DELETE RESTRICT,
            c_file_id uuid NOT NULL,
            file_name varchar(256) NOT NULL,
            attached_by_user_id uuid NOT NULL,
            attached_at_utc timestamptz NOT NULL
        );

        -- ============================================================================
        -- PHASE 7: m_org (M04 Organization & People)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_org.unit_profiles (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            c_organization_unit_id uuid NOT NULL,
            c_legal_entity_id uuid NULL,
            c_branch_id uuid NULL,
            parent_profile_id uuid NULL,
            code varchar(60) NOT NULL,
            name varchar(160) NOT NULL,
            unit_type varchar(32) NOT NULL,
            headcount_target integer NOT NULL DEFAULT 0,
            created_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_org_profiles_tenant_code ON m_org.unit_profiles (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_org.positions (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            org_unit_profile_id uuid NOT NULL REFERENCES m_org.unit_profiles(id) ON DELETE RESTRICT,
            reports_to_position_id uuid NULL,
            code varchar(60) NOT NULL,
            title varchar(160) NOT NULL,
            is_executive_seat boolean NOT NULL DEFAULT false,
            created_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_org_positions_tenant_code ON m_org.positions (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_org.assignments (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            position_id uuid NOT NULL REFERENCES m_org.positions(id) ON DELETE RESTRICT,
            org_unit_profile_id uuid NOT NULL REFERENCES m_org.unit_profiles(id) ON DELETE RESTRICT,
            c_user_id uuid NOT NULL,
            user_display_email varchar(256) NOT NULL,
            valid_from_date date NOT NULL,
            valid_to_date date NULL,
            allocation_percent numeric(6, 2) NOT NULL DEFAULT 100.00,
            created_at_utc timestamptz NOT NULL
        );

        -- ============================================================================
        -- PHASE 8: m_governance (M05 Risk & M06 Compliance)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_governance.risks (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(200) NOT NULL,
            category varchar(60) NOT NULL,
            likelihood integer NOT NULL,
            impact integer NOT NULL,
            score integer NOT NULL,
            risk_level varchar(24) NOT NULL,
            owner_user_id uuid NOT NULL,
            mitigation_plan varchar(1000) NOT NULL,
            evidence_c_file_id uuid NULL,
            status varchar(32) NOT NULL,
            assessment_version integer NOT NULL DEFAULT 1,
            updated_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_gov_risks_tenant_code ON m_governance.risks (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_governance.risk_assessment_histories (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            risk_id uuid NOT NULL REFERENCES m_governance.risks(id) ON DELETE RESTRICT,
            version integer NOT NULL,
            likelihood integer NOT NULL,
            impact integer NOT NULL,
            score integer NOT NULL,
            risk_level varchar(24) NOT NULL,
            assessed_by_user_id uuid NOT NULL,
            assessed_at_utc timestamptz NOT NULL
        );
        DROP TRIGGER IF EXISTS trg_m_gov_risk_hist_immutable ON m_governance.risk_assessment_histories;
        CREATE TRIGGER trg_m_gov_risk_hist_immutable BEFORE UPDATE OR DELETE ON m_governance.risk_assessment_histories
            FOR EACH ROW EXECUTE FUNCTION m_audit.prevent_immutable_record_mutation();

        CREATE TABLE IF NOT EXISTS m_governance.controls (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(200) NOT NULL,
            linked_risk_id uuid NULL,
            effectiveness_status varchar(32) NOT NULL,
            assessed_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS m_governance.incidents (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            linked_risk_id uuid NULL,
            title varchar(200) NOT NULL,
            severity varchar(24) NOT NULL,
            status varchar(32) NOT NULL,
            reported_at_utc timestamptz NOT NULL
        );

        CREATE TABLE IF NOT EXISTS m_governance.compliance_obligations (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(200) NOT NULL,
            jurisdiction_code varchar(32) NOT NULL,
            regulatory_source varchar(200) NOT NULL,
            content_validation_status varchar(40) NOT NULL,
            effective_from_date date NOT NULL,
            due_at_utc timestamptz NOT NULL,
            owner_user_id uuid NOT NULL,
            evidence_c_file_id uuid NULL,
            status varchar(32) NOT NULL,
            version integer NOT NULL DEFAULT 1,
            created_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_gov_obligations_tenant_code ON m_governance.compliance_obligations (tenant_id, code);

        -- ============================================================================
        -- PHASE 9: m_performance (M07 Board Reporting & M08 OKRs/Initiatives)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_performance.board_reports (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(200) NOT NULL,
            period_start_utc timestamptz NOT NULL,
            period_end_utc timestamptz NOT NULL,
            consolidation_currency varchar(3) NOT NULL,
            metric_codes_csv varchar(1000) NOT NULL,
            snapshot_json jsonb NOT NULL,
            sha256_checksum varchar(64) NOT NULL,
            status varchar(24) NOT NULL,
            prepared_by_user_id uuid NOT NULL,
            approved_by_user_id uuid NULL,
            created_at_utc timestamptz NOT NULL,
            published_at_utc timestamptz NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_perf_board_reports_tenant_code ON m_performance.board_reports (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_performance.strategic_objectives (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            code varchar(60) NOT NULL,
            title varchar(200) NOT NULL,
            period_code varchar(32) NOT NULL,
            owner_user_id uuid NOT NULL,
            c_organization_unit_id uuid NULL,
            status varchar(32) NOT NULL,
            created_at_utc timestamptz NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_m_perf_objectives_tenant_code ON m_performance.strategic_objectives (tenant_id, code);

        CREATE TABLE IF NOT EXISTS m_performance.key_result_targets (
            id uuid PRIMARY KEY,
            objective_id uuid NOT NULL REFERENCES m_performance.strategic_objectives(id) ON DELETE CASCADE,
            tenant_id uuid NOT NULL,
            title varchar(200) NOT NULL,
            linked_metric_code varchar(80) NULL,
            target_value numeric(24, 6) NOT NULL,
            unit varchar(24) NOT NULL,
            currency_code varchar(3) NULL
        );

        CREATE TABLE IF NOT EXISTS m_performance.strategic_initiatives (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            objective_id uuid NOT NULL REFERENCES m_performance.strategic_objectives(id) ON DELETE RESTRICT,
            code varchar(60) NOT NULL,
            title varchar(200) NOT NULL,
            owner_user_id uuid NOT NULL,
            budget_responsibility_amount numeric(24, 6) NOT NULL,
            currency_code varchar(3) NOT NULL,
            status varchar(32) NOT NULL,
            created_at_utc timestamptz NOT NULL
        );

        -- ============================================================================
        -- PHASE 10: m_ai (M09 Optional AI Executive Intelligence)
        -- ============================================================================
        CREATE TABLE IF NOT EXISTS m_ai.usage_records (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            user_id uuid NOT NULL,
            provider varchar(64) NOT NULL,
            model_code varchar(80) NOT NULL,
            prompt_tokens integer NOT NULL,
            completion_tokens integer NOT NULL,
            estimated_cost_usd numeric(18, 6) NOT NULL,
            cited_metrics_json jsonb NOT NULL,
            correlation_id varchar(128) NOT NULL,
            invoked_at_utc timestamptz NOT NULL
        );

        -- ============================================================================
        -- Mandatory FORCE ROW LEVEL SECURITY across ALL 35 tenant tables (ADR-004)
        -- ============================================================================
        DO $$
        DECLARE
            rec record;
        BEGIN
            FOR rec IN
                SELECT schemaname, tablename
                FROM pg_tables
                WHERE schemaname IN (
                    'm_foundation', 'm_audit', 'm_metrics', 'm_integration',
                    'm_executive', 'm_bi', 'm_workflow', 'm_org',
                    'm_governance', 'm_performance', 'm_ai'
                )
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
                        EXECUTE 'GRANT USAGE ON SCHEMA m_foundation, m_audit, m_metrics, m_integration, m_executive, m_bi, m_workflow, m_org, m_governance, m_performance, m_ai TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA m_foundation, m_integration, m_executive, m_org, m_performance TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE m_metrics.metric_definitions, m_metrics.metric_observations TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE m_bi.saved_reports, m_bi.report_exports TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE m_workflow.templates, m_workflow.approval_requests, m_workflow.comments, m_workflow.evidences TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE m_governance.risks, m_governance.controls, m_governance.incidents, m_governance.compliance_obligations TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT ON TABLE m_ai.usage_records TO "{appRoleName}"';

                        -- Strictly append-only grants for immutable tables
                        EXECUTE 'REVOKE ALL ON TABLE m_audit.business_audit_events, m_metrics.metric_definition_versions, m_bi.report_snapshots, m_workflow.decision_logs, m_governance.risk_assessment_histories FROM "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT ON TABLE m_audit.business_audit_events, m_metrics.metric_definition_versions, m_bi.report_snapshots, m_workflow.decision_logs, m_governance.risk_assessment_histories TO "{appRoleName}"';
                    END IF;
                END
                $$;
                """;

            await using var grantCmd = new NpgsqlCommand(grantSql, conn);
            await grantCmd.ExecuteNonQueryAsync(ct);
        }
    }
}
