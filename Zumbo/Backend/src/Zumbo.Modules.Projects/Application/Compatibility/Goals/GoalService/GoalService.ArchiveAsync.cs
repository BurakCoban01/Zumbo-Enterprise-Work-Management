using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class GoalService{

    public async Task ArchiveAsync(
        string goalId,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var goal = await GetDocumentAsync(goalId, includeArchived: false, ct);
        EnsureOwner(goal, actor.UserId);
        goal.Archived = true;
        goal.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(goal, ct);
        await audit.WriteAsync(
            "GoalArchived", goal.Id, "Active", "Archived", correlationId, ct);
    }
}
