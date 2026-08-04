using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemFieldDistributionResponse(
    string ProjectId,
    string Field,
    int TotalItems,
    int MissingItems,
    IReadOnlyCollection<WorkItemFieldDistributionEntry> Values);
