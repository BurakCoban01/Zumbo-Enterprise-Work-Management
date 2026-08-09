using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed record MongoIndexSpecification(
    string Module,
    string Collection,
    string Name,
    BsonDocument Keys,
    bool Unique = false,
    bool CaseInsensitive = false,
    TimeSpan? ExpireAfter = null,
    BsonDocument? PartialFilter = null);
