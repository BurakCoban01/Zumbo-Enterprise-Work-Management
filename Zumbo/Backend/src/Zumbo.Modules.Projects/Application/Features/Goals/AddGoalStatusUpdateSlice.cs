using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class AddGoalStatusUpdateSlice(
    GoalReadAccess access,
    GoalMutationPersistence persistence,
    IGoalAuditWriter audit,
    IClock clock)
{
    internal async Task<GoalResponse> HandleAsync(
        AddGoalStatusUpdateCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var goal = await access.GetDocumentAsync(command.GoalId, includeArchived: false, ct);
        GoalReadAccess.EnsureOwner(goal, actor.UserId);
        var request = command.Request;
        var status = GoalValidation.Allowed(request.Status, GoalStatuses.Allowed, "Goal status");
        var health = GoalValidation.Allowed(request.Health, GoalHealth.Allowed, "Goal health");
        var confidence = GoalValidation.Confidence(request.Confidence, "Goal confidence");
        var note = GoalValidation.Required(request.Note, "Status update note", 1000);
        goal.Status = status;
        goal.Health = health;
        goal.Confidence = confidence;
        goal.StatusUpdates.Insert(0, new GoalStatusUpdateDocument
        {
            Status = status,
            Health = health,
            Confidence = confidence,
            Note = note,
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        });
        ProjectHistoryRetentionPolicy.RetainMostRecent(
            goal.StatusUpdates,
            ProjectHistoryRetentionPolicy.MaximumGoalStatusUpdates);
        goal.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "GoalStatusUpdated", goal.Id, null, status, command.CorrelationId, ct);
        return GoalResponseMapper.ToResponse(goal, actor.UserId);
    }
}
