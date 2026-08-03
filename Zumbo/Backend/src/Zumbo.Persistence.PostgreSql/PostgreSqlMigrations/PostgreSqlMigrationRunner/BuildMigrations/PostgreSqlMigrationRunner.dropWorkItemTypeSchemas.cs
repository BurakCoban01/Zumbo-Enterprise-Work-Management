using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemTypeSchemas = """
            UPDATE work_items.work_items
            SET version = version + 1,
                document = (document
                    - 'IssueTypeSchemaVersion'
                    - 'CustomFields'
                    - 'WorkItemTypeSchemaMigratedBy')
                    || jsonb_build_object('Version', version + 1),
                updated_at = transaction_timestamp()
            WHERE document ->> 'WorkItemTypeSchemaMigratedBy' = '20260720_017';
            DROP INDEX IF EXISTS work_items.ix_workitems_custom_fields_gin;
            DROP INDEX IF EXISTS work_items.ix_workitems_project_archived_type_rank;
            DROP TABLE IF EXISTS work_items.work_item_type_schemas;
            """;
}
