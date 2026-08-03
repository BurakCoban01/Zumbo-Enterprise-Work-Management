using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropDevelopmentIntegrations = """
            DROP TABLE IF EXISTS work_items.development_webhook_receipts;
            DROP TABLE IF EXISTS work_items.work_item_development_links;
            DROP TABLE IF EXISTS work_items.development_repository_mappings;
            DROP TABLE IF EXISTS work_items.development_connections;
            """;
}
