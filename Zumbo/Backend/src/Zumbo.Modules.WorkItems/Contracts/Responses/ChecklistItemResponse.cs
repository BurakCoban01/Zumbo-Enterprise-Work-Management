using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record ChecklistItemResponse(string Id, string Text, bool Completed);
