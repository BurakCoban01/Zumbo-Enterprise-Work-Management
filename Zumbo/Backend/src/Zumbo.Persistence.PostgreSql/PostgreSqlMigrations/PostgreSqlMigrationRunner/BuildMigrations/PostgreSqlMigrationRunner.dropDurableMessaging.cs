using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropDurableMessaging = """
            DROP TABLE IF EXISTS messaging.inbox_messages;
            DROP TABLE IF EXISTS messaging.outbox_messages;
            DROP SCHEMA IF EXISTS messaging;
            """;
}
