using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWebhooks = """
            DROP TABLE IF EXISTS work_items.webhook_deliveries;
            DROP TABLE IF EXISTS work_items.webhook_subscriptions;
            """;
}
