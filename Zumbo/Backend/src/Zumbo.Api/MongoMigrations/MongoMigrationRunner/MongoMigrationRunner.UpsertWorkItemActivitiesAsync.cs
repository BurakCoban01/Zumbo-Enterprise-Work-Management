using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private async Task UpsertWorkItemActivitiesAsync(
        BsonDocument workItem,
        string organizationId,
        string projectId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var comments = mongo.GetCollection<BsonDocument>("workitemcommentactivitys", WorkItemsModule);
        var revisions = mongo.GetCollection<BsonDocument>("workitemcommentrevisionactivitys", WorkItemsModule);
        var attachments = mongo.GetCollection<BsonDocument>("workitemattachmentactivitys", WorkItemsModule);
        var workLogs = mongo.GetCollection<BsonDocument>("workitemworklogactivitys", WorkItemsModule);
        var approvals = mongo.GetCollection<BsonDocument>("workitemapprovalactivitys", WorkItemsModule);
        var timeline = mongo.GetCollection<BsonDocument>("workitemtimelineactivitys", WorkItemsModule);

        foreach (var value in ArrayValue(workItem, "Comments"))
        {
            if (!value.IsBsonDocument) continue;
            var source = value.AsBsonDocument;
            var commentId = StringValue(source, "Id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commentId)) continue;
            await ReplaceMigratedActivityAsync(comments, new BsonDocument
            {
                ["_id"] = commentId,
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId,
                ["Body"] = source.GetValue("Body", string.Empty),
                ["AuthorUserId"] = source.GetValue("AuthorUserId", "system"),
                ["Mentions"] = source.GetValue("Mentions", new BsonArray()),
                ["CreatedAt"] = source.GetValue("CreatedAt", BsonNull.Value),
                ["EditedAt"] = source.GetValue("EditedAt", BsonNull.Value),
                ["Version"] = 0L
            }, cancellationToken);

            var history = ArrayValue(source, "History");
            for (var ordinal = 0; ordinal < history.Count; ordinal++)
            {
                if (!history[ordinal].IsBsonDocument) continue;
                var revision = history[ordinal].AsBsonDocument;
                var editedAt = revision.GetValue("EditedAt", BsonNull.Value);
                if (!TryResolveUtc(editedAt, out var editedAtUtc)) continue;
                await ReplaceMigratedActivityAsync(revisions, new BsonDocument
                {
                    ["_id"] = ActivityId("revision", workItemId, commentId, ordinal.ToString(), editedAtUtc.Ticks.ToString()),
                    ["OrganizationId"] = organizationId,
                    ["ProjectId"] = projectId,
                    ["WorkItemId"] = workItemId,
                    ["CommentId"] = commentId,
                    ["Body"] = revision.GetValue("Body", string.Empty),
                    ["EditedByUserId"] = revision.GetValue("EditedByUserId", "system"),
                    ["EditedAt"] = editedAt,
                    ["Version"] = 0L
                }, cancellationToken);
            }
        }

        await CopyArrayAsync(attachments, workItem, "Attachments", organizationId, projectId, workItemId,
            ["FileName", "ContentType", "SizeBytes", "StoragePath", "ChecksumSha256", "CreatedAt"], cancellationToken);
        await CopyArrayAsync(workLogs, workItem, "WorkLogs", organizationId, projectId, workItemId,
            ["UserId", "Hours", "Note", "CreatedAt"], cancellationToken);
        await CopyArrayAsync(approvals, workItem, "Approvals", organizationId, projectId, workItemId,
            ["FromStatus", "ToStatus", "RequestedByUserId", "RequestedAt", "ExpiresAt", "Status",
                "DecidedByUserId", "DecidedAt", "Note", "ConsumedAt"], cancellationToken);

        var historyEntries = ArrayValue(workItem, "StatusHistory");
        for (var ordinal = 0; ordinal < historyEntries.Count; ordinal++)
        {
            if (!historyEntries[ordinal].IsBsonDocument) continue;
            var source = historyEntries[ordinal].AsBsonDocument;
            var changedAt = source.GetValue("ChangedAt", BsonNull.Value);
            var toStatus = StringValue(source, "ToStatus") ?? string.Empty;
            if (!TryResolveUtc(changedAt, out var changedAtUtc) || string.IsNullOrWhiteSpace(toStatus)) continue;
            await ReplaceMigratedActivityAsync(timeline, new BsonDocument
            {
                ["_id"] = ActivityId("timeline", workItemId, ordinal.ToString(), changedAtUtc.Ticks.ToString(), toStatus),
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId,
                ["FromStatus"] = source.GetValue("FromStatus", BsonNull.Value),
                ["ToStatus"] = toStatus,
                ["ChangedByUserId"] = source.GetValue("ChangedByUserId", "system"),
                ["ChangedAt"] = changedAt,
                ["Version"] = 0L
            }, cancellationToken);
        }
    }
}
