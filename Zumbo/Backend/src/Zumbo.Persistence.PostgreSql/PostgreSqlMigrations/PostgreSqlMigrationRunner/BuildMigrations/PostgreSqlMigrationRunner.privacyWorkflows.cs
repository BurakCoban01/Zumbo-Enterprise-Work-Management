using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string privacyWorkflows = """
            CREATE TABLE IF NOT EXISTS identity.privacy_workflows (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_owner_state
                ON identity.privacy_workflows (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['RequestedByUserId']),
                    (document #>> ARRAY['State']),
                    id);
            CREATE INDEX IF NOT EXISTS ix_privacy_workflows_retention
                ON identity.privacy_workflows (
                    public.zumbo_parse_timestamptz(document #>> ARRAY['ExpiresAtUtc']),
                    id);
            """;
}
