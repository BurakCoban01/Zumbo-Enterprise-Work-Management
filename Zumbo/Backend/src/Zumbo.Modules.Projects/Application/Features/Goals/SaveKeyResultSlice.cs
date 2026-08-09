using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Goals;

internal sealed class SaveKeyResultSlice(
    GoalReadAccess access,
    GoalMutationPersistence persistence,
    IGoalDirectory directory,
    IGoalAuditWriter audit,
    IClock clock)
{
    private const int MaximumKeyResults = 50;

    internal async Task<GoalResponse> HandleAsync(
        SaveKeyResultCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var goal = await access.GetDocumentAsync(command.GoalId, includeArchived: false, ct);
        GoalReadAccess.EnsureOwner(goal, actor.UserId);
        var normalized = GoalRequestNormalizer.Normalize(command.Request);
        await directory.EnsureOrganizationUsersAsync(
            goal.OrganizationId, [normalized.OwnerUserId], ct);

        KeyResultDocument keyResult;
        if (string.IsNullOrWhiteSpace(command.KeyResultId))
        {
            if (goal.KeyResults.Count >= MaximumKeyResults)
            {
                throw new ValidationException(
                    $"A goal cannot contain more than {MaximumKeyResults} key results.");
            }
            keyResult = new KeyResultDocument { CurrentValue = normalized.InitialValue };
            goal.KeyResults.Add(keyResult);
        }
        else
        {
            keyResult = goal.KeyResults.SingleOrDefault(item => item.Id == command.KeyResultId)
                ?? throw new NotFoundException("KEY_RESULT_NOT_FOUND", "Key result was not found.");
        }
        GoalMutationMapper.Apply(keyResult, normalized);
        goal.UpdatedAt = clock.UtcNow;
        await persistence.ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            string.IsNullOrWhiteSpace(command.KeyResultId)
                ? "KeyResultCreated"
                : "KeyResultUpdated",
            goal.Id,
            null,
            keyResult.Name,
            command.CorrelationId,
            ct);
        return GoalResponseMapper.ToResponse(goal, actor.UserId);
    }
}
