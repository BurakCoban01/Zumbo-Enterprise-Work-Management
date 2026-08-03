using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropIdentityCredentialStores = """
            CREATE INDEX IF NOT EXISTS ix_users_refresh_token_hash
                ON identity.users USING GIN ((document #> ARRAY['RefreshTokens']) jsonb_path_ops);
            DROP INDEX IF EXISTS identity.ix_api_keys_owner_revoked_expires;
            DROP INDEX IF EXISTS identity.ix_api_keys_owner_created;
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_retain_until;
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_owner_active;
            DROP INDEX IF EXISTS identity.ux_refresh_sessions_token_hash;
            DROP INDEX IF EXISTS identity.ix_refresh_sessions_document_gin;
            """;
}
