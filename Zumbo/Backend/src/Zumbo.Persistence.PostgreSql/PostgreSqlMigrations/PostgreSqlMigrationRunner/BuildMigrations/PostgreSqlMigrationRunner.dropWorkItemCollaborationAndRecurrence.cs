using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropWorkItemCollaborationAndRecurrence = """
            DROP TABLE IF EXISTS work_items.work_item_recurrence_occurrences;
            DROP TABLE IF EXISTS work_items.work_item_recurrences;
            DROP TABLE IF EXISTS work_items.work_item_templates;
            DROP TABLE IF EXISTS work_items.work_item_event_activities;
            DROP TABLE IF EXISTS work_items.work_item_collaborations;
            """;
}
