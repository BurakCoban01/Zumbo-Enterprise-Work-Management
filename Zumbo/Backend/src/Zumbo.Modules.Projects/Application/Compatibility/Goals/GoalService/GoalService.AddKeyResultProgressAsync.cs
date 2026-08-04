using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> AddKeyResultProgressAsync(
        string goalId,
        string keyResultId,
        AddKeyResultProgressRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureVisible(goal, actor.UserId);
        var keyResult = goal.KeyResults.SingleOrDefault(item => item.Id == keyResultId)
            ?? throw new NotFoundException("KEY_RESULT_NOT_FOUND", "Key result was not found.");
        if (goal.OwnerUserId != actor.UserId && keyResult.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the goal or key-result owner can publish progress.");
        EnsureFinite(request.CurrentValue, "Key-result current value");
        var confidence = Confidence(request.Confidence, "Key-result confidence");
        var note = Required(request.Note, "Progress note", 1000);
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
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "KeyResultProgressUpdated",
            goal.Id,
            previous.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.CurrentValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            ct);
        return ToResponse(goal, actor.UserId);
    }
}
