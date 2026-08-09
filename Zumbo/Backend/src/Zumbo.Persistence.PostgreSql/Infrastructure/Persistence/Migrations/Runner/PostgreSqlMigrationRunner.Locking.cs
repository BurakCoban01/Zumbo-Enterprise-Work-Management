using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner
{
    private async Task AcquireLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_xact_lock(hashtext(@name));";
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.AddWithValue("name", LockName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
