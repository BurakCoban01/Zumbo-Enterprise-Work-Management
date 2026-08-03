using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoAutomationIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Workflows",
            "automationrules",
            "ix_automation_rules_tenant_project_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["Archived"] = 1,
                ["UpdatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "Workflows",
            "automationrules",
            "ix_automation_rules_schedule",
            new BsonDocument
            {
                ["Active"] = 1,
                ["Archived"] = 1,
                ["NextRunAtUtc"] = 1,
                ["_id"] = 1
            })
    ];
}
