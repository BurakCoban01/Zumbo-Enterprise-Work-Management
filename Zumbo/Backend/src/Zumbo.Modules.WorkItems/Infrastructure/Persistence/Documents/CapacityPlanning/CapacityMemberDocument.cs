using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class CapacityMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string? TeamId { get; set; }
    public decimal WeeklyCapacityHours { get; set; }
}
