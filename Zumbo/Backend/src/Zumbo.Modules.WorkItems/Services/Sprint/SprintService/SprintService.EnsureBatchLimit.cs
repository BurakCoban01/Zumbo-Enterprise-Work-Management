using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private void EnsureBatchLimit(int batches)
    {
        if (batches > MaxBatches)
        {
            throw new ConflictException("SPRINT_BATCH_LIMIT", "Sprint operation exceeded its bounded batch limit.");
        }
    }
}
