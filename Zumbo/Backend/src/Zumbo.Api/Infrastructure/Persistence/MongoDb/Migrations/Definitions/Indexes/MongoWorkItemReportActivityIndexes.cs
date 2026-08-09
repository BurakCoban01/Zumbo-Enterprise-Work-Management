using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWorkItemReportActivityIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitems",
            "ix_workitems_project_archived_team_created",
            new BsonDocument
            {
                ["ProjectId"] = 1,
                ["Archived"] = 1,
                ["TeamId"] = 1,
                ["CreatedAt"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "workitemworklogactivitys",
            "ix_workitem_worklogs_project_cursor",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "workitemtimelineactivitys",
            "ix_workitem_timeline_project_cursor",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["_id"] = 1
            })
    ];
}
