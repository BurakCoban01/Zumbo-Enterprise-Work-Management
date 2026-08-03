using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string workflowLifecycleAndWipProjection = """
            CREATE TABLE IF NOT EXISTS work_items.board_column_wip_projections (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE INDEX IF NOT EXISTS ix_board_column_wip_projection_lookup
                ON work_items.board_column_wip_projections
                ((document ->> 'ProjectId'), (document ->> 'BoardId'), (document ->> 'ColumnId'));

            INSERT INTO work_items.board_column_wip_projections (id, version, document)
            SELECT
                (wi.document ->> 'BoardId') || ':' || (wi.document ->> 'ColumnId'),
                0,
                jsonb_build_object(
                    'Id', (wi.document ->> 'BoardId') || ':' || (wi.document ->> 'ColumnId'),
                    'ProjectId', wi.document ->> 'ProjectId',
                    'BoardId', wi.document ->> 'BoardId',
                    'ColumnId', wi.document ->> 'ColumnId',
                    'ActiveCount', count(*)::integer,
                    'UpdatedAt', transaction_timestamp(),
                    'Version', 0)
            FROM work_items.work_items wi
            WHERE COALESCE((wi.document ->> 'Archived')::boolean, false) = false
              AND COALESCE(wi.document ->> 'BoardId', '') <> ''
              AND COALESCE(wi.document ->> 'ColumnId', '') <> ''
            GROUP BY wi.document ->> 'ProjectId', wi.document ->> 'BoardId', wi.document ->> 'ColumnId'
            ON CONFLICT (id) DO NOTHING;

            WITH prepared AS (
                SELECT
                    workflow.id,
                    workflow.version,
                    workflow.document,
                    COALESCE((
                        SELECT status ->> 'Name'
                        FROM jsonb_array_elements(COALESCE(workflow.document -> 'Statuses', '[]'::jsonb)) status
                        WHERE status ->> 'Category' = 'Todo'
                        LIMIT 1), 'To Do') AS default_status,
                    COALESCE((
                        SELECT jsonb_agg(status ->> 'Name')
                        FROM jsonb_array_elements(COALESCE(workflow.document -> 'Statuses', '[]'::jsonb)) status),
                        '[]'::jsonb) AS status_names,
                    COALESCE((
                        SELECT jsonb_agg(status ->> 'Name')
                        FROM jsonb_array_elements(COALESCE(workflow.document -> 'Statuses', '[]'::jsonb)) status
                        WHERE status ->> 'Category' = 'Done'),
                        '[]'::jsonb) AS done_names
                FROM workflows.workflow_definitions workflow
                WHERE NOT workflow.document ? 'PublishedVersion'
                   OR NOT workflow.document ? 'IssueTypeSchemes'
                   OR NOT workflow.document ? 'Draft'
                   OR NOT workflow.document ? 'PublishedVersions'
            ), definitions AS (
                SELECT prepared.*,
                    jsonb_build_array(jsonb_build_object(
                        'IssueType', '*',
                        'DefaultStatus', prepared.default_status,
                        'Statuses', prepared.status_names,
                        'DoneStatuses', prepared.done_names)) AS schemes
                FROM prepared
            )
            UPDATE workflows.workflow_definitions workflow
            SET version = workflow.version + 1,
                document = workflow.document || jsonb_build_object(
                    'PublishedVersion', 1,
                    'IssueTypeSchemes', definitions.schemes,
                    'Draft', NULL,
                    'PublishedVersions', jsonb_build_array(jsonb_build_object(
                        'Number', 1,
                        'State', 'Published',
                        'Statuses', COALESCE(workflow.document -> 'Statuses', '[]'::jsonb),
                        'Transitions', COALESCE(workflow.document -> 'Transitions', '[]'::jsonb),
                        'IssueTypeSchemes', definitions.schemes,
                        'CreatedAt', workflow.document -> 'CreatedAt',
                        'PublishedAt', COALESCE(workflow.document -> 'UpdatedAt', workflow.document -> 'CreatedAt'))),
                    'WorkflowLifecycleMigratedBy', '20260720_015',
                    'Version', workflow.version + 1),
                updated_at = transaction_timestamp()
            FROM definitions
            WHERE workflow.id = definitions.id;
            """;
}
