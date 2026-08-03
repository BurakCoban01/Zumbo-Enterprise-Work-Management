using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> AddStatusUpdateAsync(
        string goalId,
        AddGoalStatusUpdateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureOwner(goal, actor.UserId);
        var status = Allowed(request.Status, GoalStatuses.Allowed, "Goal status");
        var health = Allowed(request.Health, GoalHealth.Allowed, "Goal health");
        var confidence = Confidence(request.Confidence, "Goal confidence");
        var note = Required(request.Note, "Status update note", 1000);
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
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "GoalStatusUpdated", goal.Id, null, status, correlationId, ct);
        return ToResponse(goal, actor.UserId);
    }
}
