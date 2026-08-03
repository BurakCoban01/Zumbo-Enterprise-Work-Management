using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string durableMessaging = """
            CREATE SCHEMA IF NOT EXISTS messaging;
            CREATE TABLE IF NOT EXISTS messaging.outbox_messages (
                id text PRIMARY KEY,
                owner_module text NOT NULL,
                event_type text NOT NULL,
                schema_version integer NOT NULL CHECK (schema_version > 0),
                tenant_id text NOT NULL,
                correlation_id text NOT NULL,
                deduplication_key text NULL,
                payload jsonb NOT NULL CHECK (jsonb_typeof(payload) = 'object'),
                occurred_at_utc timestamptz NOT NULL,
                status text NOT NULL DEFAULT 'Pending'
                    CHECK (status IN ('Pending', 'Processing', 'Completed', 'DeadLetter')),
                attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
                available_at_utc timestamptz NOT NULL,
                lease_owner text NULL,
                lease_token text NULL,
                lease_until_utc timestamptz NULL,
                last_error text NULL,
                completed_at_utc timestamptz NULL,
                dead_lettered_at_utc timestamptz NULL,
                created_at_utc timestamptz NOT NULL DEFAULT transaction_timestamp(),
                updated_at_utc timestamptz NOT NULL DEFAULT transaction_timestamp()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_outbox_deduplication
                ON messaging.outbox_messages (owner_module, event_type, deduplication_key)
                WHERE deduplication_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_outbox_claim
                ON messaging.outbox_messages (status, available_at_utc, lease_until_utc, occurred_at_utc, id)
                WHERE status IN ('Pending', 'Processing');
            CREATE INDEX IF NOT EXISTS ix_outbox_dead_letter
                ON messaging.outbox_messages (dead_lettered_at_utc, id)
                WHERE status = 'DeadLetter';
            CREATE TABLE IF NOT EXISTS messaging.inbox_messages (
                consumer_name text NOT NULL,
                message_id text NOT NULL,
                processed_at_utc timestamptz NOT NULL,
                PRIMARY KEY (consumer_name, message_id)
            );
            CREATE INDEX IF NOT EXISTS ix_inbox_processed_at
                ON messaging.inbox_messages (processed_at_utc);
            """;
}
