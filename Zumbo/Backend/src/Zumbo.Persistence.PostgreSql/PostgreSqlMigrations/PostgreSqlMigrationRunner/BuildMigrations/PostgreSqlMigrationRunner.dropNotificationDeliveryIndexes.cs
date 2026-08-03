using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropNotificationDeliveryIndexes = """
            DROP INDEX IF EXISTS notifications.ix_notifications_email_status_next_attempt;
            """;
}
