using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Sprints;

internal sealed class CreateSprintSlice(
    IDocumentRepository<SprintDocument> sprints,
    IProjectPermissionChecker permissionChecker,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IWorkItemCacheInvalidationPublisher cacheInvalidationPublisher)
{
    internal async Task<SprintResponse> HandleAsync(CreateSprintCommand command, CancellationToken ct)
    {
        var request = command.Request;
        Validate(request);
        await EnsurePermissionAsync(request.ProjectId, ct);
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
            command.CorrelationId,
            ct);
        await cacheInvalidationPublisher.InvalidateProjectAsync(sprint.ProjectId, ct);
        return ToResponse(sprint);
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
        var lease = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.LeaseSeconds, 5, 300));
        var wait = TimeSpan.FromSeconds(Math.Clamp(lockOptions.Value.WaitSeconds, 0, 30));
        return await distributedLocks.TryAcquireAsync("project-structure:" + projectId, lease, wait, ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The project structure is busy; retry the operation.");
    }

    private static void Validate(CreateSprintRequest request)
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

    private static DateTimeOffset AtStartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset AtEndOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

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
