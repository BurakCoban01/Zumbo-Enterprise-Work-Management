using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityMemberRequest(
    string UserId,
    string? TeamId,
    decimal WeeklyCapacityHours);
