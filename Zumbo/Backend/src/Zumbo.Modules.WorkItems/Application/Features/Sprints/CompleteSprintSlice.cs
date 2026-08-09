using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class CompleteSprintSlice(
    IDocumentRepository<SprintDocument> sprints,
    IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
    IDocumentRepository<SprintCompletionSnapshotDocument> completionSnapshots,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IOptions<SprintOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IExpectedVersionAccessor? expectedVersions)
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerOperation, 1, 10_000);

    internal async Task<SprintResponse> HandleAsync(CompleteSprintCommand command, CancellationToken ct)
    {
        var initial = await GetSprintAsync(command.SprintId, ct);
        await EnsurePermissionAsync(initial.ProjectId, ct);
        await using var projectLock = await AcquireProjectLockAsync(initial.ProjectId, ct);
        var sprint = await GetSprintAsync(command.SprintId, ct);
        if (sprint.Status != SprintStatuses.Active)
        {
            throw new ConflictException("SPRINT_COMPLETE_INVALID_STATE", "Only an active sprint can be completed.");
        }

        var carryoverId = NormalizeOptional(command.Request.CarryoverSprintId);
        if (carryoverId == sprint.Id)
        {
            throw new ValidationException("Carryover sprint must be different from the completed sprint.");
        }

        SprintDocument? carryover = null;
        if (carryoverId is not null)
        {
            carryover = await GetSprintAsync(carryoverId, ct);
            if (carryover.ProjectId != sprint.ProjectId)
            {
                throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Carryover sprint must belong to the same project.");
            }

            EnsurePlanned(carryover);
        }

        var now = clock.UtcNow;
        var completedItems = 0;
        var completedPoints = 0m;
        var carryoverItems = 0;
        var carryoverPoints = 0m;
        var batches = 0;
        string? cursor = null;
        do
        {
            EnsureBatchLimit(++batches);
            var page = await scopeSnapshots.ListByCursorAsync(
                snapshot => snapshot.SprintId == sprint.Id,
                cursor,
                BatchSize,
                ct);
            foreach (var scope in page.Items)
            {
                var item = await workItems.SelectAsync(x => x.Id == scope.WorkItemId, ct)
                    ?? throw new ConflictException("SPRINT_SCOPE_ITEM_MISSING", "A committed sprint work item is missing.");
                var completed = item.CompletedAt is not null;
                var itemCarryoverId = !completed && !item.Archived ? carryover?.Id : null;
                await completionSnapshots.CreateAsync(new SprintCompletionSnapshotDocument
                {
                    Id = SnapshotId(sprint.Id, scope.WorkItemId),
                    SprintId = sprint.Id,
                    ProjectId = sprint.ProjectId,
                    WorkItemId = scope.WorkItemId,
                    CommittedPoints = scope.EstimatePoints,
                    Completed = completed,
                    CompletedAt = item.CompletedAt,
                    CarryoverSprintId = itemCarryoverId,
                    CapturedAt = now
                }, ct);
                if (completed)
                {
                    completedItems++;
                    completedPoints += scope.EstimatePoints;
                }
                else if (itemCarryoverId is not null)
                {
                    item.SprintId = itemCarryoverId;
                    item.UpdatedAt = now;
                    await SaveWorkItemAsync(item, ct);
                    carryoverItems++;
                    carryoverPoints += scope.EstimatePoints;
                }
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        SprintAggregate.Rehydrate(sprint).Complete(
            completedItems,
            completedPoints,
            carryoverItems,
            carryoverPoints,
            now);
        await SaveSprintAsync(sprint, ct);
        await audit.WriteAsync(
            "SprintCompleted",
            "Sprint",
            sprint.Id,
            SprintStatuses.Active,
            $"{completedItems}|{completedPoints}|{carryoverItems}|{carryoverPoints}",
            command.CorrelationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
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

    private async Task SaveSprintAsync(SprintDocument sprint, CancellationToken ct)
    {
        var expectedVersion = expectedVersions?.ExpectedVersion ?? sprint.Version;
        var result = await sprints.ReplaceByVersionAsync(x => x.Id == sprint.Id, sprint, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("SPRINT_CONCURRENCY_CONFLICT", "Sprint changed concurrently; reload and retry.");
        }

        sprint.Version = result.Version!.Value;
    }

    private async Task SaveWorkItemAsync(WorkItemDocument item, CancellationToken ct)
    {
        var result = await workItems.ReplaceByVersionAsync(x => x.Id == item.Id, item, item.Version, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_CONCURRENCY_CONFLICT", "Work item changed concurrently; reload and retry.");
        }

        item.Version = result.Version!.Value;
    }

    private void EnsureBatchLimit(int batches)
    {
        if (batches > MaxBatches)
        {
            throw new ConflictException("SPRINT_BATCH_LIMIT", "Sprint operation exceeded its bounded batch limit.");
        }
    }

    private static void EnsurePlanned(SprintDocument sprint)
    {
        if (sprint.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_PLANNING_CLOSED", "Only a planned sprint can change scope.");
        }
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string SnapshotId(string sprintId, string workItemId) => $"{sprintId}:{workItemId}";

    private static SprintResponse ToResponse(SprintDocument sprint) =>
        new(
            sprint.Id,
            sprint.ProjectId,
            sprint.Name,
            sprint.Goal,
            DateOnly.FromDateTime(sprint.StartAtUtc.UtcDateTime),
            DateOnly.FromDateTime(sprint.EndAtUtc.UtcDateTime),
            sprint.Status,
            sprint.CommittedItems,
            sprint.CommittedPoints,
            sprint.CompletedItems,
            sprint.CompletedPoints,
            sprint.CarryoverItems,
            sprint.CarryoverPoints,
            sprint.StartedAt,
            sprint.CompletedAt,
            sprint.Version);
}
