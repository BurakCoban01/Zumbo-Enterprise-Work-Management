using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Boards;

public sealed class BoardColumnDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Todo";
    public int Position { get; set; }
    public int? WipLimit { get; set; }
    public List<string> StatusNames { get; set; } = [];
}
