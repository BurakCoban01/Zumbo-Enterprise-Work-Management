using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWorkItemReportingIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitems",
            "ix_workitems_project_archived_id",
            new BsonDocument
            {
                ["ProjectId"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            })
    ];
}
