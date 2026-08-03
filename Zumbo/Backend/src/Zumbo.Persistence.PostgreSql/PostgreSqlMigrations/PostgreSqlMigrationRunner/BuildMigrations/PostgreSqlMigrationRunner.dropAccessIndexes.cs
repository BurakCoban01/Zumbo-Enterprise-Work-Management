using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropAccessIndexes = """
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
}
