using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemRelationGraph = """
            DROP INDEX IF EXISTS work_items.ix_work_items_project_parent_archived;
            DROP TABLE IF EXISTS work_items.work_item_relation_edges;
            """;
}
