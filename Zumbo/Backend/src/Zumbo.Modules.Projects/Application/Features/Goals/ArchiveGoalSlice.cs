using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class ArchiveGoalSlice(
    GoalReadAccess access,
    GoalMutationPersistence persistence,
    IGoalAuditWriter audit,
    IClock clock)
{
    internal async Task HandleAsync(ArchiveGoalCommand command, CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var goal = await access.GetDocumentAsync(command.GoalId, includeArchived: false, ct);
        GoalReadAccess.EnsureOwner(goal, actor.UserId);
        goal.Archived = true;
        goal.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "GoalArchived", goal.Id, "Active", "Archived", command.CorrelationId, ct);
    }
}
