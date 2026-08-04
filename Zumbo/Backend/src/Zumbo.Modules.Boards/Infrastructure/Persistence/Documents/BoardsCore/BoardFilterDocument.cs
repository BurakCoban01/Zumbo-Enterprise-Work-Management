using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Boards;

public sealed class BoardFilterDocument
{
    public string? AssigneeUserId { get; set; }
    public string? TeamId { get; set; }
    public List<string> Statuses { get; set; } = [];
    public List<string> Priorities { get; set; } = [];
    public List<string> Labels { get; set; } = [];
    public string? Text { get; set; }
}
