using System.Data;
using Microsoft.Extensions.Logging;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Persistence.PostgreSql;

internal sealed class PostgreSqlConnectionLease(
    NpgsqlConnection connection,
    NpgsqlTransaction? transaction,
    bool ownsConnection) : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; } = connection;
    public NpgsqlTransaction? Transaction { get; } = transaction;

    public NpgsqlCommand CreateCommand(string sql, int commandTimeoutSeconds)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeoutSeconds;
        command.Transaction = Transaction;
        return command;
    }

    public async ValueTask DisposeAsync()
    {
        if (ownsConnection)
        {
            await Connection.DisposeAsync();
        }
    }
}
