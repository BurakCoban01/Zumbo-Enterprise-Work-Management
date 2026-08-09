using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoIntakeIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "intakeforms",
            "ux_intake_forms_public_id",
            new BsonDocument { ["PublicId"] = 1 },
            Unique: true),
        new(
            "WorkItems",
            "intakeforms",
            "ix_intake_forms_tenant_project_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["State"] = 1,
                ["UpdatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "intakeformversions",
            "ux_intake_form_versions_number",
            new BsonDocument { ["FormId"] = 1, ["DefinitionVersion"] = 1 },
            Unique: true),
        new(
            "WorkItems",
            "intakesubmissions",
            "ux_intake_submissions_idempotency",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["FormId"] = 1,
                ["SubmittedByUserId"] = 1,
                ["IdempotencyKeyHash"] = 1
            },
            Unique: true),
        new(
            "WorkItems",
            "intakesubmissions",
            "ix_intake_submissions_triage",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["FormId"] = 1,
                ["State"] = 1,
                ["CreatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "intakesubmissions",
            "ux_intake_submissions_work_item",
            new BsonDocument { ["WorkItemId"] = 1 },
            Unique: true)
    ];
}
