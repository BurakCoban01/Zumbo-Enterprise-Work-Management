using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Domain;

public sealed class SprintAggregate
{
    private readonly SprintDocument state;

    private SprintAggregate(SprintDocument state) => this.state = state;

    public static SprintAggregate Rehydrate(SprintDocument state) => new(state);

    public void Start(int committedItems, decimal committedPoints, DateTimeOffset now)
    {
        if (state.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_START_INVALID_STATE", "Only a planned sprint can be started.");
        }

        if (committedItems == 0)
        {
            throw new ConflictException("SPRINT_SCOPE_EMPTY", "A sprint requires at least one planned work item before start.");
        }

        state.Status = SprintStatuses.Active;
        state.CommittedItems = committedItems;
        state.CommittedPoints = committedPoints;
        state.StartedAt = now;
        state.UpdatedAt = now;
    }

    public void Complete(
        int completedItems,
        decimal completedPoints,
        int carryoverItems,
        decimal carryoverPoints,
        DateTimeOffset now)
    {
        if (state.Status != SprintStatuses.Active)
        {
            throw new ConflictException("SPRINT_COMPLETE_INVALID_STATE", "Only an active sprint can be completed.");
        }

        state.Status = SprintStatuses.Completed;
        state.CompletedItems = completedItems;
        state.CompletedPoints = completedPoints;
        state.CarryoverItems = carryoverItems;
        state.CarryoverPoints = carryoverPoints;
        state.CompletedAt = now;
        state.UpdatedAt = now;
    }
}
