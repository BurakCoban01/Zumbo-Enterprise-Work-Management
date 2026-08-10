using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWorkItemActivityIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        ActivityIndex("workitemcommentactivitys", "ix_workitem_comments_owner_created", "CreatedAt"),
        new(
            "WorkItems",
            "workitemcommentrevisionactivitys",
            "ix_workitem_revisions_owner_comment_edited",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["WorkItemId"] = 1,
                ["CommentId"] = 1,
                ["EditedAt"] = 1,
                ["_id"] = 1
            }),
        ActivityIndex("workitemattachmentactivitys", "ix_workitem_attachments_owner_created", "CreatedAt"),
        ActivityIndex("workitemworklogactivitys", "ix_workitem_worklogs_owner_created", "CreatedAt"),
        ActivityIndex("workitemapprovalactivitys", "ix_workitem_approvals_owner_requested", "RequestedAt"),
        ActivityIndex("workitemtimelineactivitys", "ix_workitem_timeline_owner_changed", "ChangedAt")
    ];

    private static MongoIndexSpecification ActivityIndex(
        string collection,
        string name,
        string chronologicalField) =>
        new(
            "WorkItems",
            collection,
            name,
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["WorkItemId"] = 1,
                [chronologicalField] = 1,
                ["_id"] = 1
            });
}
