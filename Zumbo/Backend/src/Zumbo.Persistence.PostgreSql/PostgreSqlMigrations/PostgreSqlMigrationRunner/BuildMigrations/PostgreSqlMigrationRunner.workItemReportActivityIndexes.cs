using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string workItemReportActivityIndexes = """
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
}
