using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed record PostgreSqlMigrationStatus(
    IReadOnlyList<PostgreSqlMigrationInfo> Applied,
    IReadOnlyList<PostgreSqlMigrationInfo> Pending);
