using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string projectLifecycleBackfill = """
            UPDATE projects.projects project
            SET version = GREATEST(project.version, 0) + 1,
                document = jsonb_build_object(
                        'Visibility', 'Internal',
                        'Archived', false,
                        'Members', '[]'::jsonb,
                        'TeamIds', '[]'::jsonb,
                        'Templates', '[]'::jsonb,
                        'Components', '[]'::jsonb,
                        'Versions', '[]'::jsonb,
                        'Releases', '[]'::jsonb,
                        'Milestones', '[]'::jsonb,
                        'ArchivedAt', NULL,
                        'RetainUntil', NULL)
                    || project.document
                    || jsonb_build_object(
                        'Version', GREATEST(project.version, 0) + 1,
                        'ProjectLifecycleMigratedBy', '20260720_014'),
                updated_at = transaction_timestamp()
            WHERE project.version <= 0
               OR NOT project.document ? 'Visibility'
               OR NOT project.document ? 'Templates'
               OR NOT project.document ? 'Components'
               OR NOT project.document ? 'Versions'
               OR NOT project.document ? 'Releases'
               OR NOT project.document ? 'Milestones'
               OR NOT project.document ? 'ArchivedAt'
               OR NOT project.document ? 'RetainUntil';
            """;
}
