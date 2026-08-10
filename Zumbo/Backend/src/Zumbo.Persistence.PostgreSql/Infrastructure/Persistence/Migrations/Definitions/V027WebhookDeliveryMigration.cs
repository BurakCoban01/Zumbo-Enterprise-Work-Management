namespace Zumbo.Persistence.PostgreSql;

internal static class V027WebhookDeliveryMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS work_items.webhook_subscriptions (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_webhook_subscriptions_tenant_active
                ON work_items.webhook_subscriptions (
                    (document #>> ARRAY['OrganizationId']),
                    ((document #>> ARRAY['IsActive'])::boolean),
                    id);
            CREATE TABLE IF NOT EXISTS work_items.webhook_deliveries (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_claim
                ON work_items.webhook_deliveries (
                    (document #>> ARRAY['Status']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['NextAttemptAtUtc']),
                    public.zumbo_parse_timestamptz(document #>> ARRAY['LeaseUntilUtc']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_tenant_subscription
                ON work_items.webhook_deliveries (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['SubscriptionId']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_webhook_deliveries_tenant_status
                ON work_items.webhook_deliveries (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['Status']),
                    id);
            """;

        private const string DownSql = """
            DROP TABLE IF EXISTS work_items.webhook_deliveries;
            DROP TABLE IF EXISTS work_items.webhook_subscriptions;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        27,
        "webhook_subscriptions_and_deliveries",
        UpSql,
        DownSql);
}
