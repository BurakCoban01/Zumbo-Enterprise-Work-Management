namespace Zumbo.Persistence.PostgreSql;

internal static class V022NotificationDeliveryIndexesMigration
{
        private const string UpSql = """
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

        private const string DownSql = """
            DROP INDEX IF EXISTS notifications.ix_notifications_email_status_next_attempt;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        22,
        "notification_delivery_indexes",
        UpSql,
        DownSql);
}
