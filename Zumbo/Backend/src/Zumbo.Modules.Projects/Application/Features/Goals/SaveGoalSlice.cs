using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class SaveGoalSlice(
    GoalReadAccess access,
    GoalMutationPersistence persistence,
    IDocumentRepository<GoalDocument> goals,
    IGoalDirectory directory,
    IGoalAuditWriter audit,
    IClock clock)
{
    internal async Task<GoalResponse> HandleAsync(SaveGoalCommand command, CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var normalized = GoalRequestNormalizer.Normalize(command.Request);
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
        if (string.IsNullOrWhiteSpace(command.GoalId))
        {
            goal = new GoalDocument
            {
                OrganizationId = actor.OrganizationId,
                OwnerUserId = actor.UserId,
                CreatedAt = now
            };
            GoalMutationMapper.Apply(goal, normalized, now);
            goal = await goals.CreateAsync(goal, ct);
            await audit.WriteAsync(
                "GoalCreated", goal.Id, null, goal.Name, command.CorrelationId, ct);
        }
        else
        {
            goal = await access.GetDocumentAsync(command.GoalId, includeArchived: false, ct);
            GoalReadAccess.EnsureOwner(goal, actor.UserId);
            var oldName = goal.Name;
            GoalMutationMapper.Apply(goal, normalized, now);
            await persistence.ReplaceAsync(goal, ct);
            await audit.WriteAsync(
                "GoalUpdated", goal.Id, oldName, goal.Name, command.CorrelationId, ct);
        }
        return GoalResponseMapper.ToResponse(goal, actor.UserId);
    }
}
