using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkflowLifecycleMarker = """
            DROP TABLE IF EXISTS work_items.board_column_wip_projections;

            UPDATE workflows.workflow_definitions
            SET version = version + 1,
                document = (document - 'WorkflowLifecycleMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'WorkflowLifecycleMigratedBy' = '20260720_015';
            """;
}
