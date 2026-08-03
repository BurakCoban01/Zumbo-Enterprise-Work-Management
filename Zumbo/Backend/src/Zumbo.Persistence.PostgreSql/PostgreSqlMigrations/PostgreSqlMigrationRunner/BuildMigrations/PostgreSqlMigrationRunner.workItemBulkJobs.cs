using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string workItemBulkJobs = """
            CREATE TABLE IF NOT EXISTS work_items.work_item_bulk_jobs (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE TABLE IF NOT EXISTS work_items.work_item_bulk_job_items (
                id text PRIMARY KEY,
                version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
                document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
                created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
                CHECK (document ->> 'Id' = id),
                CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_bulk_jobs_idempotency
                ON work_items.work_item_bulk_jobs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'RequestedByUserId'),
                    (document ->> 'IdempotencyKeyHash'));
            CREATE INDEX IF NOT EXISTS ix_workitem_bulk_jobs_owner_created
                ON work_items.work_item_bulk_jobs (
                    (document ->> 'OrganizationId'),
                    (document ->> 'ProjectId'),
                    (document ->> 'RequestedByUserId'),
                    public.zumbo_parse_timestamptz(document ->> 'CreatedAt') DESC,
                    id);
            CREATE INDEX IF NOT EXISTS ix_workitem_bulk_jobs_state_updated
                ON work_items.work_item_bulk_jobs (
                    (document ->> 'State'),
                    public.zumbo_parse_timestamptz(document ->> 'UpdatedAt'),
                    id);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_workitem_bulk_job_items_order
                ON work_items.work_item_bulk_job_items (
                    (document ->> 'JobId'),
                    ((document ->> 'ItemIndex')::integer));
            CREATE INDEX IF NOT EXISTS ix_workitem_bulk_job_items_state_order
                ON work_items.work_item_bulk_job_items (
                    (document ->> 'JobId'),
                    (document ->> 'State'),
                    ((document ->> 'ItemIndex')::integer),
                    id);
            """;
}
