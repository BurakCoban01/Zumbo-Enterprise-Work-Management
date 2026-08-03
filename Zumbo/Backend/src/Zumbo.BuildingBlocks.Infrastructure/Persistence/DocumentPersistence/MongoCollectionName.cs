using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

internal static class MongoCollectionName
{
    public static string For<TDocument>() =>
        typeof(TDocument).Name.Replace("Document", string.Empty).ToLowerInvariant() + "s";
}
