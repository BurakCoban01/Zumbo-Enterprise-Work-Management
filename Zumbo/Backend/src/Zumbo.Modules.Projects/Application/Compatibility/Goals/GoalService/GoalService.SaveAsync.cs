using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> SaveAsync(
        string? goalId,
        SaveGoalRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var normalized = Normalize(request);
        normalized.ViewerUserIds.Remove(actor.UserId);
        await directory.EnsureOrganizationUsersAsync(
            actor.OrganizationId,
            normalized.ViewerUserIds.Append(actor.UserId).ToList(),
            ct);
        await directory.EnsureSourcesReadableAsync(
            actor.OrganizationId,
            normalized.InitiativeLinks,
            normalized.ProjectIds,
            ct);

        GoalDocument goal;
        var now = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(goalId))
        {
            goal = new GoalDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            Apply(goal, normalized, now);
            goal = await goals.CreateAsync(goal, ct);
            await audit.WriteAsync(
                "GoalCreated", goal.Id, null, goal.Name, correlationId, ct);
        }
        else
        {
            goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
            EnsureOwner(goal, actor.UserId);
            var oldName = goal.Name;
            Apply(goal, normalized, now);
            await ReplaceAsync(goal, ct);
            await audit.WriteAsync(
                "GoalUpdated", goal.Id, oldName, goal.Name, correlationId, ct);
        }
        return ToResponse(goal, actor.UserId);
    }
}
