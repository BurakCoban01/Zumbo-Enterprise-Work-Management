using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record CapacityMemberResponse(
    string UserId,
    string? TeamId,
    decimal WeeklyCapacityHours);
