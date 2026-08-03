using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

internal static class DocumentSnapshot
{
    public static TDocument Clone<TDocument>(TDocument document) =>
        BsonSerializer.Deserialize<TDocument>(document.ToBson());

    public static bool AreEqual<TDocument>(TDocument left, TDocument right) =>
        left.ToBson().AsSpan().SequenceEqual(right.ToBson());
}
