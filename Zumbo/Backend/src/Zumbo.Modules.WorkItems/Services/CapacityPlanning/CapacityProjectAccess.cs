using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityProjectAccess(
    string Id,
    string Key,
    string Name,
    bool Available);
