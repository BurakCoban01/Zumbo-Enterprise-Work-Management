using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService
{
    private void EnsureBatchLimit(int batches)
    {
        if (batches > MaxBatches)
        {
            throw new ConflictException("SPRINT_BATCH_LIMIT", "Sprint operation exceeded its bounded batch limit.");
        }
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

    private static void EnsurePlanned(SprintDocument sprint)
    {
        if (sprint.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_PLANNING_CLOSED", "Only a planned sprint can change scope.");
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

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
}
