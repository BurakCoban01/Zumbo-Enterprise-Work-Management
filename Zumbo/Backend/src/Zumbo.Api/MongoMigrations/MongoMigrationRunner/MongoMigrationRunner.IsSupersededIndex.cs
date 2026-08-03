public sealed partial class MongoMigrationRunner
{
    private static bool IsSupersededIndex(string migrationId, string indexName) =>
        migrationId switch
        {
            IdentityCredentialIndexMigrationId => indexName is
                "ix_refreshsessions_owner_active" or "ix_apikeys_owner_revoked_expires",
            IndexMigrationId => indexName is
                "ux_notifications_deduplication_key" or "ix_notifications_email_status_next_attempt",
            _ => false
        };
}
