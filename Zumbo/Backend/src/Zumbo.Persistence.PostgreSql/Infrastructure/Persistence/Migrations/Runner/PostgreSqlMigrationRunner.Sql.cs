using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Zumbo.Persistence.PostgreSql;

public sealed partial class PostgreSqlMigrationRunner
{
    private static string CreateTableSql(PostgreSqlDocumentStorage storage) => $"""
        CREATE TABLE IF NOT EXISTS {Qualified(storage)} (
            id text PRIMARY KEY,
            version bigint NOT NULL DEFAULT 0 CHECK (version >= 0),
            document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
            created_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
            updated_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
            CHECK (document ->> 'Id' = id),
            CHECK (COALESCE((document ->> 'Version')::bigint, 0) = version)
        );
        """;

    private static string IndexName(PostgreSqlDocumentStorage storage)
    {
        var value = $"ix_{storage.Table}_document_gin";
        return value.Length <= 63 ? value : value[..63];
    }

    private static string Qualified(PostgreSqlDocumentStorage storage) =>
        $"{SqlIdentifier.Quote(storage.Schema)}.{SqlIdentifier.Quote(storage.Table)}";

    private static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
