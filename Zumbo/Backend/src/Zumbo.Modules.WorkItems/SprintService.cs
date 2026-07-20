using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class SprintOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerOperation { get; set; } = 1_000;
}

public interface IWorkItemSprintPolicy
{
    Task EnsurePlanningAllowedAsync(
        string projectId,
        string? currentSprintId,
        string? targetSprintId,
        CancellationToken ct);
}

public sealed class NoOpWorkItemSprintPolicy : IWorkItemSprintPolicy
{
    public Task EnsurePlanningAllowedAsync(
        string projectId,
        string? currentSprintId,
        string? targetSprintId,
        CancellationToken ct) => Task.CompletedTask;
}

public sealed class SprintService(
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
    IWorkItemReadModelCache readModelCache,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher,
    IOptions<WorkItemReadModelCacheOptions> readModelCacheOptions,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerOperation, 1, 10_000);
    private TimeSpan ReadModelTtl => TimeSpan.FromSeconds(
        Math.Clamp(readModelCacheOptions.Value.TtlSeconds, 1, 300));

    public async Task<SprintResponse> CreateAsync(
        CreateSprintRequest request,
        string correlationId,
        CancellationToken ct)
    {
        ValidateCreate(request);
        await EnsurePermissionAsync(request.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(request.ProjectId, ct);
        var normalizedName = request.Name.Trim();
        if (await sprints.ExistsByFilterAsync(
                sprint => sprint.ProjectId == request.ProjectId && sprint.Name == normalizedName,
                ct))
        {
            throw new ConflictException("SPRINT_NAME_EXISTS", "A sprint with this name already exists in the project.");
        }

        var now = clock.UtcNow;
        var sprint = await sprints.CreateAsync(new SprintDocument
        {
            ProjectId = request.ProjectId,
            Name = normalizedName,
            Goal = request.Goal?.Trim() ?? string.Empty,
            StartAtUtc = AtStartOfDay(request.StartDate),
            EndAtUtc = AtEndOfDay(request.EndDate),
            CreatedAt = now,
            UpdatedAt = now
        }, ct);
        await audit.WriteAsync(
            "SprintCreated",
            "Sprint",
            sprint.Id,
            null,
            sprint.Name,
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
    }

    public async Task<SprintResponse> GetAsync(string sprintId, CancellationToken ct)
    {
        var sprint = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(sprint.ProjectId, PermissionCatalog.WorkItemView, ct);
        return ToResponse(sprint);
    }

    public async Task<SprintCursorPageResponse> ListAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var page = await sprints.ListByCursorAsync(
            sprint => sprint.ProjectId == projectId,
            NormalizeOptional(after),
            Math.Clamp(pageSize, 1, 100),
            ct);
        return new SprintCursorPageResponse(page.Items.Select(ToResponse).ToList(), page.NextCursor);
    }

    public async Task<SprintBacklogPageResponse> BacklogAsync(
        string projectId,
        string? after,
        int pageSize,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var page = await workItems.ListByCursorAsync(
            item => item.ProjectId == projectId && !item.Archived && item.SprintId == null,
            NormalizeOptional(after),
            Math.Clamp(pageSize, 1, 100),
            ct);
        return new SprintBacklogPageResponse(
            page.Items.Select(item => new SprintBacklogItemResponse(
                item.Id,
                item.Title,
                item.Type,
                item.Priority,
                item.EstimatePoints,
                item.Rank,
                item.Version)).ToList(),
            page.NextCursor);
    }

    public async Task<SprintPlannedItemResponse> PlanAsync(
        string sprintId,
        string workItemId,
        PlanSprintWorkItemRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var estimate = NormalizeEstimate(request.EstimatePoints);
        var initialSprint = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initialSprint.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initialSprint.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        EnsurePlanned(sprint);
        var item = await workItems.SelectAsync(x => x.Id == workItemId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (item.ProjectId != sprint.ProjectId)
        {
            throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Work item and sprint must belong to the same project.");
        }

        if (item.SprintId is not null && item.SprintId != sprint.Id)
        {
            throw new ConflictException("WORK_ITEM_ALREADY_PLANNED", "Work item is already planned in another sprint.");
        }

        item.SprintId = sprint.Id;
        item.EstimatePoints = estimate;
        item.UpdatedAt = clock.UtcNow;
        await SaveWorkItemAsync(item, useRequestVersion: true, ct);
        await audit.WriteAsync(
            "SprintWorkItemPlanned",
            "WorkItem",
            item.Id,
            null,
            sprint.Id,
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return new SprintPlannedItemResponse(item.Id, item.SprintId, item.EstimatePoints, item.Version);
    }

    public async Task<SprintPlannedItemResponse> UnplanAsync(
        string sprintId,
        string workItemId,
        string correlationId,
        CancellationToken ct)
    {
        var initialSprint = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initialSprint.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initialSprint.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        EnsurePlanned(sprint);
        var item = await workItems.SelectAsync(x => x.Id == workItemId && !x.Archived, ct)
            ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        if (item.ProjectId != sprint.ProjectId || item.SprintId != sprint.Id)
        {
            throw new ConflictException("WORK_ITEM_NOT_IN_SPRINT", "Work item is not planned in this sprint.");
        }

        item.SprintId = null;
        item.UpdatedAt = clock.UtcNow;
        await SaveWorkItemAsync(item, useRequestVersion: true, ct);
        await audit.WriteAsync(
            "SprintWorkItemUnplanned",
            "WorkItem",
            item.Id,
            sprint.Id,
            null,
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return new SprintPlannedItemResponse(item.Id, null, item.EstimatePoints, item.Version);
    }

    public async Task<SprintResponse> StartAsync(
        string sprintId,
        string correlationId,
        CancellationToken ct)
    {
        var initial = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initial.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initial.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
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
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
    }

    public async Task<SprintResponse> CompleteAsync(
        string sprintId,
        CompleteSprintRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var initial = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(initial.ProjectId, PermissionCatalog.WorkItemUpdate, ct);
        await using var projectLock = await AcquireProjectLockAsync(initial.ProjectId, ct);
        var sprint = await GetSprintAsync(sprintId, ct);
        if (sprint.Status != SprintStatuses.Active)
        {
            throw new ConflictException("SPRINT_COMPLETE_INVALID_STATE", "Only an active sprint can be completed.");
        }

        var carryoverId = NormalizeOptional(request.CarryoverSprintId);
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
                    await SaveWorkItemAsync(item, useRequestVersion: false, ct);
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
            correlationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
    }

    public async Task<IReadOnlyList<SprintBurndownPointResponse>> BurndownAsync(
        string projectId,
        string sprintId,
        DateOnly? requestedStart,
        DateOnly? requestedEnd,
        CancellationToken ct) =>
        (await BurndownSnapshotAsync(projectId, sprintId, requestedStart, requestedEnd, ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<SprintBurndownPointResponse>>> BurndownSnapshotAsync(
        string projectId,
        string sprintId,
        DateOnly? requestedStart,
        DateOnly? requestedEnd,
        CancellationToken ct)
    {
        var sprint = await GetSprintAsync(sprintId, ct);
        if (sprint.ProjectId != projectId)
        {
            throw new ConflictException("SPRINT_PROJECT_MISMATCH", "Sprint does not belong to the requested project.");
        }

        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var sprintStart = DateOnly.FromDateTime(sprint.StartAtUtc.UtcDateTime);
        var sprintEnd = DateOnly.FromDateTime(sprint.EndAtUtc.UtcDateTime);
        var start = requestedStart ?? sprintStart;
        var end = requestedEnd ?? sprintEnd;
        if (start < sprintStart || end > sprintEnd || end < start || end.DayNumber - start.DayNumber + 1 > 60)
        {
            throw new ValidationException("Burndown range must be within the sprint and cannot exceed 60 days.");
        }

        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<SprintBurndownPointResponse>>(
            projectId,
            $"sprint-burndown:{sprintId}:{start:yyyyMMdd}:{end:yyyyMMdd}",
            ReadModelTtl,
            async token =>
            {
                var dayCount = end.DayNumber - start.DayNumber + 1;
                var committedItems = 0;
                var committedPoints = 0m;
                var completedPointsByDay = new decimal[dayCount];
                var completedItemsByDay = new int[dayCount];
                var batches = 0;
                string? cursor = null;
                do
                {
                    EnsureBatchLimit(++batches);
                    var page = await scopeSnapshots.ListByCursorAsync(
                        snapshot => snapshot.SprintId == sprint.Id,
                        cursor,
                        BatchSize,
                        token);
                    foreach (var scope in page.Items)
                    {
                        committedItems++;
                        committedPoints += scope.EstimatePoints;
                        DateTimeOffset? completedAt;
                        if (sprint.Status == SprintStatuses.Completed)
                        {
                            completedAt = (await completionSnapshots.SelectAsync(
                                snapshot => snapshot.Id == scope.Id && snapshot.Completed,
                                token))?.CompletedAt;
                        }
                        else
                        {
                            completedAt = (await workItems.SelectAsync(
                                item => item.Id == scope.WorkItemId,
                                token))?.CompletedAt;
                        }

                        if (completedAt is not null)
                        {
                            var date = DateOnly.FromDateTime(completedAt.Value.UtcDateTime);
                            if (date <= end)
                            {
                                var offset = Math.Max(0, date.DayNumber - start.DayNumber);
                                completedPointsByDay[offset] += scope.EstimatePoints;
                                completedItemsByDay[offset]++;
                            }
                        }
                    }

                    cursor = page.NextCursor;
                }
                while (cursor is not null);

                var result = new List<SprintBurndownPointResponse>(dayCount);
                var completedPoints = 0m;
                var completedItems = 0;
                for (var offset = 0; offset < dayCount; offset++)
                {
                    completedPoints += completedPointsByDay[offset];
                    completedItems += completedItemsByDay[offset];
                    result.Add(new SprintBurndownPointResponse(
                        start.AddDays(offset),
                        committedPoints - completedPoints,
                        committedItems - completedItems));
                }

                return result;
            },
            ct);
    }

    public async Task<IReadOnlyList<SprintVelocityResponse>> VelocityAsync(
        string projectId,
        int sprintCount,
        CancellationToken ct) =>
        (await VelocitySnapshotAsync(projectId, sprintCount, ct)).Data;

    public async Task<WorkItemReportSnapshot<IReadOnlyList<SprintVelocityResponse>>> VelocitySnapshotAsync(
        string projectId,
        int sprintCount,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var normalizedCount = Math.Clamp(sprintCount, 1, 12);
        return await readModelCache.GetOrCreateSnapshotAsync<IReadOnlyList<SprintVelocityResponse>>(
            projectId,
            $"sprint-velocity:{normalizedCount}",
            ReadModelTtl,
            async token =>
            {
                var completed = await sprints.ListByFilterAsync(
                    sprint => sprint.ProjectId == projectId && sprint.Status == SprintStatuses.Completed,
                    sprint => sprint.CompletedAt!,
                    orderDescending: true,
                    pageSize: normalizedCount,
                    cancellationToken: token);
                return completed.Select(sprint => new SprintVelocityResponse(
                    sprint.Id,
                    sprint.CompletedItems,
                    sprint.CompletedPoints)).ToList();
            },
            ct);
    }

    private async Task<ProjectResourceAuthorization> EnsurePermissionAsync(
        string projectId,
        string permission,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        return await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    private async Task<SprintDocument> GetSprintAsync(string sprintId, CancellationToken ct) =>
        await sprints.SelectAsync(sprint => sprint.Id == sprintId, ct)
        ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");

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

    private async Task SaveWorkItemAsync(WorkItemDocument item, bool useRequestVersion, CancellationToken ct)
    {
        var expectedVersion = useRequestVersion
            ? expectedVersions?.ExpectedVersion ?? item.Version
            : item.Version;
        var result = await workItems.ReplaceByVersionAsync(x => x.Id == item.Id, item, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_CONCURRENCY_CONFLICT", "Work item changed concurrently; reload and retry.");
        }

        item.Version = result.Version!.Value;
    }

    private async Task<IAsyncDisposable> AcquireProjectLockAsync(string projectId, CancellationToken ct)
    {
        var lease = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.LeaseSeconds, 5, 300));
        var wait = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.WaitSeconds, 0, 30));
        return await distributedLocks.TryAcquireAsync("project-structure:" + projectId, lease, wait, ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The project structure is busy; retry the operation.");
    }

    private void EnsureBatchLimit(int batches)
    {
        if (batches > MaxBatches)
        {
            throw new ConflictException("SPRINT_BATCH_LIMIT", "Sprint operation exceeded its bounded batch limit.");
        }
    }

    private static void ValidateCreate(CreateSprintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new ValidationException("Project id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            throw new ValidationException("Sprint name is required and cannot exceed 120 characters.");
        }

        if (request.Goal?.Trim().Length > 500)
        {
            throw new ValidationException("Sprint goal cannot exceed 500 characters.");
        }

        var days = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        if (days is < 1 or > 60)
        {
            throw new ValidationException("Sprint duration must be between 1 and 60 days.");
        }
    }

    private static decimal NormalizeEstimate(decimal? estimate)
    {
        var value = estimate ?? 0;
        if (value is < 0 or > 1_000)
        {
            throw new ValidationException("Estimate points must be between 0 and 1000.");
        }

        return value;
    }

    private static void EnsurePlanned(SprintDocument sprint)
    {
        if (sprint.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_PLANNING_CLOSED", "Only a planned sprint can change scope.");
        }
    }

    private static string SnapshotId(string sprintId, string workItemId) => $"{sprintId}:{workItemId}";
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTimeOffset AtStartOfDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static DateTimeOffset AtEndOfDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

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
