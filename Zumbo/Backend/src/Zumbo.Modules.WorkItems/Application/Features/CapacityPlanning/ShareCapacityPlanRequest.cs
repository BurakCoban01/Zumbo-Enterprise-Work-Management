using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record ShareCapacityPlanRequest(IReadOnlyCollection<string> ViewerUserIds);
