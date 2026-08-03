using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoDbSettings
{
    public string ConnectionString { get; init; } = string.Empty;
    public string DatabaseName { get; init; } = "Zumbo";
    public int ConnectTimeoutSeconds { get; init; } = 5;
    public int ServerSelectionTimeoutSeconds { get; init; } = 5;
    public int SocketTimeoutSeconds { get; init; } = 10;
    public int WaitQueueTimeoutSeconds { get; init; } = 5;
    public int MinimumPoolSize { get; init; }
    public int MaximumPoolSize { get; init; } = 100;
    public bool RetryReads { get; init; } = true;
    public bool RetryWrites { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("A MongoDB connection string is required.");
        if (string.IsNullOrWhiteSpace(DatabaseName))
            throw new InvalidOperationException("A MongoDB database name is required.");
        if (ConnectTimeoutSeconds is < 1 or > 300
            || ServerSelectionTimeoutSeconds is < 1 or > 300
            || SocketTimeoutSeconds is < 1 or > 600
            || WaitQueueTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("MongoDB timeout settings are outside the supported bounds.");
        }
        if (MinimumPoolSize < 0 || MaximumPoolSize < 1 || MinimumPoolSize > MaximumPoolSize)
            throw new InvalidOperationException("MongoDB pool sizes are invalid.");
    }
}
