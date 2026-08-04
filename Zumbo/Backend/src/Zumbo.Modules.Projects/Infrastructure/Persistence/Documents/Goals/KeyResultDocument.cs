using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public sealed class KeyResultDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal BaselineValue { get; set; }
    public decimal TargetValue { get; set; }
    public decimal CurrentValue { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Direction { get; set; } = KeyResultDirections.Increase;
    public int? Confidence { get; set; }
    public List<KeyResultProgressUpdateDocument> ProgressUpdates { get; set; } = [];
}
