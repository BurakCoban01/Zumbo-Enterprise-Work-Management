using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private async Task SaveWorkItemAsync(WorkItemDocument item, bool useRequestVersion, CancellationToken ct)
    {
        var expectedVersion = useRequestVersion
            ? expectedVersions?.ExpectedVersion ?? item.Version
            : item.Version;
        var result = await workItems.ReplaceByVersionAsync(x => x.Id == item.Id, item, expectedVersion, ct);
        if (!result.Found)
        {
            throw new ConflictException("WORK_ITEM_CONCURRENCY_CONFLICT", "Work item changed concurrently; reload and retry.");
        }

        item.Version = result.Version!.Value;
    }
}
