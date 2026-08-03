using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropSprintLifecycleMarker = """
            UPDATE work_items.work_items item
            SET version = item.version + 1,
                document = (item.document - 'SprintLifecycleMigratedBy')
                    || jsonb_build_object(
                        'SprintId', COALESCE(
                            (SELECT regexp_replace(sprint.document ->> 'Name', ' \(legacy-[0-9a-f]{8}\)$', '')
                             FROM work_items.sprints sprint
                             WHERE sprint.id = item.document ->> 'SprintId'),
                            item.document ->> 'SprintId'),
                        'Version', item.version + 1),
                updated_at = transaction_timestamp()
            WHERE item.document ->> 'SprintLifecycleMigratedBy' = '20260720_016';
            DROP TABLE IF EXISTS work_items.sprint_completion_snapshots;
            DROP TABLE IF EXISTS work_items.sprint_scope_snapshots;
            DROP TABLE IF EXISTS work_items.sprints;
            """;
}
