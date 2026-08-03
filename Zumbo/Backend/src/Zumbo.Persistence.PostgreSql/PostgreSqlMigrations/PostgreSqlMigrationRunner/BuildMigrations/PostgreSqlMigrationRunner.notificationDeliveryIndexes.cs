using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string notificationDeliveryIndexes = """
            DROP INDEX IF EXISTS notifications.ux_notifications_deduplication_key;
            CREATE UNIQUE INDEX ux_notifications_deduplication_key
                ON notifications.notifications ((document ->> 'OrganizationId'), (document ->> 'DeduplicationKey'))
                WHERE document ->> 'DeduplicationKey' IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_notifications_email_status_next_attempt
                ON notifications.notifications (
                    (document ->> 'EmailStatus'),
                    public.zumbo_parse_timestamptz(document ->> 'EmailNextAttemptAt'),
                    public.zumbo_parse_timestamptz(document ->> 'EmailLeaseUntil'),
                    (document ->> 'OrganizationId'),
                    id);
            """;
}
