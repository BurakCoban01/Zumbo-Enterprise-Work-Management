using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{
        private const string dropCapacityPlans = """
            DROP TABLE IF EXISTS work_items.capacity_plans;
            """;
}
