using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoTransactionContext : IAsyncDisposable
{
    public IClientSessionHandle? Session { get; private set; }
    public IMongoClient? Client { get; private set; }
    public bool HasActiveTransaction => Session?.IsInTransaction == true;

    internal void Attach(IMongoClient client, IClientSessionHandle session)
    {
        if (Session is not null)
        {
            throw new InvalidOperationException("A MongoDB transaction is already active in this scope.");
        }

        Client = client;
        Session = session;
    }

    internal void EnsureCompatible(IMongoClient client)
    {
        if (Session is not null && !ReferenceEquals(Client, client))
        {
            throw new InvalidOperationException(
                "The active MongoDB transaction cannot span differently configured MongoDB clients.");
        }
    }

    internal ValueTask DetachAndDisposeAsync()
    {
        var session = Session;
        Session = null;
        Client = null;
        session?.Dispose();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => DetachAndDisposeAsync();
}
