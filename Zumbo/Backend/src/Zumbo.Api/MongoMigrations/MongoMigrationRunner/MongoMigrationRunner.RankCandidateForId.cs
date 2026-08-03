using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> RankCandidateForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Rank", false),
            Builders<BsonDocument>.Filter.Eq("Rank", 0));
}
