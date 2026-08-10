using System.Globalization;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal sealed class UpsertWorkItemTypeSchemaSlice(
    IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IOptions<WorkItemTypeSchemaOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser,
    IExpectedVersionAccessor? expectedVersions)
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerValidation, 1, 10_000);

    internal async Task<WorkItemTypeSchemaResponse> HandleAsync(
        UpsertWorkItemTypeSchemaCommand command,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(command.ProjectId, ct);
        var candidate = WorkItemTypeSchemaDefinitionPolicy.Normalize(
            command.ProjectId,
            command.Request,
            clock.UtcNow);
        await using var projectLock = await AcquireProjectLockAsync(command.ProjectId, ct);
        var stored = await schemas.SelectAsync(schema => schema.ProjectId == command.ProjectId, ct);
        candidate.SchemaVersion = (stored?.SchemaVersion ?? 0) + 1;
        candidate.CreatedAt = stored?.CreatedAt ?? candidate.CreatedAt;
        await ValidateExistingItemsAsync(candidate, ct);

        if (stored is null)
        {
            if (expectedVersions?.ExpectedVersion is long expectedVersion && expectedVersion != 0)
            {
                throw ConcurrencyConflict();
            }
            await schemas.CreateAsync(candidate, ct);
        }
        else
        {
            candidate.Id = stored.Id;
            candidate.Version = stored.Version;
            var expectedVersion = expectedVersions?.ExpectedVersion ?? stored.Version;
            var result = await schemas.ReplaceByVersionAsync(
                schema => schema.Id == stored.Id,
                candidate,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw ConcurrencyConflict();
            }
        }

        await audit.WriteAsync(
            "WorkItemTypeSchemaUpdated",
            "Project",
            command.ProjectId,
            stored?.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            candidate.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            command.CorrelationId,
            ct);
        return WorkItemTypeSchemaResponseMapper.ToResponse(candidate);
    }

    private async Task EnsurePermissionAsync(string projectId, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }
        _ = await permissionChecker.EnsureCanAsync(userId, projectId, PermissionCatalog.WorkItemUpdate, ct);
    }

    private async Task<IAsyncDisposable> AcquireProjectLockAsync(string projectId, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
                "work-item-schema:" + projectId,
                TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
                TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
                ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The work item schema is busy; retry the operation.");
    }

    private async Task ValidateExistingItemsAsync(WorkItemTypeSchemaDocument candidate, CancellationToken ct)
    {
        var batches = 0;
        string? cursor = null;
        do
        {
            if (++batches > MaxBatches)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_VALIDATION_LIMIT",
                    "Work item schema validation exceeded the configured batch limit.");
            }

            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == candidate.ProjectId && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                var type = WorkItemTypeSchemaDefinitionPolicy.FindActiveIssueType(candidate, item.Type);
                WorkItemTypeSchemaDefinitionPolicy.ValidateStoredValues(candidate, type.Key, item.CustomFields);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }

    private static ConflictException ConcurrencyConflict() => new(
        "WORK_ITEM_SCHEMA_CONCURRENCY_CONFLICT",
        "Work item type schema changed concurrently; reload and retry.");
}
