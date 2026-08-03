using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropIntakeForms = """
            DROP TABLE IF EXISTS work_items.intake_submissions;
            DROP TABLE IF EXISTS work_items.intake_form_versions;
            DROP TABLE IF EXISTS work_items.intake_forms;
            """;
}
