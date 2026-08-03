using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task CopyArrayAsync(
        IMongoCollection<BsonDocument> target,
        BsonDocument workItem,
        string field,
        string organizationId,
        string projectId,
        string workItemId,
        IReadOnlyCollection<string> copiedFields,
        CancellationToken cancellationToken)
    {
        foreach (var value in ArrayValue(workItem, field))
        {
            if (!value.IsBsonDocument) continue;
            var source = value.AsBsonDocument;
            var id = StringValue(source, "Id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var activity = new BsonDocument
            {
                ["_id"] = id,
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId
            };
            foreach (var copiedField in copiedFields)
            {
                activity[copiedField] = source.GetValue(copiedField, BsonNull.Value);
            }
            activity["Version"] = 0L;
            await ReplaceMigratedActivityAsync(target, activity, cancellationToken);
        }
    }
}
