using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private async Task<SprintDocument> GetSprintAsync(string sprintId, CancellationToken ct) =>
        await sprints.SelectAsync(sprint => sprint.Id == sprintId, ct)
        ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");
}
