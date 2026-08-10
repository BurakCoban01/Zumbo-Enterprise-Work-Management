namespace Zumbo.Persistence.PostgreSql;

internal static class V016SprintLifecycleAndSnapshotsMigration
{
        private const string UpSql = """
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

        private const string DownSql = """
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

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        16,
        "sprint_lifecycle_and_snapshots",
        UpSql,
        DownSql);
}
