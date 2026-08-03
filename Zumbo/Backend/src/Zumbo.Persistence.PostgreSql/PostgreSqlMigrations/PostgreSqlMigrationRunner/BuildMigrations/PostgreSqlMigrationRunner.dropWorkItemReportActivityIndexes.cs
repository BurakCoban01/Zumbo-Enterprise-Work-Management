using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemReportActivityIndexes = """
            DROP INDEX IF EXISTS work_items.ix_work_item_timeline_project_cursor;
            DROP INDEX IF EXISTS work_items.ix_work_item_work_logs_project_cursor;
            """;
}
