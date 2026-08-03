using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal sealed class PostgreSqlOwnedCommand(
    NpgsqlCommand command,
    PostgreSqlConnectionLease lease) : IAsyncDisposable
{
    public NpgsqlCommand Command => command;
    public NpgsqlParameterCollection Parameters => command.Parameters;
    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) => command.ExecuteNonQueryAsync(cancellationToken);
    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => command.ExecuteScalarAsync(cancellationToken);
    public Task<NpgsqlDataReader> ExecuteReaderAsync(CancellationToken cancellationToken) => command.ExecuteReaderAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await command.DisposeAsync();
        await lease.DisposeAsync();
    }
}
