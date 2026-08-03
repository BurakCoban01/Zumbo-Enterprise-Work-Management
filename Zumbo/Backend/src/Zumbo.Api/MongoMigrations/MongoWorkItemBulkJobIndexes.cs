using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWorkItemBulkJobIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitembulkjobs",
            "ux_workitem_bulk_jobs_idempotency",
            new BsonDocument { ["OrganizationId"] = 1, ["RequestedByUserId"] = 1, ["IdempotencyKeyHash"] = 1 },
            Unique: true),
        new(
            "WorkItems",
            "workitembulkjobs",
            "ix_workitem_bulk_jobs_owner_created",
            new BsonDocument { ["OrganizationId"] = 1, ["ProjectId"] = 1, ["RequestedByUserId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "workitembulkjobs",
            "ix_workitem_bulk_jobs_state_updated",
            new BsonDocument { ["State"] = 1, ["UpdatedAt"] = 1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "workitembulkjobitems",
            "ux_workitem_bulk_job_items_order",
            new BsonDocument { ["JobId"] = 1, ["ItemIndex"] = 1 },
            Unique: true),
        new(
            "WorkItems",
            "workitembulkjobitems",
            "ix_workitem_bulk_job_items_state_order",
            new BsonDocument { ["JobId"] = 1, ["State"] = 1, ["ItemIndex"] = 1, ["_id"] = 1 })
    ];
}
