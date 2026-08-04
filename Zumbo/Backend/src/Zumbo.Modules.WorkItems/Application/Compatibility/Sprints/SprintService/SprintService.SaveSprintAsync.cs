using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private async Task SaveSprintAsync(SprintDocument sprint, CancellationToken ct)
    {
        var expectedVersion = expectedVersions?.ExpectedVersion ?? sprint.Version;
        var result = await sprints.ReplaceByVersionAsync(x => x.Id == sprint.Id, sprint, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("SPRINT_CONCURRENCY_CONFLICT", "Sprint changed concurrently; reload and retry.");
        }

        sprint.Version = result.Version!.Value;
    }
}
