using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    public async Task<SprintResponse> GetAsync(string sprintId, CancellationToken ct)
    {
        var sprint = await GetSprintAsync(sprintId, ct);
        await EnsurePermissionAsync(sprint.ProjectId, PermissionCatalog.WorkItemView, ct);
        return ToResponse(sprint);
    }
}
