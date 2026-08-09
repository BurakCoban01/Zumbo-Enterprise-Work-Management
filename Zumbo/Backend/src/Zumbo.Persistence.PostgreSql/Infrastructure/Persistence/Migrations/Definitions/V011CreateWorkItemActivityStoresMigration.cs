namespace Zumbo.Persistence.PostgreSql;

internal static class V011CreateWorkItemActivityStoresMigration
{
        private const string UpSql = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_comments (
                id text PRIMARY KEY, version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version));
            CREATE TABLE IF NOT EXISTS work_items.work_item_comment_revisions
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_attachments
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_work_logs
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_approvals
                (LIKE work_items.work_item_comments INCLUDING ALL);
            CREATE TABLE IF NOT EXISTS work_items.work_item_timeline
                (LIKE work_items.work_item_comments INCLUDING ALL);

            CREATE INDEX IF NOT EXISTS ix_work_item_comments_owner_created
                ON work_items.work_item_comments (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_revisions_owner_comment_edited
                ON work_items.work_item_comment_revisions (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), (document ->> 'CommentId'),
                    public.zumbo_parse_timestamptz(document ->> 'EditedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_attachments_owner_created
                ON work_items.work_item_attachments (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_work_logs_owner_created
                ON work_items.work_item_work_logs (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_approvals_owner_requested
                ON work_items.work_item_approvals (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'RequestedAt'), id);
            CREATE INDEX IF NOT EXISTS ix_work_item_timeline_owner_changed
                ON work_items.work_item_timeline (
                    (document ->> 'OrganizationId'), (document ->> 'ProjectId'),
                    (document ->> 'WorkItemId'), public.zumbo_parse_timestamptz(document ->> 'ChangedAt'), id);

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM work_items.work_items wi
                    LEFT JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
                    WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
                      AND NULLIF(p.document ->> 'OrganizationId', '') IS NULL)
                THEN
                    RAISE EXCEPTION 'Work-item activity backfill requires project tenant ownership.';
                END IF;
            END $$;

            INSERT INTO work_items.work_item_comments (id, version, document)
            SELECT comment.value ->> 'Id', 0,
                (comment.value - 'History') || jsonb_build_object(
                    'Id', comment.value ->> 'Id',
                    'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId',
                    'WorkItemId', wi.id,
                    'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Comments') = 'array'
                    THEN wi.document -> 'Comments' ELSE '[]'::jsonb END) comment(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(comment.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_comment_revisions (id, version, document)
            SELECT md5('revision' || chr(31) || wi.id || chr(31) || (comment.value ->> 'Id')
                    || chr(31) || (revision.ordinality - 1)::text || chr(31)
                    || (extract(epoch FROM public.zumbo_parse_timestamptz(revision.value ->> 'EditedAt')) * 10000000
                        + 621355968000000000)::bigint::text),
                0,
                revision.value || jsonb_build_object(
                    'Id', md5('revision' || chr(31) || wi.id || chr(31) || (comment.value ->> 'Id')
                        || chr(31) || (revision.ordinality - 1)::text || chr(31)
                        || (extract(epoch FROM public.zumbo_parse_timestamptz(revision.value ->> 'EditedAt')) * 10000000
                            + 621355968000000000)::bigint::text),
                    'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId',
                    'WorkItemId', wi.id,
                    'CommentId', comment.value ->> 'Id',
                    'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Comments') = 'array'
                    THEN wi.document -> 'Comments' ELSE '[]'::jsonb END) comment(value)
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(comment.value -> 'History') = 'array'
                    THEN comment.value -> 'History' ELSE '[]'::jsonb END)
                WITH ORDINALITY revision(value, ordinality)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(comment.value ->> 'Id', '') IS NOT NULL
              AND NULLIF(revision.value ->> 'EditedAt', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_attachments (id, version, document)
            SELECT activity.value ->> 'Id', 0,
                activity.value || jsonb_build_object(
                    'Id', activity.value ->> 'Id', 'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId', 'WorkItemId', wi.id, 'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Attachments') = 'array'
                    THEN wi.document -> 'Attachments' ELSE '[]'::jsonb END) activity(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(activity.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_work_logs (id, version, document)
            SELECT activity.value ->> 'Id', 0,
                activity.value || jsonb_build_object(
                    'Id', activity.value ->> 'Id', 'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId', 'WorkItemId', wi.id, 'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'WorkLogs') = 'array'
                    THEN wi.document -> 'WorkLogs' ELSE '[]'::jsonb END) activity(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(activity.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_approvals (id, version, document)
            SELECT activity.value ->> 'Id', 0,
                activity.value || jsonb_build_object(
                    'Id', activity.value ->> 'Id', 'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId', 'WorkItemId', wi.id, 'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'Approvals') = 'array'
                    THEN wi.document -> 'Approvals' ELSE '[]'::jsonb END) activity(value)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(activity.value ->> 'Id', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO work_items.work_item_timeline (id, version, document)
            SELECT md5('timeline' || chr(31) || wi.id || chr(31) || (history.ordinality - 1)::text
                    || chr(31) || (extract(epoch FROM public.zumbo_parse_timestamptz(history.value ->> 'ChangedAt')) * 10000000
                        + 621355968000000000)::bigint::text
                    || chr(31) || (history.value ->> 'ToStatus')),
                0,
                history.value || jsonb_build_object(
                    'Id', md5('timeline' || chr(31) || wi.id || chr(31) || (history.ordinality - 1)::text
                        || chr(31) || (extract(epoch FROM public.zumbo_parse_timestamptz(history.value ->> 'ChangedAt')) * 10000000
                            + 621355968000000000)::bigint::text
                        || chr(31) || (history.value ->> 'ToStatus')),
                    'OrganizationId', p.document ->> 'OrganizationId',
                    'ProjectId', wi.document ->> 'ProjectId',
                    'WorkItemId', wi.id,
                    'Version', 0)
            FROM work_items.work_items wi
            JOIN projects.projects p ON p.id = wi.document ->> 'ProjectId'
            CROSS JOIN LATERAL jsonb_array_elements(
                CASE WHEN jsonb_typeof(wi.document -> 'StatusHistory') = 'array'
                    THEN wi.document -> 'StatusHistory' ELSE '[]'::jsonb END)
                WITH ORDINALITY history(value, ordinality)
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) < 1
              AND NULLIF(history.value ->> 'ChangedAt', '') IS NOT NULL
              AND NULLIF(history.value ->> 'ToStatus', '') IS NOT NULL
            ON CONFLICT (id) DO NOTHING;

            UPDATE work_items.work_items
            SET version = version + 1,
                document = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            jsonb_set(
                                jsonb_set(
                                    jsonb_set(document, '{Comments}', '[]'::jsonb, true),
                                    '{Attachments}', '[]'::jsonb, true),
                                '{WorkLogs}', '[]'::jsonb, true),
                            '{Approvals}', '[]'::jsonb, true),
                        '{StatusHistory}', '[]'::jsonb, true),
                    '{ActivityStorageVersion}', '1'::jsonb, true)
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE COALESCE((document ->> 'ActivityStorageVersion')::integer, 0) < 1;
            """;

        private const string DownSql = """
            UPDATE work_items.work_items wi
            SET version = wi.version + 1,
                document = jsonb_set(
                    jsonb_set(
                        jsonb_set(
                            jsonb_set(
                                jsonb_set(
                                    jsonb_set(wi.document, '{Comments}', COALESCE((
                                        SELECT jsonb_agg(c.document || jsonb_build_object('History', COALESCE((
                                            SELECT jsonb_agg(r.document - 'Id' - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'CommentId' - 'Version'
                                                ORDER BY public.zumbo_parse_timestamptz(r.document ->> 'EditedAt'), r.id)
                                            FROM work_items.work_item_comment_revisions r
                                            WHERE r.document ->> 'WorkItemId' = wi.id
                                              AND r.document ->> 'CommentId' = c.id), '[]'::jsonb))
                                            - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                            ORDER BY public.zumbo_parse_timestamptz(c.document ->> 'CreatedAt'), c.id)
                                        FROM work_items.work_item_comments c
                                        WHERE c.document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                                    '{Attachments}', COALESCE((SELECT jsonb_agg(document - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                        ORDER BY public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id)
                                        FROM work_items.work_item_attachments WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                                '{WorkLogs}', COALESCE((SELECT jsonb_agg(document - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                    ORDER BY public.zumbo_parse_timestamptz(document ->> 'CreatedAt'), id)
                                    FROM work_items.work_item_work_logs WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                            '{Approvals}', COALESCE((SELECT jsonb_agg(document - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                                ORDER BY public.zumbo_parse_timestamptz(document ->> 'RequestedAt'), id)
                                FROM work_items.work_item_approvals WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                        '{StatusHistory}', COALESCE((SELECT jsonb_agg(document - 'Id' - 'OrganizationId' - 'ProjectId' - 'WorkItemId' - 'Version'
                            ORDER BY public.zumbo_parse_timestamptz(document ->> 'ChangedAt'), id)
                            FROM work_items.work_item_timeline WHERE document ->> 'WorkItemId' = wi.id), '[]'::jsonb), true),
                    '{ActivityStorageVersion}', '0'::jsonb, true)
                    || jsonb_build_object('Version', wi.version + 1),
                updated_at = transaction_timestamp()
            WHERE COALESCE((wi.document ->> 'ActivityStorageVersion')::integer, 0) >= 1;

            DROP TABLE IF EXISTS work_items.work_item_timeline;
            DROP TABLE IF EXISTS work_items.work_item_approvals;
            DROP TABLE IF EXISTS work_items.work_item_work_logs;
            DROP TABLE IF EXISTS work_items.work_item_attachments;
            DROP TABLE IF EXISTS work_items.work_item_comment_revisions;
            DROP TABLE IF EXISTS work_items.work_item_comments;
            """;

    internal static PostgreSqlMigrationDefinition Definition { get; } = new(
        11,
        "create_work_item_activity_stores",
        UpSql,
        DownSql);
}
