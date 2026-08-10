namespace Zumbo.Persistence.PostgreSql;

internal static class V014BackfillProjectLifecycleMigration
{
        private const string UpSql = """
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

        private const string DownSql = """
            UPDATE projects.projects
            SET version = version + 1,
                document = (document - 'ProjectLifecycleMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'ProjectLifecycleMigratedBy' = '20260720_014';
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        14,
        "backfill_project_lifecycle",
        UpSql,
        DownSql);
}
