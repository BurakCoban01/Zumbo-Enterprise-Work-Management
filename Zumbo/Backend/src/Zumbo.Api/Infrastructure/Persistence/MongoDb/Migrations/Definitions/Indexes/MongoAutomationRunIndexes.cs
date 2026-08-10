using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoAutomationRunIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Workflows",
            "automationruns",
            "ix_automation_runs_tenant_project_created",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["CreatedAtUtc"] = -1,
                ["_id"] = 1
            }),
        new(
            "Workflows",
            "automationruns",
            "ix_automation_runs_rule_created",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["RuleId"] = 1,
                ["CreatedAtUtc"] = -1,
                ["_id"] = 1
            }),
        new(
            "Workflows",
            "automationruns",
            "ix_automation_runs_retry",
            new BsonDocument
            {
                ["Status"] = 1,
                ["NextAttemptAtUtc"] = 1,
                ["_id"] = 1
            })
    ];
}
