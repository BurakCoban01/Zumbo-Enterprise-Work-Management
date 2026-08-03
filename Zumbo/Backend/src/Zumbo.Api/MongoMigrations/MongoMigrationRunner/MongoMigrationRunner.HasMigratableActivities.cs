using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static bool HasMigratableActivities(BsonDocument workItem)
    {
        foreach (var field in new[] { "Comments", "Attachments", "WorkLogs", "Approvals" })
        {
            if (ArrayValue(workItem, field).Any(value =>
                    value.IsBsonDocument
                    && !string.IsNullOrWhiteSpace(StringValue(value.AsBsonDocument, "Id"))))
            {
                return true;
            }
        }

        return ArrayValue(workItem, "StatusHistory").Any(value =>
            value.IsBsonDocument
            && !string.IsNullOrWhiteSpace(StringValue(value.AsBsonDocument, "ToStatus"))
            && TryResolveUtc(value.AsBsonDocument.GetValue("ChangedAt", BsonNull.Value), out _));
    }
}
