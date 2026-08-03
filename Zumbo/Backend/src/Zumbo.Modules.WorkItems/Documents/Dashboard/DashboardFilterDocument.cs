using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class DashboardFilterDocument
{
    public int RangeDays { get; set; } = 30;
    public int DueRiskDays { get; set; } = 30;
    public string? AssigneeUserId { get; set; }
    public string? TeamId { get; set; }
    public List<string> Statuses { get; set; } = [];
}
