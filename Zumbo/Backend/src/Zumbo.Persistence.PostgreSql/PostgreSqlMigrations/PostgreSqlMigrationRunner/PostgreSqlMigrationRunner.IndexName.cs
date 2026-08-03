using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner{

    private static string IndexName(PostgreSqlDocumentStorage storage)
    {
        var value = $"ix_{storage.Table}_document_gin";
        return value.Length <= 63 ? value : value[..63];
    }
}
