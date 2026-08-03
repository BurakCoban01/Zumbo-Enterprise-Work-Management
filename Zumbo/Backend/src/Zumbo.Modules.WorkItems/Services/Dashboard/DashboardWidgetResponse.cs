using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardWidgetResponse(
    string Id,
    string Type,
    string Title,
    int Column,
    int Row,
    int Width,
    int Height,
    string? ProjectId,
    DashboardFilterRequest? Filter);
