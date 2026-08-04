using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task<GoalResponse> SaveKeyResultAsync(
        string goalId,
        string? keyResultId,
        SaveKeyResultRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureOwner(goal, actor.UserId);
        var normalized = Normalize(request);
        await directory.EnsureOrganizationUsersAsync(
            goal.OrganizationId,
            [normalized.OwnerUserId],
            ct);

        KeyResultDocument keyResult;
        if (string.IsNullOrWhiteSpace(keyResultId))
        {
            if (goal.KeyResults.Count >= MaximumKeyResults)
            {
                throw new ValidationException(
                    $"A goal cannot contain more than {MaximumKeyResults} key results.");
            }
            keyResult = new KeyResultDocument
            {
                CurrentValue = normalized.InitialValue
            };
            goal.KeyResults.Add(keyResult);
        }
        else
        {
            keyResult = goal.KeyResults.SingleOrDefault(item => item.Id == keyResultId)
                ?? throw new NotFoundException("KEY_RESULT_NOT_FOUND", "Key result was not found.");
        }
        Apply(keyResult, normalized);
        goal.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(keyResultId) ? "KeyResultCreated" : "KeyResultUpdated",
            goal.Id,
            null,
            keyResult.Name,
            correlationId,
            ct);
        return ToResponse(goal, actor.UserId);
    }
}
