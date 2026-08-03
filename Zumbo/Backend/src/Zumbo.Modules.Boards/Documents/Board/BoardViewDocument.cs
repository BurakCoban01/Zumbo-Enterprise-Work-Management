using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Boards;

public sealed class BoardViewDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public bool IsShared { get; set; }
    public string SwimlaneMode { get; set; } = "None";
    public BoardFilterDocument Filter { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
