using Npgsql;

namespace NexusM.Modules.Foundation.Infrastructure;

/// <summary>
/// Idempotent DDL bootstrap for Phase 2 schemas (m_foundation, m_audit) with mandatory
/// PostgreSQL FORCE ROW LEVEL SECURITY, immutable audit triggers, and NOBYPASSRLS app role grants (ADR-004).
/// </summary>
public static class DatabaseSchemaBootstrap
{
    public const string Phase2MigrationSql = """
        CREATE SCHEMA IF NOT EXISTS m_foundation;
        CREATE SCHEMA IF NOT EXISTS m_audit;

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

        -- Immutable business audit trigger (ADR-007)
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

        -- Mandatory FORCE ROW LEVEL SECURITY on all tenant tables (ADR-004)
        ALTER TABLE m_foundation.workspaces ENABLE ROW LEVEL SECURITY;
        ALTER TABLE m_foundation.workspaces FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS p_tenant_isolation ON m_foundation.workspaces;
        CREATE POLICY p_tenant_isolation ON m_foundation.workspaces
            USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

        ALTER TABLE m_foundation.user_preferences ENABLE ROW LEVEL SECURITY;
        ALTER TABLE m_foundation.user_preferences FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS p_tenant_isolation ON m_foundation.user_preferences;
        CREATE POLICY p_tenant_isolation ON m_foundation.user_preferences
            USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

        ALTER TABLE m_audit.business_audit_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE m_audit.business_audit_events FORCE ROW LEVEL SECURITY;
        DROP POLICY IF EXISTS p_tenant_isolation ON m_audit.business_audit_events;
        CREATE POLICY p_tenant_isolation ON m_audit.business_audit_events
            USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
        """;

    public static async Task ApplyAsync(string adminConnectionString, string? appRoleName = "nexus_m_app", CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = new NpgsqlCommand(Phase2MigrationSql, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(appRoleName))
        {
            var grantSql = $"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{appRoleName}') THEN
                        EXECUTE 'GRANT USAGE ON SCHEMA m_foundation, m_audit TO "{appRoleName}"';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA m_foundation TO "{appRoleName}"';
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
