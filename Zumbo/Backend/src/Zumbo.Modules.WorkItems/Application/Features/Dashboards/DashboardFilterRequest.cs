using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record DashboardFilterRequest(
    int RangeDays = 30,
    int DueRiskDays = 30,
    string? AssigneeUserId = null,
    string? TeamId = null,
    IReadOnlyCollection<string>? Statuses = null);
