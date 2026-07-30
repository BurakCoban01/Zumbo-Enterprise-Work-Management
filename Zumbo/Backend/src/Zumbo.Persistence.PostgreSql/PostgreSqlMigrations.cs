using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed record PostgreSqlMigrationInfo(long Version, string Name, string Checksum);

public sealed record PostgreSqlMigrationStatus(
    IReadOnlyList<PostgreSqlMigrationInfo> Applied,
    IReadOnlyList<PostgreSqlMigrationInfo> Pending);

public interface IPostgreSqlMigrationRunner
{
    Task<PostgreSqlMigrationStatus> StatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PostgreSqlMigrationInfo>> ApplyAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PostgreSqlMigrationInfo>> RollbackAsync(
        long targetVersion,
        CancellationToken cancellationToken = default);
    Task<string> GenerateScriptAsync(
        long? fromVersion = null,
        long? toVersion = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default);
}

public sealed class PostgreSqlMigrationRunner(
    NpgsqlDataSource dataSource,
    PostgreSqlPersistenceOptions options,
    ILogger<PostgreSqlMigrationRunner>? logger = null) : IPostgreSqlMigrationRunner
{
    private const string Ledger = "public.zumbo_schema_migrations";
    private const string LockName = "zumbo-postgresql-schema-migrations-v1";

    public async Task<PostgreSqlMigrationStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var migrations = BuildMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var applied = await ReadAppliedAsync(connection, transaction: null, ledgerMayBeMissing: true, cancellationToken);
        ValidateLedger(migrations, applied);
        return new PostgreSqlMigrationStatus(
            applied.Select(row => new PostgreSqlMigrationInfo(row.Version, row.Name, row.Checksum)).ToList(),
            migrations.Where(migration => !applied.Any(row => row.Version == migration.Version))
                .Select(migration => migration.Info)
                .ToList());
    }

    public async Task<IReadOnlyList<PostgreSqlMigrationInfo>> ApplyAsync(
        CancellationToken cancellationToken = default)
    {
        var appliedNow = new List<PostgreSqlMigrationInfo>();
        var migrations = BuildMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureLedgerAsync(connection, cancellationToken);

        foreach (var migration in migrations)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireLockAsync(connection, transaction, cancellationToken);
                var applied = await ReadAppliedAsync(connection, transaction, ledgerMayBeMissing: false, cancellationToken);
                ValidateLedger(migrations, applied);
                if (applied.Any(row => row.Version == migration.Version))
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                await ExecuteAsync(connection, transaction, migration.UpSql, cancellationToken);
                await InsertLedgerAsync(connection, transaction, migration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                appliedNow.Add(migration.Info);
            }
            catch
            {
                await PostgreSqlCompensation.RunAsync(
                    "postgres.migration_apply.rollback",
                    token => transaction.RollbackAsync(token),
                    logger);
                throw;
            }
        }

        return appliedNow;
    }

    public async Task<IReadOnlyList<PostgreSqlMigrationInfo>> RollbackAsync(
        long targetVersion,
        CancellationToken cancellationToken = default)
    {
        if (targetVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVersion));
        }

        var rolledBack = new List<PostgreSqlMigrationInfo>();
        var migrations = BuildMigrations();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await EnsureLedgerAsync(connection, cancellationToken);

        foreach (var migration in migrations.Where(item => item.Version > targetVersion).OrderByDescending(item => item.Version))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireLockAsync(connection, transaction, cancellationToken);
                var applied = await ReadAppliedAsync(connection, transaction, ledgerMayBeMissing: false, cancellationToken);
                ValidateLedger(migrations, applied);
                if (!applied.Any(row => row.Version == migration.Version))
                {
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }

                var laterApplied = applied.Any(row => row.Version > migration.Version);
                if (laterApplied)
                {
                    throw new InvalidOperationException(
                        $"Migration {migration.Version} cannot be rolled back before later migrations.");
                }

                await ExecuteAsync(connection, transaction, migration.DownSql, cancellationToken);
                await DeleteLedgerAsync(connection, transaction, migration.Version, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                rolledBack.Add(migration.Info);
            }
            catch
            {
                await PostgreSqlCompensation.RunAsync(
                    "postgres.migration_rollback.rollback",
                    token => transaction.RollbackAsync(token),
                    logger);
                throw;
            }
        }

        return rolledBack;
    }

    public Task<string> GenerateScriptAsync(
        long? fromVersion = null,
        long? toVersion = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var migrations = BuildMigrations();
        var firstExcluded = fromVersion ?? 0;
        var lastIncluded = toVersion ?? migrations[^1].Version;
        if (firstExcluded < 0 || lastIncluded < 0 || firstExcluded > lastIncluded)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), "Migration version range is invalid.");
        }

        if (lastIncluded > migrations[^1].Version)
        {
            throw new ArgumentOutOfRangeException(nameof(toVersion), "Target migration version does not exist.");
        }

        var selected = migrations
            .Where(migration => migration.Version > firstExcluded && migration.Version <= lastIncluded)
            .ToList();
        var script = new StringBuilder();
        script.AppendLine("-- Generated by Zumbo.DatabaseMigrator. Review before production use.");
        script.AppendLine("BEGIN;");
        script.AppendLine("CREATE TABLE IF NOT EXISTS public.zumbo_schema_migrations (");
        script.AppendLine("    version bigint PRIMARY KEY,");
        script.AppendLine("    name text NOT NULL,");
        script.AppendLine("    checksum text NOT NULL,");
        script.AppendLine("    applied_at timestamptz NOT NULL DEFAULT transaction_timestamp()");
        script.AppendLine(");");
        script.AppendLine($"SELECT pg_advisory_xact_lock(hashtext('{LockName}'));\n");

        foreach (var migration in selected)
        {
            script.AppendLine($"-- Migration {migration.Version}: {migration.Name}");
            script.AppendLine(migration.UpSql.Trim());
            script.Append("INSERT INTO public.zumbo_schema_migrations (version, name, checksum) VALUES (")
                .Append(migration.Version)
                .Append(", '").Append(SqlLiteral(migration.Name))
                .Append("', '").Append(SqlLiteral(migration.Checksum)).Append("')");
            script.AppendLine(idempotent
                ? " ON CONFLICT (version) DO NOTHING;"
                : ";");
            if (idempotent)
            {
                script.Append("DO $zumbo$ BEGIN IF NOT EXISTS (SELECT 1 FROM public.zumbo_schema_migrations WHERE version = ")
                    .Append(migration.Version)
                    .Append(" AND name = '").Append(SqlLiteral(migration.Name))
                    .Append("' AND checksum = '").Append(SqlLiteral(migration.Checksum))
                    .AppendLine("') THEN RAISE EXCEPTION 'Migration ledger checksum mismatch'; END IF; END $zumbo$;");
            }
            script.AppendLine();
        }

        script.AppendLine("COMMIT;");
        return Task.FromResult(script.ToString());
    }

    private IReadOnlyList<Migration> BuildMigrations()
    {
        var storages = PostgreSqlDocumentCatalog.BuiltInStorages
            .OrderBy(storage => storage.Schema, StringComparer.Ordinal)
            .ThenBy(storage => storage.Table, StringComparer.Ordinal)
            .ToList();
        var schemas = storages.Select(storage => storage.Schema)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var createSchemas = string.Join('\n', schemas.Select(schema =>
            $"CREATE SCHEMA IF NOT EXISTS {SqlIdentifier.Quote(schema)};"));
        var dropSchemas = "DROP TABLE IF EXISTS identity.refresh_sessions;\n" +
            string.Join('\n', schemas.OrderDescending(StringComparer.Ordinal).Select(schema =>
                $"DROP SCHEMA IF EXISTS {SqlIdentifier.Quote(schema)};"));
        var createTables = string.Join("\n\n", storages.Select(CreateTableSql));
        var dropTables = string.Join('\n', storages.AsEnumerable().Reverse().Select(storage =>
            $"DROP TABLE IF EXISTS {Qualified(storage)};"));
        var createIndexes = string.Join('\n', storages.Select(storage =>
            $"CREATE INDEX IF NOT EXISTS {SqlIdentifier.Quote(IndexName(storage))} " +
            $"ON {Qualified(storage)} USING GIN (document jsonb_path_ops);"));
        var dropIndexes = string.Join('\n', storages.AsEnumerable().Reverse().Select(storage =>
            $"DROP INDEX IF EXISTS {SqlIdentifier.Quote(storage.Schema)}.{SqlIdentifier.Quote(IndexName(storage))};"));

        const string accessIndexes = """
            CREATE OR REPLACE FUNCTION public.zumbo_parse_timestamptz(value text)
                RETURNS timestamptz
                LANGUAGE sql
                IMMUTABLE
                STRICT
                PARALLEL SAFE
                AS 'SELECT value::timestamptz';
            CREATE UNIQUE INDEX IF NOT EXISTS ux_users_username_ci ON identity.users (lower(document #>> ARRAY['Username']));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email_ci ON identity.users (lower(document #>> ARRAY['Email']));
            CREATE INDEX IF NOT EXISTS ix_users_organization ON identity.users ((document #>> ARRAY['OrganizationId']));
            CREATE INDEX IF NOT EXISTS ix_users_refresh_token_hash ON identity.users USING GIN ((document #> ARRAY['RefreshTokens']) jsonb_path_ops);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_identity_roles_organization_name_ci
                ON identity.identity_roles ((document #>> ARRAY['OrganizationId']), lower(document #>> ARRAY['Name'])) NULLS NOT DISTINCT;
            CREATE INDEX IF NOT EXISTS ix_api_keys_user_created
                ON identity.api_keys ((document #>> ARRAY['UserId']), public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC);
            CREATE INDEX IF NOT EXISTS ix_api_keys_expires
                ON identity.api_keys (public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_organizations_tenant_key_ci
                ON organizations.organizations (lower(document #>> ARRAY['TenantKey']));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_organization_key_ci
                ON projects.projects ((document #>> ARRAY['OrganizationId']), lower(document #>> ARRAY['Key']));
            CREATE INDEX IF NOT EXISTS ix_projects_organization_archived_key
                ON projects.projects ((document #>> ARRAY['OrganizationId']), ((document #>> ARRAY['Archived'])::boolean), (document #>> ARRAY['Key']), id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_teams_organization_name_ci
                ON teams.teams ((document #>> ARRAY['OrganizationId']), lower(document #>> ARRAY['Name']));
            CREATE INDEX IF NOT EXISTS ix_teams_organization_archived_name
                ON teams.teams ((document #>> ARRAY['OrganizationId']), ((document #>> ARRAY['Archived'])::boolean), (document #>> ARRAY['Name']), id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_boards_active_project_name_ci
                ON boards.boards ((document #>> ARRAY['ProjectId']), lower(document #>> ARRAY['Name']))
                WHERE ((document #>> ARRAY['Archived'])::boolean) IS FALSE;
            CREATE INDEX IF NOT EXISTS ix_boards_project_archived_name
                ON boards.boards ((document #>> ARRAY['ProjectId']), ((document #>> ARRAY['Archived'])::boolean), (document #>> ARRAY['Name']), id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workflows_project
                ON workflows.workflow_definitions ((document #>> ARRAY['ProjectId']));
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_rank
                ON work_items.work_items ((document #>> ARRAY['ProjectId']), ((document #>> ARRAY['Archived'])::boolean), ((document #>> ARRAY['Rank'])::bigint), id);
            CREATE INDEX IF NOT EXISTS ix_work_items_board_column_archived_rank
                ON work_items.work_items ((document #>> ARRAY['BoardId']), (document #>> ARRAY['ColumnId']), ((document #>> ARRAY['Archived'])::boolean), ((document #>> ARRAY['Rank'])::bigint), id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_status_rank
                ON work_items.work_items ((document #>> ARRAY['ProjectId']), ((document #>> ARRAY['Archived'])::boolean), (document #>> ARRAY['Status']), ((document #>> ARRAY['Rank'])::bigint), id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_due
                ON work_items.work_items ((document #>> ARRAY['ProjectId']), ((document #>> ARRAY['Archived'])::boolean), public.zumbo_parse_timestamptz(document #>> ARRAY['DueDate']), id);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_entity_created
                ON audit.audit_logs ((document #>> ARRAY['EntityType']), (document #>> ARRAY['EntityId']), public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_actor_created
                ON audit.audit_logs ((document #>> ARRAY['ActorUserId']), public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_action_created
                ON audit.audit_logs ((document #>> ARRAY['Action']), public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC);
            CREATE INDEX IF NOT EXISTS ix_notifications_user_read_created
                ON notifications.notifications ((document #>> ARRAY['UserId']), ((document #>> ARRAY['Read'])::boolean), public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC, id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_notifications_deduplication_key
                ON notifications.notifications ((document #>> ARRAY['DeduplicationKey']))
                WHERE document #>> ARRAY['DeduplicationKey'] IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_notification_preferences_user
                ON notifications.notification_preferences ((document #>> ARRAY['UserId']));
            """;
        const string dropAccessIndexes = """
            DROP INDEX IF EXISTS notifications.ux_notification_preferences_user;
            DROP INDEX IF EXISTS notifications.ux_notifications_deduplication_key;
            DROP INDEX IF EXISTS notifications.ix_notifications_user_read_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_action_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_actor_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_entity_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_due;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_status_rank;
            DROP INDEX IF EXISTS work_items.ix_work_items_board_column_archived_rank;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_rank;
            DROP INDEX IF EXISTS workflows.ux_workflows_project;
            DROP INDEX IF EXISTS boards.ix_boards_project_archived_name;
            DROP INDEX IF EXISTS boards.ux_boards_active_project_name_ci;
            DROP INDEX IF EXISTS teams.ix_teams_organization_archived_name;
            DROP INDEX IF EXISTS teams.ux_teams_organization_name_ci;
            DROP INDEX IF EXISTS projects.ix_projects_organization_archived_key;
            DROP INDEX IF EXISTS projects.ux_projects_organization_key_ci;
            DROP INDEX IF EXISTS organizations.ux_organizations_tenant_key_ci;
            DROP INDEX IF EXISTS identity.ix_api_keys_expires;
            DROP INDEX IF EXISTS identity.ix_api_keys_user_created;
            DROP INDEX IF EXISTS identity.ux_identity_roles_organization_name_ci;
            DROP INDEX IF EXISTS identity.ix_users_refresh_token_hash;
            DROP INDEX IF EXISTS identity.ix_users_organization;
            DROP INDEX IF EXISTS identity.ux_users_email_ci;
            DROP INDEX IF EXISTS identity.ux_users_username_ci;
            DROP FUNCTION IF EXISTS public.zumbo_parse_timestamptz(text);
            """;
        const string durableMessaging = """
            CREATE SCHEMA IF NOT EXISTS messaging;
            CREATE TABLE IF NOT EXISTS messaging.outbox_messages (
                id text PRIMARY KEY,
                owner_module text NOT NULL,
                event_type text NOT NULL,
                schema_version integer NOT NULL CHECK (schema_version > 0),
                tenant_id text NOT NULL,
                correlation_id text NOT NULL,
                deduplication_key text NULL,
                payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
                occurred_at_utc timestamptz NOT NULL,
                status text NOT NULL DEFAULT 'Pending'
                    CHECK (status IN ('Pending', 'Processing', 'Completed', 'DeadLetter')),
                attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
                available_at_utc timestamptz NOT NULL,
                lease_owner text NULL,
                lease_token text NULL,
                lease_until_utc timestamptz NULL,
                last_error text NULL,
                completed_at_utc timestamptz NULL,
                dead_lettered_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at_utc timestamptz NOT NULL DEFAULT transaction_timestamp()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_deduplication
                ON messaging.outbox_messages (owner_module, event_type, deduplication_key)
                WHERE deduplication_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_outbox_claim
                ON messaging.outbox_messages (status, available_at_utc, lease_until_utc, occurred_at_utc, id)
                WHERE status IN ('Pending', 'Processing');
            CREATE INDEX IF NOT EXISTS ix_outbox_dead_letter
                ON messaging.outbox_messages (dead_lettered_at_utc, id)
                WHERE status = 'DeadLetter';
            CREATE TABLE IF NOT EXISTS messaging.inbox_messages (
                consumer_name text NOT NULL,
                message_id text NOT NULL,
                processed_at_utc timestamptz NOT NULL,
                PRIMARY KEY (consumer_name, message_id)
            );
            CREATE INDEX IF NOT EXISTS ix_inbox_processed_at
                ON messaging.inbox_messages (processed_at_utc);
            """;
        const string dropDurableMessaging = """
            DROP TABLE IF EXISTS messaging.inbox_messages;
            DROP TABLE IF EXISTS messaging.outbox_messages;
            DROP SCHEMA IF EXISTS messaging;
            """;
        const string durableConsumerDeduplication = """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_logs_deduplication_key
                ON audit.audit_logs ((document #>> ARRAY['DeduplicationKey']))
                WHERE document #>> ARRAY['DeduplicationKey'] IS NOT NULL;
            """;
        const string dropDurableConsumerDeduplication = """
            DROP INDEX IF EXISTS audit.ux_audit_logs_deduplication_key;
            """;
        const string identityCredentialStores = """
            CREATE TABLE IF NOT EXISTS identity.refresh_sessions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_document_gin
                ON identity.refresh_sessions USING GIN (document jsonb_path_ops);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_sessions_token_hash
                ON identity.refresh_sessions ((document #>> ARRAY['TokenHash']));
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_owner_active
                ON identity.refresh_sessions (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    (document #>> ARRAY['RevokedAtUtc']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_retain_until
                ON identity.refresh_sessions (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['RetainUntilUtc']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_api_keys_owner_created
                ON identity.api_keys (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']) DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_api_keys_owner_revoked_expires
                ON identity.api_keys (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    (document #>> ARRAY['RevokedAtUtc']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM identity.users AS users
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE
                            WHEN jsonb_typeof(users.document -> 'RefreshTokens') = 'array'
                                THEN users.document -> 'RefreshTokens'
                            ELSE '[]'::jsonb
                        END) AS token(value)
                    JOIN identity.refresh_sessions AS existing
                      ON existing.id = COALESCE(
                          NULLIF(token.value ->> 'SessionId', ''),
                          md5(users.id || ':' || (token.value ->> 'TokenHash')))
                    WHERE NULLIF(token.value ->> 'TokenHash', '') IS NOT NULL
                      AND NULLIF(token.value ->> 'ExpiresAt', '') IS NOT NULL
                      AND (
                          existing.document ->> 'UserId' IS DISTINCT FROM users.id
                          OR existing.document ->> 'OrganizationId'
                              IS DISTINCT FROM users.document ->> 'OrganizationId'
                          OR existing.document ->> 'TokenHash'
                              IS DISTINCT FROM token.value ->> 'TokenHash'))
                THEN
                    RAISE EXCEPTION
                        'Refresh session backfill conflicts with incompatible stored ownership or token data.';
                END IF;
            END $$;
            INSERT INTO identity.refresh_sessions (id, version, document)
            SELECT
                COALESCE(NULLIF(token.value ->> 'SessionId', ''), md5(users.id || ':' || (token.value ->> 'TokenHash'))),
                1,
                jsonb_build_object(
                    'Id', COALESCE(NULLIF(token.value ->> 'SessionId', ''), md5(users.id || ':' || (token.value ->> 'TokenHash'))),
                    'UserId', users.id,
                    'OrganizationId', users.document ->> 'OrganizationId',
                    'TokenHash', token.value ->> 'TokenHash',
                    'CreatedAt', token.value -> 'CreatedAt',
                    'ExpiresAt', token.value -> 'ExpiresAt',
                    'ExpiresAtUtc', token.value -> 'ExpiresAt',
                    'RevokedAt', COALESCE(token.value -> 'RevokedAt', 'null'::jsonb),
                    'RevokedAtUtc', COALESCE(token.value -> 'RevokedAt', 'null'::jsonb),
                    'ReplacedBySessionId', 'null'::jsonb,
                    'RetainUntilUtc', to_jsonb(to_char(
                        (GREATEST(
                            public.zumbo_parse_timestamptz(token.value ->> 'ExpiresAt'),
                            COALESCE(
                                public.zumbo_parse_timestamptz(token.value ->> 'RevokedAt'),
                                public.zumbo_parse_timestamptz(token.value ->> 'ExpiresAt')))
                            + interval '30 days') AT TIME ZONE 'UTC',
                        'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')),
                    'Version', 1)
            FROM identity.users AS users
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE
                    WHEN jsonb_typeof(users.document -> 'RefreshTokens') = 'array'
                        THEN users.document -> 'RefreshTokens'
                    ELSE '[]'::jsonb
                END) AS token(value)
            WHERE NULLIF(token.value ->> 'TokenHash', '') IS NOT NULL
              AND NULLIF(token.value ->> 'ExpiresAt', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;
            DROP INDEX IF EXISTS identity.ix_users_refresh_token_hash;
            """;
        const string dropIdentityCredentialStores = """
            CREATE INDEX IF NOT EXISTS ix_users_refresh_token_hash
                ON identity.users USING GIN ((document #> ARRAY['RefreshTokens']) jsonb_path_ops);
            DROP INDEX IF EXISTS identity.ix_api_keys_owner_revoked_expires;
            DROP INDEX IF EXISTS identity.ix_api_keys_owner_created;
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_retain_until;
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_owner_active;
            DROP INDEX IF EXISTS identity.ux_refresh_sessions_token_hash;
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_document_gin;
            """;
        const string apiKeyVersionBackfill = """
            UPDATE identity.api_keys
            SET version = 1,
                document = jsonb_set(
                    jsonb_set(document, ARRAY['Version'], '1'::jsonb, true),
                    ARRAY['VersionMigratedBy'],
                    '"20260719_008"'::jsonb,
                    true),
                updated_at = transaction_timestamp()
            WHERE version = 0;
            """;
        const string dropApiKeyVersionBackfill = """
            UPDATE identity.api_keys
            SET version = 0,
                document = document - 'Version' - 'VersionMigratedBy',
                updated_at = transaction_timestamp()
            WHERE version = 1
              AND document ->> 'VersionMigratedBy' = '20260719_008';
            """;
        const string apiKeyExpiryIndex = """
            CREATE INDEX IF NOT EXISTS ix_api_keys_expires_utc
                ON identity.api_keys (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAt']),
                    id);
            """;
        const string dropApiKeyExpiryIndex = """
            DROP INDEX IF EXISTS identity.ix_api_keys_expires_utc;
            """;
        const string apiKeyUtcFieldBackfill = """
            UPDATE identity.api_keys
            SET document = jsonb_set(
                    jsonb_set(
                        document,
                        ARRAY['ExpiresAtUtc'],
                        document -> 'ExpiresAt',
                        true),
                    ARRAY['ExpiresAtUtcMigratedBy'],
                    '"20260719_010"'::jsonb,
                    true),
                updated_at = transaction_timestamp()
            WHERE NOT document ? 'ExpiresAtUtc'
              AND document ? 'ExpiresAt';

            UPDATE identity.api_keys
            SET document = jsonb_set(
                    jsonb_set(
                        document,
                        ARRAY['RevokedAtUtc'],
                        document -> 'RevokedAt',
                        true),
                    ARRAY['RevokedAtUtcMigratedBy'],
                    '"20260719_010"'::jsonb,
                    true),
                updated_at = transaction_timestamp()
            WHERE NOT document ? 'RevokedAtUtc'
              AND document ? 'RevokedAt';
            """;
        const string dropApiKeyUtcFieldBackfill = """
            UPDATE identity.api_keys
            SET document = document - 'ExpiresAtUtc' - 'ExpiresAtUtcMigratedBy',
                updated_at = transaction_timestamp()
            WHERE document ->> 'ExpiresAtUtcMigratedBy' = '20260719_010';

            UPDATE identity.api_keys
            SET document = document - 'RevokedAtUtc' - 'RevokedAtUtcMigratedBy',
                updated_at = transaction_timestamp()
            WHERE document ->> 'RevokedAtUtcMigratedBy' = '20260719_010';
            """;
        const string workItemActivityStores = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_comments (
                id text PRIMARY KEY, version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version));
            CREATE TABLE IF NOT EXISTS work_items.work_item_comment_revisions
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_attachments
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_work_logs
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_approvals
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_timeline
                (LIKE work_items.work_item_comments INCLUDING ALL);

            CREATE INDEX IF NOT EXISTS ix_work_item_comments_owner_created
                ON work_items.work_item_comments (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_revisions_owner_comment_edited
                ON work_items.work_item_comment_revisions (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), (document ->> 'CommentId'),
                    public.zumbo_parse_timestamptz(document ->> 'EditedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_attachments_owner_created
                ON work_items.work_item_attachments (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_work_logs_owner_created
                ON work_items.work_item_work_logs (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_approvals_owner_requested
                ON work_items.work_item_approvals (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'RequestedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_timeline_owner_changed
                ON work_items.work_item_timeline (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'ChangedAt'), id);

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM work_items.work_items wi
                    LEFT JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
                    WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
                      AND NULLIF(p.document ->> 'OrganizationId', '') IS NULL)
                THEN
                    RAISE EXCEPTION 'Work-item activity backfill requires project tenant ownership.';
                END IF;
            END $$;

            INSERT INTO work_items.work_item_comments (id, version, document)
            SELECT comment.value ->> 'Id', 0,
                (comment.value - 'History') || jsonb_build_object(
                    'Id', comment.value ->> 'Id',
                    'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId',
                    'WorkItemId', wi.id,
                    'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Comments') = 'array'
                    THEN wi.document -> 'Comments' ELSE '[]'::jsonb END) comment(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(comment.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_comment_revisions (id, version, document)
            SELECT md5('revision' || chr(31) || wi.id || chr(31) || (comment.value ->> 'Id')
                    || chr(31) || (revision.ordinality - 1)::text || chr(31)
                    || (extract(epoch FROM public.zumbo_parse_timestamptz(revision.value ->> 'EditedAt')) * 10000000
                        + 621355968000000000)::bigint::text),
                0,
                revision.value || jsonb_build_object(
                    'Id', md5('revision' || chr(31) || wi.id || chr(31) || (comment.value ->> 'Id')
                        || chr(31) || (revision.ordinality - 1)::text || chr(31)
                        || (extract(epoch FROM public.zumbo_parse_timestamptz(revision.value ->> 'EditedAt')) * 10000000
                            + 621355968000000000)::bigint::text),
                    'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId',
                    'WorkItemId', wi.id,
                    'CommentId', comment.value ->> 'Id',
                    'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Comments') = 'array'
                    THEN wi.document -> 'Comments' ELSE '[]'::jsonb END) comment(value)
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(comment.value -> 'History') = 'array'
                    THEN comment.value -> 'History' ELSE '[]'::jsonb END)
                WITH ORDINALITY revision(value, ordinality)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(comment.value ->> 'Id', '') IS NOT NULL
              AND NULLIF(revision.value ->> 'EditedAt', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_attachments (id, version, document)
            SELECT activity.value ->> 'Id', 0,
                activity.value || jsonb_build_object(
                    'Id', activity.value ->> 'Id', 'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId', 'WorkItemId', wi.id, 'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Attachments') = 'array'
                    THEN wi.document -> 'Attachments' ELSE '[]'::jsonb END) activity(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(activity.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_work_logs (id, version, document)
            SELECT activity.value ->> 'Id', 0,
                activity.value || jsonb_build_object(
                    'Id', activity.value ->> 'Id', 'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId', 'WorkItemId', wi.id, 'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'WorkLogs') = 'array'
                    THEN wi.document -> 'WorkLogs' ELSE '[]'::jsonb END) activity(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(activity.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_approvals (id, version, document)
            SELECT activity.value ->> 'Id', 0,
                activity.value || jsonb_build_object(
                    'Id', activity.value ->> 'Id', 'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId', 'WorkItemId', wi.id, 'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Approvals') = 'array'
                    THEN wi.document -> 'Approvals' ELSE '[]'::jsonb END) activity(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(activity.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_timeline (id, version, document)
            SELECT md5('timeline' || chr(31) || wi.id || chr(31) || (history.ordinality - 1)::text
                    || chr(31) || (extract(epoch FROM public.zumbo_parse_timestamptz(history.value ->> 'ChangedAt')) * 10000000
                        + 621355968000000000)::bigint::text
                    || chr(31) || (history.value ->> 'ToStatus')),
                0,
                history.value || jsonb_build_object(
                    'Id', md5('timeline' || chr(31) || wi.id || chr(31) || (history.ordinality - 1)::text
                        || chr(31) || (extract(epoch FROM public.zumbo_parse_timestamptz(history.value ->> 'ChangedAt')) * 10000000
                            + 621355968000000000)::bigint::text
                        || chr(31) || (history.value ->> 'ToStatus')),
                    'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId',
                    'WorkItemId', wi.id,
                    'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'StatusHistory') = 'array'
                    THEN wi.document -> 'StatusHistory' ELSE '[]'::jsonb END)
                WITH ORDINALITY history(value, ordinality)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(history.value ->> 'ChangedAt', '') IS NOT NULL
              AND NULLIF(history.value ->> 'ToStatus', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            UPDATE work_items.work_items
            SET version = version + 1,
                document = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            jsonb_set(
                                jsonb_set(
                                    jsonb_set(document, '{Comments}', '[]'::jsonb, true),
                                    '{Attachments}', '[]'::jsonb, true),
                                '{WorkLogs}', '[]'::jsonb, true),
                            '{Approvals}', '[]'::jsonb, true),
                        '{StatusHistory}', '[]'::jsonb, true),
                    '{ActivityStorageVersion}', '1'::jsonb, true)
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE COALESCE((document ->> 'ActivityStorageVersion')::integer, 0) < 1;
            """;
        const string dropWorkItemActivityStores = """
            UPDATE work_items.work_items wi
            SET version = wi.version + 1,
                document = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            jsonb_set(
                                jsonb_set(
                                    jsonb_set(wi.document, '{Comments}', COALESCE((
                                        SELECT jsonb_agg(c.document || jsonb_build_object('History', COALESCE((
                                            SELECT jsonb_agg(r.document - 'Id' - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'CommentId' - 'Version'
                                                ORDER BY public.zumbo_parse_timestamptz(r.document ->> 'EditedAt'), r.id)
                                            FROM work_items.work_item_comment_revisions r
                                            WHERE r.document ->> 'WorkItemId' = wi.id
                                              AND r.document ->> 'CommentId' = c.id), '[]'::jsonb))
                                            - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                            ORDER BY public.zumbo_parse_timestamptz(c.document ->> 'CreatedAt'), c.id)
                                        FROM work_items.work_item_comments c
                                        WHERE c.document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                                    '{Attachments}', COALESCE((SELECT jsonb_agg(document - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                        ORDER BY public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id)
                                        FROM work_items.work_item_attachments WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                                '{WorkLogs}', COALESCE((SELECT jsonb_agg(document - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                    ORDER BY public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id)
                                    FROM work_items.work_item_work_logs WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                            '{Approvals}', COALESCE((SELECT jsonb_agg(document - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                ORDER BY public.zumbo_parse_timestamptz(document ->> 'RequestedAt'), id)
                                FROM work_items.work_item_approvals WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                        '{StatusHistory}', COALESCE((SELECT jsonb_agg(document - 'Id' - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                            ORDER BY public.zumbo_parse_timestamptz(document ->> 'ChangedAt'), id)
                            FROM work_items.work_item_timeline WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                    '{ActivityStorageVersion}', '0'::jsonb, true)
                    || jsonb_build_object('Version', wi.version + 1),
                updated_at = transaction_timestamp()
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) >= 1;

            DROP TABLE IF EXISTS work_items.work_item_timeline;
            DROP TABLE IF EXISTS work_items.work_item_approvals;
            DROP TABLE IF EXISTS work_items.work_item_work_logs;
            DROP TABLE IF EXISTS work_items.work_item_attachments;
            DROP TABLE IF EXISTS work_items.work_item_comment_revisions;
            DROP TABLE IF EXISTS work_items.work_item_comments;
            """;
        const string organizationVersionBackfill = """
            UPDATE organizations.organizations
            SET version = 1,
                document = jsonb_set(
                    jsonb_set(document, ARRAY['Version'], '1'::jsonb, true),
                    ARRAY['Status'],
                    COALESCE(document -> 'Status', '"Active"'::jsonb),
                    true)
                    || jsonb_build_object('OrganizationVersionMigratedBy', '20260720_012'),
                updated_at = transaction_timestamp()
            WHERE version = 0;
            """;
        const string dropOrganizationVersionBackfill = """
            UPDATE organizations.organizations
            SET version = 0,
                document = document - 'Version' - 'OrganizationVersionMigratedBy',
                updated_at = transaction_timestamp()
            WHERE version = 1
              AND document ->> 'OrganizationVersionMigratedBy' = '20260720_012';
            """;
        const string expireLegacyTeamInvites = """
            UPDATE teams.teams team
            SET version = team.version + 1,
                document = jsonb_set(
                    team.document,
                    '{Members}',
                    COALESCE((
                        SELECT jsonb_agg(
                            CASE
                                WHEN member.value ->> 'Status' = 'Invited'
                                  AND COALESCE(member.value ->> 'InvitationTokenHash', '') = ''
                                THEN member.value || jsonb_build_object(
                                    'Status', 'Expired',
                                    'InvitationTokenHash', NULL,
                                    'InvitationExpiresAt', NULL,
                                    'RespondedAt', transaction_timestamp())
                                ELSE member.value
                            END
                            ORDER BY member.ordinality)
                        FROM jsonb_array_elements(COALESCE(team.document -> 'Members', '[]'::jsonb))
                            WITH ORDINALITY AS member(value, ordinality)),
                        '[]'::jsonb),
                    true)
                    || jsonb_build_object(
                        'Version', team.version + 1,
                        'TeamInviteTokenMigratedBy', '20260720_013'),
                updated_at = transaction_timestamp()
            WHERE EXISTS (
                SELECT 1
                FROM jsonb_array_elements(COALESCE(team.document -> 'Members', '[]'::jsonb)) member
                WHERE member ->> 'Status' = 'Invited'
                  AND COALESCE(member ->> 'InvitationTokenHash', '') = '');
            """;
        const string dropLegacyTeamInviteMarker = """
            UPDATE teams.teams
            SET version = version + 1,
                document = (document - 'TeamInviteTokenMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'TeamInviteTokenMigratedBy' = '20260720_013';
            """;
        const string projectLifecycleBackfill = """
            UPDATE projects.projects project
            SET version = GREATEST(project.version, 0) + 1,
                document = jsonb_build_object(
                        'Visibility', 'Internal',
                        'Archived', false,
                        'Members', '[]'::jsonb,
                        'TeamIds', '[]'::jsonb,
                        'Templates', '[]'::jsonb,
                        'Components', '[]'::jsonb,
                        'Versions', '[]'::jsonb,
                        'Releases', '[]'::jsonb,
                        'Milestones', '[]'::jsonb,
                        'ArchivedAt', NULL,
                        'RetainUntil', NULL)
                    || project.document
                    || jsonb_build_object(
                        'Version', GREATEST(project.version, 0) + 1,
                        'ProjectLifecycleMigratedBy', '20260720_014'),
                updated_at = transaction_timestamp()
            WHERE project.version <= 0
               OR NOT project.document ? 'Visibility'
               OR NOT project.document ? 'Templates'
               OR NOT project.document ? 'Components'
               OR NOT project.document ? 'Versions'
               OR NOT project.document ? 'Releases'
               OR NOT project.document ? 'Milestones'
               OR NOT project.document ? 'ArchivedAt'
               OR NOT project.document ? 'RetainUntil';
            """;
        const string dropProjectLifecycleMarker = """
            UPDATE projects.projects
            SET version = version + 1,
                document = (document - 'ProjectLifecycleMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'ProjectLifecycleMigratedBy' = '20260720_014';
            """;
        const string workflowLifecycleAndWipProjection = """
            CREATE TABLE IF NOT EXISTS work_items.board_column_wip_projections (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_board_column_wip_projection_lookup
                ON work_items.board_column_wip_projections
                ((document ->> 'ProjectId'), (document ->> 'BoardId'), (document ->> 'ColumnId'));

            INSERT INTO work_items.board_column_wip_projections (id, version, document)
            SELECT
                (wi.document ->> 'BoardId') || ':' || (wi.document ->> 'ColumnId'),
                0,
                jsonb_build_object(
                    'Id', (wi.document ->> 'BoardId') || ':' || (wi.document ->> 'ColumnId'),
                    'ProjectId', wi.document ->> 'ProjectId',
                    'BoardId', wi.document ->> 'BoardId',
                    'ColumnId', wi.document ->> 'ColumnId',
                    'ActiveCount', count(*)::integer,
                    'UpdatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM work_items.work_items wi
            WHERE COALESCE((wi.document ->> 'Archived')::boolean, false) = false
              AND COALESCE(wi.document ->> 'BoardId', '') <> ''
              AND COALESCE(wi.document ->> 'ColumnId', '') <> ''
            GROUP BY wi.document ->> 'ProjectId', wi.document ->> 'BoardId', wi.document ->> 'ColumnId'
            ON CONFLICT (id) DO NOTHING;

            WITH prepared AS (
                SELECT
                    workflow.id,
                    workflow.version,
                    workflow.document,
                    COALESCE((
                        SELECT status ->> 'Name'
                        FROM jsonb_array_elements(COALESCE(workflow.document -> 'Statuses', '[]'::jsonb)) status
                        WHERE status ->> 'Category' = 'Todo'
                        LIMIT 1), 'To Do') AS default_status,
                    COALESCE((
                        SELECT jsonb_agg(status ->> 'Name')
                        FROM jsonb_array_elements(COALESCE(workflow.document -> 'Statuses', '[]'::jsonb)) status),
                        '[]'::jsonb) AS status_names,
                    COALESCE((
                        SELECT jsonb_agg(status ->> 'Name')
                        FROM jsonb_array_elements(COALESCE(workflow.document -> 'Statuses', '[]'::jsonb)) status
                        WHERE status ->> 'Category' = 'Done'),
                        '[]'::jsonb) AS done_names
                FROM workflows.workflow_definitions workflow
                WHERE NOT workflow.document ? 'PublishedVersion'
                   OR NOT workflow.document ? 'IssueTypeSchemes'
                   OR NOT workflow.document ? 'Draft'
                   OR NOT workflow.document ? 'PublishedVersions'
            ), definitions AS (
                SELECT prepared.*,
                    jsonb_build_array(jsonb_build_object(
                        'IssueType', '*',
                        'DefaultStatus', prepared.default_status,
                        'Statuses', prepared.status_names,
                        'DoneStatuses', prepared.done_names)) AS schemes
                FROM prepared
            )
            UPDATE workflows.workflow_definitions workflow
            SET version = workflow.version + 1,
                document = workflow.document || jsonb_build_object(
                    'PublishedVersion', 1,
                    'IssueTypeSchemes', definitions.schemes,
                    'Draft', NULL,
                    'PublishedVersions', jsonb_build_array(jsonb_build_object(
                        'Number', 1,
                        'State', 'Published',
                        'Statuses', COALESCE(workflow.document -> 'Statuses', '[]'::jsonb),
                        'Transitions', COALESCE(workflow.document -> 'Transitions', '[]'::jsonb),
                        'IssueTypeSchemes', definitions.schemes,
                        'CreatedAt', workflow.document -> 'CreatedAt',
                        'PublishedAt', COALESCE(workflow.document -> 'UpdatedAt', workflow.document -> 'CreatedAt'))),
                    'WorkflowLifecycleMigratedBy', '20260720_015',
                    'Version', workflow.version + 1),
                updated_at = transaction_timestamp()
            FROM definitions
            WHERE workflow.id = definitions.id;
            """;
        const string dropWorkflowLifecycleMarker = """
            DROP TABLE IF EXISTS work_items.board_column_wip_projections;

            UPDATE workflows.workflow_definitions
            SET version = version + 1,
                document = (document - 'WorkflowLifecycleMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'WorkflowLifecycleMigratedBy' = '20260720_015';
            """;
        const string sprintLifecycle = """
            CREATE TABLE IF NOT EXISTS work_items.sprints (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.sprint_scope_snapshots (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.sprint_completion_snapshots (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sprints_project_name_ci
                ON work_items.sprints ((document ->> 'ProjectId'), lower(document ->> 'Name'));
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sprints_active_project
                ON work_items.sprints ((document ->> 'ProjectId'))
                WHERE document ->> 'Status' = 'Active';
            CREATE INDEX IF NOT EXISTS ix_sprints_project_status_start
                ON work_items.sprints ((document ->> 'ProjectId'), (document ->> 'Status'), public.zumbo_parse_timestamptz(document ->> 'StartAtUtc'), id);
            CREATE INDEX IF NOT EXISTS ix_sprint_scope_sprint_item
                ON work_items.sprint_scope_snapshots ((document ->> 'SprintId'), (document ->> 'WorkItemId'));
            CREATE INDEX IF NOT EXISTS ix_sprint_completion_sprint_item
                ON work_items.sprint_completion_snapshots ((document ->> 'SprintId'), (document ->> 'WorkItemId'));

            WITH legacy AS (
                SELECT DISTINCT
                    document ->> 'ProjectId' AS project_id,
                    document ->> 'SprintId' AS legacy_sprint_id,
                    'legacy-' || md5((document ->> 'ProjectId') || ':' || (document ->> 'SprintId')) AS sprint_id
                FROM work_items.work_items
                WHERE document ->> 'SprintId' IS NOT NULL
                  AND document ->> 'SprintId' <> ''
                  AND NOT document ? 'SprintLifecycleMigratedBy'
            )
            INSERT INTO work_items.sprints (id, version, document)
            SELECT
                sprint_id,
                0,
                jsonb_build_object(
                    'Id', sprint_id,
                    'ProjectId', project_id,
                    'Name', legacy_sprint_id || ' (legacy-' || right(sprint_id, 8) || ')',
                    'Goal', 'Legacy sprint backfill',
                    'StartAtUtc', transaction_timestamp(),
                    'EndAtUtc', transaction_timestamp() + interval '13 days',
                    'Status', 'Planned',
                    'CommittedItems', 0,
                    'CommittedPoints', 0,
                    'CompletedItems', 0,
                    'CompletedPoints', 0,
                    'CarryoverItems', 0,
                    'CarryoverPoints', 0,
                    'CreatedAt', transaction_timestamp(),
                    'UpdatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM legacy
            ON CONFLICT (id) DO NOTHING;

            WITH legacy AS (
                SELECT
                    id,
                    version,
                    'legacy-' || md5((document ->> 'ProjectId') || ':' || (document ->> 'SprintId')) AS sprint_id
                FROM work_items.work_items
                WHERE document ->> 'SprintId' IS NOT NULL
                  AND document ->> 'SprintId' <> ''
                  AND NOT document ? 'SprintLifecycleMigratedBy'
            )
            UPDATE work_items.work_items item
            SET version = item.version + 1,
                document = item.document || jsonb_build_object(
                    'SprintId', legacy.sprint_id,
                    'SprintLifecycleMigratedBy', '20260720_016',
                    'Version', item.version + 1),
                updated_at = transaction_timestamp()
            FROM legacy
            WHERE item.id = legacy.id
              AND item.version = legacy.version;
            """;
        const string dropSprintLifecycleMarker = """
            UPDATE work_items.work_items item
            SET version = item.version + 1,
                document = (item.document - 'SprintLifecycleMigratedBy')
                    || jsonb_build_object(
                        'SprintId', COALESCE(
                            (SELECT regexp_replace(sprint.document ->> 'Name', ' \(legacy-[0-9a-f]{8}\)$', '')
                             FROM work_items.sprints sprint
                             WHERE sprint.id = item.document ->> 'SprintId'),
                            item.document ->> 'SprintId'),
                        'Version', item.version + 1),
                updated_at = transaction_timestamp()
            WHERE item.document ->> 'SprintLifecycleMigratedBy' = '20260720_016';
            DROP TABLE IF EXISTS work_items.sprint_completion_snapshots;
            DROP TABLE IF EXISTS work_items.sprint_scope_snapshots;
            DROP TABLE IF EXISTS work_items.sprints;
            """;
        const string workItemTypeSchemas = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_type_schemas (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_type_schemas_project
                ON work_items.work_item_type_schemas ((document ->> 'ProjectId'));
            CREATE INDEX IF NOT EXISTS ix_workitems_project_archived_type_rank
                ON work_items.work_items (
                    (document ->> 'ProjectId'),
                    (COALESCE((document ->> 'Archived')::boolean, false)),
                    (document ->> 'Type'),
                    (COALESCE((document ->> 'Rank')::bigint, 0)),
                    id);
            CREATE INDEX IF NOT EXISTS ix_workitems_custom_fields_gin
                ON work_items.work_items USING gin ((document -> 'CustomFields') jsonb_path_ops);

            WITH project_ids AS (
                SELECT DISTINCT document ->> 'ProjectId' AS project_id
                FROM work_items.work_items
                WHERE document ->> 'ProjectId' IS NOT NULL
                  AND document ->> 'ProjectId' <> ''
            )
            INSERT INTO work_items.work_item_type_schemas (id, version, document)
            SELECT
                project_id,
                0,
                jsonb_build_object(
                    'Id', project_id,
                    'ProjectId', project_id,
                    'SchemaVersion', 1,
                    'IssueTypes', jsonb_build_array(
                        jsonb_build_object('Key', 'Epic', 'Name', 'Epic', 'Description', '', 'HierarchyLevel', 'Epic', 'Active', true, 'Position', 0),
                        jsonb_build_object('Key', 'Story', 'Name', 'Story', 'Description', '', 'HierarchyLevel', 'Standard', 'Active', true, 'Position', 10),
                        jsonb_build_object('Key', 'Task', 'Name', 'Task', 'Description', '', 'HierarchyLevel', 'Standard', 'Active', true, 'Position', 20),
                        jsonb_build_object('Key', 'Bug', 'Name', 'Bug', 'Description', '', 'HierarchyLevel', 'Standard', 'Active', true, 'Position', 30),
                        jsonb_build_object('Key', 'Subtask', 'Name', 'Subtask', 'Description', '', 'HierarchyLevel', 'Subtask', 'Active', true, 'Position', 40)),
                    'CustomFields', '[]'::jsonb,
                    'Layouts', jsonb_build_array(
                        jsonb_build_object('IssueTypeKey', 'Epic', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Story', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Task', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Bug', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Subtask', 'FieldKeys', '[]'::jsonb)),
                    'CreatedAt', transaction_timestamp(),
                    'UpdatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM project_ids
            ON CONFLICT (id) DO NOTHING;

            UPDATE work_items.work_items
            SET version = version + 1,
                document = document || jsonb_build_object(
                    'IssueTypeSchemaVersion', COALESCE((document ->> 'IssueTypeSchemaVersion')::integer, 1),
                    'CustomFields', COALESCE(document -> 'CustomFields', '[]'::jsonb),
                    'WorkItemTypeSchemaMigratedBy', '20260720_017',
                    'Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE NOT document ? 'IssueTypeSchemaVersion'
               OR NOT document ? 'CustomFields';
            """;
        const string dropWorkItemTypeSchemas = """
            UPDATE work_items.work_items
            SET version = version + 1,
                document = (document
                    - 'IssueTypeSchemaVersion'
                    - 'CustomFields'
                    - 'WorkItemTypeSchemaMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'WorkItemTypeSchemaMigratedBy' = '20260720_017';
            DROP INDEX IF EXISTS work_items.ix_workitems_custom_fields_gin;
            DROP INDEX IF EXISTS work_items.ix_workitems_project_archived_type_rank;
            DROP TABLE IF EXISTS work_items.work_item_type_schemas;
            """;
        const string workItemRelationGraph = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_relation_edges (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_work_item_relation_edges_source
                ON work_items.work_item_relation_edges (
                    (document ->> 'ProjectId'),
                    (document ->> 'SourceWorkItemId'),
                    (document ->> 'TargetWorkItemId'),
                    (document ->> 'RelationType'));
            CREATE INDEX IF NOT EXISTS ix_work_item_relation_edges_dependency_from
                ON work_items.work_item_relation_edges (
                    (document ->> 'ProjectId'),
                    (document ->> 'DependencyFromWorkItemId'),
                    (document ->> 'DependencyToWorkItemId'))
                WHERE document ->> 'DependencyFromWorkItemId' IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_work_item_relation_edges_dependency_to
                ON work_items.work_item_relation_edges (
                    (document ->> 'ProjectId'),
                    (document ->> 'DependencyToWorkItemId'),
                    (document ->> 'DependencyFromWorkItemId'))
                WHERE document ->> 'DependencyToWorkItemId' IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_work_items_project_parent_archived
                ON work_items.work_items (
                    (document ->> 'ProjectId'),
                    (document ->> 'ParentId'),
                    ((document ->> 'Archived')::boolean),
                    id);

            INSERT INTO work_items.work_item_relation_edges (id, version, document)
            SELECT
                md5(
                    (item.document ->> 'ProjectId') || chr(10)
                    || item.id || chr(10)
                    || (relation.value ->> 'RelatedWorkItemId') || chr(10)
                    || (relation.value ->> 'RelationType')),
                0,
                jsonb_build_object(
                    'Id', md5(
                        (item.document ->> 'ProjectId') || chr(10)
                        || item.id || chr(10)
                        || (relation.value ->> 'RelatedWorkItemId') || chr(10)
                        || (relation.value ->> 'RelationType')),
                    'ProjectId', item.document ->> 'ProjectId',
                    'SourceWorkItemId', item.id,
                    'TargetWorkItemId', relation.value ->> 'RelatedWorkItemId',
                    'RelationType', relation.value ->> 'RelationType',
                    'DependencyFromWorkItemId', CASE relation.value ->> 'RelationType'
                        WHEN 'Blocks' THEN item.id
                        WHEN 'BlockedBy' THEN relation.value ->> 'RelatedWorkItemId'
                        ELSE NULL
                    END,
                    'DependencyToWorkItemId', CASE relation.value ->> 'RelationType'
                        WHEN 'Blocks' THEN relation.value ->> 'RelatedWorkItemId'
                        WHEN 'BlockedBy' THEN item.id
                        ELSE NULL
                    END,
                    'CreatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM work_items.work_items item
            CROSS JOIN LATERAL jsonb_array_elements(
                COALESCE(item.document -> 'Relations', '[]'::jsonb)) relation(value)
            WHERE item.document ->> 'ProjectId' IS NOT NULL
              AND item.document ->> 'ProjectId' <> ''
              AND jsonb_typeof(relation.value) = 'object'
              AND relation.value ->> 'RelatedWorkItemId' IS NOT NULL
              AND relation.value ->> 'RelatedWorkItemId' <> ''
              AND relation.value ->> 'RelationType' IN ('Blocks', 'BlockedBy', 'RelatesTo', 'Duplicates')
            ON CONFLICT (id) DO NOTHING;
            """;
        const string dropWorkItemRelationGraph = """
            DROP INDEX IF EXISTS work_items.ix_work_items_project_parent_archived;
            DROP TABLE IF EXISTS work_items.work_item_relation_edges;
            """;
        const string workItemCollaborationAndRecurrence = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_collaborations (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.work_item_event_activities (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.work_item_templates (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.work_item_recurrences (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.work_item_recurrence_occurrences (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_collaboration_owner
                ON work_items.work_item_collaborations (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'));
            CREATE INDEX IF NOT EXISTS ix_workitem_event_activity_owner_created
                ON work_items.work_item_event_activities (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC,
                    id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_templates_active_project_name_ci
                ON work_items.work_item_templates (
                    (document ->> 'ProjectId'),
                    lower(document ->> 'Name'))
                WHERE ((document ->> 'Archived')::boolean) IS FALSE;
            CREATE INDEX IF NOT EXISTS ix_workitem_templates_project_archived_name
                ON work_items.work_item_templates (
                    (document ->> 'ProjectId'),
                    ((document ->> 'Archived')::boolean),
                    (document ->> 'Name'),
                    id);
            CREATE INDEX IF NOT EXISTS ix_workitem_recurrences_due
                ON work_items.work_item_recurrences (
                    ((document ->> 'Active')::boolean),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'NextRunAtUtc'),
                    id);
            CREATE INDEX IF NOT EXISTS ix_workitem_recurrences_project_archived_created
                ON work_items.work_item_recurrences (
                    (document ->> 'ProjectId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC,
                    id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_recurrence_occurrence_schedule
                ON work_items.work_item_recurrence_occurrences (
                    (document ->> 'RecurrenceId'),
                    public.zumbo_parse_timestamptz(document ->> 'ScheduledForUtc'));
            CREATE INDEX IF NOT EXISTS ix_workitem_recurrence_occurrence_status_schedule
                ON work_items.work_item_recurrence_occurrences (
                    (document ->> 'RecurrenceId'),
                    (document ->> 'Status'),
                    public.zumbo_parse_timestamptz(document ->> 'ScheduledForUtc') DESC,
                    id);
            """;
        const string dropWorkItemCollaborationAndRecurrence = """
            DROP TABLE IF EXISTS work_items.work_item_recurrence_occurrences;
            DROP TABLE IF EXISTS work_items.work_item_recurrences;
            DROP TABLE IF EXISTS work_items.work_item_templates;
            DROP TABLE IF EXISTS work_items.work_item_event_activities;
            DROP TABLE IF EXISTS work_items.work_item_collaborations;
            """;
        const string workItemBulkJobs = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_bulk_jobs (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.work_item_bulk_job_items (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_bulk_jobs_idempotency
                ON work_items.work_item_bulk_jobs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'RequestedByUserId'),
                    (document ->> 'IdempotencyKeyHash'));
            CREATE INDEX IF NOT EXISTS ix_workitem_bulk_jobs_owner_created
                ON work_items.work_item_bulk_jobs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'RequestedByUserId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_workitem_bulk_jobs_state_updated
                ON work_items.work_item_bulk_jobs (
                    (document ->> 'State'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt'),
                    id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_bulk_job_items_order
                ON work_items.work_item_bulk_job_items (
                    (document ->> 'JobId'),
                    ((document ->> 'ItemIndex')::integer));
            CREATE INDEX IF NOT EXISTS ix_workitem_bulk_job_items_state_order
                ON work_items.work_item_bulk_job_items (
                    (document ->> 'JobId'),
                    (document ->> 'State'),
                    ((document ->> 'ItemIndex')::integer),
                    id);
            """;
        const string dropWorkItemBulkJobs = """
            DROP TABLE IF EXISTS work_items.work_item_bulk_job_items;
            DROP TABLE IF EXISTS work_items.work_item_bulk_jobs;
            """;
        const string auditTenantIndexes = """
            CREATE INDEX IF NOT EXISTS ix_audit_logs_organization_created
                ON audit.audit_logs ((document ->> 'OrganizationId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC, id);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_organization_entity_created
                ON audit.audit_logs ((document ->> 'OrganizationId'), (document ->> 'EntityType'), (document ->> 'EntityId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC, id);
            CREATE INDEX IF NOT EXISTS ix_audit_logs_organization_actor_created
                ON audit.audit_logs ((document ->> 'OrganizationId'), (document ->> 'ActorUserId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC, id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_logs_organization_chain_sequence
                ON audit.audit_logs ((document ->> 'OrganizationId'), ((document ->> 'ChainSequence')::bigint))
                WHERE (document ->> 'ChainSequence')::bigint > 0;
            """;
        const string dropAuditTenantIndexes = """
            DROP INDEX IF EXISTS audit.ux_audit_logs_organization_chain_sequence;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_actor_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_entity_created;
            DROP INDEX IF EXISTS audit.ix_audit_logs_organization_created;
            """;
        const string notificationDeliveryIndexes = """
            DROP INDEX IF EXISTS notifications.ux_notifications_deduplication_key;
            CREATE UNIQUE INDEX ux_notifications_deduplication_key
                ON notifications.notifications ((document ->> 'OrganizationId'), (document ->> 'DeduplicationKey'))
                WHERE document ->> 'DeduplicationKey' IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_notifications_email_status_next_attempt
                ON notifications.notifications (
                    (document ->> 'EmailStatus'),
                    public.zumbo_parse_timestamptz(document ->> 'EmailNextAttemptAt'),
                    public.zumbo_parse_timestamptz(document ->> 'EmailLeaseUntil'),
                    (document ->> 'OrganizationId'),
                    id);
            """;
        const string dropNotificationDeliveryIndexes = """
            DROP INDEX IF EXISTS notifications.ix_notifications_email_status_next_attempt;
            """;
        const string workItemReportingIndexes = """
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_id
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_created
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_completed
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CompletedAt']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_assignee
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['AssigneeUserId']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_items_project_archived_team_created
                ON work_items.work_items (
                    (document #>> ARRAY['ProjectId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['TeamId']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['CreatedAt']),
                    id);
            """;
        const string dropWorkItemReportingIndexes = """
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_team_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_assignee;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_completed;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_id;
            """;
        const string workItemReportActivityIndexes = """
            CREATE INDEX IF NOT EXISTS ix_work_item_work_logs_project_cursor
                ON work_items.work_item_work_logs (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['ProjectId']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_work_item_timeline_project_cursor
                ON work_items.work_item_timeline (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['ProjectId']),
                    id);
            """;
        const string dropWorkItemReportActivityIndexes = """
            DROP INDEX IF EXISTS work_items.ix_work_item_timeline_project_cursor;
            DROP INDEX IF EXISTS work_items.ix_work_item_work_logs_project_cursor;
            """;
        const string privacyWorkflows = """
            CREATE TABLE IF NOT EXISTS identity.privacy_workflows (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_owner_state
                ON identity.privacy_workflows (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['RequestedByUserId']),
                    (document #>> ARRAY['State']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_retention
                ON identity.privacy_workflows (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            """;
        const string dropPrivacyWorkflows = """
            DROP TABLE IF EXISTS identity.privacy_workflows;
            """;
        const string privacyWorkflowUtcIndex = """
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_retention_utc
                ON identity.privacy_workflows (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            """;
        const string dropPrivacyWorkflowUtcIndex = """
            DROP INDEX IF EXISTS identity.ix_privacy_workflows_retention_utc;
            """;
        const string webhooks = """
            CREATE TABLE IF NOT EXISTS work_items.webhook_subscriptions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_webhook_subscriptions_tenant_active
                ON work_items.webhook_subscriptions (
                    (document #>> ARRAY['OrganizationId']),
                    ((document #>> ARRAY['IsActive'])::boolean),
                    id);
            CREATE TABLE IF NOT EXISTS work_items.webhook_deliveries (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_claim
                ON work_items.webhook_deliveries (
                    (document #>> ARRAY['Status']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['NextAttemptAtUtc']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['LeaseUntilUtc']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_tenant_subscription
                ON work_items.webhook_deliveries (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['SubscriptionId']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_tenant_status
                ON work_items.webhook_deliveries (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['Status']),
                    id);
            """;
        const string dropWebhooks = """
            DROP TABLE IF EXISTS work_items.webhook_deliveries;
            DROP TABLE IF EXISTS work_items.webhook_subscriptions;
            """;
        const string intakeForms = """
            CREATE TABLE IF NOT EXISTS work_items.intake_forms (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_forms_public_id
                ON work_items.intake_forms ((document ->> 'PublicId'));
            CREATE INDEX IF NOT EXISTS ix_intake_forms_tenant_project_state
                ON work_items.intake_forms (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'State'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);

            CREATE TABLE IF NOT EXISTS work_items.intake_form_versions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_form_versions_number
                ON work_items.intake_form_versions (
                    (document ->> 'FormId'),
                    ((document ->> 'DefinitionVersion')::integer));

            CREATE TABLE IF NOT EXISTS work_items.intake_submissions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_submissions_idempotency
                ON work_items.intake_submissions (
                    (document ->> 'OrganizationId'),
                    (document ->> 'FormId'),
                    (document ->> 'SubmittedByUserId'),
                    (document ->> 'IdempotencyKeyHash'));
            CREATE INDEX IF NOT EXISTS ix_intake_submissions_triage
                ON work_items.intake_submissions (
                    (document ->> 'OrganizationId'),
                    (document ->> 'FormId'),
                    (document ->> 'State'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC,
                    id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_intake_submissions_work_item
                ON work_items.intake_submissions ((document ->> 'WorkItemId'));
            """;
        const string dropIntakeForms = """
            DROP TABLE IF EXISTS work_items.intake_submissions;
            DROP TABLE IF EXISTS work_items.intake_form_versions;
            DROP TABLE IF EXISTS work_items.intake_forms;
            """;
        const string automationRules = """
            CREATE TABLE IF NOT EXISTS workflows.automation_rules (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_automation_rules_tenant_project_state
                ON workflows.automation_rules (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_automation_rules_schedule
                ON workflows.automation_rules (
                    ((document ->> 'Active')::boolean),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'NextRunAtUtc'),
                    id);
            """;
        const string dropAutomationRules = """
            DROP TABLE IF EXISTS workflows.automation_rules;
            """;
        const string automationRuns = """
            CREATE TABLE IF NOT EXISTS workflows.automation_runs (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_automation_runs_tenant_project_created
                ON workflows.automation_runs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAtUtc') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_automation_runs_rule_created
                ON workflows.automation_runs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'RuleId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAtUtc') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_automation_runs_retry
                ON workflows.automation_runs (
                    (document ->> 'Status'),
                    public.zumbo_parse_timestamptz(document ->> 'NextAttemptAtUtc'),
                    id);
            """;
        const string dropAutomationRuns = """
            DROP TABLE IF EXISTS workflows.automation_runs;
            """;
        const string dashboards = """
            CREATE TABLE IF NOT EXISTS work_items.dashboards (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_dashboards_tenant_owner_state
                ON work_items.dashboards (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_dashboards_tenant_viewers
                ON work_items.dashboards
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_dashboards_tenant_projects
                ON work_items.dashboards
                USING gin ((document -> 'ProjectIds'));
            """;
        const string dropDashboards = """
            DROP TABLE IF EXISTS work_items.dashboards;
            """;
        const string portfolios = """
            CREATE TABLE IF NOT EXISTS projects.portfolios (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_portfolios_tenant_owner_state
                ON projects.portfolios (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_portfolios_tenant_viewers
                ON projects.portfolios
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_portfolios_tenant_initiatives
                ON projects.portfolios
                USING gin ((document -> 'Initiatives'));
            """;
        const string dropPortfolios = """
            DROP TABLE IF EXISTS projects.portfolios;
            """;
        const string goals = """
            CREATE TABLE IF NOT EXISTS projects.goals (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_goals_tenant_owner_state
                ON projects.goals (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_goals_tenant_viewers
                ON projects.goals
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_goals_tenant_key_results
                ON projects.goals
                USING gin ((document -> 'KeyResults'));
            """;
        const string dropGoals = """
            DROP TABLE IF EXISTS projects.goals;
            """;
        const string capacityPlans = """
            CREATE TABLE IF NOT EXISTS work_items.capacity_plans (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_capacity_plans_tenant_owner_state
                ON work_items.capacity_plans (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_capacity_plans_tenant_viewers
                ON work_items.capacity_plans
                USING gin ((document -> 'ViewerUserIds'));
            CREATE INDEX IF NOT EXISTS ix_capacity_plans_tenant_projects
                ON work_items.capacity_plans
                USING gin ((document -> 'ProjectIds'));
            """;
        const string dropCapacityPlans = """
            DROP TABLE IF EXISTS work_items.capacity_plans;
            """;
        const string knowledgeDocuments = """
            CREATE TABLE IF NOT EXISTS projects.knowledge_documents (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_tenant_scope_state
                ON projects.knowledge_documents (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ScopeType'),
                    (document ->> 'ScopeId'),
                    ((document ->> 'Archived')::boolean),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_knowledge_tenant_owner_state
                ON projects.knowledge_documents (
                    (document ->> 'OrganizationId'),
                    (document ->> 'OwnerUserId'),
                    ((document ->> 'Archived')::boolean),
                    id);
            CREATE INDEX IF NOT EXISTS ix_knowledge_tenant_tags
                ON projects.knowledge_documents
                USING gin ((document -> 'Tags'));
            """;
        const string dropKnowledgeDocuments = """
            DROP TABLE IF EXISTS projects.knowledge_documents;
            """;
        const string developmentIntegrations = """
            CREATE TABLE IF NOT EXISTS work_items.development_connections (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_development_connections_tenant_updated
                ON work_items.development_connections (
                    (document ->> 'OrganizationId'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAtUtc') DESC,
                    id);

            CREATE TABLE IF NOT EXISTS work_items.development_repository_mappings (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_development_mappings_tenant_connection_repository
                ON work_items.development_repository_mappings (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ConnectionId'),
                    (document ->> 'ExternalRepositoryId'));
            CREATE INDEX IF NOT EXISTS ix_development_mappings_tenant_project_active
                ON work_items.development_repository_mappings (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    ((document ->> 'IsActive')::boolean),
                    id);

            CREATE TABLE IF NOT EXISTS work_items.work_item_development_links (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_development_links_tenant_work_item_updated
                ON work_items.work_item_development_links (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAtUtc') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_development_links_tenant_mapping_commit
                ON work_items.work_item_development_links (
                    (document ->> 'OrganizationId'),
                    (document ->> 'MappingId'),
                    (document ->> 'CommitSha'),
                    (document ->> 'ExternalId'),
                    id);

            CREATE TABLE IF NOT EXISTS work_items.development_webhook_receipts (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_development_receipts_connection_expiry
                ON work_items.development_webhook_receipts (
                    (document ->> 'ConnectionId'),
                    public.zumbo_parse_timestamptz(document ->> 'ExpiresAtUtc'),
                    id);
            """;
        const string dropDevelopmentIntegrations = """
            DROP TABLE IF EXISTS work_items.development_webhook_receipts;
            DROP TABLE IF EXISTS work_items.work_item_development_links;
            DROP TABLE IF EXISTS work_items.development_repository_mappings;
            DROP TABLE IF EXISTS work_items.development_connections;
            """;
        const string highCardinalityIndexes = """
            CREATE INDEX IF NOT EXISTS ix_projects_organization_archived_key_cursor
                ON projects.projects (
                    (document #>> ARRAY['OrganizationId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['Key']),
                    id COLLATE "C");
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_owner_last_seen
                ON identity.refresh_sessions (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    public.zumbo_parse_timestamptz(
                        document #>> ARRAY['LastSeenAt']) DESC NULLS LAST,
                    id COLLATE "C");
            """;
        const string dropHighCardinalityIndexes = """
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_owner_last_seen;
            DROP INDEX IF EXISTS projects.ix_projects_organization_archived_key_cursor;
            """;

        return
        [
            Migration.Create(1, "create_module_schemas", createSchemas, dropSchemas),
            Migration.Create(2, "create_document_tables", createTables, dropTables),
            Migration.Create(3, "create_jsonb_indexes", createIndexes, dropIndexes),
            Migration.Create(4, "create_access_pattern_indexes", accessIndexes, dropAccessIndexes),
            Migration.Create(5, "create_durable_messaging", durableMessaging, dropDurableMessaging),
            Migration.Create(6, "create_durable_consumer_deduplication", durableConsumerDeduplication, dropDurableConsumerDeduplication),
            Migration.Create(7, "create_identity_credential_stores", identityCredentialStores, dropIdentityCredentialStores),
            Migration.Create(8, "backfill_api_key_versions", apiKeyVersionBackfill, dropApiKeyVersionBackfill),
            Migration.Create(9, "create_api_key_expiry_index", apiKeyExpiryIndex, dropApiKeyExpiryIndex),
            Migration.Create(10, "backfill_api_key_utc_fields", apiKeyUtcFieldBackfill, dropApiKeyUtcFieldBackfill),
            Migration.Create(11, "create_work_item_activity_stores", workItemActivityStores, dropWorkItemActivityStores),
            Migration.Create(12, "backfill_organization_versions", organizationVersionBackfill, dropOrganizationVersionBackfill),
            Migration.Create(13, "expire_legacy_team_invites", expireLegacyTeamInvites, dropLegacyTeamInviteMarker),
            Migration.Create(14, "backfill_project_lifecycle", projectLifecycleBackfill, dropProjectLifecycleMarker),
            Migration.Create(15, "workflow_lifecycle_and_wip_projection", workflowLifecycleAndWipProjection, dropWorkflowLifecycleMarker),
            Migration.Create(16, "sprint_lifecycle_and_snapshots", sprintLifecycle, dropSprintLifecycleMarker),
            Migration.Create(17, "work_item_type_schemas", workItemTypeSchemas, dropWorkItemTypeSchemas),
            Migration.Create(18, "work_item_relation_graph", workItemRelationGraph, dropWorkItemRelationGraph),
            Migration.Create(19, "work_item_collaboration_and_recurrence", workItemCollaborationAndRecurrence, dropWorkItemCollaborationAndRecurrence),
            Migration.Create(20, "work_item_bulk_jobs", workItemBulkJobs, dropWorkItemBulkJobs),
            Migration.Create(21, "audit_tenant_indexes", auditTenantIndexes, dropAuditTenantIndexes),
            Migration.Create(22, "notification_delivery_indexes", notificationDeliveryIndexes, dropNotificationDeliveryIndexes),
            Migration.Create(23, "work_item_reporting_indexes", workItemReportingIndexes, dropWorkItemReportingIndexes),
            Migration.Create(24, "work_item_report_activity_indexes", workItemReportActivityIndexes, dropWorkItemReportActivityIndexes),
            Migration.Create(25, "privacy_workflows", privacyWorkflows, dropPrivacyWorkflows),
            Migration.Create(26, "privacy_workflow_utc_index", privacyWorkflowUtcIndex, dropPrivacyWorkflowUtcIndex),
            Migration.Create(27, "webhook_subscriptions_and_deliveries", webhooks, dropWebhooks),
            Migration.Create(28, "intake_forms_and_submissions", intakeForms, dropIntakeForms),
            Migration.Create(29, "automation_rules", automationRules, dropAutomationRules),
            Migration.Create(30, "automation_runs", automationRuns, dropAutomationRuns),
            Migration.Create(31, "dashboards", dashboards, dropDashboards),
            Migration.Create(32, "portfolios", portfolios, dropPortfolios),
            Migration.Create(33, "goals", goals, dropGoals),
            Migration.Create(34, "capacity_plans", capacityPlans, dropCapacityPlans),
            Migration.Create(35, "knowledge_documents", knowledgeDocuments, dropKnowledgeDocuments),
            Migration.Create(36, "development_integrations", developmentIntegrations, dropDevelopmentIntegrations),
            Migration.Create(37, "high_cardinality_indexes", highCardinalityIndexes, dropHighCardinalityIndexes)
        ];
    }

    private static string CreateTableSql(PostgreSqlDocumentStorage storage) => $"""
        CREATE TABLE IF NOT EXISTS {Qualified(storage)} (
            id text PRIMARY KEY,
            version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
            document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
            created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
            updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
            CHECK (document ->> 'Id' = id),
            CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
        );
        """;

    private static string Qualified(PostgreSqlDocumentStorage storage) =>
        $"{SqlIdentifier.Quote(storage.Schema)}.{SqlIdentifier.Quote(storage.Table)}";

    private static string IndexName(PostgreSqlDocumentStorage storage)
    {
        var value = $"ix_{storage.Table}_document_gin";
        return value.Length <= 63 ? value : value[..63];
    }

    private async Task EnsureLedgerAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS public.zumbo_schema_migrations (
                version bigint PRIMARY KEY,
                name text NOT NULL,
                checksum text NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT transaction_timestamp()
            );
            """;
        await using var command = CreateCommand(connection, transaction: null, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<LedgerRow>> ReadAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool ledgerMayBeMissing,
        CancellationToken cancellationToken)
    {
        const string sql = $"SELECT version, name, checksum FROM {Ledger} ORDER BY version;";
        await using var command = CreateCommand(connection, transaction, sql);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<LedgerRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new LedgerRow(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }

            return rows;
        }
        catch (PostgresException exception) when (ledgerMayBeMissing && exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return [];
        }
    }

    private static void ValidateLedger(IReadOnlyList<Migration> migrations, IReadOnlyList<LedgerRow> applied)
    {
        foreach (var row in applied)
        {
            var migration = migrations.SingleOrDefault(item => item.Version == row.Version)
                ?? throw new InvalidOperationException($"Database contains unknown migration {row.Version}.");
            if (!string.Equals(migration.Name, row.Name, StringComparison.Ordinal)
                || !string.Equals(migration.Checksum, row.Checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Migration {row.Version} does not match its recorded checksum.");
            }
        }
    }

    private async Task AcquireLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_xact_lock(hashtext(@name));";
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("name", LockName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Migration migration,
        CancellationToken cancellationToken)
    {
        const string sql = $"INSERT INTO {Ledger} (version, name, checksum) VALUES (@version, @name, @checksum);";
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("version", migration.Version);
        command.Parameters.AddWithValue("name", migration.Name);
        command.Parameters.AddWithValue("checksum", migration.Checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task DeleteLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long version,
        CancellationToken cancellationToken)
    {
        const string sql = $"DELETE FROM {Ledger} WHERE version = @version;";
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.Transaction = transaction;
        return command;
    }

    private sealed record LedgerRow(long Version, string Name, string Checksum);

    private static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record Migration(long Version, string Name, string UpSql, string DownSql, string Checksum)
    {
        public PostgreSqlMigrationInfo Info => new(Version, Name, Checksum);

        public static Migration Create(long version, string name, string upSql, string downSql)
        {
            var content = $"{version}\n{name}\n{upSql}\n-- DOWN\n{downSql}";
            var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            return new Migration(version, name, upSql, downSql, checksum);
        }
    }
}
