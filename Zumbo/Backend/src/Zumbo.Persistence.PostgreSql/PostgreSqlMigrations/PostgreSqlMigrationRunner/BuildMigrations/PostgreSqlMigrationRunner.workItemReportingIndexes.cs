using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string workItemReportingIndexes = """
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
}
