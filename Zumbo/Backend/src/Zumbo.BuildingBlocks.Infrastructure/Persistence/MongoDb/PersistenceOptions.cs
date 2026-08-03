using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class PersistenceOptions
{
    public string Provider { get; init; } = "InMemory";
}
