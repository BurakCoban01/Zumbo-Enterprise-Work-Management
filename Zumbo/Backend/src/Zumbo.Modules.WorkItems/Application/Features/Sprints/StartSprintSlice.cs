using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class StartSprintSlice(
    IDocumentRepository<SprintDocument> sprints,
    IDocumentRepository<SprintScopeSnapshotDocument> scopeSnapshots,
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

    internal async Task<SprintResponse> HandleAsync(StartSprintCommand command, CancellationToken ct)
    {
        var initial = await GetSprintAsync(command.SprintId, ct);
        await EnsurePermissionAsync(initial.ProjectId, ct);
        await using var projectLock = await AcquireProjectLockAsync(initial.ProjectId, ct);
        var sprint = await GetSprintAsync(command.SprintId, ct);
        EnsurePlanned(sprint);
        if (await sprints.ExistsByFilterAsync(
                item => item.ProjectId == sprint.ProjectId
                    && item.Status == SprintStatuses.Active
                    && item.Id != sprint.Id,
                ct))
        {
            throw new ConflictException("SPRINT_ACTIVE_EXISTS", "Only one sprint can be active in a project.");
        }

        var now = clock.UtcNow;
        var count = 0;
        var points = 0m;
        var batches = 0;
        string? cursor = null;
        do
        {
            EnsureBatchLimit(++batches);
            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == sprint.ProjectId
                    && item.SprintId == sprint.Id
                    && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                await scopeSnapshots.CreateAsync(new SprintScopeSnapshotDocument
                {
                    Id = SnapshotId(sprint.Id, item.Id),
                    SprintId = sprint.Id,
                    ProjectId = sprint.ProjectId,
                    WorkItemId = item.Id,
                    Title = item.Title,
                    EstimatePoints = item.EstimatePoints,
                    CapturedAt = now
                }, ct);
                count++;
                points += item.EstimatePoints;
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        SprintAggregate.Rehydrate(sprint).Start(count, points, now);
        await SaveSprintAsync(sprint, ct);
        await audit.WriteAsync(
            "SprintStarted",
            "Sprint",
            sprint.Id,
            SprintStatuses.Planned,
            $"{count}|{points}",
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
