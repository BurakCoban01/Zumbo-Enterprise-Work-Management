using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static FilterDefinition<BsonDocument> LegacyTeamInviteFilter(BsonValue checkpoint)
    {
        var pendingWithoutHash = Builders<BsonDocument>.Filter.ElemMatch(
            "Members",
            Builders<BsonDocument>.Filter.Eq("Status", "Invited")
            & Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("InvitationTokenHash", false),
                Builders<BsonDocument>.Filter.Eq("InvitationTokenHash", BsonNull.Value),
                Builders<BsonDocument>.Filter.Eq("InvitationTokenHash", string.Empty)));
        if (!checkpoint.IsBsonNull)
        {
            pendingWithoutHash &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return pendingWithoutHash;
    }
}
