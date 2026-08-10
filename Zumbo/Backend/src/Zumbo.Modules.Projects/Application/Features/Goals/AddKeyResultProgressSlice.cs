using System.Globalization;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class AddKeyResultProgressSlice(
    GoalReadAccess access,
    GoalMutationPersistence persistence,
    IGoalAuditWriter audit,
    IClock clock)
{
    internal async Task<GoalResponse> HandleAsync(
        AddKeyResultProgressCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var goal = await access.GetDocumentAsync(command.GoalId, includeArchived: false, ct);
        GoalReadAccess.EnsureVisible(goal, actor.UserId);
        var keyResult = goal.KeyResults.SingleOrDefault(item => item.Id == command.KeyResultId)
            ?? throw new NotFoundException("KEY_RESULT_NOT_FOUND", "Key result was not found.");
        if (goal.OwnerUserId != actor.UserId && keyResult.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the goal or key-result owner can publish progress.");

        var request = command.Request;
        GoalValidation.EnsureFinite(request.CurrentValue, "Key-result current value");
        var confidence = GoalValidation.Confidence(request.Confidence, "Key-result confidence");
        var note = GoalValidation.Required(request.Note, "Progress note", 1000);
        var previous = keyResult.CurrentValue;
        keyResult.CurrentValue = request.CurrentValue;
        keyResult.Confidence = confidence;
        keyResult.ProgressUpdates.Insert(0, new KeyResultProgressUpdateDocument
        {
            PreviousValue = previous,
            CurrentValue = request.CurrentValue,
            Confidence = confidence,
            Note = note,
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        });
        ProjectHistoryRetentionPolicy.RetainMostRecent(
            keyResult.ProgressUpdates,
            ProjectHistoryRetentionPolicy.MaximumKeyResultProgressUpdates);
        goal.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "KeyResultProgressUpdated",
            goal.Id,
            previous.ToString(CultureInfo.InvariantCulture),
            request.CurrentValue.ToString(CultureInfo.InvariantCulture),
            command.CorrelationId,
            ct);
        return GoalResponseMapper.ToResponse(goal, actor.UserId);
    }
}
