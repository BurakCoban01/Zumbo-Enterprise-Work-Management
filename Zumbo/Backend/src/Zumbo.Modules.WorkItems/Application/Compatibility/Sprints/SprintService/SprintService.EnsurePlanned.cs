using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private static void EnsurePlanned(SprintDocument sprint)
    {
        if (sprint.Status != SprintStatuses.Planned)
        {
            throw new ConflictException("SPRINT_PLANNING_CLOSED", "Only a planned sprint can change scope.");
        }
    }
}
