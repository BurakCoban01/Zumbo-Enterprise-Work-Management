using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class GoalReadAccess(
    IDocumentRepository<GoalDocument> goals,
    ICurrentUser currentUser)
{
    internal (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required."));

    internal async Task<GoalDocument> GetDocumentAsync(
        string goalId,
        bool includeArchived,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        return await goals.SelectAsync(
            item => item.Id == goalId
                && item.OrganizationId == actor.OrganizationId
                && (includeArchived || !item.Archived),
            ct)
            ?? throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
    }

    internal static bool CanView(GoalDocument goal, string userId) =>
        goal.OwnerUserId == userId
        || goal.ViewerUserIds.Contains(userId, StringComparer.Ordinal)
        || goal.KeyResults.Any(item => item.OwnerUserId == userId);

    internal static void EnsureVisible(GoalDocument goal, string userId)
    {
        if (!CanView(goal, userId))
            throw new NotFoundException("GOAL_NOT_FOUND", "Goal was not found.");
    }

    internal static void EnsureOwner(GoalDocument goal, string userId)
    {
        EnsureVisible(goal, userId);
        if (goal.OwnerUserId != userId)
            throw new ForbiddenException("Only the goal owner can change this goal.");
    }
}
