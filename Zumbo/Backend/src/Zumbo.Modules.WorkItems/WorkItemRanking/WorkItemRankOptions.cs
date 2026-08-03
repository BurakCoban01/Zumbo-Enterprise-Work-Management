using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemRankOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerRun { get; set; } = 1_000;
}
