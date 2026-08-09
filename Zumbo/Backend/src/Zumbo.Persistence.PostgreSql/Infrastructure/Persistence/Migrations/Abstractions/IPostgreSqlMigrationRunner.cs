using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public interface IPostgreSqlMigrationRunner
{
    Task<PostgreSqlMigrationStatus> StatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PostgreSqlMigrationInfo>> ApplyAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PostgreSqlMigrationInfo>> RollbackAsync(
        long targetVersion,
        CancellationToken cancellationToken = default);
    Task<string> GenerateScriptAsync(
        long? fromVersion = null,
        long? toVersion = null,
        bool idempotent = false,
        CancellationToken cancellationToken = default);
}
