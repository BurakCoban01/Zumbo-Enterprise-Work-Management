using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

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
}
