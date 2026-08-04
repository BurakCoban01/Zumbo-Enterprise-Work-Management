using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;
public sealed record CompleteSprintRequest(string? CarryoverSprintId);
