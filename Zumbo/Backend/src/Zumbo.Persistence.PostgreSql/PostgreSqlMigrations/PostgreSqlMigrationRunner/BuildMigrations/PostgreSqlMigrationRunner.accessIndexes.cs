using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

        private const string accessIndexes = """
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
}
