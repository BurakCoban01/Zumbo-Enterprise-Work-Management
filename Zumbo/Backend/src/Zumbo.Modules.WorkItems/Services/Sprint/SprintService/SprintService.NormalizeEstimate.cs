using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private static decimal NormalizeEstimate(decimal? estimate)
    {
        var value = estimate ?? 0;
        if (value is < 0 or > 1_000)
        {
            throw new ValidationException("Estimate points must be between 0 and 1000.");
        }

        return value;
    }
}
