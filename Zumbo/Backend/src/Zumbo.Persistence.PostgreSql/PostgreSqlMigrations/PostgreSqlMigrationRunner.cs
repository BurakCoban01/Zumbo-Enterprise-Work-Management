using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner(
    NpgsqlDataSource dataSource,
    PostgreSqlPersistenceOptions options,
    ILogger<PostgreSqlMigrationRunner>? logger = null) : IPostgreSqlMigrationRunner
{
    private const string Ledger = "public.zumbo_schema_migrations";
    private const string LockName = "zumbo-postgresql-schema-migrations-v1";
}
