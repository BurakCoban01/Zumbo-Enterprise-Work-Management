using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class CapacityAllocationDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public DateTimeOffset StartDateUtc { get; set; }
    public DateTimeOffset EndDateUtc { get; set; }
    public decimal Percent { get; set; }
}
