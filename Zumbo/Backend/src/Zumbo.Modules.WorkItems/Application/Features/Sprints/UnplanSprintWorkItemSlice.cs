using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class UnplanSprintWorkItemSlice(
    IDocumentRepository<SprintDocument> sprints,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IExpectedVersionAccessor? expectedVersions)
{
    internal async Task<SprintPlannedItemResponse> HandleAsync(
        UnplanSprintWorkItemCommand command,
        CancellationToken ct)
    {
        var initialSprint = await GetSprintAsync(command.SprintId, ct);
        await EnsurePermissionAsync(initialSprint.ProjectId, ct);
        await using var projectLock = await AcquireProjectLockAsync(initialSprint.ProjectId, ct);
        var sprint = await GetSprintAsync(command.SprintId, ct);
        EnsurePlanned(sprint);
        var item = await workItems.SelectAsync(x => x.Id == command.WorkItemId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (item.ProjectId != sprint.ProjectId || item.SprintId != sprint.Id)
        {
            throw new ConflictException("WORK_ITEM_NOT_IN_SPRINT", "Work item is not planned in this sprint.");
        }

        item.SprintId = null;
        item.UpdatedAt = clock.UtcNow;
        await SaveWorkItemAsync(item, ct);
        await audit.WriteAsync(
            "SprintWorkItemUnplanned",
            "WorkItem",
            item.Id,
            sprint.Id,
            null,
            command.CorrelationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return new SprintPlannedItemResponse(item.Id, null, item.EstimatePoints, item.Version);
    }

    private async Task<SprintDocument> GetSprintAsync(string sprintId, CancellationToken ct) =>
        await sprints.SelectAsync(sprint => sprint.Id == sprintId, ct)
        ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");

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
        var lease = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.LeaseSeconds, 5, 300));
        var wait = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.WaitSeconds, 0, 30));
        return await distributedLocks.TryAcquireAsync("project-structure:" + projectId, lease, wait, ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The project structure is busy; retry the operation.");
    }

    private async Task SaveWorkItemAsync(WorkItemDocument item, CancellationToken ct)
    {
        var expectedVersion = expectedVersions?.ExpectedVersion ?? item.Version;
        var result = await workItems.ReplaceByVersionAsync(x => x.Id == item.Id, item, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_CONCURRENCY_CONFLICT", "Work item changed concurrently; reload and retry.");
        }

        item.Version = result.Version!.Value;
    }

    private static void EnsurePlanned(SprintDocument sprint)
    {
        if (sprint.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_PLANNING_CLOSED", "Only a planned sprint can change scope.");
        }
    }
}
