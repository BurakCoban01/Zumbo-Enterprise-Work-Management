using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string workItemTypeSchemas = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_type_schemas (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_type_schemas_project
                ON work_items.work_item_type_schemas ((document ->> 'ProjectId'));
            CREATE INDEX IF NOT EXISTS ix_workitems_project_archived_type_rank
                ON work_items.work_items (
                    (document ->> 'ProjectId'),
                    (COALESCE((document ->> 'Archived')::boolean, false)),
                    (document ->> 'Type'),
                    (COALESCE((document ->> 'Rank')::bigint, 0)),
                    id);
            CREATE INDEX IF NOT EXISTS ix_workitems_custom_fields_gin
                ON work_items.work_items USING gin ((document -> 'CustomFields') jsonb_path_ops);

            WITH project_ids AS (
                SELECT DISTINCT document ->> 'ProjectId' AS project_id
                FROM work_items.work_items
                WHERE document ->> 'ProjectId' IS NOT NULL
                  AND document ->> 'ProjectId' <> ''
            )
            INSERT INTO work_items.work_item_type_schemas (id, version, document)
            SELECT
                project_id,
                0,
                jsonb_build_object(
                    'Id', project_id,
                    'ProjectId', project_id,
                    'SchemaVersion', 1,
                    'IssueTypes', jsonb_build_array(
                        jsonb_build_object('Key', 'Epic', 'Name', 'Epic', 'Description', '', 'HierarchyLevel', 'Epic', 'Active', true, 'Position', 0),
                        jsonb_build_object('Key', 'Story', 'Name', 'Story', 'Description', '', 'HierarchyLevel', 'Standard', 'Active', true, 'Position', 10),
                        jsonb_build_object('Key', 'Task', 'Name', 'Task', 'Description', '', 'HierarchyLevel', 'Standard', 'Active', true, 'Position', 20),
                        jsonb_build_object('Key', 'Bug', 'Name', 'Bug', 'Description', '', 'HierarchyLevel', 'Standard', 'Active', true, 'Position', 30),
                        jsonb_build_object('Key', 'Subtask', 'Name', 'Subtask', 'Description', '', 'HierarchyLevel', 'Subtask', 'Active', true, 'Position', 40)),
                    'CustomFields', '[]'::jsonb,
                    'Layouts', jsonb_build_array(
                        jsonb_build_object('IssueTypeKey', 'Epic', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Story', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Task', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Bug', 'FieldKeys', '[]'::jsonb),
                        jsonb_build_object('IssueTypeKey', 'Subtask', 'FieldKeys', '[]'::jsonb)),
                    'CreatedAt', transaction_timestamp(),
                    'UpdatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM project_ids
            ON CONFLICT (id) DO NOTHING;

            UPDATE work_items.work_items
            SET version = version + 1,
                document = document || jsonb_build_object(
                    'IssueTypeSchemaVersion', COALESCE((document ->> 'IssueTypeSchemaVersion')::integer, 1),
                    'CustomFields', COALESCE(document -> 'CustomFields', '[]'::jsonb),
                    'WorkItemTypeSchemaMigratedBy', '20260720_017',
                    'Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE NOT document ? 'IssueTypeSchemaVersion'
               OR NOT document ? 'CustomFields';
            """;
}
