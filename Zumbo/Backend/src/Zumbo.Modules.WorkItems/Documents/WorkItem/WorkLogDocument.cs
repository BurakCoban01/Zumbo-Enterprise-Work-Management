using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkLogDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
