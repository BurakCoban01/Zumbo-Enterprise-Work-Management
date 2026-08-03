using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemBulkJobs = """
            DROP TABLE IF EXISTS work_items.work_item_bulk_job_items;
            DROP TABLE IF EXISTS work_items.work_item_bulk_jobs;
            """;
}
