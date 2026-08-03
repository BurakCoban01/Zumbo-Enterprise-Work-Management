using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemReportingIndexes = """
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_team_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_assignee;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_completed;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_created;
            DROP INDEX IF EXISTS work_items.ix_work_items_project_archived_id;
            """;
}
