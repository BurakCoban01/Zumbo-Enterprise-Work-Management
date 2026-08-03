using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string highCardinalityIndexes = """
            CREATE INDEX IF NOT EXISTS ix_projects_organization_archived_key_cursor
                ON projects.projects (
                    (document #>> ARRAY['OrganizationId']),
                    ((document #>> ARRAY['Archived'])::boolean),
                    (document #>> ARRAY['Key']),
                    id COLLATE "C");
            CREATE INDEX IF NOT EXISTS ix_refresh_sessions_owner_last_seen
                ON identity.refresh_sessions (
                    (document #>> ARRAY['OrganizationId']),
                    (document #>> ARRAY['UserId']),
                    public.zumbo_parse_timestamptz(
                        document #>> ARRAY['LastSeenAt']) DESC NULLS LAST,
                    id COLLATE "C");
            """;
}
