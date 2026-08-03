using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string workItemCollaborationAndRecurrence = """
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
}
