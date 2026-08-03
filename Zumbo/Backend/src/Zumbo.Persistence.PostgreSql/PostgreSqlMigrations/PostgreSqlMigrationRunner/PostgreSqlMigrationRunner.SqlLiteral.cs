using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
