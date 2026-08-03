using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

public interface IMongoDbService
{
    IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName);
    IMongoCollection<TDocument> GetCollection<TDocument>(string collectionName, string moduleName);
    IMongoDatabase GetDatabase(string moduleName);
    IMongoClient GetClient(string moduleName);
    Task CheckHealthAsync(CancellationToken cancellationToken = default);
}
