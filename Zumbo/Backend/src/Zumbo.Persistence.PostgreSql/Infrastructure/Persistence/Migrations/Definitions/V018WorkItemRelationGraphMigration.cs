namespace Zumbo.Persistence.PostgreSql;

internal static class V018WorkItemRelationGraphMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_relation_edges (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_work_item_relation_edges_source
                ON work_items.work_item_relation_edges (
                    (document ->> 'ProjectId'),
                    (document ->> 'SourceWorkItemId'),
                    (document ->> 'TargetWorkItemId'),
                    (document ->> 'RelationType'));
            CREATE INDEX IF NOT EXISTS ix_work_item_relation_edges_dependency_from
                ON work_items.work_item_relation_edges (
                    (document ->> 'ProjectId'),
                    (document ->> 'DependencyFromWorkItemId'),
                    (document ->> 'DependencyToWorkItemId'))
                WHERE document ->> 'DependencyFromWorkItemId' IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_work_item_relation_edges_dependency_to
                ON work_items.work_item_relation_edges (
                    (document ->> 'ProjectId'),
                    (document ->> 'DependencyToWorkItemId'),
                    (document ->> 'DependencyFromWorkItemId'))
                WHERE document ->> 'DependencyToWorkItemId' IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_work_items_project_parent_archived
                ON work_items.work_items (
                    (document ->> 'ProjectId'),
                    (document ->> 'ParentId'),
                    ((document ->> 'Archived')::boolean),
                    id);

            INSERT INTO work_items.work_item_relation_edges (id, version, document)
            SELECT
                md5(
                    (item.document ->> 'ProjectId') || chr(10)
                    || item.id || chr(10)
                    || (relation.value ->> 'RelatedWorkItemId') || chr(10)
                    || (relation.value ->> 'RelationType')),
                0,
                jsonb_build_object(
                    'Id', md5(
                        (item.document ->> 'ProjectId') || chr(10)
                        || item.id || chr(10)
                        || (relation.value ->> 'RelatedWorkItemId') || chr(10)
                        || (relation.value ->> 'RelationType')),
                    'ProjectId', item.document ->> 'ProjectId',
                    'SourceWorkItemId', item.id,
                    'TargetWorkItemId', relation.value ->> 'RelatedWorkItemId',
                    'RelationType', relation.value ->> 'RelationType',
                    'DependencyFromWorkItemId', CASE relation.value ->> 'RelationType'
                        WHEN 'Blocks' THEN item.id
                        WHEN 'BlockedBy' THEN relation.value ->> 'RelatedWorkItemId'
                        ELSE NULL
                    END,
                    'DependencyToWorkItemId', CASE relation.value ->> 'RelationType'
                        WHEN 'Blocks' THEN relation.value ->> 'RelatedWorkItemId'
                        WHEN 'BlockedBy' THEN item.id
                        ELSE NULL
                    END,
                    'CreatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM work_items.work_items item
            CROSS JOIN LATERAL jsonb_array_elements(
                COALESCE(item.document -> 'Relations', '[]'::jsonb)) relation(value)
            WHERE item.document ->> 'ProjectId' IS NOT NULL
              AND item.document ->> 'ProjectId' <> ''
              AND jsonb_typeof(relation.value) = 'object'
              AND relation.value ->> 'RelatedWorkItemId' IS NOT NULL
              AND relation.value ->> 'RelatedWorkItemId' <> ''
              AND relation.value ->> 'RelationType' IN ('Blocks', 'BlockedBy', 'RelatesTo', 'Duplicates')
            ON CONFLICT (id) DO NOTHING;
            """;

        private const string DownSql = """
            DROP INDEX IF EXISTS work_items.ix_work_items_project_parent_archived;
            DROP TABLE IF EXISTS work_items.work_item_relation_edges;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        18,
        "work_item_relation_graph",
        UpSql,
        DownSql);
}
