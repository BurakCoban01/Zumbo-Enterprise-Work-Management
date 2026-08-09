namespace Zumbo.Persistence.PostgreSql;

internal static class V012BackfillOrganizationVersionsMigration
{
        private const string UpSql = """
            UPDATE organizations.organizations
            SET version = 1,
                document = jsonb_set(
                    jsonb_set(document, ARRAY['Version'], '1'::jsonb, true),
                    ARRAY['Status'],
                    COALESCE(document -> 'Status', '"Active"'::jsonb),
                    true)
                    || jsonb_build_object('OrganizationVersionMigratedBy', '20260720_012'),
                updated_at = transaction_timestamp()
            WHERE version = 0;
            """;

        private const string DownSql = """
            UPDATE organizations.organizations
            SET version = 0,
                document = document - 'Version' - 'OrganizationVersionMigratedBy',
                updated_at = transaction_timestamp()
            WHERE version = 1
              AND document ->> 'OrganizationVersionMigratedBy' = '20260720_012';
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        12,
        "backfill_organization_versions",
        UpSql,
        DownSql);
}
