using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemActivityStores = """
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
}
