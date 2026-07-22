using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class WorkItemTypeSchemaServiceTests
{
    private readonly InMemoryDocumentRepository<WorkItemTypeSchemaDocument> schemas = new();
    private readonly InMemoryDocumentRepository<WorkItemDocument> workItems = new();

    [Fact]
    public async Task TypedValues_AreCanonicalizedAndRequiredFieldsAreEnforced()
    {
        var service = CreateService();
        await service.UpsertAsync("project-1", Schema(["Critical", "High"]), "correlation", default);

        var shape = await service.ValidateAsync(
            "project-1",
            "incident",
            [
                new("summary", TextValue: "  Database unavailable  "),
                new("impact", NumberValue: 8.5m),
                new("confirmed", BooleanValue: false),
                new("detected", DateValue: new DateOnly(2026, 7, 20)),
                new("severity", OptionKey: "critical")
            ],
            default);

        Assert.Equal("Incident", shape.IssueTypeKey);
        Assert.Equal(5, shape.CustomFields.Count);
        Assert.Equal("Database unavailable", shape.CustomFields.Single(x => x.FieldKey == "summary").TextValue);
        Assert.Equal("Critical", shape.CustomFields.Single(x => x.FieldKey == "severity").OptionKey);
        Assert.All(shape.CustomFields, value => Assert.True(value.Indexed));
        await Assert.ThrowsAsync<ValidationException>(() => service.ValidateAsync(
            "project-1",
            "Incident",
            [new("summary", TextValue: "Missing required values")],
            default));
    }

    [Fact]
    public async Task ExistingValue_PreventsDestructiveConstraintChange()
    {
        var service = CreateService();
        await service.UpsertAsync("project-1", Schema(["Critical", "High"]), "correlation", default);
        var shape = await service.ValidateAsync(
            "project-1",
            "Incident",
            RequiredValues("Critical"),
            default);
        await workItems.CreateAsync(new WorkItemDocument
        {
            Id = "item-1",
            ProjectId = "project-1",
            Type = "Incident",
            CustomFields = shape.CustomFields.ToList()
        });

        var exception = await Assert.ThrowsAsync<ConflictException>(() => service.UpsertAsync(
            "project-1",
            Schema(["High"]),
            "correlation",
            default));

        Assert.Equal("WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID", exception.Code);
    }

    private WorkItemTypeSchemaService CreateService() => new(
        schemas,
        workItems,
        new AllowPermissionChecker(),
        new NoOpAuditPublisher(),
        new InMemoryDistributedLockProvider(),
        Options.Create(new DistributedLockOptions()),
        Options.Create(new WorkItemTypeSchemaOptions()),
        new FixedClock(),
        new FixedCurrentUser());

    private static UpsertWorkItemTypeSchemaRequest Schema(IReadOnlyCollection<string> severities) => new(
        [new IssueTypeDefinitionRequest("Incident", "Incident", null, "Standard")],
        [
            new("summary", "Summary", "Text", false, true, 100, null, null, null, ["Incident"]),
            new("impact", "Impact", "Number", true, true, null, 0, 10, null, ["Incident"]),
            new("confirmed", "Confirmed", "Boolean", true, true, null, null, null, null, ["Incident"]),
            new("detected", "Detected", "Date", true, true, null, null, null, null, ["Incident"]),
            new("severity", "Severity", "Select", true, true, null, null, null, severities, ["Incident"])
        ],
        [new IssueTypeLayoutRequest("Incident", ["summary", "impact", "confirmed", "detected", "severity"])]);

    private static IReadOnlyCollection<WorkItemCustomFieldValueRequest> RequiredValues(string severity) =>
    [
        new("impact", NumberValue: 7),
        new("confirmed", BooleanValue: true),
        new("detected", DateValue: new DateOnly(2026, 7, 20)),
        new("severity", OptionKey: severity)
    ];

    private sealed class AllowPermissionChecker : IProjectPermissionChecker
    {
        public Task<ProjectResourceAuthorization> EnsureCanAsync(
            string userId,
            string projectId,
            string permission,
            CancellationToken ct) =>
            Task.FromResult(new ProjectResourceAuthorization(projectId, "org-1", userId, "ProjectOwner", false));
    }

    private sealed class NoOpAuditPublisher : IWorkItemAuditPublisher
    {
        public Task WriteAsync(
            string action,
            string entityType,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public string? UserId => "user-1";
        public string? OrganizationId => "org-1";
        public IReadOnlyCollection<string> Roles => ["User"];
    }
}
